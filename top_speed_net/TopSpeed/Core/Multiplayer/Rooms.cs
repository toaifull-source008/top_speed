using System;
using System.Collections.Generic;
using TopSpeed.Common;
using TopSpeed.Data;
using TopSpeed.Menu;
using TopSpeed.Network;
using TopSpeed.Protocol;
using TopSpeed.Speech;

namespace TopSpeed.Core.Multiplayer
{
    internal sealed partial class MultiplayerCoordinator
    {
        public bool IsInRoom => _roomState.InRoom;

        public void ConfigureMenuCloseHandlers()
        {
            _menu.SetCloseHandler(MultiplayerLobbyMenuId, _ =>
            {
                OpenDisconnectConfirmation();
                return true;
            });

            _menu.SetCloseHandler(MultiplayerRoomControlsMenuId, _ =>
            {
                OpenLeaveRoomConfirmation();
                return true;
            });

            _menu.SetCloseHandler(MultiplayerSavedServerFormMenuId, _ =>
            {
                CloseSavedServerForm();
                return true;
            });

            _menu.SetCloseHandler(MultiplayerLoadoutTransmissionMenuId, _ =>
            {
                _menu.ShowRoot(MultiplayerLoadoutVehicleMenuId);
                return true;
            });

            _menu.SetCloseHandler(MultiplayerLoadoutVehicleMenuId, _ =>
            {
                _speech.Speak("Choose your vehicle and transmission mode to get ready for the race.");
                _menu.ShowRoot(MultiplayerLoadoutVehicleMenuId);
                return true;
            });
        }

        public void ShowMultiplayerMenuAfterRace()
        {
            if (_roomState.InRoom)
                _menu.ShowRoot(MultiplayerRoomControlsMenuId);
            else
                _menu.ShowRoot(MultiplayerLobbyMenuId);
        }

        public void BeginRaceLoadoutSelection()
        {
            if (!_roomState.InRoom)
                return;

            _pendingLoadoutVehicleIndex = 0;
            RebuildLoadoutVehicleMenu();
            RebuildLoadoutTransmissionMenu();
            _menu.ShowRoot(MultiplayerLoadoutVehicleMenuId);
            _enterMenuState();
        }

        private void RebuildLobbyMenu()
        {
            var items = new List<MenuItem>
            {
                new MenuItem("Create a new game room", MenuAction.None, onActivate: OpenCreateRoomMenu),
                new MenuItem("Join an existing game", MenuAction.None, onActivate: OpenRoomBrowser),
                new MenuItem("Options", MenuAction.None, nextMenuId: "options_main"),
                new MenuItem("Disconnect from server", MenuAction.None, flags: MenuItemFlags.Close)
            };

            _menu.UpdateItems(MultiplayerLobbyMenuId, items);
        }

        private void RebuildCreateRoomMenu()
        {
            var items = new List<MenuItem>
            {
                new RadioButton(
                    "Game type",
                    RoomTypeOptions,
                    GetCreateRoomTypeIndex,
                    SetCreateRoomType,
                    hint: "Choose whether this room is a race with bots, a multiplayer race without bots, or a one-on-one game. Use LEFT or RIGHT to change."),
                new RadioButton(
                    "Maximum players allowed in this room",
                    PlayerCountOptions,
                    GetCreateRoomPlayersToStartIndex,
                    SetCreateRoomPlayersToStart,
                    hint: "Choose the player capacity from 1 to 10. Use LEFT or RIGHT to change."),
                new MenuItem(
                    () => string.IsNullOrWhiteSpace(_createRoomName)
                        ? "Room name, currently automatic"
                        : $"Room name, currently {_createRoomName}",
                    MenuAction.None,
                    onActivate: UpdateCreateRoomName,
                    hint: "Press ENTER to enter a room name. Leave it empty to use an automatic name."),
                new MenuItem("Create this game room", MenuAction.None, onActivate: ConfirmCreateRoom),
                new MenuItem("Cancel room creation", MenuAction.Back)
            };

            _menu.UpdateItems(MultiplayerCreateRoomMenuId, items);
        }

        private void RebuildRoomControlsMenu()
        {
            var items = new List<MenuItem>();
            if (!_roomState.InRoom)
            {
                items.Add(new MenuItem("You are not currently inside a game room.", MenuAction.None));
                items.Add(new MenuItem("Return to multiplayer lobby", MenuAction.None, onActivate: () => _menu.ShowRoot(MultiplayerLobbyMenuId)));
                _menu.UpdateItems(MultiplayerRoomControlsMenuId, items);
                return;
            }

            if (_roomState.IsHost)
                items.Add(new MenuItem("Start this game now", MenuAction.None, onActivate: StartGame));
            if (_roomState.IsHost)
                items.Add(new MenuItem("Change game options", MenuAction.None, nextMenuId: "multiplayer_room_options"));
            if (_roomState.IsHost && _roomState.RoomType == GameRoomType.BotsRace)
                items.Add(new MenuItem("Add a bot to this game room", MenuAction.None, onActivate: AddBotToRoom));
            if (_roomState.IsHost && _roomState.RoomType == GameRoomType.BotsRace)
                items.Add(new MenuItem("Remove the last bot that was added", MenuAction.None, onActivate: RemoveLastBotFromRoom));
            items.Add(new MenuItem("Who is currently present in this game room", MenuAction.None, onActivate: OpenRoomPlayersMenu));
            items.Add(new MenuItem("Leave this game room", MenuAction.None, flags: MenuItemFlags.Close));
            _menu.UpdateItems(MultiplayerRoomControlsMenuId, items);
        }

        private void RebuildRoomOptionsMenu()
        {
            var items = new List<MenuItem>();
            if (!_roomState.InRoom)
            {
                items.Add(new MenuItem("You are not currently inside a game room.", MenuAction.None));
                items.Add(new MenuItem("Return to room controls", MenuAction.Back));
                _menu.UpdateItems(MultiplayerRoomOptionsMenuId, items);
                return;
            }

            if (!_roomState.IsHost)
            {
                items.Add(new MenuItem("Only the host can change game options.", MenuAction.None));
                items.Add(new MenuItem("Return to room controls", MenuAction.Back));
                _menu.UpdateItems(MultiplayerRoomOptionsMenuId, items);
                return;
            }

            items.Add(new RadioButton(
                "Track",
                RoomTrackLabels,
                GetCurrentRoomTrackIndex,
                SetRoomTrackByIndex,
                hint: "Choose which track this room will use. Use LEFT or RIGHT to change."));

            items.Add(new RadioButton(
                "Number of laps",
                LapCountOptions,
                () => Math.Max(0, Math.Min(LapCountOptions.Length - 1, (_roomState.Laps > 0 ? _roomState.Laps : (byte)1) - 1)),
                value => SetLaps((byte)(value + 1)),
                hint: "Choose the number of laps for this room. Use LEFT or RIGHT to change."));

            items.Add(new RadioButton(
                "Players required before the host can start",
                PlayerCountOptions,
                () => Math.Max(0, Math.Min(PlayerCountOptions.Length - 1, (_roomState.PlayersToStart > 0 ? _roomState.PlayersToStart : (byte)1) - 1)),
                value => SetPlayersToStart((byte)(value + 1)),
                hint: "Select how many players are required before the host can start this game. Use LEFT or RIGHT to change."));

            items.Add(new MenuItem("Return to room controls", MenuAction.Back));
            var preserveSelection = string.Equals(_menu.CurrentId, MultiplayerRoomOptionsMenuId, StringComparison.Ordinal);
            _menu.UpdateItems(MultiplayerRoomOptionsMenuId, items, preserveSelection);
        }

        private void RebuildLoadoutVehicleMenu()
        {
            var items = new List<MenuItem>();
            for (var i = 0; i < VehicleCatalog.VehicleCount; i++)
            {
                var vehicleIndex = i;
                var vehicleName = VehicleCatalog.Vehicles[i].Name;
                items.Add(new MenuItem(vehicleName, MenuAction.None, nextMenuId: MultiplayerLoadoutTransmissionMenuId, onActivate: () => _pendingLoadoutVehicleIndex = vehicleIndex));
            }

            items.Add(new MenuItem("Random vehicle", MenuAction.None, nextMenuId: MultiplayerLoadoutTransmissionMenuId, onActivate: () => _pendingLoadoutVehicleIndex = Algorithm.RandomInt(VehicleCatalog.VehicleCount)));
            _menu.UpdateItems(MultiplayerLoadoutVehicleMenuId, items);
        }

        private void RebuildLoadoutTransmissionMenu()
        {
            var items = new List<MenuItem>
            {
                new MenuItem("Automatic transmission", MenuAction.None, onActivate: () => SubmitLoadoutReady(true)),
                new MenuItem("Manual transmission", MenuAction.None, onActivate: () => SubmitLoadoutReady(false)),
                new MenuItem("Random transmission mode", MenuAction.None, onActivate: () => SubmitLoadoutReady(Algorithm.RandomInt(2) == 0)),
                new MenuItem("Go back to vehicle selection", MenuAction.Back)
            };
            _menu.UpdateItems(MultiplayerLoadoutTransmissionMenuId, items);
        }

        private void OpenRoomBrowser()
        {
            var session = SessionOrNull();
            if (session == null)
            {
                _speech.Speak("Not connected to a server.");
                return;
            }

            if (_roomBrowserOpenPending)
                return;

            _roomBrowserOpenPending = true;
            session.SendRoomListRequest();
        }

        private void OpenCreateRoomMenu()
        {
            if (SessionOrNull() == null)
            {
                _speech.Speak("Not connected to a server.");
                return;
            }

            ResetCreateRoomDraft();
            RebuildCreateRoomMenu();
            _menu.Push(MultiplayerCreateRoomMenuId);
        }

        private void UpdateCreateRoomName()
        {
            var result = _promptTextInput(
                "Enter a room name. Leave this field empty to use an automatic room name.",
                _createRoomName,
                SpeechService.SpeakFlag.None,
                true);

            if (result.Cancelled)
                return;

            _createRoomName = (result.Text ?? string.Empty).Trim();
            RebuildCreateRoomMenu();

            if (string.IsNullOrWhiteSpace(_createRoomName))
            {
                _speech.Speak("Automatic room name selected.");
                return;
            }

            _speech.Speak($"Room name set to {_createRoomName}.");
        }

        private void ConfirmCreateRoom()
        {
            var session = SessionOrNull();
            if (session == null)
            {
                _speech.Speak("Not connected to a server.");
                return;
            }

            var playersToStart = _createRoomPlayersToStart;
            if (playersToStart < 1 || playersToStart > ProtocolConstants.MaxRoomPlayersToStart)
                playersToStart = 2;

            session.SendRoomCreate(_createRoomName, _createRoomType, playersToStart);
            _speech.Speak("Creating game room.");
            _menu.ShowRoot(MultiplayerLobbyMenuId);
        }

        private int GetCreateRoomTypeIndex()
        {
            return _createRoomType switch
            {
                GameRoomType.PlayersRace => 1,
                GameRoomType.OneOnOne => 2,
                _ => 0
            };
        }

        private void SetCreateRoomType(int index)
        {
            _createRoomType = index switch
            {
                2 => GameRoomType.OneOnOne,
                1 => GameRoomType.PlayersRace,
                _ => GameRoomType.BotsRace
            };
        }

        private int GetCreateRoomPlayersToStartIndex()
        {
            var playersToStart = _createRoomPlayersToStart;
            if (playersToStart < 1 || playersToStart > ProtocolConstants.MaxRoomPlayersToStart)
                playersToStart = 2;
            return playersToStart - 1;
        }

        private void SetCreateRoomPlayersToStart(int index)
        {
            var playersToStart = (byte)(index + 1);
            if (playersToStart < 1 || playersToStart > ProtocolConstants.MaxRoomPlayersToStart)
                return;
            _createRoomPlayersToStart = playersToStart;
        }

        private void ResetCreateRoomDraft()
        {
            _createRoomType = GameRoomType.BotsRace;
            _createRoomPlayersToStart = 2;
            _createRoomName = string.Empty;
        }

        private void JoinRoom(uint roomId)
        {
            var session = SessionOrNull();
            if (session == null)
            {
                _speech.Speak("Not connected to a server.");
                return;
            }

            session.SendRoomJoin(roomId);
        }

        private void OpenLeaveRoomConfirmation()
        {
            if (!_roomState.InRoom)
            {
                _speech.Speak("You are not currently inside a game room.");
                return;
            }

            if (_questions.IsQuestionMenu(_menu.CurrentId))
                return;

            _questions.Show(new Question(
                "Leave this game room?",
                "Are you sure you want to leave the current room?",
                QuestionId.No,
                HandleLeaveRoomQuestionResult,
                new QuestionButton(QuestionId.Yes, "Yes, leave this game room"),
                new QuestionButton(QuestionId.No, "No, stay in this game room", flags: QuestionButtonFlags.Default)));
        }

        private void HandleLeaveRoomQuestionResult(int resultId)
        {
            if (resultId == QuestionId.Yes)
                ConfirmLeaveRoom();
        }

        private void ConfirmLeaveRoom()
        {
            var session = SessionOrNull();
            if (session == null)
            {
                _speech.Speak("Not connected to a server.");
                return;
            }

            session.SendRoomLeave();
            _speech.Speak("Leaving game room.");
            _menu.ShowRoot(MultiplayerLobbyMenuId);
        }

        private void StartGame()
        {
            var session = SessionOrNull();
            if (session == null)
            {
                _speech.Speak("Not connected to a server.");
                return;
            }

            if (!_roomState.InRoom || !_roomState.IsHost)
            {
                _speech.Speak("Only the host can start the game.");
                return;
            }

            session.SendRoomStartRace();
        }

        private int GetCurrentRoomTrackIndex()
        {
            var currentTrack = string.IsNullOrWhiteSpace(_roomState.TrackName) ? TrackList.RaceTracks[0].Key : _roomState.TrackName;
            for (var i = 0; i < RoomTrackOptions.Length; i++)
            {
                if (string.Equals(RoomTrackOptions[i].Key, currentTrack, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return 0;
        }

        private void SetRoomTrackByIndex(int index)
        {
            var session = SessionOrNull();
            if (session == null)
                return;
            if (!_roomState.InRoom || !_roomState.IsHost)
                return;
            if (index < 0 || index >= RoomTrackOptions.Length)
                return;

            session.SendRoomSetTrack(RoomTrackOptions[index].Key);
        }

        private void SetLaps(byte laps)
        {
            var session = SessionOrNull();
            if (session == null || !_roomState.IsHost || !_roomState.InRoom)
                return;
            if (laps < 1 || laps > 16)
                return;

            session.SendRoomSetLaps(laps);
        }

        private void SetPlayersToStart(byte playersToStart)
        {
            var session = SessionOrNull();
            if (session == null || !_roomState.IsHost || !_roomState.InRoom)
                return;

            session.SendRoomSetPlayersToStart(playersToStart);
        }

        private void AddBotToRoom()
        {
            var session = SessionOrNull();
            if (session == null)
            {
                _speech.Speak("Not connected to a server.");
                return;
            }

            if (!_roomState.InRoom || !_roomState.IsHost || _roomState.RoomType != GameRoomType.BotsRace)
            {
                _speech.Speak("Bots can only be managed by the host in race-with-bots rooms.");
                return;
            }

            session.SendRoomAddBot();
        }

        private void RemoveLastBotFromRoom()
        {
            var session = SessionOrNull();
            if (session == null)
            {
                _speech.Speak("Not connected to a server.");
                return;
            }

            if (!_roomState.InRoom || !_roomState.IsHost || _roomState.RoomType != GameRoomType.BotsRace)
            {
                _speech.Speak("Bots can only be managed by the host in race-with-bots rooms.");
                return;
            }

            session.SendRoomRemoveBot();
        }

        private void SubmitLoadoutReady(bool automaticTransmission)
        {
            var session = SessionOrNull();
            if (session == null)
            {
                _speech.Speak("Not connected to a server.");
                return;
            }

            if (!_roomState.InRoom)
            {
                _speech.Speak("You are not in a game room.");
                return;
            }

            var vehicleIndex = Math.Max(0, Math.Min(VehicleCatalog.VehicleCount - 1, _pendingLoadoutVehicleIndex));
            var selectedCar = (CarType)vehicleIndex;
            _setLocalMultiplayerLoadout(vehicleIndex, automaticTransmission);
            session.SendRoomPlayerReady(selectedCar, automaticTransmission);
            _speech.Speak("Ready. Waiting for other players.");
            _menu.ShowRoot(MultiplayerRoomControlsMenuId);
        }

        private void RebuildRoomPlayersMenu()
        {
            var items = new List<MenuItem>();
            if (!_roomState.InRoom)
            {
                items.Add(new MenuItem("You are not currently inside a game room.", MenuAction.None));
                items.Add(new MenuItem("Go back", MenuAction.Back));
                _menu.UpdateItems(MultiplayerRoomPlayersMenuId, items);
                return;
            }

            var players = _roomState.Players ?? Array.Empty<PacketRoomPlayer>();
            if (players.Length == 0)
            {
                items.Add(new MenuItem("No players are currently in this game room.", MenuAction.None));
            }
            else
            {
                foreach (var player in players)
                {
                    var name = string.IsNullOrWhiteSpace(player.Name) ? $"Player {player.PlayerNumber + 1}" : player.Name;
                    var label = player.PlayerId == _roomState.HostPlayerId ? $"{name}, host" : name;
                    items.Add(new MenuItem(label, MenuAction.None));
                }
            }

            items.Add(new MenuItem("Go back", MenuAction.Back));
            var preserveSelection = string.Equals(_menu.CurrentId, MultiplayerRoomPlayersMenuId, StringComparison.Ordinal);
            _menu.UpdateItems(MultiplayerRoomPlayersMenuId, items, preserveSelection);
        }

        private void OpenRoomPlayersMenu()
        {
            RebuildRoomPlayersMenu();
            _menu.Push(MultiplayerRoomPlayersMenuId);
        }

        private void Disconnect()
        {
            _clearSession();
            _speech.Speak("Disconnected from server.");
            _menu.ShowRoot("main");
            _enterMenuState();
        }

        private void OpenDisconnectConfirmation()
        {
            if (_questions.IsQuestionMenu(_menu.CurrentId))
                return;

            _questions.Show(new Question(
                "Leave server?",
                "Are you sure you want to disconnect?",
                QuestionId.No,
                HandleDisconnectQuestionResult,
                new QuestionButton(QuestionId.Yes, "Yes, disconnect from the server"),
                new QuestionButton(QuestionId.No, "No, stay connected", flags: QuestionButtonFlags.Default)));
        }

        private void HandleDisconnectQuestionResult(int resultId)
        {
            if (resultId == QuestionId.Yes)
                Disconnect();
        }

        private void UpdateRoomBrowserMenu()
        {
            var items = new List<MenuItem>();
            var rooms = _roomList.Rooms ?? Array.Empty<PacketRoomSummary>();
            if (rooms.Length == 0)
            {
                items.Add(new MenuItem("No game rooms found", MenuAction.None));
            }
            else
            {
                foreach (var room in rooms)
                {
                    var roomCopy = room;
                    var typeText = roomCopy.RoomType switch
                    {
                        GameRoomType.OneOnOne => "one-on-one",
                        GameRoomType.PlayersRace => "race without bots",
                        _ => "race with bots"
                    };
                    var label = $"{typeText} game with {roomCopy.PlayerCount} people";
                    label += $", maximum {roomCopy.PlayersToStart} players";
                    if (roomCopy.RaceStarted)
                        label += ", in progress";
                    else if (roomCopy.PlayerCount >= roomCopy.PlayersToStart)
                        label += ", room is full";
                    items.Add(new MenuItem(label, MenuAction.None, onActivate: () => JoinRoom(roomCopy.RoomId)));
                }
            }

            items.Add(new MenuItem("Return to multiplayer lobby", MenuAction.Back));
            _menu.UpdateItems(MultiplayerRoomBrowserMenuId, items);
        }
    }
}
