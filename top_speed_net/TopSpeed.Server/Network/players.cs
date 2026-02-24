using TopSpeed.Protocol;
using TopSpeed.Server.Protocol;

namespace TopSpeed.Server.Network
{
    internal sealed partial class RaceServer
    {
        private void HandlePlayerHello(PlayerConnection player, PacketPlayerHello hello)
        {
            var name = (hello.Name ?? string.Empty).Trim();
            if (name.Length > ProtocolConstants.MaxPlayerNameLength)
                name = name.Substring(0, ProtocolConstants.MaxPlayerNameLength);
            player.Name = name;
            if (!player.ServerPresenceAnnounced)
            {
                player.ServerPresenceAnnounced = true;
                BroadcastServerConnectAnnouncement(player);
            }
            if (player.RoomId.HasValue && _rooms.TryGetValue(player.RoomId.Value, out var room))
            {
                TouchRoomVersion(room);
                EmitRoomParticipantEvent(
                    room,
                    RoomEventKind.ParticipantStateChanged,
                    player.Id,
                    player.PlayerNumber,
                    player.State,
                    string.IsNullOrWhiteSpace(player.Name) ? $"Player {player.PlayerNumber + 1}" : player.Name);
            }
        }

        private void HandlePlayerState(PlayerConnection player, PacketPlayerState state)
        {
            if (!player.RoomId.HasValue || !_rooms.TryGetValue(player.RoomId.Value, out var room))
            {
                _authorityDropsPlayerState++;
                return;
            }

            var previousState = player.State;

            if (room.RaceStarted)
            {
                if (state.State == PlayerState.AwaitingStart
                    || state.State == PlayerState.Racing
                    || state.State == PlayerState.Finished)
                {
                    player.State = state.State;
                }
                else
                {
                    _authorityDropsPlayerState++;
                }
            }
            else
            {
                if (state.State != PlayerState.NotReady && state.State != PlayerState.Undefined)
                    _authorityDropsPlayerState++;
                player.State = PlayerState.NotReady;
                if (room.TrackSelected)
                    SendTrack(room, player);
            }

            if (previousState != player.State)
                _logger.Debug($"Player state transition: room={room.Id}, player={player.Id}, {previousState} -> {player.State} (packet={state.State}).");
            if (previousState != player.State)
            {
                TouchRoomVersion(room);
                EmitRoomParticipantEvent(
                    room,
                    RoomEventKind.ParticipantStateChanged,
                    player.Id,
                    player.PlayerNumber,
                    player.State,
                    string.IsNullOrWhiteSpace(player.Name) ? $"Player {player.PlayerNumber + 1}" : player.Name);
            }
        }

        private void HandlePlayerData(PlayerConnection player, PacketPlayerData data)
        {
            if (!player.RoomId.HasValue || !_rooms.TryGetValue(player.RoomId.Value, out var room))
            {
                _authorityDropsPlayerData++;
                return;
            }

            var previousState = player.State;
            player.Car = NormalizeNetworkCar(data.Car);
            ApplyVehicleDimensions(player, player.Car);
            player.PositionX = data.RaceData.PositionX;
            player.PositionY = data.RaceData.PositionY;
            player.Speed = data.RaceData.Speed;
            player.Frequency = data.RaceData.Frequency;
            player.EngineRunning = data.EngineRunning;
            player.Braking = data.Braking;
            player.Horning = data.Horning;
            player.Backfiring = data.Backfiring;
            UpdateMediaState(player, room, data);
            var nextState = data.State;

            if (room.RaceStarted)
            {
                if (nextState == PlayerState.Undefined || nextState == PlayerState.NotReady)
                {
                    _authorityDropsPlayerData++;
                    nextState = player.State;
                }

                if (nextState != PlayerState.AwaitingStart
                    && nextState != PlayerState.Racing
                    && nextState != PlayerState.Finished)
                {
                    _authorityDropsPlayerData++;
                    nextState = player.State;
                }
            }
            else
            {
                if (nextState != PlayerState.NotReady && nextState != PlayerState.Undefined)
                    _authorityDropsPlayerData++;
                nextState = PlayerState.NotReady;
            }

            player.State = nextState;
            if (previousState != nextState)
                _logger.Debug($"Player state transition from data: room={room.Id}, player={player.Id}, {previousState} -> {nextState}.");
        }

        private void HandlePlayerFinished(PlayerConnection player, PacketPlayer finished)
        {
            if (!player.RoomId.HasValue || !_rooms.TryGetValue(player.RoomId.Value, out var room))
            {
                _authorityDropsPlayerFinished++;
                return;
            }
            if (!room.RaceStarted)
            {
                _authorityDropsPlayerFinished++;
                return;
            }

            if (finished.PlayerId != player.Id || finished.PlayerNumber != player.PlayerNumber)
            {
                _authorityDropsPlayerFinished++;
                _logger.Debug($"PlayerFinished payload mismatch: room={room.Id}, connectionPlayer={player.Id}/{player.PlayerNumber}, payload={finished.PlayerId}/{finished.PlayerNumber}.");
            }

            player.State = PlayerState.Finished;
            if (!room.RaceResults.Contains(player.PlayerNumber))
                room.RaceResults.Add(player.PlayerNumber);

            SendToRoomExceptOnStream(room, player.Id, PacketSerializer.WritePlayer(Command.PlayerFinished, player.Id, player.PlayerNumber), PacketStream.RaceEvent);
            _logger.Debug($"Player finished: room={room.Id}, player={player.Id}, number={player.PlayerNumber}, results={room.RaceResults.Count}.");
            if (CountActiveRaceParticipants(room) == 0)
                StopRace(room);
        }

        private void HandlePlayerStarted(PlayerConnection player)
        {
            if (!player.RoomId.HasValue || !_rooms.TryGetValue(player.RoomId.Value, out var room))
            {
                _authorityDropsPlayerStarted++;
                return;
            }
            if (!room.RaceStarted)
            {
                _authorityDropsPlayerStarted++;
                return;
            }

            if (player.State == PlayerState.AwaitingStart || player.State == PlayerState.Racing)
            {
                player.State = PlayerState.Racing;
            }
            else
            {
                _authorityDropsPlayerStarted++;
            }
        }

        private void HandlePlayerCrashed(PlayerConnection player, PacketPlayer crashed)
        {
            if (!player.RoomId.HasValue || !_rooms.TryGetValue(player.RoomId.Value, out var room))
            {
                _authorityDropsPlayerCrashed++;
                return;
            }
            if (!room.RaceStarted)
            {
                _authorityDropsPlayerCrashed++;
                return;
            }

            if (crashed.PlayerId != player.Id || crashed.PlayerNumber != player.PlayerNumber)
            {
                _authorityDropsPlayerCrashed++;
                _logger.Debug($"PlayerCrashed payload mismatch: room={room.Id}, connectionPlayer={player.Id}/{player.PlayerNumber}, payload={crashed.PlayerId}/{crashed.PlayerNumber}.");
            }

            SendToRoomExceptOnStream(room, player.Id, PacketSerializer.WritePlayer(Command.PlayerCrashed, player.Id, player.PlayerNumber), PacketStream.RaceEvent);
        }

        private void BroadcastServerConnectAnnouncement(PlayerConnection connected)
        {
            var name = string.IsNullOrWhiteSpace(connected.Name) ? "A player" : connected.Name;
            var text = $"{name} has connected to the server.";
            foreach (var player in _players.Values)
            {
                if (player.Id == connected.Id || !player.ServerPresenceAnnounced)
                    continue;

                SendProtocolMessage(player, ProtocolMessageCode.ServerPlayerConnected, text);
            }
        }

        private void BroadcastServerDisconnectAnnouncement(PlayerConnection disconnected, string reason)
        {
            var name = string.IsNullOrWhiteSpace(disconnected.Name) ? "A player" : disconnected.Name;
            var normalizedReason = (reason ?? string.Empty).Trim();
            var text = string.Equals(normalizedReason, "timeout", System.StringComparison.OrdinalIgnoreCase)
                ? $"{name} has lost connection to the server."
                : $"{name} has disconnected from the server.";

            foreach (var player in _players.Values)
            {
                if (player.Id == disconnected.Id || !player.ServerPresenceAnnounced)
                    continue;

                SendProtocolMessage(player, ProtocolMessageCode.ServerPlayerDisconnected, text);
            }
        }
    }
}
