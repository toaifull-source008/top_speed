using TopSpeed.Network;
using TopSpeed.Protocol;

namespace TopSpeed.Core
{
    internal sealed partial class Game
    {
        private void RegisterMultiplayerRaceStatePacketHandlers()
        {
            _mpPktReg.Add("race_state", Command.RaceSnapshot, HandleMpRaceSnapshotPacket);
        }

        private void RegisterMultiplayerRaceEventPacketHandlers()
        {
            _mpPktReg.Add("race_event", Command.StartRace, HandleMpStartRacePacket);
            _mpPktReg.Add("race_event", Command.RaceAborted, HandleMpRaceAbortedPacket);
            _mpPktReg.Add("race_event", Command.PlayerBumped, HandleMpPlayerBumpedPacket);
            _mpPktReg.Add("race_event", Command.PlayerCrashed, HandleMpPlayerCrashedPacket);
            _mpPktReg.Add("race_event", Command.PlayerFinished, HandleMpPlayerFinishedPacket);
            _mpPktReg.Add("race_event", Command.PlayerDisconnected, HandleMpPlayerDisconnectedPacket);
            _mpPktReg.Add("race_event", Command.StopRace, HandleMpStopRacePacket);
        }

        private bool HandleMpRaceSnapshotPacket(IncomingPacket packet)
        {
            if (_multiplayerRace == null)
                return true;

            if (ClientPacketSerializer.TryReadRaceSnapshot(packet.Payload, out var snapshot))
                _multiplayerRace.ApplyRaceSnapshot(snapshot);
            return true;
        }

        private bool HandleMpStartRacePacket(IncomingPacket _)
        {
            StartMultiplayerRace();
            return true;
        }

        private bool HandleMpRaceAbortedPacket(IncomingPacket _)
        {
            if (_state == AppState.MultiplayerRace)
                EndMultiplayerRace();
            return true;
        }

        private bool HandleMpPlayerBumpedPacket(IncomingPacket packet)
        {
            if (_multiplayerRace == null)
                return true;

            if (ClientPacketSerializer.TryReadPlayerBumped(packet.Payload, out var bump))
                _multiplayerRace.ApplyBump(bump);
            return true;
        }

        private bool HandleMpPlayerCrashedPacket(IncomingPacket packet)
        {
            if (_multiplayerRace == null)
                return true;

            if (ClientPacketSerializer.TryReadPlayer(packet.Payload, out var crashed))
                _multiplayerRace.ApplyRemoteCrash(crashed);
            return true;
        }

        private bool HandleMpPlayerFinishedPacket(IncomingPacket packet)
        {
            if (_multiplayerRace == null)
                return true;

            if (ClientPacketSerializer.TryReadPlayer(packet.Payload, out var finished))
                _multiplayerRace.ApplyRemoteFinish(finished);
            return true;
        }

        private bool HandleMpPlayerDisconnectedPacket(IncomingPacket packet)
        {
            if (_multiplayerRace == null)
                return true;

            if (ClientPacketSerializer.TryReadPlayer(packet.Payload, out var disconnected))
                _multiplayerRace.RemoveRemotePlayer(disconnected.PlayerNumber);
            return true;
        }

        private bool HandleMpStopRacePacket(IncomingPacket packet)
        {
            if (_state != AppState.MultiplayerRace || _multiplayerRace == null)
                return true;

            if (ClientPacketSerializer.TryReadRaceResults(packet.Payload, out var results))
                _multiplayerRace.HandleServerRaceStopped(results);
            else
                _multiplayerRace.HandleServerRaceStopped(new PacketRaceResults());
            return true;
        }
    }
}
