using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using TopSpeed.Audio;
using TopSpeed.Bots;
using TopSpeed.Common;
using TopSpeed.Core;
using TopSpeed.Data;
using TopSpeed.Input;
using TopSpeed.Protocol;
using TopSpeed.Tracks;
using TS.Audio;

namespace TopSpeed.Vehicles
{
    internal sealed class Car : IDisposable
    {
        private const int MaxSurfaceFreq = 100000;
        private const float BaseLateralSpeed = 7.0f;
        private const float StabilitySpeedRef = 45.0f;
        private const float CrashVibrationSeconds = 1.5f;
        private const float BumpVibrationSeconds = 0.2f;
        private const int ReverseGear = 0;
        private const int FirstForwardGear = 1;
        private const float ReverseShiftMaxSpeedKmh = 15.0f;
        private static bool s_stickReleased = true;

        private readonly AudioManager _audio;
        private readonly Track _track;
        private readonly RaceInput _input;
        private readonly RaceSettings _settings;
        private readonly Func<float> _currentTime;
        private readonly Func<bool> _started;
        private readonly string _legacyRoot;
        private readonly string _effectsRoot;
        private readonly List<CarEvent> _events;

        private CarState _state;
        private TrackSurface _surface;
        private int _gear;
        private bool _isComputerControlled;
        private int _computerRandom;
        private float _speed;
        private float _positionX;
        private float _positionY;
        private bool _manualTransmission;
        private bool _backfirePlayed;
        private bool _backfirePlayedAuto;
        private int _hasWipers;
        private int _switchingGear;
        private float _autoShiftCooldown;
        private CarType _carType;
        private ICarListener? _listener;
        private string? _customFile;
        private bool _userDefined;

        private float _surfaceTractionFactor;
        private float _deceleration;
        private float _topSpeed;
        private float _massKg;
        private float _drivetrainEfficiency;
        private float _engineBrakingTorqueNm;
        private float _tireGripCoefficient;
        private float _brakeStrength;
        private float _wheelRadiusM;
        private float _engineBraking;
        private float _idleRpm;
        private float _revLimiter;
        private float _finalDriveRatio;
        private float _reverseMaxSpeedKph;
        private float _reversePowerFactor;
        private float _reverseGearRatio;
        private float _powerFactor;
        private float _peakTorqueNm;
        private float _peakTorqueRpm;
        private float _idleTorqueNm;
        private float _redlineTorqueNm;
        private float _dragCoefficient;
        private float _frontalAreaM2;
        private float _rollingResistanceCoefficient;
        private float _launchRpm;
        private float _lastDriveRpm;
        private float _lateralGripCoefficient;
        private float _highSpeedStability;
        private float _wheelbaseM;
        private float _maxSteerDeg;
        private float _widthM;
        private float _lengthM;
        private int _idleFreq;
        private int _topFreq;
        private int _shiftFreq;
        private int _gears;
        private float _steering;
        private float _thrust;
        private int _prevFrequency;
        private int _frequency;
        private int _prevBrakeFrequency;
        private int _brakeFrequency;
        private int _prevSurfaceFrequency;
        private int _surfaceFrequency;
        private float _laneWidth;
        private float _relPos;
        private int _panPos;
        private int _currentSteering;
        private int _currentThrottle;
        private int _currentBrake;
        private float _currentSurfaceTractionFactor;
        private float _currentDeceleration;
        private float _speedDiff;
        private int _factor1;
        private int _frame;
        private float _prevThrottleVolume;
        private float _throttleVolume;
        private float _lastAudioX;
        private float _lastAudioY;
        private bool _audioInitialized;
        private float _lastAudioElapsed;

        private AudioSourceHandle _soundEngine;
        private AudioSourceHandle? _soundThrottle;
        private AudioSourceHandle _soundHorn;
        private AudioSourceHandle _soundStart;
        private AudioSourceHandle _soundBrake;
        private AudioSourceHandle _soundCrash;
        private AudioSourceHandle[] _soundCrashVariants = Array.Empty<AudioSourceHandle>();
        private AudioSourceHandle _soundMiniCrash;
        private AudioSourceHandle _soundAsphalt;
        private AudioSourceHandle _soundGravel;
        private AudioSourceHandle _soundWater;
        private AudioSourceHandle _soundSand;
        private AudioSourceHandle _soundSnow;
        private AudioSourceHandle? _soundWipers;
        private AudioSourceHandle _soundBump;
        private AudioSourceHandle _soundBadSwitch;
        private AudioSourceHandle? _soundBackfire;
        private AudioSourceHandle[] _soundBackfireVariants = Array.Empty<AudioSourceHandle>();
        private int _lastPlayerEngineVolumePercent = -1;
        private int _lastPlayerEventsVolumePercent = -1;
        private int _lastSurfaceLoopVolumePercent = -1;

        private readonly IVibrationDevice? _vibration;

        private EngineModel _engine;
        private TransmissionPolicy _transmissionPolicy;

        public Car(
            AudioManager audio,
            Track track,
            RaceInput input,
            RaceSettings settings,
            int vehicleIndex,
            string? vehicleFile,
            Func<float> currentTime,
            Func<bool> started,
            IVibrationDevice? vibrationDevice = null)
        {
            _audio = audio;
            _track = track;
            _input = input;
            _settings = settings;
            _currentTime = currentTime;
            _started = started;
            _legacyRoot = Path.Combine(AssetPaths.SoundsRoot, "Legacy");
            _effectsRoot = Path.Combine(AssetPaths.Root, "Effects");
            _events = new List<CarEvent>();

            _surface = track.InitialSurface;
            _gear = 1;
            _state = CarState.Stopped;
            _manualTransmission = false;
            _hasWipers = 0;
            _switchingGear = 0;
            _speed = 0;
            _frame = 1;
            _computerRandom = Algorithm.RandomInt(100);
            _throttleVolume = 0.0f;
            _prevThrottleVolume = 0.0f;
            _prevFrequency = 0;
            _prevBrakeFrequency = 0;
            _brakeFrequency = 0;
            _prevSurfaceFrequency = 0;
            _surfaceFrequency = 0;
            _laneWidth = track.LaneWidth * 2;
            _relPos = 0f;
            _panPos = 0;
            _currentSteering = 0;
            _currentThrottle = 0;
            _currentBrake = 0;
            _currentSurfaceTractionFactor = 0;
            _currentDeceleration = 0;
            _speedDiff = 0;
            _factor1 = 100;

            VehicleDefinition definition;
            if (string.IsNullOrWhiteSpace(vehicleFile))
            {
                definition = VehicleLoader.LoadOfficial(vehicleIndex, track.Weather);
                _carType = definition.CarType;
            }
            else
            {
                definition = VehicleLoader.LoadCustom(vehicleFile!, track.Weather);
                _carType = definition.CarType;
                _customFile = definition.CustomFile;
                _userDefined = true;
            }

            VehicleName = definition.Name;
            _surfaceTractionFactor = Math.Max(0.01f, SanitizeFinite(definition.SurfaceTractionFactor, 0.01f));
            _deceleration = Math.Max(0.01f, SanitizeFinite(definition.Deceleration, 0.01f));
            _topSpeed = Math.Max(1f, SanitizeFinite(definition.TopSpeed, 1f));
            _massKg = Math.Max(1f, SanitizeFinite(definition.MassKg, 1f));
            _drivetrainEfficiency = Math.Max(0.1f, Math.Min(1.0f, SanitizeFinite(definition.DrivetrainEfficiency, 0.85f)));
            _engineBrakingTorqueNm = Math.Max(0f, SanitizeFinite(definition.EngineBrakingTorqueNm, 0f));
            _tireGripCoefficient = Math.Max(0.1f, SanitizeFinite(definition.TireGripCoefficient, 0.1f));
            _brakeStrength = Math.Max(0.1f, SanitizeFinite(definition.BrakeStrength, 0.1f));
            _wheelRadiusM = Math.Max(0.01f, SanitizeFinite(definition.TireCircumferenceM, 0f) / (2.0f * (float)Math.PI));
            _engineBraking = Math.Max(0.05f, Math.Min(1.0f, SanitizeFinite(definition.EngineBraking, 0.3f)));
            _idleRpm = Math.Max(0f, SanitizeFinite(definition.IdleRpm, 0f));
            _revLimiter = Math.Max(_idleRpm, SanitizeFinite(definition.RevLimiter, _idleRpm));
            _finalDriveRatio = Math.Max(0.1f, SanitizeFinite(definition.FinalDriveRatio, 0.1f));
            _reverseMaxSpeedKph = Math.Max(5f, SanitizeFinite(definition.ReverseMaxSpeedKph, 35f));
            _reversePowerFactor = Math.Max(0.1f, SanitizeFinite(definition.ReversePowerFactor, 0.55f));
            _reverseGearRatio = Math.Max(0.1f, SanitizeFinite(definition.ReverseGearRatio, 3.2f));
            _powerFactor = Math.Max(0.1f, SanitizeFinite(definition.PowerFactor, 0.1f));
            _peakTorqueNm = Math.Max(0f, SanitizeFinite(definition.PeakTorqueNm, 0f));
            _peakTorqueRpm = Math.Max(_idleRpm + 100f, SanitizeFinite(definition.PeakTorqueRpm, _idleRpm + 100f));
            _idleTorqueNm = Math.Max(0f, SanitizeFinite(definition.IdleTorqueNm, 0f));
            _redlineTorqueNm = Math.Max(0f, SanitizeFinite(definition.RedlineTorqueNm, 0f));
            _dragCoefficient = Math.Max(0.01f, SanitizeFinite(definition.DragCoefficient, 0.01f));
            _frontalAreaM2 = Math.Max(0.1f, SanitizeFinite(definition.FrontalAreaM2, 0.1f));
            _rollingResistanceCoefficient = Math.Max(0.001f, SanitizeFinite(definition.RollingResistanceCoefficient, 0.001f));
            _launchRpm = Math.Max(_idleRpm, Math.Min(_revLimiter, SanitizeFinite(definition.LaunchRpm, _idleRpm)));
            _lateralGripCoefficient = Math.Max(0.1f, SanitizeFinite(definition.LateralGripCoefficient, 0.1f));
            _highSpeedStability = Math.Max(0f, Math.Min(1.0f, SanitizeFinite(definition.HighSpeedStability, 0f)));
            _wheelbaseM = Math.Max(0.5f, SanitizeFinite(definition.WheelbaseM, 0.5f));
            _maxSteerDeg = Math.Max(5f, Math.Min(60f, SanitizeFinite(definition.MaxSteerDeg, 35f)));
            _widthM = Math.Max(0.5f, SanitizeFinite(definition.WidthM, 0.5f));
            _lengthM = Math.Max(0.5f, SanitizeFinite(definition.LengthM, 0.5f));
            _idleFreq = definition.IdleFreq;
            _topFreq = definition.TopFreq;
            _shiftFreq = definition.ShiftFreq;
            _gears = Math.Max(1, definition.Gears);
            _steering = SanitizeFinite(definition.Steering, 0.1f);
            _frequency = _idleFreq;

            // Initialize engine model
            _engine = new EngineModel(
                definition.IdleRpm,
                definition.MaxRpm,
                definition.RevLimiter,
                definition.AutoShiftRpm,
                definition.EngineBraking,
                definition.TopSpeed,
                definition.FinalDriveRatio,
                definition.TireCircumferenceM,
                definition.Gears,
                definition.GearRatios);
            _transmissionPolicy = definition.TransmissionPolicy ?? TransmissionPolicy.Default;

            _soundEngine = CreateRequiredSound(definition.GetSoundPath(VehicleAction.Engine), looped: true, allowHrtf: true);
            _soundStart = CreateRequiredSound(definition.GetSoundPath(VehicleAction.Start));
            _soundHorn = CreateRequiredSound(definition.GetSoundPath(VehicleAction.Horn), looped: true);
            _soundThrottle = TryCreateSound(definition.GetSoundPath(VehicleAction.Throttle), looped: true, allowHrtf: true);
            _soundCrashVariants = CreateRequiredSoundVariants(definition.GetSoundPaths(VehicleAction.Crash), definition.GetSoundPath(VehicleAction.Crash));
            _soundCrash = _soundCrashVariants[0];
            _soundBrake = CreateRequiredSound(definition.GetSoundPath(VehicleAction.Brake), looped: true, allowHrtf: false);
            _soundBackfireVariants = CreateOptionalSoundVariants(definition.GetSoundPaths(VehicleAction.Backfire), definition.GetSoundPath(VehicleAction.Backfire));
            _soundBackfire = _soundBackfireVariants.Length > 0 ? _soundBackfireVariants[0] : null;

            if (definition.HasWipers == 1)
                _hasWipers = 1;

            if (_hasWipers == 1)
                _soundWipers = CreateRequiredSound(Path.Combine(_legacyRoot, "wipers.wav"), looped: true, allowHrtf: false);

            _soundAsphalt = CreateRequiredSound(Path.Combine(_legacyRoot, "asphalt.wav"), looped: true, allowHrtf: false);
            _soundGravel = CreateRequiredSound(Path.Combine(_legacyRoot, "gravel.wav"), looped: true, allowHrtf: false);
            _soundWater = CreateRequiredSound(Path.Combine(_legacyRoot, "water.wav"), looped: true, allowHrtf: false);
            _soundSand = CreateRequiredSound(Path.Combine(_legacyRoot, "sand.wav"), looped: true, allowHrtf: false);
            _soundSnow = CreateRequiredSound(Path.Combine(_legacyRoot, "snow.wav"), looped: true, allowHrtf: false);
            _soundMiniCrash = CreateRequiredSound(Path.Combine(_legacyRoot, "crashshort.wav"));
            _soundBump = CreateRequiredSound(Path.Combine(_legacyRoot, "bump.wav"), allowHrtf: false);
            _soundBadSwitch = CreateRequiredSound(Path.Combine(_legacyRoot, "badswitch.wav"), allowHrtf: false);

            for (var i = 0; i < _soundCrashVariants.Length; i++)
                _soundCrashVariants[i].SetDopplerFactor(0f);
            _soundMiniCrash.SetDopplerFactor(0f);
            _soundBump.SetDopplerFactor(0f);
            _soundWipers?.SetDopplerFactor(0f);

            _vibration = vibrationDevice != null && vibrationDevice.IsAvailable && vibrationDevice.ForceFeedbackCapable && _settings.ForceFeedback && _settings.UseJoystick
                ? vibrationDevice
                : null;
            if (_vibration != null)
            {
                _vibration.LoadEffect(VibrationEffectType.Start, Path.Combine(_effectsRoot, "carstart.ffe"));
                _vibration.LoadEffect(VibrationEffectType.Crash, Path.Combine(_effectsRoot, "crash.ffe"));
                _vibration.LoadEffect(VibrationEffectType.Spring, Path.Combine(_effectsRoot, "spring.ffe"));
                _vibration.LoadEffect(VibrationEffectType.Engine, Path.Combine(_effectsRoot, "engine.ffe"));
                _vibration.LoadEffect(VibrationEffectType.CurbLeft, Path.Combine(_effectsRoot, "curbleft.ffe"));
                _vibration.LoadEffect(VibrationEffectType.CurbRight, Path.Combine(_effectsRoot, "curbright.ffe"));
                _vibration.LoadEffect(VibrationEffectType.Gravel, Path.Combine(_effectsRoot, "gravel.ffe"));
                _vibration.LoadEffect(VibrationEffectType.BumpLeft, Path.Combine(_effectsRoot, "bumpleft.ffe"));
                _vibration.LoadEffect(VibrationEffectType.BumpRight, Path.Combine(_effectsRoot, "bumpright.ffe"));
                _vibration.Gain(VibrationEffectType.Gravel, 0);
            }

            _soundEngine.SetDopplerFactor(0f);
            _soundThrottle?.SetDopplerFactor(0f);
            _soundHorn.SetDopplerFactor(0f);
            _soundBrake.SetDopplerFactor(0f);
            _soundAsphalt.SetDopplerFactor(0f);
            _soundGravel.SetDopplerFactor(0f);
            _soundWater.SetDopplerFactor(0f);
            _soundSand.SetDopplerFactor(0f);
            _soundSnow.SetDopplerFactor(0f);
            RefreshCategoryVolumes(force: true);
        }

        public CarState State => _state;
        public float PositionX => _positionX;
        public float PositionY => _positionY;
        public float Speed => _speed;
        public int Frequency => _frequency;
        public int Gear => _gear;
        public bool InReverseGear => _gear == ReverseGear;
        public bool ManualTransmission
        {
            get => _manualTransmission;
            set => _manualTransmission = value;
        }
        public CarType CarType => _carType;
        public ICarListener? Listener
        {
            get => _listener;
            set => _listener = value;
        }
        public bool EngineRunning => _soundEngine.IsPlaying;
        public bool Braking => _soundBrake.IsPlaying;
        public bool Horning => _soundHorn.IsPlaying;
        public bool UserDefined => _userDefined;
        public string? CustomFile => _customFile;
        public string VehicleName { get; private set; } = "Vehicle";
        public float WidthM => _widthM;
        public float LengthM => _lengthM;

        // Engine simulation properties for reporting
        public float SpeedKmh => _engine.SpeedKmh;
        public float EngineRpm => _engine.Rpm;
        public float DistanceMeters => _engine.DistanceMeters;

        public void Initialize(float positionX = 0, float positionY = 0)        
        {
            _positionX = positionX;
            _positionY = Math.Max(0f, positionY);
            _laneWidth = _track.LaneWidth * 2;
            s_stickReleased = true;
            _audioInitialized = false;
            _lastAudioX = positionX;
            _lastAudioY = _positionY;
            _lastAudioElapsed = 0f;
            _vibration?.PlayEffect(VibrationEffectType.Spring);
        }

        public void SetPosition(float positionX, float positionY)
        {
            _positionX = positionX;
            _positionY = Math.Max(0f, positionY);
        }

        public void FinalizeCar()
        {
            _soundEngine.Stop();
            _soundThrottle?.Stop();
            _vibration?.StopEffect(VibrationEffectType.Spring);
        }

        public void Start()
        {
            var delay = Math.Max(0f, _soundStart.GetLengthSeconds() - 0.1f);
            PushEvent(CarEventType.CarStart, delay);
            _soundStart.Restart(loop: false);
            _speed = 0;
            _engine.Reset();
            _prevFrequency = _idleFreq;
            _frequency = _idleFreq;
            _prevBrakeFrequency = 0;
            _brakeFrequency = 0;
            _prevSurfaceFrequency = 0;
            _surfaceFrequency = 0;
            _switchingGear = 0;
            _throttleVolume = 0.0f;
            _soundAsphalt.SetFrequency(_surfaceFrequency);
            _soundGravel.SetFrequency(_surfaceFrequency);
            _soundWater.SetFrequency(_surfaceFrequency);
            _soundSand.SetFrequency(_surfaceFrequency);
            _soundSnow.SetFrequency(_surfaceFrequency);
            s_stickReleased = true;
            _state = CarState.Starting;
            _listener?.OnStart();
            _vibration?.PlayEffect(VibrationEffectType.Start);
            _vibration?.PlayEffect(VibrationEffectType.Engine);
        }

        /// <summary>
        /// Restarts the car after a crash, preserving distance traveled.
        /// </summary>
        public void RestartAfterCrash()
        {
            var delay = Math.Max(0f, _soundStart.GetLengthSeconds() - 0.1f);
            PushEvent(CarEventType.CarStart, delay);
            _soundStart.Restart(loop: false);
            _speed = 0;
            // Do NOT call _engine.Reset() - preserve distance
            _prevFrequency = _idleFreq;
            _frequency = _idleFreq;
            _prevBrakeFrequency = 0;
            _brakeFrequency = 0;
            _prevSurfaceFrequency = 0;
            _surfaceFrequency = 0;
            _switchingGear = 0;
            _throttleVolume = 0.0f;
            _soundAsphalt.SetFrequency(_surfaceFrequency);
            _soundGravel.SetFrequency(_surfaceFrequency);
            _soundWater.SetFrequency(_surfaceFrequency);
            _soundSand.SetFrequency(_surfaceFrequency);
            _soundSnow.SetFrequency(_surfaceFrequency);
            s_stickReleased = true;
            _state = CarState.Starting;
            _listener?.OnStart();
            _vibration?.PlayEffect(VibrationEffectType.Start);
            _vibration?.PlayEffect(VibrationEffectType.Engine);
        }

        public void Crash()
        {
            _speed = 0;
            _engine.ResetForCrash();
            _throttleVolume = 0.0f;
            _soundCrash = SelectRandomCrashHandle();
            _soundCrash.Restart(loop: false);
            _soundEngine.Stop();
            _soundEngine.SeekToStart();
            if (_soundThrottle != null)
            {
                _soundThrottle.Stop();
                _soundThrottle.SeekToStart();
            }
            _soundStart.SetPanPercent(0);
            switch (_surface)
            {
                case TrackSurface.Asphalt:
                    _soundAsphalt.Stop();
                    _soundAsphalt.SetPanPercent(0);
                    SetSurfaceLoopVolumePercent(_soundAsphalt, 90);
                    break;
                case TrackSurface.Gravel:
                    _soundGravel.Stop();
                    _soundGravel.SetPanPercent(0);
                    SetSurfaceLoopVolumePercent(_soundGravel, 90);
                    break;
                case TrackSurface.Water:
                    _soundWater.Stop();
                    _soundWater.SetPanPercent(0);
                    SetSurfaceLoopVolumePercent(_soundWater, 90);
                    break;
                case TrackSurface.Sand:
                    _soundSand.Stop();
                    _soundSand.SetPanPercent(0);
                    SetSurfaceLoopVolumePercent(_soundSand, 90);
                    break;
                case TrackSurface.Snow:
                    _soundSnow.Stop();
                    _soundSnow.SetPanPercent(0);
                    SetSurfaceLoopVolumePercent(_soundSnow, 90);
                    break;
            }
            _soundBrake.Stop();
            _soundBrake.SeekToStart();
            _soundBrake.SetPanPercent(0);
            if (_hasWipers == 1 && _soundWipers != null)
            {
                _soundWipers.Stop();
                _soundWipers.SeekToStart();
                _soundWipers.SetPanPercent(0);
            }
            _soundHorn.Stop();
            _soundHorn.SeekToStart();
            _soundHorn.SetPanPercent(0);
            _gear = 1;
            _switchingGear = 0;
            _state = CarState.Crashing;
            // Transition to Crashed state after crash animation completes (player must manually restart)
            PushEvent(CarEventType.CrashComplete, _soundCrash.GetLengthSeconds() + 1.25f);
            _listener?.OnCrash();
            _vibration?.StopEffect(VibrationEffectType.Engine);
            _vibration?.PlayEffect(VibrationEffectType.Crash);
            PushEvent(CarEventType.StopVibration, CrashVibrationSeconds, VibrationEffectType.Crash);
            _vibration?.StopEffect(VibrationEffectType.CurbLeft);
            _vibration?.StopEffect(VibrationEffectType.CurbRight);
        }

        public void MiniCrash(float newPosition)
        {
            _speed /= 4;
            if (_positionX < newPosition)
                _vibration?.PlayEffect(VibrationEffectType.BumpLeft);
            if (_positionX > newPosition)
                _vibration?.PlayEffect(VibrationEffectType.BumpRight);
            PushEvent(CarEventType.StopBumpVibration, BumpVibrationSeconds);

            _positionX = newPosition;
            _throttleVolume = 0.0f;
            _soundMiniCrash.SeekToStart();
            _soundMiniCrash.Play(loop: false);
        }

        public void Bump(float bumpX, float bumpY, float bumpSpeed)
        {
            if (bumpY != 0)
            {
                _speed -= bumpSpeed;
                var currentLapStart = GetLapStartPosition(_positionY);
                _positionY += bumpY;
                if (_positionY < currentLapStart)
                    _positionY = currentLapStart;
                if (_positionY < 0f)
                    _positionY = 0f;
            }

            if (bumpX > 0)
            {
                _positionX += 2 * bumpX;
                _speed -= _speed / 5;
                _vibration?.PlayEffect(VibrationEffectType.BumpLeft);
            }
            else if (bumpX < 0)
            {
                _positionX += 2 * bumpX;
                _speed -= _speed / 5;
                _vibration?.PlayEffect(VibrationEffectType.BumpRight);
            }

            if (_speed < 0)
                _speed = 0;
            _soundBump.Play(loop: false);
            PushEvent(CarEventType.StopBumpVibration, BumpVibrationSeconds);
        }

        public void Stop()
        {
            _soundBrake.Stop();
            _soundWipers?.Stop();
            _vibration?.StopEffect(VibrationEffectType.CurbLeft);
            _vibration?.StopEffect(VibrationEffectType.CurbRight);
            _state = CarState.Stopping;
        }

        public void Quiet()
        {
            _soundBrake.Stop();
            SetPlayerEngineVolumePercent(_soundEngine, 90);
            _soundThrottle?.Stop();
            for (var i = 0; i < _soundBackfireVariants.Length; i++)
                SetPlayerEventVolumePercent(_soundBackfireVariants[i], 90);
            SetSurfaceLoopVolumePercent(_soundAsphalt, 90);
            SetSurfaceLoopVolumePercent(_soundGravel, 90);
            SetSurfaceLoopVolumePercent(_soundWater, 90);
            SetSurfaceLoopVolumePercent(_soundSand, 90);
            SetSurfaceLoopVolumePercent(_soundSnow, 90);
            _vibration?.StopEffect(VibrationEffectType.CurbLeft);
            _vibration?.StopEffect(VibrationEffectType.CurbRight);
            _vibration?.StopEffect(VibrationEffectType.Engine);
        }
        public void Run(float elapsed)
        {
            RefreshCategoryVolumes();
            _lastAudioElapsed = elapsed;
            var horning = _input.GetHorn();

            if (_state == CarState.Running && _started())
            {
                if (!IsFinite(_speed))
                    _speed = 0f;
                if (!IsFinite(_positionX))
                    _positionX = 0f;
                if (!IsFinite(_positionY))
                    _positionY = 0f;
                if (_positionY < 0f)
                    _positionY = 0f;

                if (_input.GetToggleComputerControl())
                {
                    _isComputerControlled = !_isComputerControlled;
                }

                if (_isComputerControlled)
                {
                    var road = _track.RoadComputer(_positionY);
                    var laneHalfWidth = Math.Max(0.1f, Math.Abs(road.Right - road.Left) * 0.5f);
                    var relPos = TopSpeed.Bots.BotRaceRules.CalculateRelativeLanePosition(_positionX, road.Left, laneHalfWidth);
                    var nextRoad = _track.RoadComputer(_positionY + 30.0f);
                    var nextLaneHalfWidth = Math.Max(0.1f, Math.Abs(nextRoad.Right - nextRoad.Left) * 0.5f);

                    TopSpeed.Bots.BotSharedModel.GetControlInputs(1, _computerRandom, road.Type, nextRoad.Type, relPos, out var botThrottle, out var botSteering);
                    _currentThrottle = (int)Math.Round(botThrottle);
                    _currentSteering = (int)Math.Round(botSteering);
                    _currentBrake = 0;
                }
                else
                {
                    _currentSteering = _input.GetSteering();
                    _currentThrottle = _input.GetThrottle();
                    _currentBrake = _input.GetBrake();
                }
                var gearUp = _input.GetGearUp();
                var gearDown = _input.GetGearDown();

                _currentSurfaceTractionFactor = _surfaceTractionFactor;
                _currentDeceleration = _deceleration;
                _speedDiff = 0;
                switch (_surface)
                {
                    case TrackSurface.Gravel:
                        _currentSurfaceTractionFactor = (_currentSurfaceTractionFactor * 2) / 3;
                        _currentDeceleration = (_currentDeceleration * 2) / 3;
                        break;
                    case TrackSurface.Water:
                        _currentSurfaceTractionFactor = (_currentSurfaceTractionFactor * 3) / 5;
                        _currentDeceleration = (_currentDeceleration * 3) / 5;
                        break;
                    case TrackSurface.Sand:
                        _currentSurfaceTractionFactor = _currentSurfaceTractionFactor / 2;
                        _currentDeceleration = (_currentDeceleration * 3) / 2;
                        break;
                    case TrackSurface.Snow:
                        _currentDeceleration = _currentDeceleration / 2;
                        break;
                }

                _factor1 = 100;
                if (_manualTransmission)
                {
                    if (!gearUp && !gearDown)
                        s_stickReleased = true;

                    if (gearDown && s_stickReleased)
                    {
                        if (_gear > FirstForwardGear)
                        {
                            s_stickReleased = false;
                            _switchingGear = -1;
                            --_gear;
                            if (_soundEngine.GetPitch() > 3f * _topFreq / (2f * _soundEngine.InputSampleRate))
                                _soundBadSwitch.Play(loop: false);
                            if (!AnyBackfirePlaying() && Algorithm.RandomInt(5) == 1)
                                PlayRandomBackfire();
                            PushEvent(CarEventType.InGear, 0.2f);
                        }
                        else if (_gear == FirstForwardGear)
                        {
                            s_stickReleased = false;
                            if (_speed <= ReverseShiftMaxSpeedKmh)
                            {
                                _switchingGear = -1;
                                _gear = ReverseGear;
                                PushEvent(CarEventType.InGear, 0.2f);
                            }
                            else
                            {
                                _soundBadSwitch.Play(loop: false);
                            }
                        }
                    }
                    else if (gearUp && s_stickReleased)
                    {
                        if (_gear == ReverseGear)
                        {
                            s_stickReleased = false;
                            if (_speed <= ReverseShiftMaxSpeedKmh)
                            {
                                _switchingGear = 1;
                                _gear = FirstForwardGear;
                                PushEvent(CarEventType.InGear, 0.2f);
                            }
                            else
                            {
                                _soundBadSwitch.Play(loop: false);
                            }
                        }
                        else if (_gear < _gears)
                        {
                            s_stickReleased = false;
                            _switchingGear = 1;
                            ++_gear;
                            if (_soundEngine.GetPitch() < _idleFreq / (float)_soundEngine.InputSampleRate)
                                _soundBadSwitch.Play(loop: false);
                            if (!AnyBackfirePlaying() && Algorithm.RandomInt(5) == 1)
                                PlayRandomBackfire();
                            PushEvent(CarEventType.InGear, 0.2f);
                        }
                    }
                }
                else
                {
                    var reverseRequested = _input.GetReverseRequested();
                    var forwardRequested = _input.GetForwardRequested();

                    if (reverseRequested && _gear != ReverseGear)
                    {
                        if (_speed <= ReverseShiftMaxSpeedKmh)
                        {
                            _switchingGear = -1;
                            _gear = ReverseGear;
                            PushEvent(CarEventType.InGear, 0.2f);
                        }
                        else
                        {
                            _currentThrottle = 0;
                            _currentBrake = -100;
                            _soundBadSwitch.Play(loop: false);
                        }
                    }
                    else if (forwardRequested && _gear == ReverseGear)
                    {
                        if (_speed <= ReverseShiftMaxSpeedKmh)
                        {
                            _switchingGear = 1;
                            _gear = FirstForwardGear;
                            PushEvent(CarEventType.InGear, 0.2f);
                        }
                        else
                        {
                            _soundBadSwitch.Play(loop: false);
                        }
                    }
                }

                if (_soundThrottle != null)
                {
                    if (_soundEngine.IsPlaying)
                    {
                        if (_currentThrottle > 50)
                        {
                            if (!_soundThrottle.IsPlaying)
                            {
                                if (_throttleVolume < 80.0f)
                                    _throttleVolume = 80.0f;
                                SetPlayerEngineVolumePercent(_soundThrottle, (int)_throttleVolume);
                                _prevThrottleVolume = _throttleVolume;
                                _soundThrottle.Play(loop: true);
                            }
                            else
                            {
                                if (_throttleVolume >= 80.0f)
                                    _throttleVolume += (100.0f - _throttleVolume) * elapsed;
                                else
                                    _throttleVolume = 80.0f;
                                if (_throttleVolume > 100.0f)
                                    _throttleVolume = 100.0f;
                                if ((int)_throttleVolume != (int)_prevThrottleVolume)
                                {
                                    SetPlayerEngineVolumePercent(_soundThrottle, (int)_throttleVolume);
                                    _prevThrottleVolume = _throttleVolume;
                                }
                            }
                        }
                        else
                        {
                            _throttleVolume -= 10.0f * elapsed;
                            var min = _speed * 95 / _topSpeed;
                            if (_throttleVolume < min)
                                _throttleVolume = min;
                            if ((int)_throttleVolume != (int)_prevThrottleVolume)
                            {
                                SetPlayerEngineVolumePercent(_soundThrottle, (int)_throttleVolume);
                                _prevThrottleVolume = _throttleVolume;
                            }
                        }
                    }
                    else if (_soundThrottle.IsPlaying)
                    {
                        _soundThrottle.Stop();
                    }
                }

                _thrust = _currentThrottle;
                if (_currentThrottle == 0)
                    _thrust = _currentBrake;
                else if (_currentBrake == 0)
                    _thrust = _currentThrottle;
                else if (-_currentBrake > _currentThrottle)
                    _thrust = _currentBrake;

                var speedMpsCurrent = _speed / 3.6f;
                var throttle = Math.Max(0f, Math.Min(100f, _currentThrottle)) / 100f;
                var inReverse = _gear == ReverseGear;
                var currentLapStart = GetLapStartPosition(_positionY);
                var reverseBlockedAtLapStart = inReverse && _positionY <= currentLapStart + 0.001f;
                var surfaceTractionMod = _surfaceTractionFactor > 0f
                    ? _currentSurfaceTractionFactor / _surfaceTractionFactor
                    : 1.0f;
                var longitudinalGripFactor = 1.0f;

                // Original speed calculation with proper gear physics
                if (_thrust > 10)
                {
                    if (reverseBlockedAtLapStart)
                    {
                        _speedDiff = 0f;
                        _lastDriveRpm = 0f;
                    }
                    else
                    {
                    var steeringCommandAccel = (_currentSteering / 100.0f) * _steering;
                    if (steeringCommandAccel > 1.0f)
                        steeringCommandAccel = 1.0f;
                    else if (steeringCommandAccel < -1.0f)
                        steeringCommandAccel = -1.0f;
                    var steerRadAccel = (float)(Math.PI / 180.0) * (_maxSteerDeg * steeringCommandAccel);
                    var curvatureAccel = (float)Math.Tan(steerRadAccel) / _wheelbaseM;
                    var desiredLatAccel = curvatureAccel * speedMpsCurrent * speedMpsCurrent;
                    var desiredLatAccelAbs = Math.Abs(desiredLatAccel);
                    var grip = _tireGripCoefficient * surfaceTractionMod * _lateralGripCoefficient;
                    var maxLatAccel = grip * 9.80665f;
                    var lateralRatio = maxLatAccel > 0f ? Math.Min(1.0f, desiredLatAccelAbs / maxLatAccel) : 0f;
                    longitudinalGripFactor = (float)Math.Sqrt(Math.Max(0.0, 1.0 - (lateralRatio * lateralRatio)));
                    var driveRpm = CalculateDriveRpm(speedMpsCurrent, throttle);
                    var engineTorque = CalculateEngineTorqueNm(driveRpm) * throttle * _powerFactor;
                    var gearRatio = inReverse ? _reverseGearRatio : _engine.GetGearRatio(GetDriveGear());
                    var wheelTorque = engineTorque * gearRatio * _finalDriveRatio * _drivetrainEfficiency;
                    var wheelForce = wheelTorque / _wheelRadiusM;
                    var tractionLimit = _tireGripCoefficient * surfaceTractionMod * _massKg * 9.80665f;
                    if (wheelForce > tractionLimit)
                        wheelForce = tractionLimit;
                    wheelForce *= (float)longitudinalGripFactor;
                    wheelForce *= (_factor1 / 100f);
                    if (inReverse)
                        wheelForce *= _reversePowerFactor;

                    var dragForce = 0.5f * 1.225f * _dragCoefficient * _frontalAreaM2 * speedMpsCurrent * speedMpsCurrent;
                    var rollingForce = _rollingResistanceCoefficient * _massKg * 9.80665f;
                    var netForce = wheelForce - dragForce - rollingForce;
                    var accelMps2 = netForce / _massKg;
                    var newSpeedMps = speedMpsCurrent + (accelMps2 * elapsed);
                    if (newSpeedMps < 0f)
                        newSpeedMps = 0f;
                    _speedDiff = (newSpeedMps - speedMpsCurrent) * 3.6f;
                    _lastDriveRpm = CalculateDriveRpm(newSpeedMps, throttle);

                    if (_backfirePlayed)
                        _backfirePlayed = false;
                    }
                }
                else
                {
                    var surfaceDecelMod = _deceleration > 0f ? _currentDeceleration / _deceleration : 1.0f;
                    var brakeInput = Math.Max(0f, Math.Min(100f, -_currentBrake)) / 100f;
                    var brakeDecel = CalculateBrakeDecel(brakeInput, surfaceDecelMod);
                    var engineBrakeDecel = CalculateEngineBrakingDecel(surfaceDecelMod);
                    var totalDecel = _thrust < -10 ? (brakeDecel + engineBrakeDecel) : engineBrakeDecel;
                    _speedDiff = -totalDecel * elapsed;
                    _lastDriveRpm = 0f;
                }

                _speed += _speedDiff;
                if (_speed > _topSpeed)
                    _speed = _topSpeed;
                if (_speed < 0)
                    _speed = 0;
                if (!IsFinite(_speed))
                {
                    _speed = 0f;
                    _speedDiff = 0f;
                }
                if (!IsFinite(_lastDriveRpm))
                    _lastDriveRpm = _idleRpm;

                if (reverseBlockedAtLapStart && _thrust > 10)
                {
                    _speed = 0f;
                    _speedDiff = 0f;
                    _lastDriveRpm = 0f;
                }

                if (_gear == ReverseGear)
                {
                    var reverseMax = Math.Max(5.0f, _reverseMaxSpeedKph);
                    if (_speed > reverseMax)
                        _speed = reverseMax;
                }
                else if (_manualTransmission)
                {
                    var gearMax = _engine.GetGearMaxSpeedKmh(_gear);
                    if (_speed > gearMax)
                        _speed = gearMax;
                }
                else if (_gear != ReverseGear)
                {
                    UpdateAutomaticGear(elapsed, _speed / 3.6f, throttle, surfaceTractionMod, longitudinalGripFactor);
                }

                // Update engine model for RPM and distance tracking (reporting only)
                _engine.SyncFromSpeed(_speed, GetDriveGear(), elapsed, _currentThrottle);
                if (_lastDriveRpm > 0f && _lastDriveRpm > _engine.Rpm)
                    _engine.OverrideRpm(_lastDriveRpm);

                if (_thrust <= 0)
                {
                    if (!AnyBackfirePlaying() && !_backfirePlayed)
                    {
                        if (Algorithm.RandomInt(5) == 1)
                            PlayRandomBackfire();
                    }
                    _backfirePlayed = true;
                }

                if (_thrust < -50 && _speed > 0)
                {
                    BrakeSound();
                    _vibration?.Gain(VibrationEffectType.Spring, (int)(50.0f * _speed / _topSpeed));
                    _currentSteering = _currentSteering * 2 / 3;
                }
                else if (_currentSteering != 0 && _speed > _topSpeed / 2)
                {
                    if (_thrust > -50)
                        BrakeCurveSound();
                }
                else
                {
                    if (_soundBrake.IsPlaying)
                        _soundBrake.Stop();
                    SetSurfaceLoopVolumePercent(_soundAsphalt, 90);
                    SetSurfaceLoopVolumePercent(_soundGravel, 90);
                    SetSurfaceLoopVolumePercent(_soundWater, 90);
                    SetSurfaceLoopVolumePercent(_soundSand, 90);
                    SetSurfaceLoopVolumePercent(_soundSnow, 90);
                }

                var speedMps = _speed / 3.6f;
                var longitudinalDelta = speedMps * elapsed;
                if (_gear == ReverseGear)
                {
                    var nextPositionY = _positionY - longitudinalDelta;
                    if (nextPositionY < currentLapStart)
                        nextPositionY = currentLapStart;
                    if (nextPositionY < 0f)
                        nextPositionY = 0f;
                    _positionY = nextPositionY;
                }
                else
                {
                    _positionY += longitudinalDelta;
                }
                var surfaceMultiplier = _surface == TrackSurface.Snow ? 1.44f : 1.0f;
                var steeringCommandLat = (_currentSteering / 100.0f) * _steering;
                if (steeringCommandLat > 1.0f)
                    steeringCommandLat = 1.0f;
                else if (steeringCommandLat < -1.0f)
                    steeringCommandLat = -1.0f;
                var steerRadLat = (float)(Math.PI / 180.0) * (_maxSteerDeg * steeringCommandLat);
                var curvatureLat = (float)Math.Tan(steerRadLat) / _wheelbaseM;
                var surfaceTractionModLat = _surfaceTractionFactor > 0f ? _currentSurfaceTractionFactor / _surfaceTractionFactor : 1.0f;
                var gripLat = _tireGripCoefficient * surfaceTractionModLat * _lateralGripCoefficient;
                var maxLatAccelLat = gripLat * 9.80665f;
                var desiredLatAccelLat = curvatureLat * speedMps * speedMps;
                var massFactor = (float)Math.Sqrt(1500f / _massKg);
                if (massFactor > 3.0f)
                    massFactor = 3.0f;
                var stabilityScale = 1.0f - (_highSpeedStability * (speedMps / StabilitySpeedRef) * massFactor);
                if (stabilityScale < 0.2f)
                    stabilityScale = 0.2f;
                else if (stabilityScale > 1.0f)
                    stabilityScale = 1.0f;
                var responseTime = BaseLateralSpeed / 20.0f;
                var maxLatSpeed = maxLatAccelLat * responseTime * stabilityScale;
                var desiredLatSpeed = desiredLatAccelLat * responseTime;
                if (desiredLatSpeed > maxLatSpeed)
                    desiredLatSpeed = maxLatSpeed;
                else if (desiredLatSpeed < -maxLatSpeed)
                    desiredLatSpeed = -maxLatSpeed;
                var lateralSpeed = desiredLatSpeed * surfaceMultiplier;
                _positionX += (lateralSpeed * elapsed);

                if (_frame % 4 == 0)
                {
                    _frame = 0;
                    _brakeFrequency = (int)(11025 + 22050 * _speed / _topSpeed);
                    if (_brakeFrequency != _prevBrakeFrequency)
                    {
                        _soundBrake.SetFrequency(_brakeFrequency);
                        _prevBrakeFrequency = _brakeFrequency;
                    }
                    if (_speed <= 50.0f)
                        SetPlayerEventVolumePercent(_soundBrake, (int)(100 - (50 - (_speed))));
                    else
                        SetPlayerEventVolumePercent(_soundBrake, 100);
                    if (_manualTransmission)
                        UpdateEngineFreqManual();
                    else
                        UpdateEngineFreq();
                    UpdateSoundRoad();
                    if (_vibration != null)
                    {
                        if (_surface == TrackSurface.Gravel)
                            _vibration.Gain(VibrationEffectType.Gravel, (int)(_speed * 10000 / _topSpeed));
                        else
                            _vibration.Gain(VibrationEffectType.Gravel, 0);

                        if (_speed == 0)
                            _vibration.Gain(VibrationEffectType.Spring, 10000);
                        else
                            _vibration.Gain(VibrationEffectType.Spring, (int)(10000 * _speed / _topSpeed));

                        if (_speed < _topSpeed / 10)
                            _vibration.Gain(VibrationEffectType.Engine, (int)(10000 - _speed * 10 / _topSpeed));
                        else
                            _vibration.Gain(VibrationEffectType.Engine, 0);
                    }
                }

                switch (_surface)
                {
                    case TrackSurface.Asphalt:
                        if (!_soundAsphalt.IsPlaying)
                        {
                            _soundAsphalt.SetFrequency(Math.Min(_surfaceFrequency, MaxSurfaceFreq));
                            _soundAsphalt.Play(loop: true);
                        }
                        break;
                    case TrackSurface.Gravel:
                        if (!_soundGravel.IsPlaying)
                        {
                            _soundGravel.SetFrequency(Math.Min(_surfaceFrequency, MaxSurfaceFreq));
                            _soundGravel.Play(loop: true);
                        }
                        break;
                    case TrackSurface.Water:
                        if (!_soundWater.IsPlaying)
                        {
                            _soundWater.SetFrequency(Math.Min(_surfaceFrequency, MaxSurfaceFreq));
                            _soundWater.Play(loop: true);
                        }
                        break;
                    case TrackSurface.Sand:
                        if (!_soundSand.IsPlaying)
                        {
                            _soundSand.SetFrequency((int)(_surfaceFrequency / 2.5f));
                            _soundSand.Play(loop: true);
                        }
                        break;
                    case TrackSurface.Snow:
                        if (!_soundSnow.IsPlaying)
                        {
                            _soundSnow.SetFrequency(Math.Min(_surfaceFrequency, MaxSurfaceFreq));
                            _soundSnow.Play(loop: true);
                        }
                        break;
                }
            }
            else if (_state == CarState.Stopping)
            {
                _speed -= (elapsed * 100 * _deceleration);
                if (_speed < 0)
                    _speed = 0;
                if (_frame % 4 == 0)
                {
                    _frame = 0;
                    UpdateEngineFreq();
                    UpdateSoundRoad();
                }
            }

            if (horning && _state != CarState.Stopped && _state != CarState.Crashing)
            {
                if (!_soundHorn.IsPlaying)
                    _soundHorn.Play(loop: true);
            }
            else
            {
                if (_soundHorn.IsPlaying)
                    _soundHorn.Stop();
            }

            var now = _currentTime();
            for (var i = _events.Count - 1; i >= 0; i--)
            {
                var e = _events[i];
                if (e.Time < now)
                {
                    switch (e.Type)
                    {
                        case CarEventType.CarStart:
                            _soundEngine.SetFrequency(_idleFreq);
                            _soundThrottle?.SetFrequency(_idleFreq);
                            _vibration?.StopEffect(VibrationEffectType.Start);
                            _soundEngine.Play(loop: true);
                            _soundWipers?.Play(loop: true);
                            _engine.StartEngine();  // Set RPM to idle
                            _state = CarState.Running;
                            break;
                        case CarEventType.CarRestart:
                            _vibration?.StopEffect(VibrationEffectType.Crash);
                            Start();
                            break;
                        case CarEventType.CrashComplete:
                            // Crash animation done - set to Crashed state, awaiting manual restart
                            _vibration?.StopEffect(VibrationEffectType.Crash);
                            _state = CarState.Crashed;
                            break;
                        case CarEventType.InGear:
                            _switchingGear = 0;
                            break;
                        case CarEventType.StopVibration:
                            if (e.Effect.HasValue)
                                _vibration?.StopEffect(e.Effect.Value);
                            break;
                        case CarEventType.StopBumpVibration:
                            _vibration?.StopEffect(VibrationEffectType.BumpLeft);
                            _vibration?.StopEffect(VibrationEffectType.BumpRight);
                            break;
                    }
                    _events.RemoveAt(i);
                }
            }
        }

        public void BrakeSound()
        {
            switch (_surface)
            {
                case TrackSurface.Asphalt:
                    if (!_soundBrake.IsPlaying)
                    {
                        SetSurfaceLoopVolumePercent(_soundAsphalt, 90);
                        _soundBrake.Play(loop: true);
                    }
                    break;
                case TrackSurface.Gravel:
                    if (_soundBrake.IsPlaying)
                        _soundBrake.Stop();
                    if (_speed <= 50.0f)
                        SetSurfaceLoopVolumePercent(_soundGravel, (int)(100 - (10 - (_speed / 5))));
                    else
                        SetSurfaceLoopVolumePercent(_soundGravel, 100);
                    break;
                case TrackSurface.Water:
                    if (_soundBrake.IsPlaying)
                        _soundBrake.Stop();
                    if (_speed <= 50.0f)
                        SetSurfaceLoopVolumePercent(_soundWater, (int)(100 - (10 - (_speed / 5))));
                    else
                        SetSurfaceLoopVolumePercent(_soundWater, 100);
                    break;
                case TrackSurface.Sand:
                    if (_soundBrake.IsPlaying)
                        _soundBrake.Stop();
                    if (_speed <= 50.0f)
                        SetSurfaceLoopVolumePercent(_soundSand, (int)(100 - (10 - (_speed / 5))));
                    else
                        SetSurfaceLoopVolumePercent(_soundSand, 100);
                    break;
                case TrackSurface.Snow:
                    if (_soundBrake.IsPlaying)
                        _soundBrake.Stop();
                    if (_speed <= 50.0f)
                        SetSurfaceLoopVolumePercent(_soundSnow, (int)(100 - (10 - (_speed / 5))));
                    else
                        SetSurfaceLoopVolumePercent(_soundSnow, 100);
                    break;
            }
        }

        public void BrakeCurveSound()
        {
            switch (_surface)
            {
                case TrackSurface.Asphalt:
                    if (_soundBrake.IsPlaying)
                        _soundBrake.Stop();
                    SetSurfaceLoopVolumePercent(_soundAsphalt, 92 * Math.Abs(_currentSteering) / 100);
                    break;
                case TrackSurface.Gravel:
                    if (_soundBrake.IsPlaying)
                        _soundBrake.Stop();
                    SetSurfaceLoopVolumePercent(_soundGravel, 92 * Math.Abs(_currentSteering) / 100);
                    break;
                case TrackSurface.Water:
                    if (_soundBrake.IsPlaying)
                        _soundBrake.Stop();
                    SetSurfaceLoopVolumePercent(_soundWater, 92 * Math.Abs(_currentSteering) / 100);
                    break;
                case TrackSurface.Sand:
                    if (_soundBrake.IsPlaying)
                        _soundBrake.Stop();
                    SetSurfaceLoopVolumePercent(_soundSand, 92 * Math.Abs(_currentSteering) / 100);
                    break;
                case TrackSurface.Snow:
                    if (_soundBrake.IsPlaying)
                        _soundBrake.Stop();
                    SetSurfaceLoopVolumePercent(_soundSnow, 92 * Math.Abs(_currentSteering) / 100);
                    break;
            }
        }

        public void Evaluate(Track.Road road)
        {
            var roadWidth = road.Right - road.Left;
            if (roadWidth > 0f)
                _laneWidth = roadWidth;
            else
                roadWidth = _laneWidth;

            var updateAudioThisFrame = true;

            if (_state == CarState.Stopped || _state == CarState.Starting || _state == CarState.Crashed)
            {
                if (updateAudioThisFrame)
                {
                    _relPos = roadWidth <= 0f
                        ? 0.5f
                        : (_positionX - road.Left) / roadWidth;
                    _panPos = CalculatePan(_relPos);
                    _soundStart.SetPanPercent(_panPos);
                    _soundHorn.SetPanPercent(_panPos);
                    _soundWipers?.SetPanPercent(_panPos);
                    UpdateSpatialAudio(road);
                }
            }

            if (_state == CarState.Running && _started())
            {
                if (updateAudioThisFrame)
                {
                    if (_surface == TrackSurface.Asphalt && road.Surface != TrackSurface.Asphalt)
                    {
                        _soundAsphalt.Stop();
                        SwitchSurfaceSound(road.Surface);
                    }
                    else if (_surface == TrackSurface.Gravel && road.Surface != TrackSurface.Gravel)
                    {
                        _soundGravel.Stop();
                        SwitchSurfaceSound(road.Surface);
                    }
                    else if (_surface == TrackSurface.Water && road.Surface != TrackSurface.Water)
                    {
                        _soundWater.Stop();
                        SwitchSurfaceSound(road.Surface);
                    }
                    else if (_surface == TrackSurface.Sand && road.Surface != TrackSurface.Sand)
                    {
                        _soundSand.Stop();
                        SwitchSurfaceSound(road.Surface);
                    }
                    else if (_surface == TrackSurface.Snow && road.Surface != TrackSurface.Snow)
                    {
                        _soundSnow.Stop();
                        SwitchSurfaceSound(road.Surface);
                    } 

                    _surface = road.Surface;
                    _relPos = roadWidth <= 0f
                        ? 0.5f
                        : (_positionX - road.Left) / roadWidth;
                    _panPos = CalculatePan(_relPos);
                    ApplyPan(_panPos);
                    UpdateSpatialAudio(road);

                    if (_vibration != null)
                    {
                        if (_relPos < 0.05 && _speed > _topSpeed / 10)
                            _vibration.PlayEffect(VibrationEffectType.CurbLeft);
                        else
                            _vibration.StopEffect(VibrationEffectType.CurbLeft);

                        if (_relPos > 0.95 && _speed > _topSpeed / 10)
                            _vibration.PlayEffect(VibrationEffectType.CurbRight);
                        else
                            _vibration.StopEffect(VibrationEffectType.CurbRight);
                    }
                    if (_relPos < 0 || _relPos > 1)
                    {
                        var fullCrash = _gear > 1 || _speed >= 50.0f;
                        if (fullCrash)
                            Crash();
                        else
                            MiniCrash((road.Right + road.Left) / 2);
                    }
                }
            }
            else if (_state == CarState.Crashing)
            {
                _positionX = (road.Right + road.Left) / 2;
                if (updateAudioThisFrame)
                {
                    _relPos = roadWidth <= 0f
                        ? 0.5f
                        : (_positionX - road.Left) / roadWidth;
                    _panPos = CalculatePan(_relPos);
                    _soundStart.SetPanPercent(_panPos);
                    _soundHorn.SetPanPercent(_panPos);
                    _soundWipers?.SetPanPercent(_panPos);
                    UpdateSpatialAudio(road);
                }
            }
            _frame++;
        }

        public bool Backfiring() => AnyBackfirePlaying();

        public void Pause()
        {
            _soundEngine.Stop();
            _soundThrottle?.Stop();
            if (_soundBrake.IsPlaying)
                _soundBrake.Stop();
            if (_soundHorn.IsPlaying)
                _soundHorn.Stop();
            StopResetBackfireVariants();
            _soundWipers?.Stop();
            switch (_surface)
            {
                case TrackSurface.Asphalt:
                    _soundAsphalt.Stop();
                    break;
                case TrackSurface.Gravel:
                    _soundGravel.Stop();
                    break;
                case TrackSurface.Water:
                    _soundWater.Stop();
                    break;
                case TrackSurface.Sand:
                    _soundSand.Stop();
                    break;
                case TrackSurface.Snow:
                    _soundSnow.Stop();
                    break;
            }
        }

        public void Unpause()
        {
            _soundEngine.Play(loop: true);
            _soundThrottle?.Play(loop: true);
            _soundWipers?.Play(loop: true);
            switch (_surface)
            {
                case TrackSurface.Asphalt:
                    _soundAsphalt.Play(loop: true);
                    break;
                case TrackSurface.Gravel:
                    _soundGravel.Play(loop: true);
                    break;
                case TrackSurface.Water:
                    _soundWater.Play(loop: true);
                    break;
                case TrackSurface.Sand:
                    _soundSand.Play(loop: true);
                    break;
                case TrackSurface.Snow:
                    _soundSnow.Play(loop: true);
                    break;
            }
        }

        public void Dispose()
        {
            StopAllVibrations();
            _soundEngine.Dispose();
            _soundThrottle?.Dispose();
            _soundHorn.Dispose();
            _soundStart.Dispose();
            DisposeSoundVariants(_soundCrashVariants);
            _soundBrake.Dispose();
            _soundAsphalt.Dispose();
            _soundGravel.Dispose();
            _soundWater.Dispose();
            _soundSand.Dispose();
            _soundSnow.Dispose();
            _soundMiniCrash.Dispose();
            _soundWipers?.Dispose();
            _soundBump.Dispose();
            _soundBadSwitch.Dispose();
            DisposeSoundVariants(_soundBackfireVariants);
        }

        private void StopAllVibrations()
        {
            if (_vibration == null)
                return;
            foreach (VibrationEffectType effect in Enum.GetValues(typeof(VibrationEffectType)))
                _vibration.StopEffect(effect);
        }

        private void PushEvent(CarEventType type, float time, VibrationEffectType? effect = null)
        {
            _events.Add(new CarEvent
            {
                Type = type,
                Time = _currentTime() + time,
                Effect = effect
            });
        }

        private void UpdateEngineFreq()
        {
            var gearForSound = _gear;
            if (gearForSound > _gears)
                gearForSound = _gears;
            if (gearForSound < 1)
                gearForSound = 1;

            UpdateEngineFreqForGear(gearForSound);
        }

        private void UpdateEngineFreqManual()
        {
            var driveGear = GetDriveGear();
            var gearRange = _engine.GetGearRangeKmh(driveGear);
            var gearMin = _engine.GetGearMinSpeedKmh(driveGear);

            if (driveGear == FirstForwardGear)
            {
                // Gear 1: frequency scales with speed relative to gear range   
                if (_speed < (4.0f / 3.0f) * gearRange)
                {
                    _frequency = _idleFreq + (int)((_speed * 3.0f / (2.0f * gearRange)) * (_topFreq - _idleFreq));
                }
                else
                {
                    // Cap at 2x the frequency range above idle when speed exceeds gear capability
                    _frequency = _idleFreq + 2 * (_topFreq - _idleFreq);        
                }
            }
            else
            {
                // Higher gears: frequency = (speed / shiftPoint) * topFreq     
                // where shiftPoint = gearMin + (2/3) * gearRange
                var shiftPoint = gearMin + ((2.0f / 3.0f) * gearRange);
                if (shiftPoint > 0f)
                    _frequency = (int)((_speed / shiftPoint) * _topFreq);
                else
                    _frequency = _idleFreq;

                // Clamp frequency to valid range
                if (_frequency > 2 * _topFreq)
                    _frequency = 2 * _topFreq;
                if (_frequency < _idleFreq / 2)
                    _frequency = _idleFreq / 2;
            }

            // Smooth gear transition
            if (_switchingGear != 0)
                _frequency = (2 * _prevFrequency + _frequency) / 3;

            // Apply frequency change to sound
            if (_frequency != _prevFrequency)
            {
                _soundEngine.SetFrequency(_frequency);
                if (_soundThrottle != null)
                {
                    if ((int)_throttleVolume != (int)_prevThrottleVolume)
                    {
                        SetPlayerEngineVolumePercent(_soundThrottle, (int)_throttleVolume);
                        _prevThrottleVolume = _throttleVolume;
                    }
                    _soundThrottle.SetFrequency(_frequency);
                }
                _prevFrequency = _frequency;
            }
        }

        private void UpdateEngineFreqForGear(int gear)
        {
            var clampedGear = gear;
            if (clampedGear > _gears)
                clampedGear = _gears;
            if (clampedGear < 1)
                clampedGear = 1;

            var gearRange = _engine.GetGearRangeKmh(clampedGear);
            var gearMin = _engine.GetGearMinSpeedKmh(clampedGear);

            if (clampedGear == 1)
            {
                var gearSpeed = gearRange <= 0f ? 0f : Math.Min(1.0f, _speed / gearRange);
                _frequency = (int)(gearSpeed * (_topFreq - _idleFreq)) + _idleFreq;
            }
            else
            {
                var gearSpeed = (_speed - gearMin) / (float)gearRange;
                if (gearSpeed <= 0f)
                {
                    _frequency = _idleFreq;
                    if (_soundBackfireVariants.Length > 0 && _backfirePlayedAuto)
                        _backfirePlayedAuto = false;
                }
                else
                {
                    if (gearSpeed > 1.0f)
                        gearSpeed = 1.0f;
                    if (gearSpeed < 0.07f)
                    {
                        _frequency = (int)(((0.07f - gearSpeed) / 0.07f) * (_topFreq - _shiftFreq) + _shiftFreq);
                        if (_soundBackfireVariants.Length > 0)
                        {
                            if (!_backfirePlayedAuto)
                            {
                                if (Algorithm.RandomInt(5) == 1 && !AnyBackfirePlaying())
                                    PlayRandomBackfire();
                            }
                            _backfirePlayedAuto = true;
                        }
                    }
                    else
                    {
                        _frequency = (int)(gearSpeed * (_topFreq - _shiftFreq) + _shiftFreq);
                        if (_soundBackfireVariants.Length > 0 && _backfirePlayedAuto)
                            _backfirePlayedAuto = false;
                    }
                }
            }

            if (_switchingGear != 0)
                _frequency = (2 * _prevFrequency + _frequency) / 3;
            if (_frequency != _prevFrequency)
            {
                _soundEngine.SetFrequency(_frequency);
                if (_soundThrottle != null)
                {
                    if ((int)_throttleVolume != (int)_prevThrottleVolume)
                    {
                        SetPlayerEngineVolumePercent(_soundThrottle, (int)_throttleVolume);
                        _prevThrottleVolume = _throttleVolume;
                    }
                    _soundThrottle.SetFrequency(_frequency);
                }
                _prevFrequency = _frequency;
            }
        }

        private int CalculateAcceleration()
        {
            var driveGear = GetDriveGear();
            var gearRange = _engine.GetGearRangeKmh(driveGear);
            var gearMin = _engine.GetGearMinSpeedKmh(driveGear);
            var gearCenter = gearMin + (gearRange * 0.18f);
            _speedDiff = _speed - gearCenter;
            var relSpeedDiff = _speedDiff / gearRange;
            if (Math.Abs(relSpeedDiff) < 1.9f)
            {
                var acceleration = (int)(100.0f * (0.5f + Math.Cos(relSpeedDiff * Math.PI * 0.5f)));
                return acceleration < 5 ? 5 : acceleration;
            }
            else
            {
                var acceleration = (int)(100.0f * (0.5f + Math.Cos(0.95f * Math.PI)));
                return acceleration < 5 ? 5 : acceleration;
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static float SanitizeFinite(float value, float fallback)
        {
            return IsFinite(value) ? value : fallback;
        }

        private float CalculateDriveRpm(float speedMps, float throttle)
        {
            var wheelCircumference = _wheelRadiusM * 2.0f * (float)Math.PI;
            var gearRatio = _gear == ReverseGear ? _reverseGearRatio : _engine.GetGearRatio(GetDriveGear());
            var speedBasedRpm = wheelCircumference > 0f
                ? (speedMps / wheelCircumference) * 60f * gearRatio * _finalDriveRatio
                : 0f;
            var launchTarget = _idleRpm + (throttle * (_launchRpm - _idleRpm));
            var rpm = Math.Max(speedBasedRpm, launchTarget);
            if (rpm < _idleRpm)
                rpm = _idleRpm;
            if (rpm > _revLimiter)
                rpm = _revLimiter;
            return rpm;
        }

        private void UpdateAutomaticGear(float elapsed, float speedMps, float throttle, float surfaceTractionMod, float longitudinalGripFactor)
        {
            if (_gears <= 1)
                return;

            if (_autoShiftCooldown > 0f)
            {
                _autoShiftCooldown -= elapsed;
                return;
            }

            var currentAccel = ComputeNetAccelForGear(_gear, speedMps, throttle, surfaceTractionMod, longitudinalGripFactor);
            var currentRpm = SpeedToRpm(speedMps, _gear);
            var upAccel = _gear < _gears
                ? ComputeNetAccelForGear(_gear + 1, speedMps, throttle, surfaceTractionMod, longitudinalGripFactor)
                : float.NegativeInfinity;
            var downAccel = _gear > 1
                ? ComputeNetAccelForGear(_gear - 1, speedMps, throttle, surfaceTractionMod, longitudinalGripFactor)
                : float.NegativeInfinity;

            var decision = AutomaticTransmissionLogic.Decide(
                new AutomaticShiftInput(
                    _gear,
                    _gears,
                    speedMps,
                    _topSpeed / 3.6f,
                    _idleRpm,
                    _revLimiter,
                    currentRpm,
                    currentAccel,
                    upAccel,
                    downAccel),
                _transmissionPolicy);

            if (decision.Changed)
                ShiftAutomaticGear(decision.NewGear, decision.CooldownSeconds);
        }

        private void ShiftAutomaticGear(int newGear, float cooldownSeconds)
        {
            if (newGear == _gear)
                return;
            var upshift = newGear > _gear;
            _switchingGear = upshift ? 1 : -1;
            _gear = newGear;
            var inGearDelay = upshift ? Math.Max(0.2f, cooldownSeconds) : 0.2f;
            PushEvent(CarEventType.InGear, inGearDelay);
            _autoShiftCooldown = Math.Max(0f, cooldownSeconds);
        }

        private float ComputeNetAccelForGear(int gear, float speedMps, float throttle, float surfaceTractionMod, float longitudinalGripFactor)
        {
            var rpm = SpeedToRpm(speedMps, gear);
            if (rpm <= 0f)
                return float.NegativeInfinity;
            if (rpm > _revLimiter && gear < _gears)
                return float.NegativeInfinity;

            var engineTorque = CalculateEngineTorqueNm(rpm) * throttle * _powerFactor;
            var gearRatio = _engine.GetGearRatio(gear);
            var wheelTorque = engineTorque * gearRatio * _finalDriveRatio * _drivetrainEfficiency;
            var wheelForce = wheelTorque / _wheelRadiusM;
            var tractionLimit = _tireGripCoefficient * surfaceTractionMod * _massKg * 9.80665f;
            if (wheelForce > tractionLimit)
                wheelForce = tractionLimit;
            wheelForce *= longitudinalGripFactor;

            var dragForce = 0.5f * 1.225f * _dragCoefficient * _frontalAreaM2 * speedMps * speedMps;
            var rollingForce = _rollingResistanceCoefficient * _massKg * 9.80665f;
            var netForce = wheelForce - dragForce - rollingForce;
            return netForce / _massKg;
        }

        private float SpeedToRpm(float speedMps, int gear)
        {
            var wheelCircumference = _wheelRadiusM * 2.0f * (float)Math.PI;
            if (wheelCircumference <= 0f)
                return 0f;
            var gearRatio = _engine.GetGearRatio(gear);
            return (speedMps / wheelCircumference) * 60f * gearRatio * _finalDriveRatio;
        }

        private float CalculateEngineTorqueNm(float rpm)
        {
            if (_peakTorqueNm <= 0f)
                return 0f;
            var clampedRpm = Math.Max(_idleRpm, Math.Min(_revLimiter, rpm));
            if (clampedRpm <= _peakTorqueRpm)
            {
                var denom = _peakTorqueRpm - _idleRpm;
                var t = denom > 0f ? (clampedRpm - _idleRpm) / denom : 0f;
                return SmoothStep(_idleTorqueNm, _peakTorqueNm, t);
            }
            else
            {
                var denom = _revLimiter - _peakTorqueRpm;
                var t = denom > 0f ? (clampedRpm - _peakTorqueRpm) / denom : 0f;
                return SmoothStep(_peakTorqueNm, _redlineTorqueNm, t);
            }
        }

        private static float SmoothStep(float a, float b, float t)
        {
            var clamped = Math.Max(0f, Math.Min(1f, t));
            clamped = clamped * clamped * (3f - 2f * clamped);
            return a + (b - a) * clamped;
        }

        private float CalculateBrakeDecel(float brakeInput, float surfaceDecelMod)
        {
            if (brakeInput <= 0f)
                return 0f;
            var grip = Math.Max(0.1f, _tireGripCoefficient * surfaceDecelMod);
            var decelMps2 = brakeInput * _brakeStrength * grip * 9.80665f;
            return decelMps2 * 3.6f;
        }

        private float CalculateEngineBrakingDecel(float surfaceDecelMod)
        {
            if (_engineBrakingTorqueNm <= 0f || _massKg <= 0f || _wheelRadiusM <= 0f)
                return 0f;
            var rpmRange = _revLimiter - _idleRpm;
            if (rpmRange <= 0f)
                return 0f;
            var rpmFactor = (_engine.Rpm - _idleRpm) / rpmRange;
            if (rpmFactor <= 0f)
                return 0f;
            rpmFactor = Math.Max(0f, Math.Min(1f, rpmFactor));
            var gearRatio = _gear == ReverseGear ? _reverseGearRatio : _engine.GetGearRatio(GetDriveGear());
            var drivelineTorque = _engineBrakingTorqueNm * _engineBraking * rpmFactor;
            var wheelTorque = drivelineTorque * gearRatio * _finalDriveRatio * _drivetrainEfficiency;
            var wheelForce = wheelTorque / _wheelRadiusM;
            var decelMps2 = (wheelForce / _massKg) * surfaceDecelMod;
            return Math.Max(0f, decelMps2 * 3.6f);
        }

        private float GetLapStartPosition(float position)
        {
            var lapLength = _track.Length;
            if (lapLength <= 0f)
                return 0f;
            var lapIndex = (float)Math.Floor(position / lapLength);
            if (lapIndex < 0f)
                lapIndex = 0f;
            return lapIndex * lapLength;
        }

        private void UpdateSoundRoad()
        {
            _surfaceFrequency = (int)(_speed * 500);
            if (_surfaceFrequency != _prevSurfaceFrequency)
            {
                switch (_surface)
                {
                    case TrackSurface.Asphalt:
                        _soundAsphalt.SetFrequency(Math.Min(_surfaceFrequency, MaxSurfaceFreq));
                        break;
                    case TrackSurface.Gravel:
                        _soundGravel.SetFrequency(Math.Min(_surfaceFrequency, MaxSurfaceFreq));
                        break;
                    case TrackSurface.Water:
                        _soundWater.SetFrequency(Math.Min(_surfaceFrequency, MaxSurfaceFreq));
                        break;
                    case TrackSurface.Sand:
                        _soundSand.SetFrequency((int)(_surfaceFrequency / 2.5f));
                        break;
                    case TrackSurface.Snow:
                        _soundSnow.SetFrequency(Math.Min(_surfaceFrequency, MaxSurfaceFreq));
                        break;
                }
                _prevSurfaceFrequency = _surfaceFrequency;
            }
        }

        private void SwitchSurfaceSound(TrackSurface surface)
        {
            switch (surface)
            {
                case TrackSurface.Gravel:
                    _soundGravel.SetFrequency(Math.Min(_surfaceFrequency, MaxSurfaceFreq));
                    _soundGravel.Play(loop: true);
                    break;
                case TrackSurface.Water:
                    _soundWater.SetFrequency(Math.Min(_surfaceFrequency, MaxSurfaceFreq));
                    _soundWater.Play(loop: true);
                    break;
                case TrackSurface.Sand:
                    _soundSand.SetFrequency((int)(_surfaceFrequency / 2.5f));
                    _soundSand.Play(loop: true);
                    break;
                case TrackSurface.Snow:
                    _soundSnow.SetFrequency(Math.Min(_surfaceFrequency, MaxSurfaceFreq));
                    _soundSnow.Play(loop: true);
                    break;
                case TrackSurface.Asphalt:
                    _soundAsphalt.SetFrequency(Math.Min(_surfaceFrequency, MaxSurfaceFreq));
                    _soundAsphalt.Play(loop: true);
                    break;
            }
        }

        private void ApplyPan(int pan)
        {
            _soundHorn.SetPanPercent(pan);
            _soundBrake.SetPanPercent(pan);
            _soundBackfire?.SetPanPercent(pan);
            _soundWipers?.SetPanPercent(pan);
            switch (_surface)
            {
                case TrackSurface.Asphalt:
                    _soundAsphalt.SetPanPercent(pan);
                    break;
                case TrackSurface.Gravel:
                    _soundGravel.SetPanPercent(pan);
                    break;
                case TrackSurface.Water:
                    _soundWater.SetPanPercent(pan);
                    break;
                case TrackSurface.Sand:
                    _soundSand.SetPanPercent(pan);
                    break;
                case TrackSurface.Snow:
                    _soundSnow.SetPanPercent(pan);
                    break;
            }
        }

        private static int CalculatePan(float relPos)
        {
            var pan = (relPos - 0.5f) * 200.0f;
            if (pan < -100.0f) pan = -100.0f;
            if (pan > 100.0f) pan = 100.0f;
            return (int)pan;
        }

        private void RefreshCategoryVolumes(bool force = false)
        {
            var enginePercent = _settings.AudioVolumes?.PlayerVehicleEnginePercent ?? 100;
            var eventsPercent = _settings.AudioVolumes?.PlayerVehicleEventsPercent ?? 100;
            var surfacePercent = _settings.AudioVolumes?.SurfaceLoopsPercent ?? 70;

            if (!force &&
                enginePercent == _lastPlayerEngineVolumePercent &&
                eventsPercent == _lastPlayerEventsVolumePercent &&
                surfacePercent == _lastSurfaceLoopVolumePercent)
            {
                return;
            }

            _lastPlayerEngineVolumePercent = enginePercent;
            _lastPlayerEventsVolumePercent = eventsPercent;
            _lastSurfaceLoopVolumePercent = surfacePercent;

            SetPlayerEngineVolumePercent(_soundEngine, 90);
            SetPlayerEngineVolumePercent(_soundStart, 100);
            SetPlayerEngineVolumePercent(_soundThrottle, (int)Math.Round(_throttleVolume));
            SetPlayerEventVolumePercent(_soundHorn, 100);
            SetPlayerEventVolumePercent(_soundBrake, 100);
            SetPlayerEventVolumePercent(_soundMiniCrash, 100);
            SetPlayerEventVolumePercent(_soundBump, 100);
            SetPlayerEventVolumePercent(_soundBadSwitch, 100);
            SetPlayerEventVolumePercent(_soundWipers, 100);
            SetPlayerEventVolumePercent(_soundCrash, 100);
            SetPlayerEventVolumePercent(_soundBackfire, 100);
            for (var i = 0; i < _soundCrashVariants.Length; i++)
                SetPlayerEventVolumePercent(_soundCrashVariants[i], 100);
            for (var i = 0; i < _soundBackfireVariants.Length; i++)
                SetPlayerEventVolumePercent(_soundBackfireVariants[i], 100);

            SetSurfaceLoopVolumePercent(_soundAsphalt, 90);
            SetSurfaceLoopVolumePercent(_soundGravel, 90);
            SetSurfaceLoopVolumePercent(_soundWater, 90);
            SetSurfaceLoopVolumePercent(_soundSand, 90);
            SetSurfaceLoopVolumePercent(_soundSnow, 90);
        }

        private void SetPlayerEngineVolumePercent(AudioSourceHandle? sound, int percent)
        {
            sound.SetVolumePercent(_settings, AudioVolumeCategory.PlayerVehicleEngine, percent);
        }

        private void SetPlayerEventVolumePercent(AudioSourceHandle? sound, int percent)
        {
            sound.SetVolumePercent(_settings, AudioVolumeCategory.PlayerVehicleEvents, percent);
        }

        private void SetSurfaceLoopVolumePercent(AudioSourceHandle? sound, int percent)
        {
            sound.SetVolumePercent(_settings, AudioVolumeCategory.SurfaceLoops, percent);
        }

        private AudioSourceHandle CreateRequiredSound(string? path, bool looped = false, bool spatialize = true, bool allowHrtf = true)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("Sound path not provided.");
            if (!File.Exists(path))
                throw new FileNotFoundException("Sound file not found.", path);
            if (!spatialize)
            {
                return looped
                    ? _audio.CreateLoopingSource(path!, useHrtf: false)
                    : _audio.CreateSource(path!, streamFromDisk: true, useHrtf: false);
            }

            return looped
                ? _audio.CreateLoopingSpatialSource(path!, allowHrtf: allowHrtf)
                : _audio.CreateSpatialSource(path!, streamFromDisk: true, allowHrtf: allowHrtf);
        }

        private AudioSourceHandle[] CreateRequiredSoundVariants(IReadOnlyList<string>? paths, string? fallbackSinglePath)
        {
            if (paths != null && paths.Count > 0)
            {
                var result = new AudioSourceHandle[paths.Count];
                for (var i = 0; i < paths.Count; i++)
                    result[i] = CreateRequiredSound(paths[i]);
                return result;
            }

            return new[] { CreateRequiredSound(fallbackSinglePath) };
        }

        private AudioSourceHandle[] CreateOptionalSoundVariants(IReadOnlyList<string>? paths, string? fallbackSinglePath)
        {
            if (paths != null && paths.Count > 0)
            {
                var items = new List<AudioSourceHandle>();
                for (var i = 0; i < paths.Count; i++)
                {
                    var sound = TryCreateSound(paths[i]);
                    if (sound != null)
                        items.Add(sound);
                }
                return items.ToArray();
            }

            var single = TryCreateSound(fallbackSinglePath);
            return single == null ? Array.Empty<AudioSourceHandle>() : new[] { single };
        }

        private AudioSourceHandle SelectRandomCrashHandle()
        {
            if (_soundCrashVariants.Length == 0)
                return _soundCrash;
            return _soundCrashVariants[Algorithm.RandomInt(_soundCrashVariants.Length)];
        }

        private bool AnyBackfirePlaying()
        {
            for (var i = 0; i < _soundBackfireVariants.Length; i++)
            {
                if (_soundBackfireVariants[i].IsPlaying)
                    return true;
            }
            return false;
        }

        private void PlayRandomBackfire()
        {
            if (_soundBackfireVariants.Length == 0)
                return;
            _soundBackfire = _soundBackfireVariants[Algorithm.RandomInt(_soundBackfireVariants.Length)];
            _soundBackfire.Play(loop: false);
        }

        private void StopResetBackfireVariants()
        {
            for (var i = 0; i < _soundBackfireVariants.Length; i++)
            {
                if (_soundBackfireVariants[i].IsPlaying)
                    _soundBackfireVariants[i].Stop();
                _soundBackfireVariants[i].SeekToStart();
            }
        }

        private static void DisposeSoundVariants(AudioSourceHandle[] sounds)
        {
            for (var i = 0; i < sounds.Length; i++)
            {
                sounds[i].Stop();
                sounds[i].Dispose();
            }
        }

        private AudioSourceHandle? TryCreateSound(string? path, bool looped = false, bool spatialize = true, bool allowHrtf = true)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;
            if (!spatialize)
            {
                return looped
                    ? _audio.CreateLoopingSource(path!, useHrtf: false)
                    : _audio.CreateSource(path!, streamFromDisk: true, useHrtf: false);
            }

            return looped
                ? _audio.CreateLoopingSpatialSource(path!, allowHrtf: allowHrtf)
                : _audio.CreateSpatialSource(path!, streamFromDisk: true, allowHrtf: allowHrtf);
        }

        private void UpdateSpatialAudio(Track.Road road)
        {
            var elapsed = _lastAudioElapsed;
            if (elapsed <= 0f)
                return;

            var worldX = _positionX;
            var worldZ = _positionY;

            var velocity = Vector3.Zero;
            var velUnits = Vector3.Zero;
            if (_audioInitialized && elapsed > 0f)
            {
                velUnits = new Vector3((worldX - _lastAudioX) / elapsed, 0f, (worldZ - _lastAudioY) / elapsed);
                velocity = AudioWorld.ToMeters(velUnits);
            }
            _lastAudioX = worldX;
            _lastAudioY = worldZ;
            _audioInitialized = true;

            var left = Math.Min(road.Left, road.Right);
            var right = Math.Max(road.Left, road.Right);
            var centerX = (left + right) * 0.5f;
            if (!IsFinite(centerX))
                centerX = worldX;

            var trackHalfWidth = (right - left) * 0.5f;
            if (!IsFinite(trackHalfWidth) || trackHalfWidth <= 0.01f)
                trackHalfWidth = Math.Max(0.01f, _laneWidth);

            var clampedX = worldX;
            var minX = centerX - trackHalfWidth;
            var maxX = centerX + trackHalfWidth;
            if (clampedX < minX)
                clampedX = minX;
            else if (clampedX > maxX)
                clampedX = maxX;

            var normalized = (clampedX - centerX) / trackHalfWidth;
            if (!IsFinite(normalized))
                normalized = 0f;
            if (normalized < -1f)
                normalized = -1f;
            else if (normalized > 1f)
                normalized = 1f;

            var driverOffsetX = -_widthM * 0.25f;
            var driverOffsetZ = _lengthM * 0.1f;
            var listenerX = worldX + driverOffsetX;
            var listenerZ = worldZ + driverOffsetZ;

            var engineOffsetZ = _lengthM * 0.35f;
            var engineForwardOffset = engineOffsetZ - driverOffsetZ;
            if (engineForwardOffset < 0.01f)
                engineForwardOffset = 0.01f;

            var vehicleForwardOffset = -driverOffsetZ;
            if (Math.Abs(vehicleForwardOffset) < 0.01f)
                vehicleForwardOffset = vehicleForwardOffset >= 0f ? 0.01f : -0.01f;

            var angle = normalized * (float)(Math.PI / 2.0);

            Vector3 enginePos;
            Vector3 brakePos;
            Vector3 vehiclePos;

            enginePos = PlaceOnArc(listenerX, listenerZ, angle, engineForwardOffset);
            var brakeForwardOffset = Math.Max(0.01f, engineForwardOffset * 0.6f);
            brakePos = PlaceOnArc(listenerX, listenerZ, angle, brakeForwardOffset);
            vehiclePos = PlaceOnArc(listenerX, listenerZ, angle, vehicleForwardOffset);
            var crashPos = vehiclePos;
            if (_state == CarState.Crashing || _state == CarState.Crashed || _state == CarState.Starting)
            {
                crashPos = new Vector3(
                    AudioWorld.ToMeters(listenerX),
                    0f,
                    AudioWorld.ToMeters(listenerZ));
            }

            SetSpatial(_soundEngine, enginePos, velocity);
            SetSpatial(_soundThrottle, enginePos, velocity);
            SetSpatial(_soundHorn, enginePos, velocity);
            SetSpatial(_soundBrake, brakePos, velocity);
            SetSpatial(_soundBackfire, enginePos, velocity);
            SetSpatial(_soundStart, enginePos, velocity);
            SetSpatial(_soundCrash, crashPos, velocity);
            SetSpatial(_soundMiniCrash, vehiclePos, velocity);
            SetSpatial(_soundBump, vehiclePos, velocity);
            SetSpatial(_soundBadSwitch, enginePos, velocity);
            SetSpatial(_soundWipers, vehiclePos, velocity);

            switch (_surface)
            {
                case TrackSurface.Asphalt:
                    SetSpatial(_soundAsphalt, vehiclePos, velocity);
                    break;
                case TrackSurface.Gravel:
                    SetSpatial(_soundGravel, vehiclePos, velocity);
                    break;
                case TrackSurface.Water:
                    SetSpatial(_soundWater, vehiclePos, velocity);
                    break;
                case TrackSurface.Sand:
                    SetSpatial(_soundSand, vehiclePos, velocity);
                    break;
                case TrackSurface.Snow:
                    SetSpatial(_soundSnow, vehiclePos, velocity);
                    break;
            }
        }

        private static Vector3 PlaceOnArc(float listenerX, float listenerZ, float angle, float forwardOffset)
        {
            var radius = Math.Abs(forwardOffset);
            if (radius < 0.01f)
                radius = 0.01f;

            var offsetX = (float)Math.Sin(angle) * radius;
            var offsetZ = (float)Math.Cos(angle) * radius;
            if (forwardOffset < 0f)
                offsetZ = -offsetZ;

            return new Vector3(
                AudioWorld.ToMeters(listenerX + offsetX),
                0f,
                AudioWorld.ToMeters(listenerZ + offsetZ));
        }
        private static void SetSpatial(AudioSourceHandle? sound, Vector3 position, Vector3 velocity)
        {
            if (sound == null)
                return;
            sound.SetPosition(position);
            sound.SetVelocity(velocity);
        }

        private int GetDriveGear()
        {
            return _gear < FirstForwardGear ? FirstForwardGear : _gear;
        }

        private sealed class CarEvent
        {
            public float Time { get; set; }
            public CarEventType Type { get; set; }
            public VibrationEffectType? Effect { get; set; }
        }

        private enum CarEventType
        {
            CarStart,
            CarRestart,
            CrashComplete,
            InGear,
            StopVibration,
            StopBumpVibration
        }
    }
}
