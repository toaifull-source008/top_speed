using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib;
using TopSpeed.Protocol;

namespace TopSpeed.Network
{
    internal sealed class MultiplayerSession : IDisposable
    {
        private const int RadioChunkSize = ProtocolConstants.MaxMediaChunkBytes;
        private readonly NetManager _manager;
        private readonly NetPeer _peer;
        private readonly IPEndPoint _serverEndPoint;
        private readonly CancellationTokenSource _cts;
        private readonly Task _pollTask;
        private readonly Task _keepAliveTask;
        private readonly ConcurrentQueue<IncomingPacket> _incoming;
        private byte _playerNumber;

        public MultiplayerSession(
            NetManager manager,
            NetPeer peer,
            IPEndPoint serverEndPoint,
            uint playerId,
            byte playerNumber,
            string? motd,
            string? playerName,
            ConcurrentQueue<IncomingPacket> incoming)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _peer = peer ?? throw new ArgumentNullException(nameof(peer));
            _serverEndPoint = serverEndPoint ?? throw new ArgumentNullException(nameof(serverEndPoint));
            _incoming = incoming ?? throw new ArgumentNullException(nameof(incoming));
            PlayerId = playerId;
            _playerNumber = playerNumber;
            Motd = motd ?? string.Empty;
            PlayerName = playerName ?? string.Empty;
            _cts = new CancellationTokenSource();
            _pollTask = Task.Run(() => PollLoop(_cts.Token));
            _keepAliveTask = Task.Run(() => KeepAliveLoop(_cts.Token));
        }

        public IPAddress Address => _serverEndPoint.Address;
        public int Port => _serverEndPoint.Port;
        public uint PlayerId { get; }
        public byte PlayerNumber => _playerNumber;
        public string Motd { get; }
        public string PlayerName { get; }

        public void UpdatePlayerNumber(byte playerNumber)
        {
            _playerNumber = playerNumber;
        }

        private void PollLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    _manager.PollEvents();
                }
                catch
                {
                    // Ignore poll failures to keep the session alive.
                }

                Thread.Sleep(1);
            }
        }

        private async Task KeepAliveLoop(CancellationToken token)
        {
            var payload = new[] { ProtocolConstants.Version, (byte)Command.KeepAlive };
            while (!token.IsCancellationRequested)
            {
                SafeSendStream(payload, PacketStream.Control, PacketDeliveryKind.Unreliable);

                try
                {
                    await Task.Delay(1000, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        public bool TryDequeuePacket(out IncomingPacket packet)
        {
            return _incoming.TryDequeue(out packet);
        }

        public void SendPlayerState(PlayerState state)
        {
            var payload = ClientPacketSerializer.WritePlayerState(Command.PlayerState, PlayerId, PlayerNumber, state);
            SafeSendStream(payload, PacketStream.Control);
        }

        public void SendPlayerData(
            PlayerRaceData raceData,
            CarType car,
            PlayerState state,
            bool engine,
            bool braking,
            bool horning,
            bool backfiring,
            bool radioLoaded,
            bool radioPlaying,
            uint radioMediaId)
        {
            var payload = ClientPacketSerializer.WritePlayerDataToServer(
                PlayerId,
                PlayerNumber,
                car,
                raceData,
                state,
                engine,
                braking,
                horning,
                backfiring,
                radioLoaded,
                radioPlaying,
                radioMediaId);
            SafeSendStream(payload, PacketStream.RaceState, PacketDeliveryKind.Sequenced);
        }

        public bool SendRadioMedia(uint mediaId, string filePath)
        {
            if (mediaId == 0 || string.IsNullOrWhiteSpace(filePath))
                return false;
            if (!File.Exists(filePath))
                return false;

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(filePath);
            }
            catch
            {
                return false;
            }

            if (bytes.Length == 0 || bytes.Length > ProtocolConstants.MaxMediaBytes)
                return false;

            var extension = Path.GetExtension(filePath).Trim().TrimStart('.');
            if (extension.Length > ProtocolConstants.MaxMediaFileExtensionLength)
                extension = extension.Substring(0, ProtocolConstants.MaxMediaFileExtensionLength);

            SafeSendStream(ClientPacketSerializer.WritePlayerMediaBegin(PlayerId, PlayerNumber, mediaId, (uint)bytes.Length, extension), PacketStream.Media);

            var chunkIndex = 0;
            var offset = 0;
            while (offset < bytes.Length)
            {
                var length = Math.Min(RadioChunkSize, bytes.Length - offset);
                var chunk = new byte[length];
                Buffer.BlockCopy(bytes, offset, chunk, 0, length);
                SafeSendStream(ClientPacketSerializer.WritePlayerMediaChunk(PlayerId, PlayerNumber, mediaId, (ushort)chunkIndex, chunk), PacketStream.Media);
                chunkIndex++;
                offset += length;
            }

            SafeSendStream(ClientPacketSerializer.WritePlayerMediaEnd(PlayerId, PlayerNumber, mediaId), PacketStream.Media);
            return true;
        }

        public bool SendRadioMediaStreamed(uint mediaId, string filePath)
        {
            if (mediaId == 0 || string.IsNullOrWhiteSpace(filePath))
                return false;
            if (!File.Exists(filePath))
                return false;

            FileStream? stream = null;
            try
            {
                stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length <= 0 || stream.Length > ProtocolConstants.MaxMediaBytes)
                    return false;

                var extension = Path.GetExtension(filePath).Trim().TrimStart('.');
                if (extension.Length > ProtocolConstants.MaxMediaFileExtensionLength)
                    extension = extension.Substring(0, ProtocolConstants.MaxMediaFileExtensionLength);

                SafeSendStream(
                    ClientPacketSerializer.WritePlayerMediaBegin(
                        PlayerId,
                        PlayerNumber,
                        mediaId,
                        (uint)stream.Length,
                        extension),
                    PacketStream.Media);

                var chunkIndex = 0;
                var buffer = new byte[RadioChunkSize];
                while (true)
                {
                    var read = stream.Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                        break;

                    var chunk = new byte[read];
                    Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                    SafeSendStream(
                        ClientPacketSerializer.WritePlayerMediaChunk(PlayerId, PlayerNumber, mediaId, (ushort)chunkIndex, chunk),
                        PacketStream.Media);
                    chunkIndex++;
                }

                SafeSendStream(ClientPacketSerializer.WritePlayerMediaEnd(PlayerId, PlayerNumber, mediaId), PacketStream.Media);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                stream?.Dispose();
            }
        }

        public void SendPlayerStarted()
        {
            var payload = ClientPacketSerializer.WritePlayer(Command.PlayerStarted, PlayerId, PlayerNumber);
            SafeSendStream(payload, PacketStream.RaceEvent);
        }

        public void SendPlayerFinished()
        {
            var payload = ClientPacketSerializer.WritePlayer(Command.PlayerFinished, PlayerId, PlayerNumber);
            SafeSendStream(payload, PacketStream.RaceEvent);
        }

        public void SendPlayerFinalize(PlayerState state)
        {
            var payload = ClientPacketSerializer.WritePlayerState(Command.PlayerFinalize, PlayerId, PlayerNumber, state);
            SafeSendStream(payload, PacketStream.Control);
        }

        public void SendPlayerCrashed()
        {
            var payload = ClientPacketSerializer.WritePlayer(Command.PlayerCrashed, PlayerId, PlayerNumber);
            SafeSendStream(payload, PacketStream.RaceEvent);
        }

        public void SendRoomListRequest()
        {
            SafeSendStream(ClientPacketSerializer.WriteRoomListRequest(), PacketStream.Room);
        }

        public void SendRoomStateRequest()
        {
            SafeSendStream(ClientPacketSerializer.WriteRoomStateRequest(), PacketStream.Room);
        }

        public void SendRoomGetRequest(uint roomId)
        {
            SafeSendStream(ClientPacketSerializer.WriteRoomGetRequest(roomId), PacketStream.Room);
        }

        public void SendRoomCreate(string roomName, GameRoomType roomType, byte playersToStart)
        {
            SafeSendStream(ClientPacketSerializer.WriteRoomCreate(roomName, roomType, playersToStart), PacketStream.Room);
        }

        public void SendRoomJoin(uint roomId)
        {
            SafeSendStream(ClientPacketSerializer.WriteRoomJoin(roomId), PacketStream.Room);
        }

        public void SendRoomLeave()
        {
            SafeSendStream(ClientPacketSerializer.WriteRoomLeave(), PacketStream.Room);
        }

        public void SendRoomSetTrack(string trackName)
        {
            SafeSendStream(ClientPacketSerializer.WriteRoomSetTrack(trackName), PacketStream.Room);
        }

        public void SendRoomSetLaps(byte laps)
        {
            SafeSendStream(ClientPacketSerializer.WriteRoomSetLaps(laps), PacketStream.Room);
        }

        public void SendRoomStartRace()
        {
            SafeSendStream(ClientPacketSerializer.WriteRoomStartRace(), PacketStream.Room);
        }

        public void SendRoomSetPlayersToStart(byte playersToStart)
        {
            SafeSendStream(ClientPacketSerializer.WriteRoomSetPlayersToStart(playersToStart), PacketStream.Room);
        }

        public void SendRoomAddBot()
        {
            SafeSendStream(ClientPacketSerializer.WriteRoomAddBot(), PacketStream.Room);
        }

        public void SendRoomRemoveBot()
        {
            SafeSendStream(ClientPacketSerializer.WriteRoomRemoveBot(), PacketStream.Room);
        }

        public void SendRoomPlayerReady(CarType car, bool automaticTransmission)
        {
            SafeSendStream(ClientPacketSerializer.WriteRoomPlayerReady(car, automaticTransmission), PacketStream.Room);
        }

        private void SafeSendStream(byte[] payload, PacketStream stream)
        {
            var spec = PacketStreams.Get(stream);
            SafeSendStream(payload, stream, spec.Delivery);
        }

        private void SafeSendStream(byte[] payload, PacketStream stream, PacketDeliveryKind deliveryOverride)
        {
            try
            {
                if (_peer.ConnectionState != ConnectionState.Connected)
                    return;

                var spec = PacketStreams.Get(stream);
                _peer.Send(payload, spec.Channel, ToDelivery(deliveryOverride));
            }
            catch
            {
                // Ignore send failures to keep the client running.
            }
        }

        private static DeliveryMethod ToDelivery(PacketDeliveryKind kind)
        {
            return kind switch
            {
                PacketDeliveryKind.Unreliable => DeliveryMethod.Unreliable,
                PacketDeliveryKind.Sequenced => DeliveryMethod.Sequenced,
                _ => DeliveryMethod.ReliableOrdered
            };
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _pollTask.Wait(250); } catch { }
            try { _keepAliveTask.Wait(250); } catch { }
            _manager.Stop();
            _cts.Dispose();
        }
    }

    internal readonly struct IncomingPacket
    {
        public IncomingPacket(Command command, byte[] payload)
        {
            Command = command;
            Payload = payload;
        }

        public Command Command { get; }
        public byte[] Payload { get; }
    }
}
