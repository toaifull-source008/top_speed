using System;
using System.Collections.Generic;
using System.Net;
using TopSpeed.Bots;
using TopSpeed.Data;
using TopSpeed.Protocol;

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
            WidthM = 1.8f;
            LengthM = 4.5f;
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
        public bool ServerPresenceAnnounced { get; set; }
        public bool EngineRunning { get; set; }
        public bool Braking { get; set; }
        public bool Horning { get; set; }
        public bool Backfiring { get; set; }
        public bool MediaLoaded { get; set; }
        public bool MediaPlaying { get; set; }
        public uint MediaId { get; set; }
        public InMedia? IncomingMedia { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public float WidthM { get; set; }
        public float LengthM { get; set; }

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
                Backfiring = Backfiring,
                MediaLoaded = MediaLoaded,
                MediaPlaying = MediaPlaying,
                MediaId = MediaId
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
        public uint Version { get; set; }
        public string Name { get; set; }
        public GameRoomType RoomType { get; set; }
        public byte PlayersToStart { get; set; }
        public uint HostId { get; set; }
        public HashSet<uint> PlayerIds { get; } = new HashSet<uint>();
        public List<RoomBot> Bots { get; } = new List<RoomBot>();
        public Dictionary<uint, PlayerLoadout> PendingLoadouts { get; } = new Dictionary<uint, PlayerLoadout>();
        public bool PreparingRace { get; set; }
        public bool RaceStarted { get; set; }
        public bool TrackSelected { get; set; }
        public TrackData? TrackData { get; set; }
        public string TrackName { get; set; }
        public byte Laps { get; set; }
        public List<byte> RaceResults { get; } = new List<byte>();
        public HashSet<ulong> ActiveBumpPairs { get; } = new HashSet<ulong>();
        public Dictionary<uint, MediaBlob> MediaMap { get; } = new Dictionary<uint, MediaBlob>();
        public uint RaceSnapshotSequence { get; set; }
        public uint RaceSnapshotTick { get; set; }
    }

    internal sealed class MediaBlob
    {
        public uint MediaId { get; set; }
        public string Extension { get; set; } = string.Empty;
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }

    internal sealed class InMedia
    {
        public uint MediaId { get; set; }
        public string Extension { get; set; } = string.Empty;
        public uint TotalBytes { get; set; }
        public ushort NextChunk { get; set; }
        public byte[] Buffer { get; set; } = Array.Empty<byte>();
        public int Offset { get; set; }

        public bool IsComplete => Buffer.Length > 0 && Offset >= Buffer.Length;
    }

    internal readonly struct PlayerLoadout
    {
        public PlayerLoadout(CarType car, bool automaticTransmission)
        {
            Car = car;
            AutomaticTransmission = automaticTransmission;
        }

        public CarType Car { get; }
        public bool AutomaticTransmission { get; }
    }

    internal readonly struct VehicleDimensions
    {
        public VehicleDimensions(float widthM, float lengthM)
        {
            WidthM = widthM;
            LengthM = lengthM;
        }

        public float WidthM { get; }
        public float LengthM { get; }
    }

    internal readonly struct BotAudioProfile
    {
        public BotAudioProfile(int idleFrequency, int topFrequency, int shiftFrequency)
        {
            IdleFrequency = idleFrequency;
            TopFrequency = topFrequency;
            ShiftFrequency = shiftFrequency;
        }

        public int IdleFrequency { get; }
        public int TopFrequency { get; }
        public int ShiftFrequency { get; }
    }

    internal enum BotDifficulty
    {
        Easy = 0,
        Normal = 1,
        Hard = 2
    }

    internal enum BotRacePhase
    {
        Normal = 0,
        Crashing = 1,
        Restarting = 2
    }

    internal sealed class RoomBot
    {
        public uint Id { get; set; }
        public byte PlayerNumber { get; set; }
        public string Name { get; set; } = string.Empty;
        public BotDifficulty Difficulty { get; set; }
        public int AddedOrder { get; set; }
        public CarType Car { get; set; } = CarType.Vehicle1;
        public bool AutomaticTransmission { get; set; } = true;
        public PlayerState State { get; set; } = PlayerState.NotReady;
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float SpeedKph { get; set; }
        public float StartDelaySeconds { get; set; }
        public float EngineStartSecondsRemaining { get; set; }
        public float WidthM { get; set; } = 1.8f;
        public float LengthM { get; set; } = 4.5f;
        public BotPhysicsState PhysicsState { get; set; }
        public BotPhysicsConfig PhysicsConfig { get; set; } = BotPhysicsCatalog.Get(CarType.Vehicle1);
        public BotAudioProfile AudioProfile { get; set; } = new BotAudioProfile(22050, 55000, 26000);
        public int EngineFrequency { get; set; } = 22050;
        public bool Horning { get; set; }
        public float HornSecondsRemaining { get; set; }
        public bool BackfireArmed { get; set; } = true;
        public float BackfirePulseSeconds { get; set; }
        public BotRacePhase RacePhase { get; set; } = BotRacePhase.Normal;
        public float CrashRecoverySeconds { get; set; }
    }
}
