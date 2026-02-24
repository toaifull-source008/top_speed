using System;
using System.Collections.Generic;
using TopSpeed.Audio;
using TopSpeed.Common;
using TopSpeed.Data;
using TopSpeed.Input;
using TopSpeed.Network;
using TopSpeed.Protocol;
using TopSpeed.Speech;
using TopSpeed.Tracks;
using TopSpeed.Vehicles;
using TS.Audio;

namespace TopSpeed.Race
{
    internal sealed class LevelMultiplayer : Level
    {
        private const int MaxPlayers = ProtocolConstants.MaxPlayers;
        private const float SendIntervalSeconds = 1f / 60f;
        private const float StartLineY = 140.0f;
        private const float ServerTickRate = 125.0f;
        private const float SnapshotDelayTicks = 4.0f;
        private const int SnapshotBufferMax = 8;

        private sealed class RemotePlayer
        {
            public RemotePlayer(ComputerPlayer player)
            {
                Player = player;
                State = PlayerState.NotReady;
                Finished = false;
            }

            public ComputerPlayer Player { get; }
            public PlayerState State { get; set; }
            public bool Finished { get; set; }
        }

        private sealed class RemoteMediaTransfer
        {
            public uint MediaId { get; set; }
            public string Extension { get; set; } = string.Empty;
            public byte[] Data { get; set; } = Array.Empty<byte>();
            public int Offset { get; set; }
            public ushort NextChunkIndex { get; set; }

            public bool IsComplete => Data.Length > 0 && Offset >= Data.Length;
        }

        private sealed class RaceSnapshotFrame
        {
            public uint Tick { get; set; }
            public PacketPlayerData[] Players { get; set; } = Array.Empty<PacketPlayerData>();
        }

        private readonly MultiplayerSession _session;
        private readonly uint _playerId;
        private readonly byte _playerNumber;
        private readonly Dictionary<byte, RemotePlayer> _remotePlayers;
        private readonly Dictionary<byte, RemoteMediaTransfer> _remoteMediaTransfers;
        private readonly List<RaceSnapshotFrame> _snapshotFrames;
        private readonly AudioSourceHandle?[] _soundPosition;
        private readonly AudioSourceHandle?[] _soundPlayerNr;
        private readonly AudioSourceHandle?[] _soundFinished;

        private AudioSourceHandle? _soundYouAre;
        private AudioSourceHandle? _soundPlayer;
        private float _lastComment;
        private bool _infoKeyReleased;
        private int _positionFinish;
        private int _position;
        private int _positionComment;
        private bool _pauseKeyReleased = true;
        private float _sendAccumulator;
        private bool _sentStart;
        private bool _sentFinish;
        private bool _serverStopReceived;
        private PlayerState _currentState;
        private CarState _lastCarState;
        private uint _lastRaceSnapshotSequence;
        private uint _lastRaceSnapshotTick;
        private bool _hasRaceSnapshotSequence;
        private float _snapshotTickNow;
        private bool _hasSnapshotTickNow;

        public LevelMultiplayer(
            AudioManager audio,
            SpeechService speech,
            RaceSettings settings,
            RaceInput input,
            TrackData trackData,
            string trackName,
            bool automaticTransmission,
            int nrOfLaps,
            int vehicle,
            string? vehicleFile,
            IVibrationDevice? vibrationDevice,
            MultiplayerSession session,
            uint playerId,
            byte playerNumber)
            : base(audio, speech, settings, input, trackName, automaticTransmission, nrOfLaps, vehicle, vehicleFile, vibrationDevice, trackData, trackData.UserDefined)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _playerId = playerId;
            _playerNumber = playerNumber;
            _remotePlayers = new Dictionary<byte, RemotePlayer>();
            _remoteMediaTransfers = new Dictionary<byte, RemoteMediaTransfer>();
            _snapshotFrames = new List<RaceSnapshotFrame>(SnapshotBufferMax);
            _soundPosition = new AudioSourceHandle?[MaxPlayers];
            _soundPlayerNr = new AudioSourceHandle?[MaxPlayers];
            _soundFinished = new AudioSourceHandle?[MaxPlayers];
            _currentState = PlayerState.NotReady;
        }

        public void Initialize()
        {
            InitializeLevel();
            _positionFinish = 0;
            _position = _playerNumber + 1;
            _positionComment = _position;
            _lastComment = 0.0f;
            _infoKeyReleased = true;
            _sendAccumulator = 0.0f;
            _sentStart = false;
            _sentFinish = false;
            _serverStopReceived = false;
            _lastCarState = _car.State;
            _lastRaceSnapshotSequence = 0;
            _lastRaceSnapshotTick = 0;
            _hasRaceSnapshotSequence = false;
            _snapshotFrames.Clear();
            _snapshotTickNow = 0f;
            _hasSnapshotTickNow = false;

            var rowSpacing = Math.Max(10.0f, _car.LengthM * 1.5f);
            var positionX = CalculateStartX(_playerNumber, _car.WidthM);
            var positionY = CalculateStartY(_playerNumber, rowSpacing);
            _car.SetPosition(positionX, positionY);

            for (var i = 0; i < MaxPlayers; i++)
            {
                _soundPlayerNr[i] = LoadLanguageSound($"race\\info\\player{i + 1}");
                _soundPosition[i] = LoadLanguageSound($"race\\info\\youarepos{i + 1}");
                _soundFinished[i] = LoadLanguageSound($"race\\info\\finished{i + 1}");
            }

            LoadRandomSounds(RandomSound.Front, "race\\info\\front");
            LoadRandomSounds(RandomSound.Tail, "race\\info\\tail");

            _soundYouAre = LoadLanguageSound("race\\youare");
            _soundPlayer = LoadLanguageSound("race\\player");
            _soundTheme4 = LoadLanguageSound("music\\theme4", streamFromDisk: false);
            _soundPause = LoadLanguageSound("race\\pause");
            _soundUnpause = LoadLanguageSound("race\\unpause");
            _soundTheme4.SetVolumePercent((int)Math.Round(_settings.MusicVolume * 100f));

            Speak(_soundYouAre);
            Speak(_soundPlayer);
            Speak(_soundNumbers[_playerNumber + 1]);

            _currentState = PlayerState.AwaitingStart;
            _session.SendPlayerState(_currentState);
        }

        public void FinalizeLevelMultiplayer()
        {
            foreach (var remote in _remotePlayers.Values)
            {
                remote.Player.FinalizePlayer();
                remote.Player.Dispose();
            }
            _remotePlayers.Clear();
            _remoteMediaTransfers.Clear();
            _snapshotFrames.Clear();

            for (var i = 0; i < _soundPosition.Length; i++)
            {
                DisposeSound(_soundPosition[i]);
                DisposeSound(_soundPlayerNr[i]);
                DisposeSound(_soundFinished[i]);
            }

            DisposeSound(_soundYouAre);
            DisposeSound(_soundPlayer);
            FinalizeLevel();
        }

        public void Run(float elapsed)
        {
            if (_elapsedTotal == 0.0f)
            {
                var countdownLength = _soundStart.GetLengthSeconds();
                var countdownTotal = 1.5f + Math.Max(0f, countdownLength);
                var raceStartDelay = Math.Max(6.5f, countdownTotal);
                PushEvent(RaceEventType.CarStart, 3.0f);
                PushEvent(RaceEventType.RaceStart, raceStartDelay);
                PushEvent(RaceEventType.PlaySound, 1.5f, _soundStart);
            }

            var dueEvents = CollectDueEvents();
            foreach (var e in dueEvents)
            {
                switch (e.Type)
                {
                    case RaceEventType.CarStart:
                        // Manual start now
                        break;
                    case RaceEventType.RaceStart:
                        _raceTime = 0;
                        _stopwatch.Restart();
                        _lap = 0;
                        _started = true;
                        if (!_sentStart)
                        {
                            _sentStart = true;
                            _currentState = PlayerState.Racing;
                            _session.SendPlayerStarted();
                            _session.SendPlayerState(_currentState);
                        }
                        break;
                    case RaceEventType.RaceFinish:
                        PushEvent(RaceEventType.PlaySound, _sayTimeLength, _soundYourTime);
                        _sayTimeLength += _soundYourTime.GetLengthSeconds() + 0.5f;
                        SayTime(_raceTime);
                        PushEvent(RaceEventType.RaceTimeFinalize, _sayTimeLength);
                        break;
                    case RaceEventType.PlaySound:
                        QueueSound(e.Sound);
                        break;
                    case RaceEventType.RaceTimeFinalize:
                        _sayTimeLength = 0.0f;
                        break;
                    case RaceEventType.PlayRadioSound:
                        _unkeyQueue--;
                        if (_unkeyQueue == 0)
                            Speak(_soundUnkey[Algorithm.RandomInt(MaxUnkeys)]);
                        break;
                    case RaceEventType.AcceptPlayerInfo:
                        _acceptPlayerInfo = true;
                        break;
                    case RaceEventType.AcceptCurrentRaceInfo:
                        _acceptCurrentRaceInfo = true;
                        break;
                }
            }

            ApplyBufferedRaceSnapshots(elapsed);
            UpdatePositions();
            UpdateVehiclePanels(elapsed);
            _car.Run(elapsed);
            _track.Run(_car.PositionY);
            var spatialTrackLength = GetSpatialTrackLength();
            foreach (var remote in _remotePlayers.Values)
                remote.Player.UpdateRemoteAudio(_car.PositionX, _car.PositionY, spatialTrackLength, elapsed);

            if (_started
                && !_sentFinish
                && _lastCarState != CarState.Crashing
                && _lastCarState != CarState.Crashed
                && (_car.State == CarState.Crashing || _car.State == CarState.Crashed))
            {
                _session.SendPlayerCrashed();
            }
            _lastCarState = _car.State;

            var road = _track.RoadAtPosition(_car.PositionY);
            _car.Evaluate(road);
            UpdateAudioListener(elapsed);
            if (_track.NextRoad(_car.PositionY, _car.Speed, (int)_settings.CurveAnnouncement, out var nextRoad))
                CallNextRoad(nextRoad);

            if (_track.Lap(_car.PositionY) > _lap)
            {
                _lap = _track.Lap(_car.PositionY);
                if (_lap > _nrOfLaps)
                {
                    var finishSound = _randomSounds[(int)RandomSound.Finish][Algorithm.RandomInt(_totalRandomSounds[(int)RandomSound.Finish])];
                    if (finishSound != null)
                        Speak(finishSound, true);
                    _car.ManualTransmission = false;
                    _car.Quiet();
                    _car.Stop();
                    _raceTime = (int)(_stopwatch.ElapsedMilliseconds - _stopwatchDiffMs);
                    SpeakIfAvailable(_soundPlayerNr[_playerNumber], true);
                    SpeakIfAvailable(_soundFinished[Math.Min(_positionFinish++, _soundFinished.Length - 1)], true);
                    if (!_sentFinish)
                    {
                        _sentFinish = true;
                        _currentState = PlayerState.Finished;
                        _session.SendPlayerFinished();
                        _session.SendPlayerState(_currentState);
                    }
                    PushEvent(RaceEventType.RaceFinish, 1.0f + _speakTime - _elapsedTotal);
                }
                else if (_settings.AutomaticInfo != AutomaticInfoMode.Off && _lap > 1 && _lap <= _nrOfLaps)
                {
                    Speak(_soundLaps[_nrOfLaps - _lap], true);
                }
            }

            // Allow starting engine initially or restarting after crash
            HandleEngineStartRequest();

            HandleCurrentGearRequest();
            HandleCurrentLapNumberRequest();
            HandleCurrentRacePercentageRequest();
            HandleCurrentLapPercentageRequest();
            HandleCurrentRaceTimeRequestWithFinish();

            _lastComment += elapsed;
            if (_settings.AutomaticInfo == AutomaticInfoMode.On && _lastComment > 6.0f)
            {
                Comment(automatic: true);
                _lastComment = 0.0f;
            }

            if (_input.GetRequestInfo() && _infoKeyReleased)
            {
                if (_lastComment > 2.0f)
                {
                    _infoKeyReleased = false;
                    Comment(automatic: false);
                    _lastComment = 0.0f;
                }
            }
            else if (!_input.GetRequestInfo() && !_infoKeyReleased)
            {
                _infoKeyReleased = true;
            }

            if (_input.TryGetPlayerInfo(out var infoPlayer)
                && _acceptPlayerInfo
                && infoPlayer >= 0
                && infoPlayer < MaxPlayers
                && HasPlayerInRace(infoPlayer))
            {
                _acceptPlayerInfo = false;
                SpeakText(GetVehicleNameForPlayer(infoPlayer));
                PushEvent(RaceEventType.AcceptPlayerInfo, 0.5f);
            }

            if (_input.TryGetPlayerPosition(out var positionPlayer)
                && _acceptPlayerInfo
                && _started
                && positionPlayer >= 0
                && positionPlayer < MaxPlayers
                && HasPlayerInRace(positionPlayer))
            {
                _acceptPlayerInfo = false;
                var perc = CalculatePlayerPerc(positionPlayer);
                SpeakText(FormatPercentageText(string.Empty, perc));
                PushEvent(RaceEventType.AcceptPlayerInfo, 0.5f);
            }

            HandleTrackNameRequest();

            if (_input.GetPlayerNumber() && _acceptCurrentRaceInfo)
            {
                _acceptCurrentRaceInfo = false;
                QueueSound(_soundNumbers[_playerNumber + 1]);
                PushEvent(RaceEventType.AcceptCurrentRaceInfo, _soundNumbers[_playerNumber + 1].GetLengthSeconds());
            }

            HandleSpeedReportRequest();
            HandleDistanceReportRequest();
            HandlePauseRequest(ref _pauseKeyReleased);

            _sendAccumulator += elapsed;
            if (_sendAccumulator >= SendIntervalSeconds)
            {
                _sendAccumulator = 0.0f;
                var state = _currentState;
                if (_sentFinish)
                    state = PlayerState.Finished;
                else if (_started)
                    state = PlayerState.Racing;
                var raceData = new PlayerRaceData
                {
                    PositionX = _car.PositionX,
                    PositionY = _car.PositionY,
                    Speed = (ushort)_car.Speed,
                    Frequency = _car.Frequency
                };
                _session.SendPlayerData(
                    raceData,
                    _car.CarType,
                    state,
                    _car.EngineRunning,
                    _car.Braking,
                    _car.Horning,
                    _car.Backfiring(),
                    LocalMediaLoaded,
                    LocalMediaPlaying,
                    LocalMediaId);
            }

            if (UpdateExitWhenQueueIdle())
                return;

            _elapsedTotal += elapsed;
        }

        public void Pause()
        {
            _soundTheme4?.SetVolumePercent((int)Math.Round(_settings.MusicVolume * 100f));
            _soundTheme4?.Play(loop: true);
            FadeIn();
            PauseVehiclePanels();
            _car.Pause();
            foreach (var remote in _remotePlayers.Values)
                remote.Player.Pause();
            _soundPause?.Play(loop: false);
        }

        public void Unpause()
        {
            _car.Unpause();
            ResumeVehiclePanels();
            foreach (var remote in _remotePlayers.Values)
                remote.Player.Unpause();
            FadeOut();
            _soundTheme4?.Stop();
            _soundTheme4?.SeekToStart();
            _soundUnpause?.Play(loop: false);
        }

        protected override void OnLocalRadioMediaLoaded(uint mediaId, string mediaPath)
        {
            if (!_session.SendRadioMediaStreamed(mediaId, mediaPath))
                SpeakText("Failed to transmit radio media.");
        }

        public void ApplyRemoteData(PacketPlayerData data)
        {
            ApplyRemoteDataCore(
                data.PlayerNumber,
                data.Car,
                data.State,
                data.RaceData.PositionX,
                data.RaceData.PositionY,
                data.RaceData.Speed,
                data.RaceData.Frequency,
                data.EngineRunning,
                data.Braking,
                data.Horning,
                data.Backfiring,
                data.MediaLoaded,
                data.MediaPlaying,
                data.MediaId);
        }

        public void ApplyRaceSnapshot(PacketRaceSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            if (_hasRaceSnapshotSequence && snapshot.Sequence <= _lastRaceSnapshotSequence)
                return;

            _lastRaceSnapshotSequence = snapshot.Sequence;
            _lastRaceSnapshotTick = snapshot.Tick;
            _hasRaceSnapshotSequence = true;
            EnqueueRaceSnapshot(snapshot);
            ApplyBufferedRaceSnapshots(0f);
        }

        public void ApplyRemoteMediaBegin(PacketPlayerMediaBegin media)
        {
            if (media.PlayerNumber == _playerNumber)
                return;
            if (media.TotalBytes == 0 || media.TotalBytes > ProtocolConstants.MaxMediaBytes)
                return;

            _remoteMediaTransfers[media.PlayerNumber] = new RemoteMediaTransfer
            {
                MediaId = media.MediaId,
                Extension = media.FileExtension,
                Data = new byte[media.TotalBytes],
                Offset = 0,
                NextChunkIndex = 0
            };
        }

        public void ApplyRemoteMediaChunk(PacketPlayerMediaChunk media)
        {
            if (media.PlayerNumber == _playerNumber)
                return;
            if (!_remoteMediaTransfers.TryGetValue(media.PlayerNumber, out var transfer))
                return;
            if (transfer.MediaId != media.MediaId)
                return;
            if (transfer.NextChunkIndex != media.ChunkIndex)
                return;
            if (media.Data == null || media.Data.Length == 0)
                return;

            var remaining = transfer.Data.Length - transfer.Offset;
            if (media.Data.Length > remaining)
            {
                _remoteMediaTransfers.Remove(media.PlayerNumber);
                return;
            }

            Buffer.BlockCopy(media.Data, 0, transfer.Data, transfer.Offset, media.Data.Length);
            transfer.Offset += media.Data.Length;
            transfer.NextChunkIndex++;
        }

        public void ApplyRemoteMediaEnd(PacketPlayerMediaEnd media)
        {
            if (media.PlayerNumber == _playerNumber)
                return;
            if (!_remoteMediaTransfers.TryGetValue(media.PlayerNumber, out var transfer))
                return;
            if (transfer.MediaId != media.MediaId)
                return;
            if (!transfer.IsComplete)
                return;
            if (!_remotePlayers.TryGetValue(media.PlayerNumber, out var remote))
                return;

            remote.Player.ApplyRadioMedia(transfer.MediaId, transfer.Extension, transfer.Data);
            _remoteMediaTransfers.Remove(media.PlayerNumber);
        }

        public void ApplyBump(PacketPlayerBumped bump)
        {
            if (bump.PlayerNumber != _playerNumber)
                return;
            _car.Bump(bump.BumpX, bump.BumpY, bump.BumpSpeed);
        }

        public void ApplyRemoteCrash(PacketPlayer crashed)
        {
            if (crashed.PlayerNumber == _playerNumber)
                return;
            if (_remotePlayers.TryGetValue(crashed.PlayerNumber, out var remote))
                remote.Player.Crash(remote.Player.PositionX, scheduleRestart: false);
        }

        public void ApplyRemoteFinish(PacketPlayer finished)
        {
            if (finished.PlayerNumber == _playerNumber)
                return;
            if (!_remotePlayers.TryGetValue(finished.PlayerNumber, out var remote))
                return;
            if (remote.Finished)
                return;

            remote.Finished = true;
            remote.State = PlayerState.Finished;
            remote.Player.Stop();

            if ((int)finished.PlayerNumber < _soundPlayerNr.Length)
            {
                SpeakIfAvailable(_soundPlayerNr[finished.PlayerNumber], true);
                SpeakIfAvailable(_soundFinished[Math.Min(_positionFinish++, _soundFinished.Length - 1)], true);
            }
        }

        public void RemoveRemotePlayer(byte playerNumber)
        {
            _remoteMediaTransfers.Remove(playerNumber);
            if (_remotePlayers.TryGetValue(playerNumber, out var remote))
            {
                remote.Player.FinalizePlayer();
                remote.Player.Dispose();
                _remotePlayers.Remove(playerNumber);
            }
        }

        public void HandleServerRaceStopped(PacketRaceResults _)
        {
            if (_serverStopReceived)
                return;

            _serverStopReceived = true;
            _snapshotFrames.Clear();
            _hasSnapshotTickNow = false;
            if (!_sentFinish)
            {
                _sentFinish = true;
                _currentState = PlayerState.Finished;
                _session.SendPlayerState(_currentState);
            }

            RequestExitWhenQueueIdle();
        }

        private RemotePlayer GetOrCreateRemotePlayer(byte playerNumber, CarType car, float positionX, float positionY)
        {
            if (_remotePlayers.TryGetValue(playerNumber, out var existing))
                return existing;

            var vehicleIndex = car == CarType.CustomVehicle ? 0 : (int)car;
            var bot = new ComputerPlayer(_audio, _track, _settings, vehicleIndex, playerNumber, () => _elapsedTotal, () => _started);
            bot.Initialize(positionX, positionY, GetSpatialTrackLength());
            var remote = new RemotePlayer(bot);
            _remotePlayers[playerNumber] = remote;
            return remote;
        }

        private void TryApplyPendingRemoteMedia(byte playerNumber, RemotePlayer remote)
        {
            if (!_remoteMediaTransfers.TryGetValue(playerNumber, out var transfer))
                return;
            if (!transfer.IsComplete)
                return;

            remote.Player.ApplyRadioMedia(transfer.MediaId, transfer.Extension, transfer.Data);
            _remoteMediaTransfers.Remove(playerNumber);
        }

        private void ApplyRemoteDataCore(
            byte playerNumber,
            CarType car,
            PlayerState state,
            float positionX,
            float positionY,
            ushort speed,
            int frequency,
            bool engineRunning,
            bool braking,
            bool horning,
            bool backfiring,
            bool mediaLoaded,
            bool mediaPlaying,
            uint mediaId)
        {
            if (playerNumber == _playerNumber)
                return;

            var remote = GetOrCreateRemotePlayer(playerNumber, car, positionX, positionY);
            remote.State = state;
            if (state == PlayerState.Finished && !remote.Finished)
            {
                remote.Finished = true;
                remote.Player.Stop();
                if ((int)playerNumber < _soundPlayerNr.Length)
                {
                    SpeakIfAvailable(_soundPlayerNr[playerNumber], true);
                    SpeakIfAvailable(_soundFinished[Math.Min(_positionFinish++, _soundFinished.Length - 1)], true);
                }
            }

            remote.Player.ApplyNetworkState(
                positionX,
                positionY,
                speed,
                frequency,
                engineRunning,
                braking,
                horning,
                backfiring,
                mediaLoaded,
                mediaPlaying,
                mediaId,
                _car.PositionX,
                _car.PositionY,
                GetSpatialTrackLength());
            TryApplyPendingRemoteMedia(playerNumber, remote);
        }

        private void EnqueueRaceSnapshot(PacketRaceSnapshot snapshot)
        {
            var frame = new RaceSnapshotFrame
            {
                Tick = snapshot.Tick,
                Players = ClonePlayers(snapshot.Players)
            };

            if (_snapshotFrames.Count > 0)
            {
                var last = _snapshotFrames[_snapshotFrames.Count - 1];
                if (frame.Tick < last.Tick)
                    return;
                if (frame.Tick == last.Tick)
                {
                    _snapshotFrames[_snapshotFrames.Count - 1] = frame;
                }
                else
                {
                    _snapshotFrames.Add(frame);
                }
            }
            else
            {
                _snapshotFrames.Add(frame);
            }

            while (_snapshotFrames.Count > SnapshotBufferMax)
                _snapshotFrames.RemoveAt(0);

            if (!_hasSnapshotTickNow)
            {
                _snapshotTickNow = frame.Tick;
                _hasSnapshotTickNow = true;
            }
            else if (frame.Tick > _snapshotTickNow)
            {
                _snapshotTickNow = frame.Tick;
            }
        }

        private static PacketPlayerData[] ClonePlayers(PacketPlayerData[]? source)
        {
            if (source == null || source.Length == 0)
                return Array.Empty<PacketPlayerData>();

            var result = new PacketPlayerData[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                var item = source[i];
                if (item == null)
                    continue;

                result[i] = new PacketPlayerData
                {
                    PlayerId = item.PlayerId,
                    PlayerNumber = item.PlayerNumber,
                    Car = item.Car,
                    RaceData = item.RaceData,
                    State = item.State,
                    EngineRunning = item.EngineRunning,
                    Braking = item.Braking,
                    Horning = item.Horning,
                    Backfiring = item.Backfiring,
                    MediaLoaded = item.MediaLoaded,
                    MediaPlaying = item.MediaPlaying,
                    MediaId = item.MediaId
                };
            }

            return result;
        }

        private void ApplyBufferedRaceSnapshots(float elapsed)
        {
            if (_snapshotFrames.Count == 0)
                return;

            if (!_hasSnapshotTickNow)
            {
                _snapshotTickNow = _snapshotFrames[_snapshotFrames.Count - 1].Tick;
                _hasSnapshotTickNow = true;
            }

            if (elapsed > 0f)
                _snapshotTickNow += elapsed * ServerTickRate;

            var latestTick = (float)_snapshotFrames[_snapshotFrames.Count - 1].Tick;
            var maxTickNow = latestTick + SnapshotDelayTicks;
            if (_snapshotTickNow > maxTickNow)
                _snapshotTickNow = maxTickNow;

            var renderTick = _snapshotTickNow - SnapshotDelayTicks;
            if (renderTick < 0f)
                renderTick = 0f;

            while (_snapshotFrames.Count > 2 && renderTick >= _snapshotFrames[1].Tick)
                _snapshotFrames.RemoveAt(0);

            if (_snapshotFrames.Count == 1)
            {
                ApplySnapshotFrame(_snapshotFrames[0]);
                return;
            }

            var from = _snapshotFrames[0];
            var to = _snapshotFrames[1];
            if (renderTick <= from.Tick)
            {
                ApplySnapshotFrame(from);
                return;
            }

            if (renderTick >= to.Tick)
            {
                ApplySnapshotFrame(to);
                return;
            }

            var span = (float)(to.Tick - from.Tick);
            if (span <= 0f)
            {
                ApplySnapshotFrame(to);
                return;
            }

            var alpha = (renderTick - from.Tick) / span;
            if (alpha < 0f)
                alpha = 0f;
            else if (alpha > 1f)
                alpha = 1f;

            ApplyInterpolatedSnapshotFrame(from, to, alpha);
        }

        private void ApplySnapshotFrame(RaceSnapshotFrame frame)
        {
            var players = frame.Players ?? Array.Empty<PacketPlayerData>();
            for (var i = 0; i < players.Length; i++)
            {
                var data = players[i];
                if (data == null)
                    continue;
                ApplyRemoteData(data);
            }
        }

        private void ApplyInterpolatedSnapshotFrame(RaceSnapshotFrame from, RaceSnapshotFrame to, float alpha)
        {
            var players = to.Players ?? Array.Empty<PacketPlayerData>();
            for (var i = 0; i < players.Length; i++)
            {
                var target = players[i];
                if (target == null)
                    continue;

                if (!TryGetPlayerFrameData(from, target.PlayerNumber, out var source) || source == null)
                {
                    ApplyRemoteData(target);
                    continue;
                }

                var posX = Lerp(source.RaceData.PositionX, target.RaceData.PositionX, alpha);
                var posY = Lerp(source.RaceData.PositionY, target.RaceData.PositionY, alpha);
                var speed = (ushort)Math.Max(0, Math.Min(ushort.MaxValue, (int)Math.Round(Lerp(source.RaceData.Speed, target.RaceData.Speed, alpha))));
                var freq = (int)Math.Round(Lerp(source.RaceData.Frequency, target.RaceData.Frequency, alpha));

                ApplyRemoteDataCore(
                    target.PlayerNumber,
                    target.Car,
                    target.State,
                    posX,
                    posY,
                    speed,
                    freq,
                    target.EngineRunning,
                    target.Braking,
                    target.Horning,
                    target.Backfiring,
                    target.MediaLoaded,
                    target.MediaPlaying,
                    target.MediaId);
            }
        }

        private static bool TryGetPlayerFrameData(RaceSnapshotFrame frame, byte playerNumber, out PacketPlayerData? data)
        {
            var players = frame.Players ?? Array.Empty<PacketPlayerData>();
            for (var i = 0; i < players.Length; i++)
            {
                var item = players[i];
                if (item == null)
                    continue;
                if (item.PlayerNumber != playerNumber)
                    continue;
                data = item;
                return true;
            }

            data = null;
            return false;
        }

        private static float Lerp(float a, float b, float alpha)
        {
            return a + ((b - a) * alpha);
        }

        private void UpdatePositions()
        {
            _position = 1;
            foreach (var remote in _remotePlayers.Values)
            {
                if (remote.Player.PositionY > _car.PositionY)
                    _position++;
            }
        }

        private float CalculateStartX(int gridIndex, float vehicleWidth)
        {
            var halfWidth = Math.Max(0.1f, vehicleWidth * 0.5f);
            var margin = 0.3f;
            var laneHalfWidth = _track.LaneHalfWidthAtPosition(StartLineY);
            var laneOffset = laneHalfWidth - halfWidth - margin;
            if (laneOffset < 0f)
                laneOffset = 0f;
            return gridIndex % 2 == 1 ? laneOffset : -laneOffset;
        }

        private float CalculateStartY(int gridIndex, float rowSpacing)
        {
            var row = gridIndex / 2;
            return StartLineY - (row * rowSpacing);
        }

        private void Comment(bool automatic)
        {
            if (!_started || _lap > _nrOfLaps)
                return;

            var position = 1;
            int inFrontNumber = -1;
            var inFrontDist = 500.0f;
            int onTailNumber = -1;
            var onTailDist = 500.0f;

            foreach (var remote in _remotePlayers.Values)
            {
                var bot = remote.Player;
                if (bot.PositionY > _car.PositionY)
                {
                    position++;
                }

                var delta = GetRelativeRaceDelta(bot.PositionY);
                if (delta > 0f)
                {
                    var dist = delta;
                    if (dist < inFrontDist)
                    {
                        inFrontNumber = bot.PlayerNumber;
                        inFrontDist = dist;
                    }
                }
                else if (delta < 0f)
                {
                    var dist = -delta;
                    if (dist < onTailDist)
                    {
                        onTailNumber = bot.PlayerNumber;
                        onTailDist = dist;
                    }
                }
            }

            if (automatic && position != _positionComment)
            {
                if (position - 1 < _soundPosition.Length)
                    SpeakIfAvailable(_soundPosition[position - 1], true);
                _positionComment = position;
                return;
            }

            if (inFrontDist < onTailDist)
            {
                if (inFrontNumber != -1 && inFrontNumber < _soundPlayerNr.Length)
                {
                    SpeakIfAvailable(_soundPlayerNr[inFrontNumber], true);
                    var sound = _randomSounds[(int)RandomSound.Front][Algorithm.RandomInt(_totalRandomSounds[(int)RandomSound.Front])];
                    if (sound != null)
                        Speak(sound, true);
                    return;
                }
            }
            else
            {
                if (onTailNumber != -1 && onTailNumber < _soundPlayerNr.Length)
                {
                    SpeakIfAvailable(_soundPlayerNr[onTailNumber], true);
                    var sound = _randomSounds[(int)RandomSound.Tail][Algorithm.RandomInt(_totalRandomSounds[(int)RandomSound.Tail])];
                    if (sound != null)
                        Speak(sound, true);
                    return;
                }
            }

            if (inFrontNumber == -1 && onTailNumber == -1 && !automatic)
            {
                if (position - 1 < _soundPosition.Length)
                    SpeakIfAvailable(_soundPosition[position - 1], true);
                _positionComment = position;
            }
        }

        private void SpeakIfAvailable(AudioSourceHandle? sound, bool queue = false)
        {
            if (sound == null)
                return;
            Speak(sound, queue);
        }

        private int CalculatePlayerPerc(int player)
        {
            if (player == _playerNumber)
                return ClampPercent(_car.PositionY);

            var targetNumber = (byte)player;
            if (_remotePlayers.TryGetValue(targetNumber, out var remote))
                return ClampPercent(remote.Player.PositionY);

            return 0;
        }

        private int ClampPercent(float positionY)
        {
            var perc = (int)((positionY / (float)(_track.Length * _nrOfLaps)) * 100.0f);
            if (perc > 100)
                perc = 100;
            if (perc < 0)
                perc = 0;
            return perc;
        }

        private float GetRaceDistance()
        {
            if (_track.Length <= 0f)
                return 0f;

            var laps = _nrOfLaps > 0 ? _nrOfLaps : 1;
            return _track.Length * laps;
        }

        private float GetSpatialTrackLength()
        {
            // Multiplayer race positions are linear across all laps. Double race length prevents wrap inversion.
            var raceDistance = GetRaceDistance();
            if (raceDistance <= 0f)
                return _track.Length;
            return raceDistance * 2f;
        }

        private float GetRelativeRaceDelta(float otherPositionY)
        {
            return otherPositionY - _car.PositionY;
        }

        private string GetVehicleNameForPlayer(int playerIndex)
        {
            if (playerIndex == _playerNumber)
            {
                if (_car.UserDefined && !string.IsNullOrWhiteSpace(_car.CustomFile))
                    return FormatVehicleName(_car.CustomFile);
                return _car.VehicleName;
            }

            var targetNumber = (byte)playerIndex;
            if (_remotePlayers.TryGetValue(targetNumber, out var remote))
                return VehicleCatalog.Vehicles[remote.Player.VehicleIndex].Name;

            return "Vehicle";
        }

        private bool HasPlayerInRace(int playerIndex)
        {
            if (playerIndex == _playerNumber)
                return true;

            if (playerIndex < 0 || playerIndex >= MaxPlayers)
                return false;

            return _remotePlayers.ContainsKey((byte)playerIndex);
        }
    }
}
