using System;
using System.Linq;
using LiteNetLib;
using TopSpeed.Bots;
using TopSpeed.Data;
using TopSpeed.Protocol;
using TopSpeed.Server.Protocol;
using TopSpeed.Server.Tracks;

namespace TopSpeed.Server.Network
{
    internal sealed partial class RaceServer
    {
        private void HandleRoomStateRequest(PlayerConnection player)
        {
            if (!player.RoomId.HasValue)
            {
                SendRoomState(player, null);
                return;
            }

            if (_rooms.TryGetValue(player.RoomId.Value, out var room))
                SendRoomState(player, room);
            else
                SendRoomState(player, null);
        }

        private void HandleRoomGetRequest(PlayerConnection player, PacketRoomGetRequest packet)
        {
            if (!_rooms.TryGetValue(packet.RoomId, out var room))
            {
                SendRoomGet(player, null);
                return;
            }

            SendRoomGet(player, room);
        }

        private void HandleCreateRoom(PlayerConnection player, PacketRoomCreate packet)
        {
            var roomName = (packet.RoomName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(roomName))
                roomName = $"Game {_nextRoomId}";
            if (roomName.Length > ProtocolConstants.MaxRoomNameLength)
                roomName = roomName.Substring(0, ProtocolConstants.MaxRoomNameLength);

            var roomType = packet.RoomType;
            var playersToStart = packet.PlayersToStart;
            if (playersToStart < 1 || playersToStart > ProtocolConstants.MaxRoomPlayersToStart)
                playersToStart = 2;

            var room = new RaceRoom(_nextRoomId++, roomName, roomType, playersToStart);
            _rooms[room.Id] = room;
            SetTrack(room, room.TrackName);
            JoinRoom(player, room);
            SendProtocolMessage(player, ProtocolMessageCode.Ok, $"Created {room.Name}.");
            EmitRoomLifecycleEvent(room, RoomEventKind.RoomCreated);
            BroadcastLobbyAnnouncement($"{DescribePlayer(player)} created game room {room.Name}.");
            _logger.Info($"Room created: room={room.Id} \"{room.Name}\", host={player.Id}, type={room.RoomType}, playersToStart={room.PlayersToStart}.");
        }

        private void HandleJoinRoom(PlayerConnection player, PacketRoomJoin packet)
        {
            if (!_rooms.TryGetValue(packet.RoomId, out var room))
            {
                SendProtocolMessage(player, ProtocolMessageCode.RoomNotFound, "Game room not found.");
                return;
            }

            if (room.RaceStarted || room.PreparingRace)
            {
                _joinDeniedRaceInProgress++;
                _logger.Debug($"Join denied: player={player.Id}, room={room.Id}, raceStarted={room.RaceStarted}, preparing={room.PreparingRace}.");
                SendProtocolMessage(player, ProtocolMessageCode.Failed, "This game room is currently in progress.");
                return;
            }

            if (GetRoomParticipantCount(room) >= room.PlayersToStart)
            {
                SendProtocolMessage(player, ProtocolMessageCode.RoomFull, "This game room is unavailable because it is full.");
                return;
            }

            JoinRoom(player, room);
            SendProtocolMessage(player, ProtocolMessageCode.Ok, $"Joined {room.Name}.");
            EmitRoomLifecycleEvent(room, RoomEventKind.RoomSummaryUpdated);
            _logger.Info($"Player joined room: room={room.Id} \"{room.Name}\", player={player.Id}, participants={GetRoomParticipantCount(room)}/{room.PlayersToStart}.");
        }

        private void HandleLeaveRoom(PlayerConnection player, bool notify)
        {
            if (!player.RoomId.HasValue)
            {
                SendProtocolMessage(player, ProtocolMessageCode.NotInRoom, "You are not in a game room.");
                return;
            }

            var roomId = player.RoomId.Value;
            if (!_rooms.TryGetValue(roomId, out var room))
            {
                player.RoomId = null;
                SendRoomState(player, null);
                return;
            }

            var oldNumber = player.PlayerNumber;
            var leftName = DescribePlayer(player);
            room.PlayerIds.Remove(player.Id);
            var previousHostId = room.HostId;
            player.RoomId = null;
            player.PlayerNumber = 0;
            player.State = PlayerState.NotReady;
            room.PendingLoadouts.Remove(player.Id);
            room.MediaMap.Remove(player.Id);
            player.IncomingMedia = null;
            player.MediaLoaded = false;
            player.MediaPlaying = false;
            player.MediaId = 0;

            if (notify)
            {
                var stream = room.RaceStarted ? PacketStream.RaceEvent : PacketStream.Room;
                SendToRoomOnStream(room, PacketSerializer.WritePlayer(Command.PlayerDisconnected, player.Id, oldNumber), stream);
                SendProtocolMessageToRoom(room, $"{leftName} has left the game.");
            }

            SendRoomState(player, null);

            if (room.PlayerIds.Count == 0)
            {
                _rooms.Remove(room.Id);
                EmitRoomRemovedEvent(roomId, room.Name);
                _logger.Info($"Room closed: room={room.Id} \"{room.Name}\".");
            }
            else
            {
                if (room.HostId == player.Id)
                    room.HostId = room.PlayerIds.OrderBy(x => x).First();
                if (room.RaceStarted && CountActiveRaceParticipants(room) == 0)
                    StopRace(room);
                if (room.PreparingRace)
                    TryStartRaceAfterLoadout(room);
                TouchRoomVersion(room);
                EmitRoomParticipantEvent(room, RoomEventKind.ParticipantLeft, player.Id, oldNumber, PlayerState.NotReady, leftName);
                if (previousHostId != room.HostId)
                    EmitRoomLifecycleEvent(room, RoomEventKind.HostChanged);
                EmitRoomLifecycleEvent(room, RoomEventKind.RoomSummaryUpdated);
            }
            _logger.Info($"Player left room: room={room.Id} \"{room.Name}\", player={player.Id}, notify={notify}.");
        }

        private void HandleSetTrack(PlayerConnection player, PacketRoomSetTrack packet)
        {
            if (!TryGetHostedRoom(player, out var room))
                return;
            if (room.RaceStarted || room.PreparingRace)
            {
                _roomMutationDenied++;
                _logger.Debug($"Room track change denied: room={room.Id}, player={player.Id}, raceStarted={room.RaceStarted}, preparing={room.PreparingRace}.");
                SendProtocolMessage(player, ProtocolMessageCode.Failed, "Cannot change track while race setup or race is active.");
                return;
            }

            var trackName = (packet.TrackName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trackName))
            {
                SendProtocolMessage(player, ProtocolMessageCode.InvalidTrack, "Track cannot be empty.");
                return;
            }

            SetTrack(room, trackName);
            SendTrackToNotReady(room);
            TouchRoomVersion(room);
            EmitRoomLifecycleEvent(room, RoomEventKind.TrackChanged);
        }

        private void HandleSetLaps(PlayerConnection player, PacketRoomSetLaps packet)
        {
            if (!TryGetHostedRoom(player, out var room))
                return;
            if (room.RaceStarted || room.PreparingRace)
            {
                _roomMutationDenied++;
                _logger.Debug($"Room laps change denied: room={room.Id}, player={player.Id}, raceStarted={room.RaceStarted}, preparing={room.PreparingRace}.");
                SendProtocolMessage(player, ProtocolMessageCode.Failed, "Cannot change laps while race setup or race is active.");
                return;
            }

            if (packet.Laps < 1 || packet.Laps > 16)
            {
                SendProtocolMessage(player, ProtocolMessageCode.InvalidLaps, "Laps must be between 1 and 16.");
                return;
            }

            room.Laps = packet.Laps;
            if (room.TrackSelected)
                SetTrack(room, room.TrackName);
            SendTrackToNotReady(room);
            TouchRoomVersion(room);
            EmitRoomLifecycleEvent(room, RoomEventKind.LapsChanged);
        }

        private void HandleStartRace(PlayerConnection player)
        {
            if (!TryGetHostedRoom(player, out var room))
                return;

            if (GetRoomParticipantCount(room) < room.PlayersToStart)
            {
                SendProtocolMessage(player, ProtocolMessageCode.Failed, $"Not enough players. {room.PlayersToStart} required.");
                return;
            }

            if (room.RaceStarted)
            {
                SendProtocolMessage(player, ProtocolMessageCode.Failed, "A race is already in progress.");
                return;
            }

            if (room.PreparingRace)
            {
                SendProtocolMessage(player, ProtocolMessageCode.Failed, "Race setup is already in progress.");
                return;
            }

            room.PreparingRace = true;
            room.PendingLoadouts.Clear();
            AssignRandomBotLoadouts(room);
            AnnounceBotsReady(room);
            TouchRoomVersion(room);
            _logger.Info($"Race prepare started: room={room.Id} \"{room.Name}\", requestedBy={player.Id}, humans={room.PlayerIds.Count}, bots={room.Bots.Count}, required={room.PlayersToStart}.");

            SendProtocolMessageToRoom(room, $"{DescribePlayer(player)} is about to start the game. Choose your vehicle and transmission mode.");
            EmitRoomLifecycleEvent(room, RoomEventKind.PrepareStarted);
            TryStartRaceAfterLoadout(room);
        }

        private void HandlePlayerReady(PlayerConnection player, PacketRoomPlayerReady ready)
        {
            if (!player.RoomId.HasValue || !_rooms.TryGetValue(player.RoomId.Value, out var room))
            {
                SendProtocolMessage(player, ProtocolMessageCode.NotInRoom, "You are not in a game room.");
                return;
            }

            if (!room.PreparingRace)
            {
                SendProtocolMessage(player, ProtocolMessageCode.Failed, "Race setup has not started yet.");
                return;
            }

            if (!room.PlayerIds.Contains(player.Id))
            {
                SendProtocolMessage(player, ProtocolMessageCode.NotInRoom, "You are not in this game room.");
                return;
            }

            var selectedCar = NormalizeNetworkCar(ready.Car);
            player.Car = selectedCar;
            ApplyVehicleDimensions(player, selectedCar);
            room.PendingLoadouts[player.Id] = new PlayerLoadout(selectedCar, ready.AutomaticTransmission);
            _logger.Debug($"Player ready: room={room.Id}, player={player.Id}, car={selectedCar}, automatic={ready.AutomaticTransmission}, ready={room.PendingLoadouts.Count}/{room.PlayerIds.Count}.");
            SendProtocolMessageToRoom(room, $"{DescribePlayer(player)} is ready.");
            TryStartRaceAfterLoadout(room);
        }

        private void HandleSetPlayersToStart(PlayerConnection player, PacketRoomSetPlayersToStart packet)
        {
            if (!TryGetHostedRoom(player, out var room))
                return;
            if (room.RaceStarted || room.PreparingRace)
            {
                _roomMutationDenied++;
                _logger.Debug($"Room player-limit change denied: room={room.Id}, player={player.Id}, raceStarted={room.RaceStarted}, preparing={room.PreparingRace}.");
                SendProtocolMessage(player, ProtocolMessageCode.Failed, "Cannot change player limit while race setup or race is active.");
                return;
            }

            var value = packet.PlayersToStart;
            if (value < 1 || value > ProtocolConstants.MaxRoomPlayersToStart)
            {
                SendProtocolMessage(player, ProtocolMessageCode.InvalidPlayersToStart, "Players to start must be between 1 and 10.");
                return;
            }

            if (GetRoomParticipantCount(room) > value)
            {
                SendProtocolMessage(player, ProtocolMessageCode.InvalidPlayersToStart, "Cannot set lower than current players in room.");
                return;
            }

            room.PlayersToStart = value;
            TouchRoomVersion(room);
            EmitRoomLifecycleEvent(room, RoomEventKind.PlayersToStartChanged);
            EmitRoomLifecycleEvent(room, RoomEventKind.RoomSummaryUpdated);
        }

        private void HandleAddBot(PlayerConnection player)
        {
            if (!TryGetHostedRoom(player, out var room))
                return;
            if (room.RaceStarted || room.PreparingRace)
            {
                _roomMutationDenied++;
                _logger.Debug($"Room add-bot denied: room={room.Id}, player={player.Id}, raceStarted={room.RaceStarted}, preparing={room.PreparingRace}.");
                SendProtocolMessage(player, ProtocolMessageCode.Failed, "Cannot add bots while race setup or race is active.");
                return;
            }

            if (room.RoomType != GameRoomType.BotsRace)
            {
                SendProtocolMessage(player, ProtocolMessageCode.Failed, "Bots can only be added in race-with-bots rooms.");
                return;
            }

            if (GetRoomParticipantCount(room) >= room.PlayersToStart)
            {
                SendProtocolMessage(player, ProtocolMessageCode.RoomFull, "This game room is unavailable because it is full.");
                return;
            }

            var bot = CreateBot(room);
            room.Bots.Add(bot);
            TouchRoomVersion(room);
            EmitRoomParticipantEvent(room, RoomEventKind.BotAdded, bot.Id, bot.PlayerNumber, bot.State, FormatBotDisplayName(bot));
            EmitRoomLifecycleEvent(room, RoomEventKind.RoomSummaryUpdated);
            SendToRoomOnStream(room, PacketSerializer.WritePlayerJoined(new PacketPlayerJoined
            {
                PlayerId = bot.Id,
                PlayerNumber = bot.PlayerNumber,
                Name = FormatBotJoinName(bot)
            }), PacketStream.Room);
            if (room.PreparingRace)
                TryStartRaceAfterLoadout(room);
        }

        private void HandleRemoveBot(PlayerConnection player)
        {
            if (!TryGetHostedRoom(player, out var room))
                return;
            if (room.RaceStarted || room.PreparingRace)
            {
                _roomMutationDenied++;
                _logger.Debug($"Room remove-bot denied: room={room.Id}, player={player.Id}, raceStarted={room.RaceStarted}, preparing={room.PreparingRace}.");
                SendProtocolMessage(player, ProtocolMessageCode.Failed, "Cannot remove bots while race setup or race is active.");
                return;
            }

            if (room.RoomType != GameRoomType.BotsRace)
            {
                SendProtocolMessage(player, ProtocolMessageCode.Failed, "Bots can only be removed in race-with-bots rooms.");
                return;
            }

            if (room.Bots.Count == 0)
            {
                SendProtocolMessage(player, ProtocolMessageCode.Failed, "There are no bots to remove.");
                return;
            }

            var bot = room.Bots.OrderByDescending(b => b.AddedOrder).First();
            room.Bots.Remove(bot);
            SendToRoomOnStream(room, PacketSerializer.WritePlayer(Command.PlayerDisconnected, bot.Id, bot.PlayerNumber), PacketStream.Room);
            TouchRoomVersion(room);
            EmitRoomParticipantEvent(room, RoomEventKind.BotRemoved, bot.Id, bot.PlayerNumber, bot.State, FormatBotDisplayName(bot));
            EmitRoomLifecycleEvent(room, RoomEventKind.RoomSummaryUpdated);
            SendProtocolMessage(player, ProtocolMessageCode.Ok, $"Removed bot {bot.Name}.");
            if (room.RaceStarted && CountActiveRaceParticipants(room) == 0)
                StopRace(room);
            if (room.PreparingRace)
                TryStartRaceAfterLoadout(room);
        }

        private bool TryGetHostedRoom(PlayerConnection player, out RaceRoom room)
        {
            room = null!;
            if (!player.RoomId.HasValue)
            {
                SendProtocolMessage(player, ProtocolMessageCode.NotInRoom, "You are not in a game room.");
                return false;
            }

            if (!_rooms.TryGetValue(player.RoomId.Value, out var foundRoom) || foundRoom == null)
            {
                SendProtocolMessage(player, ProtocolMessageCode.NotInRoom, "You are not in a game room.");
                return false;
            }

            room = foundRoom;

            if (room.HostId != player.Id)
            {
                SendProtocolMessage(player, ProtocolMessageCode.NotHost, "Only host can do this.");
                return false;
            }

            return true;
        }

        private void JoinRoom(PlayerConnection player, RaceRoom room)
        {
            if (player.RoomId.HasValue)
                HandleLeaveRoom(player, true);

            room.PlayerIds.Add(player.Id);
            if (room.HostId == 0 || !room.PlayerIds.Contains(room.HostId))
                room.HostId = player.Id;

            player.RoomId = room.Id;
            player.PlayerNumber = (byte)FindFreeRoomNumber(room);
            player.State = PlayerState.NotReady;

            SendStream(player, PacketSerializer.WritePlayerNumber(player.Id, player.PlayerNumber), PacketStream.Control);
            SendTrack(room, player);
            SyncMediaTo(room, player);
            TouchRoomVersion(room);
            SendRoomState(player, room);
            EmitRoomParticipantEvent(
                room,
                RoomEventKind.ParticipantJoined,
                player.Id,
                player.PlayerNumber,
                player.State,
                string.IsNullOrWhiteSpace(player.Name) ? $"Player {player.PlayerNumber + 1}" : player.Name);

            var joinedName = string.IsNullOrWhiteSpace(player.Name)
                ? $"Player {player.PlayerNumber + 1}"
                : player.Name;
            var joined = new PacketPlayerJoined { PlayerId = player.Id, PlayerNumber = player.PlayerNumber, Name = joinedName };
            SendToRoomExceptOnStream(room, player.Id, PacketSerializer.WritePlayerJoined(joined), PacketStream.Room);
            _logger.Debug($"Join room assignment: room={room.Id}, player={player.Id}, playerNumber={player.PlayerNumber}, host={room.HostId}.");
        }

        private int FindFreeRoomNumber(RaceRoom room)
        {
            for (var i = 0; i < room.PlayersToStart; i++)
            {
                var usedByPlayer = room.PlayerIds.Any(id => _players.TryGetValue(id, out var p) && p.PlayerNumber == i);
                var usedByBot = room.Bots.Any(bot => bot.PlayerNumber == i);
                var used = usedByPlayer || usedByBot;
                if (!used)
                    return i;
            }

            return 0;
        }

        private void SetTrack(RaceRoom room, string trackName)
        {
            room.TrackName = trackName;
            room.TrackData = TrackLoader.LoadTrack(room.TrackName, room.Laps);
            room.TrackSelected = true;
        }

        private void StartRace(RaceRoom room)
        {
            if (room.RaceStarted)
                return;

            room.PreparingRace = false;
            room.PendingLoadouts.Clear();

            if (!room.TrackSelected || room.TrackData == null)
                SetTrack(room, room.TrackName);

            room.RaceStarted = true;
            room.RaceResults.Clear();
            room.ActiveBumpPairs.Clear();
            room.RaceSnapshotSequence = 0;
            room.RaceSnapshotTick = 0;
            var laneHalfWidth = GetLaneHalfWidth(room);
            var rowSpacing = GetStartRowSpacing(room);
            foreach (var id in room.PlayerIds)
            {
                if (_players.TryGetValue(id, out var p))
                {
                    p.State = PlayerState.AwaitingStart;
                    p.PositionX = CalculateStartX(p.PlayerNumber, p.WidthM, laneHalfWidth);
                    p.PositionY = CalculateStartY(p.PlayerNumber, rowSpacing);
                    p.Speed = 0;
                    p.Frequency = ProtocolConstants.DefaultFrequency;
                    p.EngineRunning = false;
                    p.Braking = false;
                    p.Horning = false;
                    p.Backfiring = false;
                }
            }
            foreach (var bot in room.Bots)
            {
                bot.State = PlayerState.AwaitingStart;
                bot.RacePhase = BotRacePhase.Normal;
                bot.CrashRecoverySeconds = 0f;
                bot.SpeedKph = 0f;
                bot.StartDelaySeconds = BotRaceStartDelaySeconds + GetBotReactionDelay(bot.Difficulty);
                bot.EngineStartSecondsRemaining = 0f;
                bot.EngineFrequency = bot.AudioProfile.IdleFrequency;
                bot.Horning = false;
                bot.HornSecondsRemaining = 0f;
                bot.BackfireArmed = true;
                bot.BackfirePulseSeconds = 0f;
                bot.PositionX = CalculateStartX(bot.PlayerNumber, bot.WidthM, laneHalfWidth);
                bot.PositionY = CalculateStartY(bot.PlayerNumber, rowSpacing);
                bot.PhysicsState = new BotPhysicsState
                {
                    PositionX = bot.PositionX,
                    PositionY = bot.PositionY,
                    SpeedKph = 0f,
                    Gear = 1,
                    AutoShiftCooldownSeconds = 0f
                };
            }

            SendTrackToRoom(room);
            SendToRoomOnStream(room, PacketSerializer.WriteGeneral(Command.StartRace), PacketStream.RaceEvent);
            SendRaceSnapshot(room, DeliveryMethod.ReliableOrdered);
            TouchRoomVersion(room);
            EmitRoomLifecycleEvent(room, RoomEventKind.RaceStarted);
            EmitRoomLifecycleEvent(room, RoomEventKind.RoomSummaryUpdated);
            _logger.Info($"Race started: room={room.Id} \"{room.Name}\", track={room.TrackName}, laps={room.Laps}, humans={room.PlayerIds.Count}, bots={room.Bots.Count}.");
        }

        private void StopRace(RaceRoom room)
        {
            room.RaceStarted = false;
            room.PreparingRace = false;
            room.PendingLoadouts.Clear();
            room.ActiveBumpPairs.Clear();

            var results = room.RaceResults.ToArray();
            SendToRoomOnStream(room, PacketSerializer.WriteRaceResults(new PacketRaceResults
            {
                NPlayers = (byte)Math.Min(results.Length, ProtocolConstants.MaxPlayers),
                Results = results
            }), PacketStream.RaceEvent);

            room.RaceResults.Clear();
            room.RaceSnapshotSequence = 0;
            room.RaceSnapshotTick = 0;
            foreach (var id in room.PlayerIds)
            {
                if (_players.TryGetValue(id, out var p))
                    p.State = PlayerState.NotReady;
            }
            foreach (var bot in room.Bots)
            {
                bot.State = PlayerState.NotReady;
                bot.RacePhase = BotRacePhase.Normal;
                bot.CrashRecoverySeconds = 0f;
                bot.SpeedKph = 0f;
                bot.StartDelaySeconds = 0f;
                bot.EngineStartSecondsRemaining = 0f;
                bot.EngineFrequency = bot.AudioProfile.IdleFrequency;
                bot.Horning = false;
                bot.HornSecondsRemaining = 0f;
                bot.BackfireArmed = true;
                bot.BackfirePulseSeconds = 0f;
                bot.PhysicsState = new BotPhysicsState
                {
                    PositionX = bot.PositionX,
                    PositionY = bot.PositionY,
                    SpeedKph = 0f,
                    Gear = 1,
                    AutoShiftCooldownSeconds = 0f
                };
            }

            TouchRoomVersion(room);
            EmitRoomLifecycleEvent(room, RoomEventKind.RaceStopped);
            EmitRoomLifecycleEvent(room, RoomEventKind.RoomSummaryUpdated);
            _logger.Info($"Race stopped: room={room.Id} \"{room.Name}\", results={string.Join(",", results)}.");
        }

        private void SendTrackToRoom(RaceRoom room)
        {
            foreach (var id in room.PlayerIds)
            {
                if (_players.TryGetValue(id, out var player))
                    SendTrack(room, player);
            }
        }

        private void SendTrackToNotReady(RaceRoom room)
        {
            foreach (var id in room.PlayerIds)
            {
                if (_players.TryGetValue(id, out var player) && player.State == PlayerState.NotReady)
                    SendTrack(room, player);
            }
        }

        private void SendTrack(RaceRoom room, PlayerConnection player)
        {
            if (!room.TrackSelected || room.TrackData == null)
                return;

            var trackLength = (ushort)Math.Min(room.TrackData.Definitions.Length, ProtocolConstants.MaxMultiTrackLength);
            SendStream(player, PacketSerializer.WriteLoadCustomTrack(new PacketLoadCustomTrack
            {
                NrOfLaps = room.TrackData.Laps,
                TrackName = room.TrackData.UserDefined ? "custom" : room.TrackName,
                TrackWeather = room.TrackData.Weather,
                TrackAmbience = room.TrackData.Ambience,
                TrackLength = trackLength,
                Definitions = room.TrackData.Definitions
            }), PacketStream.Room);
        }

        private void SendRoomList(PlayerConnection player)
        {
            var list = new PacketRoomList
            {
                Rooms = _rooms.Values
                    .OrderBy(r => r.Id)
                    .Take(ProtocolConstants.MaxRoomListEntries)
                    .Select(BuildRoomSummary)
                    .ToArray()
            };

            SendStream(player, PacketSerializer.WriteRoomList(list), PacketStream.Query);
        }

        private uint TouchRoomVersion(RaceRoom room)
        {
            if (room == null)
                return 0;

            room.Version++;
            if (room.Version == 0)
                room.Version = 1;
            return room.Version;
        }

        private PacketRoomSummary BuildRoomSummary(RaceRoom room)
        {
            return new PacketRoomSummary
            {
                RoomId = room.Id,
                RoomName = room.Name,
                RoomType = room.RoomType,
                PlayerCount = (byte)Math.Min(ProtocolConstants.MaxPlayers, GetRoomParticipantCount(room)),
                PlayersToStart = room.PlayersToStart,
                RaceStarted = room.RaceStarted,
                TrackName = room.TrackName
            };
        }

        private PacketRoomPlayer[] BuildRoomPlayers(RaceRoom room)
        {
            return room.PlayerIds
                .Where(id => _players.ContainsKey(id))
                .Select(id => _players[id])
                .Select(p => new PacketRoomPlayer
                {
                    PlayerId = p.Id,
                    PlayerNumber = p.PlayerNumber,
                    State = p.State,
                    Name = string.IsNullOrWhiteSpace(p.Name) ? $"Player {p.PlayerNumber + 1}" : p.Name
                })
                .Concat(room.Bots.Select(bot => new PacketRoomPlayer
                {
                    PlayerId = bot.Id,
                    PlayerNumber = bot.PlayerNumber,
                    State = bot.State,
                    Name = FormatBotDisplayName(bot)
                }))
                .OrderBy(p => p.PlayerNumber)
                .ToArray();
        }

        private PacketRoomEvent CreateRoomEvent(RaceRoom room, RoomEventKind kind)
        {
            return new PacketRoomEvent
            {
                RoomId = room.Id,
                RoomVersion = room.Version,
                Kind = kind,
                HostPlayerId = room.HostId,
                RoomType = room.RoomType,
                PlayerCount = (byte)Math.Min(ProtocolConstants.MaxPlayers, GetRoomParticipantCount(room)),
                PlayersToStart = room.PlayersToStart,
                RaceStarted = room.RaceStarted,
                PreparingRace = room.PreparingRace,
                TrackName = room.TrackName,
                Laps = room.Laps,
                RoomName = room.Name
            };
        }

        private void EmitRoomLifecycleEvent(RaceRoom room, RoomEventKind kind)
        {
            var evt = CreateRoomEvent(room, kind);
            var payload = PacketSerializer.WriteRoomEvent(evt);
            var roomOnly =
                kind == RoomEventKind.HostChanged ||
                kind == RoomEventKind.TrackChanged ||
                kind == RoomEventKind.LapsChanged ||
                kind == RoomEventKind.PlayersToStartChanged ||
                kind == RoomEventKind.PrepareStarted ||
                kind == RoomEventKind.PrepareCancelled;

            if (roomOnly)
            {
                foreach (var id in room.PlayerIds)
                {
                    if (_players.TryGetValue(id, out var player))
                        SendStream(player, payload, PacketStream.Room);
                }
                return;
            }

            foreach (var player in _players.Values)
                SendStream(player, payload, PacketStream.Room);
        }

        private void EmitRoomParticipantEvent(RaceRoom room, RoomEventKind kind, uint playerId, byte playerNumber, PlayerState state, string name)
        {
            var evt = CreateRoomEvent(room, kind);
            evt.SubjectPlayerId = playerId;
            evt.SubjectPlayerNumber = playerNumber;
            evt.SubjectPlayerState = state;
            evt.SubjectPlayerName = name ?? string.Empty;
            var payload = PacketSerializer.WriteRoomEvent(evt);

            foreach (var id in room.PlayerIds)
            {
                if (_players.TryGetValue(id, out var player))
                    SendStream(player, payload, PacketStream.Room);
            }
        }

        private void EmitRoomRemovedEvent(uint roomId, string roomName)
        {
            var evt = new PacketRoomEvent
            {
                RoomId = roomId,
                RoomVersion = 0,
                Kind = RoomEventKind.RoomRemoved,
                RoomName = roomName ?? string.Empty
            };

            var payload = PacketSerializer.WriteRoomEvent(evt);
            foreach (var player in _players.Values)
                SendStream(player, payload, PacketStream.Room);
        }

        private void SendRoomState(PlayerConnection player, RaceRoom? room)
        {
            if (room == null)
            {
                SendStream(player, PacketSerializer.WriteRoomState(new PacketRoomState
                {
                    RoomVersion = 0,
                    InRoom = false,
                    HostPlayerId = 0,
                    RoomType = GameRoomType.BotsRace,
                    PlayersToStart = 0,
                    PreparingRace = false,
                    Players = Array.Empty<PacketRoomPlayer>()
                }), PacketStream.Query);
                return;
            }

            SendStream(player, PacketSerializer.WriteRoomState(new PacketRoomState
            {
                RoomVersion = room.Version,
                RoomId = room.Id,
                HostPlayerId = room.HostId,
                RoomName = room.Name,
                RoomType = room.RoomType,
                PlayersToStart = room.PlayersToStart,
                InRoom = true,
                IsHost = room.HostId == player.Id,
                RaceStarted = room.RaceStarted,
                PreparingRace = room.PreparingRace,
                TrackName = room.TrackName,
                Laps = room.Laps,
                Players = BuildRoomPlayers(room)
            }), PacketStream.Query);
        }

        private void SendRoomGet(PlayerConnection player, RaceRoom? room)
        {
            if (room == null)
            {
                SendStream(player, PacketSerializer.WriteRoomGet(new PacketRoomGet
                {
                    Found = false,
                    Players = Array.Empty<PacketRoomPlayer>()
                }), PacketStream.Query);
                return;
            }

            SendStream(player, PacketSerializer.WriteRoomGet(new PacketRoomGet
            {
                Found = true,
                RoomVersion = room.Version,
                RoomId = room.Id,
                HostPlayerId = room.HostId,
                RoomName = room.Name,
                RoomType = room.RoomType,
                PlayersToStart = room.PlayersToStart,
                RaceStarted = room.RaceStarted,
                PreparingRace = room.PreparingRace,
                TrackName = room.TrackName,
                Laps = room.Laps,
                Players = BuildRoomPlayers(room)
            }), PacketStream.Query);
        }

        private void AssignRandomBotLoadouts(RaceRoom room)
        {
            foreach (var bot in room.Bots)
            {
                bot.Car = (CarType)_random.Next((int)CarType.Vehicle1, (int)CarType.CustomVehicle);
                bot.AutomaticTransmission = _random.Next(0, 2) == 0;
                ApplyVehicleDimensions(bot, bot.Car);
            }
        }

        private void AnnounceBotsReady(RaceRoom room)
        {
            foreach (var bot in room.Bots.OrderBy(b => b.PlayerNumber))
            {
                SendProtocolMessageToRoom(room, $"{FormatBotJoinName(bot)} is ready.");
            }
        }

        private void TryStartRaceAfterLoadout(RaceRoom room)
        {
            if (!room.PreparingRace)
                return;
            if (GetRoomParticipantCount(room) < room.PlayersToStart)
            {
                room.PreparingRace = false;
                room.PendingLoadouts.Clear();
                TouchRoomVersion(room);
                EmitRoomLifecycleEvent(room, RoomEventKind.PrepareCancelled);
                SendProtocolMessageToRoom(room, "Race start cancelled because there are not enough players.");
                _logger.Info($"Race prepare cancelled: room={room.Id} \"{room.Name}\", participants={GetRoomParticipantCount(room)}, required={room.PlayersToStart}.");
                return;
            }
            if (room.PendingLoadouts.Count < room.PlayerIds.Count)
            {
                _logger.Debug($"Waiting for loadouts: room={room.Id}, ready={room.PendingLoadouts.Count}/{room.PlayerIds.Count}.");
                return;
            }

            room.PreparingRace = false;
            SendProtocolMessageToRoom(room, "All players are ready. Starting game.");
            _logger.Info($"All loadouts ready: room={room.Id} \"{room.Name}\", starting race.");
            StartRace(room);
        }

        private void SendProtocolMessage(PlayerConnection player, ProtocolMessageCode code, string text)
        {
            SendStream(player, PacketSerializer.WriteProtocolMessage(new PacketProtocolMessage
            {
                Code = code,
                Message = text ?? string.Empty
            }), PacketStream.Direct);
        }

        private void SendProtocolMessageToRoom(RaceRoom room, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var payload = PacketSerializer.WriteProtocolMessage(new PacketProtocolMessage
            {
                Code = ProtocolMessageCode.Ok,
                Message = text
            });

            SendToRoomOnStream(room, payload, PacketStream.Chat);
        }

        private void BroadcastLobbyAnnouncement(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            foreach (var player in _players.Values)
            {
                if (player.RoomId.HasValue)
                    continue;

                SendProtocolMessage(player, ProtocolMessageCode.Ok, text);
            }
        }

        private static string DescribePlayer(PlayerConnection player)
        {
            if (!string.IsNullOrWhiteSpace(player.Name))
                return player.Name;
            return "A player";
        }

        private RoomBot CreateBot(RaceRoom room)
        {
            var name = (_faker.Name.FirstName() ?? "Bot").Trim();
            if (string.IsNullOrWhiteSpace(name))
                name = "Bot";
            if (name.Length > ProtocolConstants.MaxPlayerNameLength)
                name = name.Substring(0, ProtocolConstants.MaxPlayerNameLength);

            var car = (CarType)_random.Next((int)CarType.Vehicle1, (int)CarType.CustomVehicle);
            var bot = new RoomBot
            {
                Id = _nextBotId++,
                PlayerNumber = (byte)FindFreeRoomNumber(room),
                Name = name,
                Difficulty = (BotDifficulty)_random.Next(0, 3),
                AddedOrder = room.Bots.Count == 0 ? 1 : room.Bots.Max(b => b.AddedOrder) + 1,
                Car = car,
                AutomaticTransmission = _random.Next(0, 2) == 0
            };

            ApplyVehicleDimensions(bot, car);
            bot.EngineFrequency = bot.AudioProfile.IdleFrequency;
            return bot;
        }

        private static int GetRoomParticipantCount(RaceRoom room)
        {
            return room.PlayerIds.Count + room.Bots.Count;
        }

        private static string DifficultyLabel(BotDifficulty difficulty)
        {
            return difficulty switch
            {
                BotDifficulty.Easy => "easy",
                BotDifficulty.Hard => "hard",
                _ => "normal"
            };
        }

        private float GetBotReactionDelay(BotDifficulty difficulty)
        {
            return difficulty switch
            {
                BotDifficulty.Hard => 0.1f + (float)_random.NextDouble() * 0.4f,
                BotDifficulty.Normal => 1.0f + (float)_random.NextDouble() * 1.5f,
                _ => 2.5f + (float)_random.NextDouble() * 2.5f
            };
        }

        private static string FormatBotDisplayName(RoomBot bot)
        {
            var label = $"{FormatBotJoinName(bot)} ({DifficultyLabel(bot.Difficulty)})";
            if (label.Length > ProtocolConstants.MaxPlayerNameLength)
                return label.Substring(0, ProtocolConstants.MaxPlayerNameLength);
            return label;
        }

        private static string FormatBotJoinName(RoomBot bot)
        {
            var label = $"Bot {bot.Name}";
            if (label.Length > ProtocolConstants.MaxPlayerNameLength)
                return label.Substring(0, ProtocolConstants.MaxPlayerNameLength);
            return label;
        }

        private static CarType NormalizeNetworkCar(CarType car)
        {
            if (car < CarType.Vehicle1 || car >= CarType.CustomVehicle)
                return CarType.Vehicle1;
            return car;
        }

        private static void ApplyVehicleDimensions(PlayerConnection player, CarType car)
        {
            var dimensions = GetVehicleDimensions(car);
            player.WidthM = dimensions.WidthM;
            player.LengthM = dimensions.LengthM;
        }

        private static void ApplyVehicleDimensions(RoomBot bot, CarType car)
        {
            var dimensions = GetVehicleDimensions(car);
            bot.WidthM = dimensions.WidthM;
            bot.LengthM = dimensions.LengthM;
            bot.PhysicsConfig = BotPhysicsCatalog.Get(car);
            bot.AudioProfile = GetVehicleAudioProfile(car);
            bot.EngineFrequency = bot.AudioProfile.IdleFrequency;
            var state = bot.PhysicsState;
            if (state.Gear <= 0)
                state.Gear = 1;
            bot.PhysicsState = state;
        }

        private static VehicleDimensions GetVehicleDimensions(CarType car)
        {
            return car switch
            {
                CarType.Vehicle1 => new VehicleDimensions(1.895f, 4.689f),
                CarType.Vehicle2 => new VehicleDimensions(1.852f, 4.572f),
                CarType.Vehicle3 => new VehicleDimensions(1.627f, 3.546f),
                CarType.Vehicle4 => new VehicleDimensions(1.744f, 3.876f),
                CarType.Vehicle5 => new VehicleDimensions(1.811f, 4.760f),
                CarType.Vehicle6 => new VehicleDimensions(1.839f, 4.879f),
                CarType.Vehicle7 => new VehicleDimensions(2.030f, 4.780f),
                CarType.Vehicle8 => new VehicleDimensions(1.811f, 4.624f),
                CarType.Vehicle9 => new VehicleDimensions(2.019f, 5.931f),
                CarType.Vehicle10 => new VehicleDimensions(0.749f, 2.085f),
                CarType.Vehicle11 => new VehicleDimensions(0.806f, 2.110f),
                CarType.Vehicle12 => new VehicleDimensions(0.690f, 2.055f),
                _ => new VehicleDimensions(1.8f, 4.5f)
            };
        }

        private static BotAudioProfile GetVehicleAudioProfile(CarType car)
        {
            return car switch
            {
                CarType.Vehicle1 => new BotAudioProfile(22050, 55000, 26000),
                CarType.Vehicle2 => new BotAudioProfile(22050, 60000, 35000),
                CarType.Vehicle3 => new BotAudioProfile(6000, 25000, 19000),
                CarType.Vehicle4 => new BotAudioProfile(6000, 27000, 20000),
                CarType.Vehicle5 => new BotAudioProfile(6000, 33000, 27500),
                CarType.Vehicle6 => new BotAudioProfile(7025, 40000, 32500),
                CarType.Vehicle7 => new BotAudioProfile(6000, 26000, 21000),
                CarType.Vehicle8 => new BotAudioProfile(10000, 45000, 34000),
                CarType.Vehicle9 => new BotAudioProfile(22050, 30550, 22550),
                CarType.Vehicle10 => new BotAudioProfile(22050, 60000, 35000),
                CarType.Vehicle11 => new BotAudioProfile(22050, 60000, 35000),
                CarType.Vehicle12 => new BotAudioProfile(22050, 27550, 23550),
                _ => new BotAudioProfile(22050, 55000, 26000)
            };
        }

        private int CountActiveRaceParticipants(RaceRoom room)
        {
            var humanRacers = room.PlayerIds.Count(id => _players.TryGetValue(id, out var player) && IsActiveRaceState(player.State));
            var botRacers = room.Bots.Count(bot => IsActiveRaceState(bot.State));
            return humanRacers + botRacers;
        }

        private static bool IsActiveRaceState(PlayerState state)
        {
            return state == PlayerState.AwaitingStart || state == PlayerState.Racing;
        }

        private void SendRaceSnapshot(RaceRoom room, DeliveryMethod deliveryMethod)
        {
            _raceSnapshotSends++;
            _logger.Debug($"Race snapshot send: room={room.Id}, delivery={deliveryMethod}.");
            var payload = BuildRaceSnapshotPayload(room);
            if (payload == null)
                return;

            var delivery = deliveryMethod == DeliveryMethod.ReliableOrdered
                ? PacketDeliveryKind.ReliableOrdered
                : deliveryMethod == DeliveryMethod.Sequenced
                    ? PacketDeliveryKind.Sequenced
                    : PacketDeliveryKind.Unreliable;
            SendToRoomOnStream(room, payload, PacketStream.RaceState, delivery);
        }

        private void BroadcastPlayerData()
        {
            foreach (var room in _rooms.Values)
            {
                if (!room.RaceStarted)
                    continue;
                var payload = BuildRaceSnapshotPayload(room);
                if (payload == null)
                    continue;
                SendToRoomOnStream(room, payload, PacketStream.RaceState, PacketDeliveryKind.Unreliable);
            }
        }

        private byte[]? BuildRaceSnapshotPayload(RaceRoom room)
        {
            var max = ProtocolConstants.MaxPlayers;
            var items = new PacketPlayerData[max];
            var count = 0;

            foreach (var id in room.PlayerIds)
            {
                if (!_players.TryGetValue(id, out var player))
                    continue;
                if (player.State == PlayerState.NotReady || player.State == PlayerState.Undefined)
                    continue;
                if (count >= max)
                    break;
                items[count++] = player.ToPacket();
            }

            if (count < max)
            {
                foreach (var bot in room.Bots)
                {
                    if (bot.State == PlayerState.NotReady || bot.State == PlayerState.Undefined)
                        continue;
                    if (count >= max)
                        break;
                    items[count++] = ToBotPacket(bot);
                }
            }

            if (count == 0)
                return null;

            var players = new PacketPlayerData[count];
            Array.Copy(items, players, count);
            var snapshot = new PacketRaceSnapshot
            {
                Sequence = ++room.RaceSnapshotSequence,
                Tick = room.RaceSnapshotTick = _simulationTick,
                Players = players
            };
            _stateSyncFramesSent += count;
            return PacketSerializer.WriteRaceSnapshot(snapshot);
        }

    }
}
