using System;
using System.Linq;
using System.Net;
using LiteNetLib;

namespace TopSpeed.Server.Network
{
    internal sealed partial class RaceServer
    {
        public void Start()
        {
            ResetStreamTxMetrics();
            _transport.Start(_config.Port);
            _logger.Info("Race server started.");
        }

        public void Stop()
        {
            lock (_lock)
            {
                _rooms.Clear();
                _players.Clear();
                _endpointIndex.Clear();
                ResetStreamTxMetrics();
            }

            _transport.Stop();
            _logger.Info("Race server stopped.");
        }

        public void Update(float deltaSeconds)
        {
            lock (_lock)
            {
                if (deltaSeconds <= 0f)
                    return;

                _simulationAccumulator += deltaSeconds;
                while (_simulationAccumulator >= ServerSimulationStepSeconds)
                {
                    _simulationAccumulator -= ServerSimulationStepSeconds;
                    _simulationTick++;
                    _cleanupAccumulator += ServerSimulationStepSeconds;
                    _snapshotAccumulator += ServerSimulationStepSeconds;

                    if (_cleanupAccumulator >= CleanupIntervalSeconds)
                    {
                        _cleanupAccumulator -= CleanupIntervalSeconds;
                        CleanupConnections();
                    }

                    UpdateBots(ServerSimulationStepSeconds);
                    CheckForBumps();

                    if (_snapshotAccumulator >= ServerSnapshotIntervalSeconds)
                    {
                        _snapshotAccumulator -= ServerSnapshotIntervalSeconds;
                        BroadcastPlayerData();
                    }
                }
            }
        }

        private void CleanupConnections()
        {
            var expired = _players.Values.Where(p => DateTime.UtcNow - p.LastSeenUtc > ConnectionTimeout).Select(p => p.Id).ToList();
            foreach (var id in expired)
            {
                if (!_players.TryGetValue(id, out var player))
                    continue;

                RemoveConnection(player, notifyRoom: true, sendDisconnectPacket: true, reason: "timeout");
            }
        }

        private void OnPeerDisconnected(IPEndPoint endpoint)
        {
            lock (_lock)
            {
                var key = endpoint.ToString();
                if (!_endpointIndex.TryGetValue(key, out var id))
                    return;
                if (!_players.TryGetValue(id, out var player))
                    return;

                RemoveConnection(player, notifyRoom: true, sendDisconnectPacket: false, reason: "peer_disconnect");
            }
        }

        private void RemoveConnection(PlayerConnection player, bool notifyRoom, bool sendDisconnectPacket, string reason)
        {
            var roomId = player.RoomId;
            if (player.RoomId.HasValue)
                HandleLeaveRoom(player, notifyRoom);
            if (player.ServerPresenceAnnounced)
                BroadcastServerDisconnectAnnouncement(player, reason);
            if (sendDisconnectPacket)
                SendStream(player, TopSpeed.Server.Protocol.PacketSerializer.WriteGeneral(TopSpeed.Protocol.Command.Disconnect), TopSpeed.Protocol.PacketStream.Control);
            _endpointIndex.Remove(player.EndPoint.ToString());
            _players.Remove(player.Id);
            _logger.Info($"Connection removed: player={player.Id}, endpoint={player.EndPoint}, room={roomId?.ToString() ?? "none"}, reason={reason}.");
        }

        public ServerSnapshot GetSnapshot()
        {
            lock (_lock)
            {
                var raceStarted = _rooms.Values.Any(r => r.RaceStarted);
                var trackSelected = _rooms.Values.Any(r => r.TrackSelected);
                var trackName = _rooms.Count == 1 ? _rooms.Values.First().TrackName : (_rooms.Count > 1 ? "multiple" : string.Empty);
                return new ServerSnapshot(_config.Name ?? "TopSpeed Server", _config.Port, _config.MaxPlayers, _players.Count, raceStarted, trackSelected, trackName);
            }
        }

        public void Dispose()
        {
            _transport.Dispose();
        }
    }
}
