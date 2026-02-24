using System;
using System.Collections.Generic;
using TopSpeed.Menu;
using TopSpeed.Network;
using TopSpeed.Protocol;

namespace TopSpeed.Core.Multiplayer
{
    internal sealed partial class MultiplayerCoordinator
    {
        public void HandleRoomList(PacketRoomList roomList)
        {
            _roomList = roomList ?? new PacketRoomList();
            if (!_roomBrowserOpenPending)
                return;

            _roomBrowserOpenPending = false;
            if (!string.Equals(_menu.CurrentId, MultiplayerLobbyMenuId, StringComparison.Ordinal))
                return;

            UpdateRoomBrowserMenu();
            _menu.Push(MultiplayerRoomBrowserMenuId);
        }

        public void HandleRoomState(PacketRoomState roomState)
        {
            var wasInRoom = _wasInRoom;
            var previousRoomId = _lastRoomId;
            var previousIsHost = _wasHost;
            var previousRoomType = _roomState.RoomType;
            _roomState = roomState ?? new PacketRoomState { InRoom = false, Players = Array.Empty<PacketRoomPlayer>() };

            if (_roomState.InRoom)
            {
                if (!wasInRoom || previousRoomId != _roomState.RoomId)
                {
                    var roomName = string.IsNullOrWhiteSpace(_roomState.RoomName) ? "game room" : _roomState.RoomName;
                    _speech.Speak($"Joined {roomName}.");
                }

                if (_roomState.IsHost && (!_wasHost || !wasInRoom))
                    _speech.Speak("You are now host of this game.");
            }
            else if (wasInRoom)
            {
                _speech.Speak("You left the game room.");
            }

            _wasInRoom = _roomState.InRoom;
            _lastRoomId = _roomState.RoomId;
            _wasHost = _roomState.IsHost;

            if (_roomState.InRoom && (!wasInRoom || previousRoomId != _roomState.RoomId))
            {
                _menu.ShowRoot(MultiplayerRoomControlsMenuId);
            }
            else if (!_roomState.InRoom && wasInRoom)
            {
                _menu.ShowRoot(MultiplayerLobbyMenuId);
            }

            var roomControlsChanged =
                wasInRoom != _roomState.InRoom ||
                previousIsHost != _roomState.IsHost ||
                previousRoomType != _roomState.RoomType;
            if (roomControlsChanged)
            {
                RebuildRoomControlsMenu();
                RebuildRoomOptionsMenu();
            }

            RebuildRoomPlayersMenu();
        }

        public void HandleRoomEvent(PacketRoomEvent roomEvent)
        {
            if (roomEvent == null)
                return;

            ApplyRoomListEvent(roomEvent);

            ApplyCurrentRoomEvent(roomEvent, out var beginLoadout, out var localHostChanged);
            if (localHostChanged)
            {
                RebuildRoomControlsMenu();
                RebuildRoomOptionsMenu();
            }
            if (_roomState.InRoom)
                RebuildRoomPlayersMenu();

            if (beginLoadout)
                BeginRaceLoadoutSelection();
        }

        public void HandleProtocolMessage(PacketProtocolMessage message)
        {
            if (message == null)
                return;

            if (message.Code == ProtocolMessageCode.ServerPlayerConnected)
                PlayNetworkSound("online.wav");
            else if (message.Code == ProtocolMessageCode.ServerPlayerDisconnected)
                PlayNetworkSound("offline.wav");

            if (!string.IsNullOrWhiteSpace(message.Message))
                _speech.Speak(message.Message);
        }

        private void ApplyRoomListEvent(PacketRoomEvent roomEvent)
        {
            if (roomEvent.Kind == RoomEventKind.None)
                return;

            var rooms = new List<PacketRoomSummary>(_roomList.Rooms ?? Array.Empty<PacketRoomSummary>());
            var index = rooms.FindIndex(r => r.RoomId == roomEvent.RoomId);

            switch (roomEvent.Kind)
            {
                case RoomEventKind.RoomRemoved:
                    if (index >= 0)
                        rooms.RemoveAt(index);
                    break;

                case RoomEventKind.RoomCreated:
                case RoomEventKind.RoomSummaryUpdated:
                case RoomEventKind.RaceStarted:
                case RoomEventKind.RaceStopped:
                case RoomEventKind.ParticipantJoined:
                case RoomEventKind.ParticipantLeft:
                case RoomEventKind.BotAdded:
                case RoomEventKind.BotRemoved:
                case RoomEventKind.PlayersToStartChanged:
                    var summary = new PacketRoomSummary
                    {
                        RoomId = roomEvent.RoomId,
                        RoomName = roomEvent.RoomName ?? string.Empty,
                        RoomType = roomEvent.RoomType,
                        PlayerCount = roomEvent.PlayerCount,
                        PlayersToStart = roomEvent.PlayersToStart,
                        RaceStarted = roomEvent.RaceStarted,
                        TrackName = roomEvent.TrackName ?? string.Empty
                    };
                    if (index >= 0)
                        rooms[index] = summary;
                    else if (roomEvent.Kind != RoomEventKind.RoomSummaryUpdated || roomEvent.RoomId != 0)
                        rooms.Add(summary);
                    break;
            }

            rooms.Sort((a, b) => a.RoomId.CompareTo(b.RoomId));
            _roomList = new PacketRoomList { Rooms = rooms.ToArray() };
        }

        private bool ApplyCurrentRoomEvent(PacketRoomEvent roomEvent, out bool beginLoadout, out bool localHostChanged)
        {
            beginLoadout = false;
            localHostChanged = false;

            if (!_roomState.InRoom || _roomState.RoomId != roomEvent.RoomId)
                return false;

            var previousIsHost = _roomState.IsHost;
            var session = SessionOrNull();

            _roomState.RoomVersion = roomEvent.RoomVersion;
            if (!string.IsNullOrWhiteSpace(roomEvent.RoomName))
                _roomState.RoomName = roomEvent.RoomName;
            _roomState.HostPlayerId = roomEvent.HostPlayerId;
            _roomState.RoomType = roomEvent.RoomType;
            _roomState.PlayersToStart = roomEvent.PlayersToStart;
            _roomState.RaceStarted = roomEvent.RaceStarted;
            _roomState.PreparingRace = roomEvent.PreparingRace;
            _roomState.TrackName = roomEvent.TrackName ?? string.Empty;
            _roomState.Laps = roomEvent.Laps;
            _roomState.IsHost = session != null && roomEvent.HostPlayerId == session.PlayerId;

            switch (roomEvent.Kind)
            {
                case RoomEventKind.ParticipantJoined:
                case RoomEventKind.BotAdded:
                    UpsertCurrentRoomParticipant(roomEvent);
                    break;

                case RoomEventKind.ParticipantLeft:
                case RoomEventKind.BotRemoved:
                    RemoveCurrentRoomParticipant(roomEvent.SubjectPlayerId);
                    break;

                case RoomEventKind.ParticipantStateChanged:
                    UpsertCurrentRoomParticipant(roomEvent);
                    break;

                case RoomEventKind.PrepareStarted:
                    beginLoadout = true;
                    break;
            }

            localHostChanged = previousIsHost != _roomState.IsHost;
            if (localHostChanged && _roomState.IsHost)
                _speech.Speak("You are now host of this game.");

            _wasHost = _roomState.IsHost;
            return true;
        }

        private void UpsertCurrentRoomParticipant(PacketRoomEvent roomEvent)
        {
            if (roomEvent.SubjectPlayerId == 0)
                return;

            var players = new List<PacketRoomPlayer>(_roomState.Players ?? Array.Empty<PacketRoomPlayer>());
            var index = players.FindIndex(p => p.PlayerId == roomEvent.SubjectPlayerId);
            var name = string.IsNullOrWhiteSpace(roomEvent.SubjectPlayerName)
                ? $"Player {roomEvent.SubjectPlayerNumber + 1}"
                : roomEvent.SubjectPlayerName;
            var item = new PacketRoomPlayer
            {
                PlayerId = roomEvent.SubjectPlayerId,
                PlayerNumber = roomEvent.SubjectPlayerNumber,
                State = roomEvent.SubjectPlayerState,
                Name = name
            };

            if (index >= 0)
                players[index] = item;
            else
                players.Add(item);

            players.Sort((a, b) => a.PlayerNumber.CompareTo(b.PlayerNumber));
            _roomState.Players = players.ToArray();
        }

        private void RemoveCurrentRoomParticipant(uint playerId)
        {
            if (playerId == 0)
                return;

            var players = new List<PacketRoomPlayer>(_roomState.Players ?? Array.Empty<PacketRoomPlayer>());
            var removed = players.RemoveAll(p => p.PlayerId == playerId);
            if (removed == 0)
                return;

            players.Sort((a, b) => a.PlayerNumber.CompareTo(b.PlayerNumber));
            _roomState.Players = players.ToArray();
        }
    }
}
