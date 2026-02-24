namespace TopSpeed.Protocol
{
    public enum CarType : byte
    {
        Vehicle1 = 0,
        Vehicle2 = 1,
        Vehicle3 = 2,
        Vehicle4 = 3,
        Vehicle5 = 4,
        Vehicle6 = 5,
        Vehicle7 = 6,
        Vehicle8 = 7,
        Vehicle9 = 8,
        Vehicle10 = 9,
        Vehicle11 = 10,
        Vehicle12 = 11,
        CustomVehicle = 12
    }

    public enum PlayerState : byte
    {
        Undefined = 0,
        NotReady = 1,
        AwaitingStart = 2,
        Racing = 3,
        Finished = 4
    }

    public enum VehicleAction : byte
    {
        Engine = 0,
        Start = 1,
        Horn = 2,
        Throttle = 3,
        Crash = 4,
        CrashMono = 5,
        Brake = 6,
        Backfire = 7
    }

    public enum Command : byte
    {
        Disconnect = 0,
        PlayerNumber = 1,
        PlayerData = 2,
        PlayerState = 3,
        StartRace = 4,
        StopRace = 5,
        RaceAborted = 6,
        PlayerDataToServer = 7,
        PlayerFinished = 8,
        PlayerFinalize = 9,
        PlayerStarted = 10,
        PlayerCrashed = 11,
        PlayerBumped = 12,
        PlayerDisconnected = 13,
        LoadCustomTrack = 14,
        PlayerHello = 15,
        ServerInfo = 16,
        KeepAlive = 17,
        PlayerJoined = 18,
        RoomListRequest = 19,
        RoomList = 20,
        RoomCreate = 21,
        RoomJoin = 22,
        RoomLeave = 23,
        RoomState = 24,
        RoomSetTrack = 25,
        RoomSetLaps = 26,
        RoomStartRace = 27,
        ProtocolMessage = 28,
        RoomSetPlayersToStart = 29,
        RoomAddBot = 30,
        RoomRemoveBot = 31,
        RoomPrepareRace = 32,
        RoomPlayerReady = 33,
        PlayerMediaBegin = 34,
        PlayerMediaChunk = 35,
        PlayerMediaEnd = 36,
        RaceSnapshot = 37,
        RoomStateRequest = 38,
        RoomEvent = 39,
        RoomGetRequest = 40,
        RoomGet = 41
    }

    public enum ProtocolMessageCode : byte
    {
        None = 0,
        Ok = 1,
        Failed = 2,
        NotHost = 3,
        RoomFull = 4,
        RoomNotFound = 5,
        InvalidTrack = 6,
        InvalidLaps = 7,
        NotInRoom = 8,
        InvalidPlayersToStart = 9,
        ServerPlayerConnected = 10,
        ServerPlayerDisconnected = 11
    }

    public enum GameRoomType : byte
    {
        BotsRace = 0,
        OneOnOne = 1,
        PlayersRace = 2
    }

    public enum RoomEventKind : byte
    {
        None = 0,
        RoomCreated = 1,
        RoomRemoved = 2,
        RoomSummaryUpdated = 3,
        HostChanged = 4,
        TrackChanged = 5,
        LapsChanged = 6,
        PlayersToStartChanged = 7,
        ParticipantJoined = 8,
        ParticipantLeft = 9,
        ParticipantStateChanged = 10,
        BotAdded = 11,
        BotRemoved = 12,
        PrepareStarted = 13,
        PrepareCancelled = 14,
        RaceStarted = 15,
        RaceStopped = 16
    }
}
