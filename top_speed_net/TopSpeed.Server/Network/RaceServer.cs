using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using TopSpeed.Data;
using TopSpeed.Protocol;
using TopSpeed.Server.Logging;
using TopSpeed.Server.Protocol;
using TopSpeed.Server.Tracks;

namespace TopSpeed.Server.Network
{
    internal sealed class PlayerConnection
    {
        public PlayerConnection(IPEndPoint endPoint, uint id)
        {
            EndPoint = endPoint;
            Id = id;
            Frequency = ProtocolConstants.DefaultFrequency;
            State = PlayerState.NotReady;
            Name = string.Empty;
            LastSeenUtc = DateTime.UtcNow;
        }

        public IPEndPoint EndPoint { get; }
        public uint Id { get; }
        public uint? RoomId { get; set; }
        public byte PlayerNumber { get; set; }
        public CarType Car { get; set; }
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public ushort Speed { get; set; }
        public int Frequency { get; set; }
        public PlayerState State { get; set; }
        public string Name { get; set; }
        public bool EngineRunning { get; set; }
        public bool Braking { get; set; }
        public bool Horning { get; set; }
        public bool Backfiring { get; set; }
        public DateTime LastSeenUtc { get; set; }

        public PacketPlayerData ToPacket()
        {
            return new PacketPlayerData
            {
                PlayerId = Id,
                PlayerNumber = PlayerNumber,
                Car = Car,
                RaceData = new PlayerRaceData
                {
                    PositionX = PositionX,
                    PositionY = PositionY,
                    Speed = Speed,
                    Frequency = Frequency
                },
                State = State,
                EngineRunning = EngineRunning,
                Braking = Braking,
                Horning = Horning,
                Backfiring = Backfiring
            };
        }
    }

    internal sealed class RaceRoom
    {
        public RaceRoom(uint id, string name, GameRoomType roomType, byte playersToStart)
        {
            Id = id;
            Name = name;
            RoomType = roomType;
            PlayersToStart = playersToStart;
            TrackName = "america";
            Laps = 3;
        }

        public uint Id { get; }
        public string Name { get; set; }
        public GameRoomType RoomType { get; set; }
        public byte PlayersToStart { get; set; }
        public uint HostId { get; set; }
        public HashSet<uint> PlayerIds { get; } = new HashSet<uint>();
        public bool RaceStarted { get; set; }
        public bool TrackSelected { get; set; }
        public TrackData? TrackData { get; set; }
        public string TrackName { get; set; }
        public byte Laps { get; set; }
        public List<byte> RaceResults { get; } = new List<byte>();
    }

    internal sealed class RaceServer : IDisposable
    {
        private const float ServerUpdateTime = 0.1f;
        private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);

        private readonly RaceServerConfig _config;
        private readonly Logger _logger;
        private readonly object _lock = new object();
        private readonly UdpServerTransport _transport;
        private readonly Dictionary<uint, PlayerConnection> _players = new Dictionary<uint, PlayerConnection>();
        private readonly Dictionary<string, uint> _endpointIndex = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<uint, RaceRoom> _rooms = new Dictionary<uint, RaceRoom>();

        private uint _nextPlayerId = 1;
        private uint _nextRoomId = 1;
        private float _lastUpdateTime;

        public RaceServer(RaceServerConfig config, Logger logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _transport = new UdpServerTransport(_logger);
            _transport.PacketReceived += OnPacketReceived;
            _transport.PeerDisconnected += OnPeerDisconnected;
        }

        public void Start()
        {
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
            }

            _transport.Stop();
            _logger.Info("Race server stopped.");
        }

        public void Update(float deltaSeconds)
        {
            lock (_lock)
            {
                _lastUpdateTime += deltaSeconds;
                if (_lastUpdateTime < ServerUpdateTime)
                    return;
                _lastUpdateTime = 0f;

                CleanupConnections();
                BroadcastPlayerData();
                CheckForBumps();
            }
        }

        private void OnPacketReceived(IPEndPoint endPoint, byte[] payload)
        {
            if (!PacketSerializer.TryReadHeader(payload, out var header))
                return;
            if (header.Version != ProtocolConstants.Version)
                return;

            lock (_lock)
            {
                var player = GetOrAddConnection(endPoint);
                if (player == null)
                    return;

                player.LastSeenUtc = DateTime.UtcNow;

                switch (header.Command)
                {
                    case Command.KeepAlive:
                        break;
                    case Command.PlayerHello:
                        if (PacketSerializer.TryReadPlayerHello(payload, out var hello))
                            HandlePlayerHello(player, hello);
                        break;
                    case Command.PlayerState:
                        if (PacketSerializer.TryReadPlayerState(payload, out var state))
                            HandlePlayerState(player, state);
                        break;
                    case Command.PlayerDataToServer:
                        if (PacketSerializer.TryReadPlayerData(payload, out var playerData))
                            HandlePlayerData(player, playerData);
                        break;
                    case Command.PlayerStarted:
                        if (PacketSerializer.TryReadPlayer(payload, out _))
                            player.State = PlayerState.Racing;
                        break;
                    case Command.PlayerFinished:
                        if (PacketSerializer.TryReadPlayer(payload, out var finished))
                            HandlePlayerFinished(player, finished);
                        break;
                    case Command.PlayerCrashed:
                        if (PacketSerializer.TryReadPlayer(payload, out var crashed))
                            HandlePlayerCrashed(player, crashed);
                        break;
                    case Command.RoomListRequest:
                        SendRoomList(player);
                        break;
                    case Command.RoomCreate:
                        if (PacketSerializer.TryReadRoomCreate(payload, out var create))
                            HandleCreateRoom(player, create);
                        break;
                    case Command.RoomJoin:
                        if (PacketSerializer.TryReadRoomJoin(payload, out var join))
                            HandleJoinRoom(player, join);
                        break;
                    case Command.RoomLeave:
                        HandleLeaveRoom(player, true);
                        break;
                    case Command.RoomSetTrack:
                        if (PacketSerializer.TryReadRoomSetTrack(payload, out var track))
                            HandleSetTrack(player, track);
                        break;
                    case Command.RoomSetLaps:
                        if (PacketSerializer.TryReadRoomSetLaps(payload, out var laps))
                            HandleSetLaps(player, laps);
                        break;
                    case Command.RoomStartRace:
                        HandleStartRace(player);
                        break;
                    case Command.RoomSetPlayersToStart:
                        if (PacketSerializer.TryReadRoomSetPlayersToStart(payload, out var setPlayers))
                            HandleSetPlayersToStart(player, setPlayers);
                        break;
                }
            }
        }

        private PlayerConnection? GetOrAddConnection(IPEndPoint endpoint)
        {
            var key = endpoint.ToString();
            if (_endpointIndex.TryGetValue(key, out var id) && _players.TryGetValue(id, out var existing))
                return existing;

            if (_players.Count >= _config.MaxPlayers)
            {
                _transport.Send(endpoint, PacketSerializer.WriteGeneral(Command.Disconnect));
                return null;
            }

            var playerId = _nextPlayerId++;
            var player = new PlayerConnection(endpoint, playerId);
            _players[playerId] = player;
            _endpointIndex[key] = playerId;

            _transport.Send(endpoint, PacketSerializer.WritePlayerNumber(playerId, 0));
            if (!string.IsNullOrWhiteSpace(_config.Motd))
                _transport.Send(endpoint, PacketSerializer.WriteServerInfo(new PacketServerInfo { Motd = _config.Motd }));

            SendRoomState(player, null);
            SendRoomList(player);
            return player;
        }

        private void HandlePlayerHello(PlayerConnection player, PacketPlayerHello hello)
        {
            var name = (hello.Name ?? string.Empty).Trim();
            if (name.Length > ProtocolConstants.MaxPlayerNameLength)
                name = name.Substring(0, ProtocolConstants.MaxPlayerNameLength);
            player.Name = name;
            if (player.RoomId.HasValue && _rooms.TryGetValue(player.RoomId.Value, out var room))
                BroadcastRoomState(room);
        }

        private void HandlePlayerState(PlayerConnection player, PacketPlayerState state)
        {
            player.State = state.State;
            if (!player.RoomId.HasValue || !_rooms.TryGetValue(player.RoomId.Value, out var room))
                return;

            if (player.State == PlayerState.NotReady && room.TrackSelected)
                SendTrack(room, player);
            BroadcastRoomState(room);
        }

        private void HandlePlayerData(PlayerConnection player, PacketPlayerData data)
        {
            if (!player.RoomId.HasValue)
                return;

            player.Car = data.Car;
            player.PositionX = data.RaceData.PositionX;
            player.PositionY = data.RaceData.PositionY;
            player.Speed = data.RaceData.Speed;
            player.Frequency = data.RaceData.Frequency;
            player.EngineRunning = data.EngineRunning;
            player.Braking = data.Braking;
            player.Horning = data.Horning;
            player.Backfiring = data.Backfiring;
            player.State = data.State;
        }

        private void HandlePlayerFinished(PlayerConnection player, PacketPlayer finished)
        {
            if (!player.RoomId.HasValue || !_rooms.TryGetValue(player.RoomId.Value, out var room))
                return;

            player.State = PlayerState.Finished;
            if (!room.RaceResults.Contains(finished.PlayerNumber))
                room.RaceResults.Add(finished.PlayerNumber);

            SendToRoomExcept(room, player.Id, PacketSerializer.WritePlayer(Command.PlayerFinished, finished.PlayerId, finished.PlayerNumber));
            if (CountRacingPlayers(room) == 0)
                StopRace(room);
        }

        private void HandlePlayerCrashed(PlayerConnection player, PacketPlayer crashed)
        {
            if (!player.RoomId.HasValue || !_rooms.TryGetValue(player.RoomId.Value, out var room))
                return;

            SendToRacingPlayersExcept(room, player.Id, PacketSerializer.WritePlayer(Command.PlayerCrashed, crashed.PlayerId, crashed.PlayerNumber));
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
            BroadcastRoomList();
            BroadcastLobbyAnnouncement($"{DescribePlayer(player)} created game room {room.Name}.");
        }

        private void HandleJoinRoom(PlayerConnection player, PacketRoomJoin packet)
        {
            if (!_rooms.TryGetValue(packet.RoomId, out var room))
            {
                SendProtocolMessage(player, ProtocolMessageCode.RoomNotFound, "Game room not found.");
                return;
            }

            if (room.PlayerIds.Count >= room.PlayersToStart)
            {
                SendProtocolMessage(player, ProtocolMessageCode.RoomFull, "This game room is unavailable because it is full.");
                return;
            }

            JoinRoom(player, room);
            SendProtocolMessage(player, ProtocolMessageCode.Ok, $"Joined {room.Name}.");
            BroadcastRoomList();
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
            room.PlayerIds.Remove(player.Id);
            player.RoomId = null;
            player.PlayerNumber = 0;
            player.State = PlayerState.NotReady;

            if (notify)
                SendToRoom(room, PacketSerializer.WritePlayer(Command.PlayerDisconnected, player.Id, oldNumber));

            SendRoomState(player, null);

            if (room.PlayerIds.Count == 0)
            {
                _rooms.Remove(room.Id);
            }
            else
            {
                if (room.HostId == player.Id)
                    room.HostId = room.PlayerIds.OrderBy(x => x).First();
                if (room.RaceStarted && CountRacingPlayers(room) == 0)
                    StopRace(room);
                BroadcastRoomState(room);
            }

            BroadcastRoomList();
        }

        private void HandleSetTrack(PlayerConnection player, PacketRoomSetTrack packet)
        {
            if (!TryGetHostedRoom(player, out var room))
                return;

            var trackName = (packet.TrackName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trackName))
            {
                SendProtocolMessage(player, ProtocolMessageCode.InvalidTrack, "Track cannot be empty.");
                return;
            }

            SetTrack(room, trackName);
            SendTrackToNotReady(room);
            BroadcastRoomState(room);
            SendProtocolMessage(player, ProtocolMessageCode.Ok, $"Track set to {room.TrackName}.");
        }

        private void HandleSetLaps(PlayerConnection player, PacketRoomSetLaps packet)
        {
            if (!TryGetHostedRoom(player, out var room))
                return;

            if (packet.Laps < 1 || packet.Laps > 16)
            {
                SendProtocolMessage(player, ProtocolMessageCode.InvalidLaps, "Laps must be between 1 and 16.");
                return;
            }

            room.Laps = packet.Laps;
            if (room.TrackSelected)
                SetTrack(room, room.TrackName);
            SendTrackToNotReady(room);
            BroadcastRoomState(room);
            SendProtocolMessage(player, ProtocolMessageCode.Ok, $"Laps set to {room.Laps}.");
        }

        private void HandleStartRace(PlayerConnection player)
        {
            if (!TryGetHostedRoom(player, out var room))
                return;

            if (room.PlayerIds.Count < room.PlayersToStart)
            {
                SendProtocolMessage(player, ProtocolMessageCode.Failed, $"Not enough players. {room.PlayersToStart} required.");
                return;
            }

            StartRace(room);
        }

        private void HandleSetPlayersToStart(PlayerConnection player, PacketRoomSetPlayersToStart packet)
        {
            if (!TryGetHostedRoom(player, out var room))
                return;

            var value = packet.PlayersToStart;
            if (value < 1 || value > ProtocolConstants.MaxRoomPlayersToStart)
            {
                SendProtocolMessage(player, ProtocolMessageCode.InvalidPlayersToStart, "Players to start must be between 1 and 10.");
                return;
            }

            if (room.PlayerIds.Count > value)
            {
                SendProtocolMessage(player, ProtocolMessageCode.InvalidPlayersToStart, "Cannot set lower than current players in room.");
                return;
            }

            room.PlayersToStart = value;
            BroadcastRoomState(room);
            BroadcastRoomList();
            SendProtocolMessage(player, ProtocolMessageCode.Ok, $"Players required to start set to {value}.");
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

            _transport.Send(player.EndPoint, PacketSerializer.WritePlayerNumber(player.Id, player.PlayerNumber));
            SendTrack(room, player);
            BroadcastRoomState(room);

            var joinedName = string.IsNullOrWhiteSpace(player.Name)
                ? $"Player {player.PlayerNumber + 1}"
                : player.Name;
            var joined = new PacketPlayerJoined { PlayerId = player.Id, PlayerNumber = player.PlayerNumber, Name = joinedName };
            SendToRoomExcept(room, player.Id, PacketSerializer.WritePlayerJoined(joined));
        }

        private int FindFreeRoomNumber(RaceRoom room)
        {
            for (var i = 0; i < room.PlayersToStart; i++)
            {
                var used = room.PlayerIds.Any(id => _players.TryGetValue(id, out var p) && p.PlayerNumber == i);
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

            if (!room.TrackSelected || room.TrackData == null)
                SetTrack(room, room.TrackName);

            room.RaceStarted = true;
            room.RaceResults.Clear();
            foreach (var id in room.PlayerIds)
            {
                if (_players.TryGetValue(id, out var p))
                    p.State = PlayerState.AwaitingStart;
            }

            SendTrackToRoom(room);
            SendToRoom(room, PacketSerializer.WriteGeneral(Command.StartRace));
            BroadcastRoomState(room);
        }

        private void StopRace(RaceRoom room)
        {
            room.RaceStarted = false;

            var results = room.RaceResults.ToArray();
            SendToRoom(room, PacketSerializer.WriteRaceResults(new PacketRaceResults
            {
                NPlayers = (byte)Math.Min(results.Length, ProtocolConstants.MaxPlayers),
                Results = results
            }));

            room.RaceResults.Clear();
            foreach (var id in room.PlayerIds)
            {
                if (_players.TryGetValue(id, out var p))
                    p.State = PlayerState.NotReady;
            }

            BroadcastRoomState(room);
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
            _transport.Send(player.EndPoint, PacketSerializer.WriteLoadCustomTrack(new PacketLoadCustomTrack
            {
                NrOfLaps = room.TrackData.Laps,
                TrackName = room.TrackData.UserDefined ? "custom" : room.TrackName,
                TrackWeather = room.TrackData.Weather,
                TrackAmbience = room.TrackData.Ambience,
                TrackLength = trackLength,
                Definitions = room.TrackData.Definitions
            }));
        }

        private void SendRoomList(PlayerConnection player)
        {
            var list = new PacketRoomList
            {
                Rooms = _rooms.Values.OrderBy(r => r.Id).Take(ProtocolConstants.MaxRoomListEntries).Select(r => new PacketRoomSummary
                {
                    RoomId = r.Id,
                    RoomName = r.Name,
                    RoomType = r.RoomType,
                    PlayerCount = (byte)r.PlayerIds.Count,
                    PlayersToStart = r.PlayersToStart,
                    RaceStarted = r.RaceStarted,
                    TrackName = r.TrackName
                }).ToArray()
            };

            _transport.Send(player.EndPoint, PacketSerializer.WriteRoomList(list));
        }

        private void BroadcastRoomList()
        {
            foreach (var player in _players.Values)
                SendRoomList(player);
        }

        private void SendRoomState(PlayerConnection player, RaceRoom? room)
        {
            if (room == null)
            {
                _transport.Send(player.EndPoint, PacketSerializer.WriteRoomState(new PacketRoomState
                {
                    InRoom = false,
                    HostPlayerId = 0,
                    RoomType = GameRoomType.BotsRace,
                    PlayersToStart = 0,
                    Players = Array.Empty<PacketRoomPlayer>()
                }));
                return;
            }

            var players = room.PlayerIds
                .Where(id => _players.ContainsKey(id))
                .Select(id => _players[id])
                .OrderBy(p => p.PlayerNumber)
                .Select(p => new PacketRoomPlayer
                {
                    PlayerId = p.Id,
                    PlayerNumber = p.PlayerNumber,
                    State = p.State,
                    Name = string.IsNullOrWhiteSpace(p.Name) ? $"Player {p.PlayerNumber + 1}" : p.Name
                }).ToArray();

            _transport.Send(player.EndPoint, PacketSerializer.WriteRoomState(new PacketRoomState
            {
                RoomId = room.Id,
                HostPlayerId = room.HostId,
                RoomName = room.Name,
                RoomType = room.RoomType,
                PlayersToStart = room.PlayersToStart,
                InRoom = true,
                IsHost = room.HostId == player.Id,
                RaceStarted = room.RaceStarted,
                TrackName = room.TrackName,
                Laps = room.Laps,
                Players = players
            }));
        }

        private void BroadcastRoomState(RaceRoom room)
        {
            foreach (var id in room.PlayerIds)
            {
                if (_players.TryGetValue(id, out var player))
                    SendRoomState(player, room);
            }
        }

        private void SendProtocolMessage(PlayerConnection player, ProtocolMessageCode code, string text)
        {
            _transport.Send(player.EndPoint, PacketSerializer.WriteProtocolMessage(new PacketProtocolMessage
            {
                Code = code,
                Message = text ?? string.Empty
            }));
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

        private void SendToRoom(RaceRoom room, byte[] payload)
        {
            foreach (var id in room.PlayerIds)
            {
                if (_players.TryGetValue(id, out var player))
                    _transport.Send(player.EndPoint, payload);
            }
        }

        private void SendToRoomExcept(RaceRoom room, uint exceptId, byte[] payload)
        {
            foreach (var id in room.PlayerIds)
            {
                if (id == exceptId)
                    continue;
                if (_players.TryGetValue(id, out var player))
                    _transport.Send(player.EndPoint, payload);
            }
        }

        private void SendToRacingPlayersExcept(RaceRoom room, uint exceptId, byte[] payload)
        {
            foreach (var id in room.PlayerIds)
            {
                if (id == exceptId)
                    continue;
                if (_players.TryGetValue(id, out var player) && player.State == PlayerState.Racing)
                    _transport.Send(player.EndPoint, payload);
            }
        }

        private int CountRacingPlayers(RaceRoom room)
        {
            return room.PlayerIds.Count(id => _players.TryGetValue(id, out var player) && player.State == PlayerState.Racing);
        }

        private void BroadcastPlayerData()
        {
            foreach (var room in _rooms.Values)
            {
                foreach (var id in room.PlayerIds)
                {
                    if (!_players.TryGetValue(id, out var player))
                        continue;
                    if (player.State == PlayerState.NotReady || player.State == PlayerState.Undefined)
                        continue;

                    SendToRacingPlayersExcept(room, player.Id, PacketSerializer.WritePlayerData(player.ToPacket()));
                }
            }
        }

        private void CheckForBumps()
        {
            foreach (var room in _rooms.Values)
            {
                var racers = room.PlayerIds.Where(id => _players.TryGetValue(id, out var p) && p.State == PlayerState.Racing)
                    .Select(id => _players[id]).ToList();

                for (var i = 0; i < racers.Count; i++)
                {
                    for (var j = 0; j < racers.Count; j++)
                    {
                        if (i == j)
                            continue;

                        var player = racers[i];
                        var other = racers[j];
                        if (Math.Abs(player.PositionX - other.PositionX) < 10.0f && Math.Abs(player.PositionY - other.PositionY) < 5.0f)
                        {
                            _transport.Send(player.EndPoint, PacketSerializer.WritePlayerBumped(new PacketPlayerBumped
                            {
                                PlayerId = player.Id,
                                PlayerNumber = player.PlayerNumber,
                                BumpX = player.PositionX - other.PositionX,
                                BumpY = player.PositionY - other.PositionY,
                                BumpSpeed = (ushort)Math.Max(0, player.Speed - other.Speed)
                            }));
                        }
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

                RemoveConnection(player, notifyRoom: true, sendDisconnectPacket: true);
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

                RemoveConnection(player, notifyRoom: true, sendDisconnectPacket: false);
            }
        }

        private void RemoveConnection(PlayerConnection player, bool notifyRoom, bool sendDisconnectPacket)
        {
            if (player.RoomId.HasValue)
                HandleLeaveRoom(player, notifyRoom);
            if (sendDisconnectPacket)
                _transport.Send(player.EndPoint, PacketSerializer.WriteGeneral(Command.Disconnect));
            _endpointIndex.Remove(player.EndPoint.ToString());
            _players.Remove(player.Id);
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
