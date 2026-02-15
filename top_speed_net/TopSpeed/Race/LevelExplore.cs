using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using SharpDX.DirectInput;
using TopSpeed.Audio;
using TopSpeed.Core;
using TopSpeed.Data;
using TopSpeed.Input;
using TopSpeed.Speech;
using TopSpeed.Tracks.Acoustics;
using TopSpeed.Tracks.Areas;
using TopSpeed.Tracks.Guidance;
using TopSpeed.Tracks.Map;
using TopSpeed.Tracks.Rooms;
using TopSpeed.Tracks.Sectors;
using TopSpeed.Tracks.Surfaces;
using TopSpeed.Tracks.Topology;
using TopSpeed.Tracks.Walls;
using TS.Audio;

namespace TopSpeed.Race
{
    internal sealed class LevelExplore : IDisposable
    {
        private static readonly float[] StepSizes = { 1f, 5f, 10f, 20f, 30f, 50f, 100f };
        private const float ApproachBeaconRangeMeters = 50f;
        private const float DefaultApproachToleranceDegrees = 10f;
        private const float ExploreListenerHeightM = 1.0f;
        private const float MetersToFeet = 3.28084f;

        private readonly AudioManager _audio;
        private readonly SpeechService _speech;
        private readonly RaceSettings _settings;
        private readonly InputManager _input;
        private readonly TrackMap _map;

        private Vector3 _worldPosition;
        private int _stepIndex;
        private bool _initialized;
        private bool _exitRequested;
        private Vector3 _listenerForward = Vector3.UnitZ;
        private MapMovementState _mapState;
        private MapSnapshot _mapSnapshot;
        private float _headingDegrees;
        private TrackAreaManager? _areaManager;
        private TrackSectorManager? _sectorManager;
        private TrackSectorRuleManager? _sectorRuleManager;
        private TrackBranchManager? _branchManager;
        private TrackPortalManager? _portalManager;
        private TrackWallManager? _wallManager;
        private TrackSurfaceSystem? _surfaceSystem;
        private TrackSteamAudioScene? _steamAudioScene;
        private TrackApproachBeacon? _approachBeacon;
        private AudioSourceHandle? _soundBeacon;
        private string? _lastApproachPortalId;
        private string? _lastApproachHeading;
        private float _beaconCooldown;
        private RoomAcoustics _currentRoomAcoustics;
        private bool _hasRoomAcoustics;

        private Vector3 _lastListenerPosition;
        private bool _listenerInitialized;

        public LevelExplore(
            AudioManager audio,
            SpeechService speech,
            RaceSettings settings,
            InputManager input,
            string track)
        {
            _audio = audio ?? throw new ArgumentNullException(nameof(audio));
            _speech = speech ?? throw new ArgumentNullException(nameof(speech));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _input = input ?? throw new ArgumentNullException(nameof(input));

            if (!TrackMapLoader.TryResolvePath(track, out var mapPath))
                throw new FileNotFoundException("Track map not found.", track);

            _map = TrackMapLoader.Load(mapPath);
            _stepIndex = 1; // default 5 meters
        }

        public bool WantsExit => _exitRequested;

        public void Initialize()
        {
            _mapState = MapMovement.CreateStart(_map);
            _headingDegrees = _mapState.HeadingDegrees;
            _worldPosition = _mapState.WorldPosition;
            _listenerForward = MapMovement.HeadingVector(_headingDegrees);
            _areaManager = _map.BuildAreaManager();
            _portalManager = _map.BuildPortalManager();
            _sectorManager = new TrackSectorManager(_map.Sectors, _areaManager, _portalManager);
            _sectorRuleManager = new TrackSectorRuleManager(_map.Sectors, _portalManager);
            _branchManager = _map.BuildBranchManager();
            _wallManager = new TrackWallManager(_map.Geometries, _map.Walls);
            _surfaceSystem = _map.BuildSurfaceSystem();
            var steam = _audio.SteamAudio;
            if (steam != null)
            {
                _steamAudioScene?.Dispose();
                _steamAudioScene = SteamAudioSceneBuilder.Build(_map, steam);
                if (_steamAudioScene != null)
                    steam.SetScene(_steamAudioScene.Scene, _steamAudioScene.ProbeBatch, _steamAudioScene.BakedIdentifier, _steamAudioScene.HasBakedReflections);
            }
            _approachBeacon = new TrackApproachBeacon(_map, ApproachBeaconRangeMeters);
            InitializeBeacon();
            _mapSnapshot = BuildMapSnapshot(_worldPosition, _headingDegrees);
            _speech.Speak($"Track {FormatTrackName(_map.Name)}.");
            _speech.Speak($"Step {StepSizes[_stepIndex]:0.#} meters.");
            _initialized = true;
        }

        public void Run(float elapsed)
        {
            if (!_initialized)
                return;

            if (_input.WasPressed(Key.Escape))
                _exitRequested = true;

            HandleStepAdjust();
            HandleCoordinateKeys();
            HandleMovement();
            UpdateApproachGuidance(elapsed);
            UpdateAudioListener(elapsed);
        }

        public void Dispose()
        {
            if (_soundBeacon != null)
            {
                _soundBeacon.Stop();
                _soundBeacon.Dispose();
            }
            _steamAudioScene?.Dispose();
        }

        private void HandleStepAdjust()
        {
            if (!_input.WasPressed(Key.Back))
                return;

            var shift = _input.IsDown(Key.LeftShift) || _input.IsDown(Key.RightShift);
            if (shift)
            {
                if (_stepIndex > 0)
                    _stepIndex--;
            }
            else
            {
                if (_stepIndex < StepSizes.Length - 1)
                    _stepIndex++;
            }

            _speech.Speak($"Step {StepSizes[_stepIndex]:0.#} meters.");
        }

        private void HandleCoordinateKeys()
        {
            if (_input.WasPressed(Key.K))
                _speech.Speak($"Z {Math.Round(_worldPosition.Z, 2):0.##} meters.");
            if (_input.WasPressed(Key.L))
                _speech.Speak($"X {Math.Round(_worldPosition.X, 2):0.##} meters.");
            if (_input.WasPressed(Key.Semicolon))
                ReportHeight();
        }

        private void HandleMovement()
        {
            if (_input.WasPressed(Key.Up))
            {
                AttemptMoveMap(StepSizes[_stepIndex], MapMovement.HeadingFromDirection(MapDirection.North));
                return;
            }

            if (_input.WasPressed(Key.Down))
            {
                AttemptMoveMap(StepSizes[_stepIndex], MapMovement.HeadingFromDirection(MapDirection.South));
                return;
            }

            if (_input.WasPressed(Key.Left))
            {
                AttemptMoveMap(StepSizes[_stepIndex], MapMovement.HeadingFromDirection(MapDirection.West));
                return;
            }

            if (_input.WasPressed(Key.Right))
            {
                AttemptMoveMap(StepSizes[_stepIndex], MapMovement.HeadingFromDirection(MapDirection.East));
            }
        }

        private void AttemptMoveMap(float distanceMeters, float headingDegrees)
        {
            var delta = MapMovement.HeadingVector(headingDegrees) * distanceMeters;
            var nextWorld = _worldPosition + delta;
            if (TryGetWallCollision(_worldPosition, nextWorld))
            {
                _speech.Speak("Wall.");
                return;
            }
            if (!IsWithinTrack(nextWorld))
            {
                _speech.Speak("Track boundary.");
                return;
            }
            if (!AllowsSectorTransition(_worldPosition, nextWorld, headingDegrees, out var deniedReason))
            {
                _speech.Speak(deniedReason);
                return;
            }

            _worldPosition = nextWorld;
            _mapState.WorldPosition = nextWorld;
            _mapState.DistanceMeters += distanceMeters;
            _headingDegrees = MapMovement.NormalizeDegrees(headingDegrees);
            _mapState.HeadingDegrees = _headingDegrees;
            _listenerForward = MapMovement.HeadingVector(_headingDegrees);

            var previous = _mapSnapshot;
            var current = BuildMapSnapshot(nextWorld, _headingDegrees);
            AnnounceMapChanges(previous, current);
            _mapSnapshot = current;
        }

        private bool TryGetWallCollision(Vector3 fromWorld, Vector3 toWorld)
        {
            if (_wallManager == null || !_wallManager.HasWalls)
                return false;

            var from = new Vector2(fromWorld.X, fromWorld.Z);
            var to = new Vector2(toWorld.X, toWorld.Z);
            var delta = to - from;
            var distance = delta.Length();
            if (distance <= 0.001f)
                return false;

            var steps = Math.Max(1, (int)Math.Ceiling(distance / 1.0f));
            var step = delta / steps;
            var pos = from;
            for (var i = 0; i <= steps; i++)
            {
                if (TryFindWallAt(pos))
                    return true;
                pos += step;
            }

            return false;
        }

        private bool TryFindWallAt(Vector2 position)
        {
            if (_wallManager == null || !_wallManager.HasWalls)
                return false;
            foreach (var candidate in _wallManager.Walls)
            {
                if (candidate == null)
                    continue;
                if (candidate.CollisionMode == TrackWallCollisionMode.Pass)
                    continue;
                if (_wallManager.Contains(candidate, position))
                    return true;
            }
            return false;
        }

        private void UpdateAudioListener(float elapsed)
        {
            var forward = _listenerForward.LengthSquared() > 0.0001f ? Vector3.Normalize(_listenerForward) : Vector3.UnitZ;
            var up = Vector3.UnitY;
            var listenerPosition = _worldPosition + (up * ExploreListenerHeightM);
            if (TryGetCeilingHeight(listenerPosition, out var ceilingHeight))
            {
                var maxY = ceilingHeight - 0.1f;
                if (listenerPosition.Y > maxY)
                    listenerPosition = new Vector3(listenerPosition.X, maxY, listenerPosition.Z);
            }

            var velocity = Vector3.Zero;
            if (_listenerInitialized && elapsed > 0f)
                velocity = (listenerPosition - _lastListenerPosition) / elapsed;
            _lastListenerPosition = listenerPosition;
            _listenerInitialized = true;

            var position = AudioWorld.ToMeters(listenerPosition);
            var velocityMeters = AudioWorld.ToMeters(velocity);
            _audio.UpdateListener(position, forward, up, velocityMeters);
            UpdateRoomAcoustics(_worldPosition);
        }

        private void UpdateRoomAcoustics(Vector3 worldPosition)
        {
            var acoustics = ResolveRoomAcoustics(worldPosition);
            if (!_hasRoomAcoustics || !RoomAcousticsEquals(_currentRoomAcoustics, acoustics))
            {
                _audio.SetRoomAcoustics(acoustics);
                _currentRoomAcoustics = acoustics;
                _hasRoomAcoustics = true;
            }
        }

        private bool TryGetCeilingHeight(Vector3 worldPosition, out float ceilingHeight)
        {
            ceilingHeight = 0f;
            if (_areaManager == null)
                return false;

            var areas = _areaManager.FindAreasContaining(worldPosition);
            if (areas.Count == 0)
                return false;

            float? minCeiling = null;
            foreach (var area in areas)
            {
                if (area == null || !area.CeilingHeightMeters.HasValue)
                    continue;
                var value = area.CeilingHeightMeters.Value;
                if (!minCeiling.HasValue || value < minCeiling.Value)
                    minCeiling = value;
            }

            if (!minCeiling.HasValue)
                return false;

            ceilingHeight = minCeiling.Value;
            return true;
        }

        private RoomAcoustics ResolveRoomAcoustics(Vector3 worldPosition)
        {
            if (_areaManager == null)
                return RoomAcoustics.Default;

            var areas = _areaManager.FindAreasContaining(worldPosition);
            if (areas.Count == 0)
                return RoomAcoustics.Default;

            TrackAreaDefinition? roomArea = null;
            foreach (var candidate in areas)
            {
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.RoomId))
                    continue;
                roomArea = candidate;
            }

            if (roomArea == null || !TryResolveRoom(roomArea, out var room))
                return RoomAcoustics.Default;

            return new RoomAcoustics
            {
                HasRoom = true,
                ReverbTimeSeconds = room.ReverbTimeSeconds,
                ReverbGain = room.ReverbGain,
                ReflectionWet = room.ReflectionWet,
                HfDecayRatio = room.HfDecayRatio,
                EarlyReflectionsGain = room.EarlyReflectionsGain,
                LateReverbGain = room.LateReverbGain,
                Diffusion = room.Diffusion,
                AirAbsorptionScale = room.AirAbsorption,
                OcclusionScale = room.OcclusionScale,
                TransmissionScale = room.TransmissionScale,
                OcclusionOverride = room.OcclusionOverride,
                TransmissionOverrideLow = room.TransmissionOverrideLow,
                TransmissionOverrideMid = room.TransmissionOverrideMid,
                TransmissionOverrideHigh = room.TransmissionOverrideHigh,
                AirAbsorptionOverrideLow = room.AirAbsorptionOverrideLow,
                AirAbsorptionOverrideMid = room.AirAbsorptionOverrideMid,
                AirAbsorptionOverrideHigh = room.AirAbsorptionOverrideHigh
            };
        }

        private bool TryResolveRoom(TrackAreaDefinition area, out TrackRoomDefinition room)
        {
            room = null!;
            if (area == null || string.IsNullOrWhiteSpace(area.RoomId))
                return false;

            var id = area.RoomId!.Trim();
            if (!TryGetRoomById(id, out var baseRoom))
                return false;

            room = ApplyRoomOverrides(baseRoom, area.RoomOverrides);
            return true;
        }

        private bool TryGetRoomById(string id, out TrackRoomDefinition room)
        {
            room = null!;
            if (string.IsNullOrWhiteSpace(id))
                return false;

            foreach (var candidate in _map.Rooms)
            {
                if (candidate != null && string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    room = candidate;
                    return true;
                }
            }

            return TrackRoomLibrary.TryGetPreset(id, out room);
        }

        private static TrackRoomDefinition ApplyRoomOverrides(TrackRoomDefinition room, TrackRoomOverrides? overrides)
        {
            if (overrides == null || !overrides.HasAny)
                return room;

            return new TrackRoomDefinition(
                room.Id,
                room.Name,
                overrides.ReverbTimeSeconds ?? room.ReverbTimeSeconds,
                overrides.ReverbGain ?? room.ReverbGain,
                overrides.ReflectionWet ?? room.ReflectionWet,
                overrides.HfDecayRatio ?? room.HfDecayRatio,
                overrides.EarlyReflectionsGain ?? room.EarlyReflectionsGain,
                overrides.LateReverbGain ?? room.LateReverbGain,
                overrides.Diffusion ?? room.Diffusion,
                overrides.AirAbsorption ?? room.AirAbsorption,
                overrides.OcclusionScale ?? room.OcclusionScale,
                overrides.TransmissionScale ?? room.TransmissionScale,
                overrides.OcclusionOverride ?? room.OcclusionOverride,
                overrides.TransmissionOverrideLow ?? room.TransmissionOverrideLow,
                overrides.TransmissionOverrideMid ?? room.TransmissionOverrideMid,
                overrides.TransmissionOverrideHigh ?? room.TransmissionOverrideHigh,
                overrides.AirAbsorptionOverrideLow ?? room.AirAbsorptionOverrideLow,
                overrides.AirAbsorptionOverrideMid ?? room.AirAbsorptionOverrideMid,
                overrides.AirAbsorptionOverrideHigh ?? room.AirAbsorptionOverrideHigh);
        }

        private static bool RoomAcousticsEquals(RoomAcoustics left, RoomAcoustics right)
        {
            if (left.HasRoom != right.HasRoom)
                return false;

            if (!NearlyEqual(left.ReverbTimeSeconds, right.ReverbTimeSeconds)) return false;
            if (!NearlyEqual(left.ReverbGain, right.ReverbGain)) return false;
            if (!NearlyEqual(left.ReflectionWet, right.ReflectionWet)) return false;
            if (!NearlyEqual(left.HfDecayRatio, right.HfDecayRatio)) return false;
            if (!NearlyEqual(left.EarlyReflectionsGain, right.EarlyReflectionsGain)) return false;
            if (!NearlyEqual(left.LateReverbGain, right.LateReverbGain)) return false;
            if (!NearlyEqual(left.Diffusion, right.Diffusion)) return false;
            if (!NearlyEqual(left.AirAbsorptionScale, right.AirAbsorptionScale)) return false;
            if (!NearlyEqual(left.OcclusionScale, right.OcclusionScale)) return false;
            if (!NearlyEqual(left.TransmissionScale, right.TransmissionScale)) return false;

            if (!NullableEqual(left.OcclusionOverride, right.OcclusionOverride)) return false;
            if (!NullableEqual(left.TransmissionOverrideLow, right.TransmissionOverrideLow)) return false;
            if (!NullableEqual(left.TransmissionOverrideMid, right.TransmissionOverrideMid)) return false;
            if (!NullableEqual(left.TransmissionOverrideHigh, right.TransmissionOverrideHigh)) return false;
            if (!NullableEqual(left.AirAbsorptionOverrideLow, right.AirAbsorptionOverrideLow)) return false;
            if (!NullableEqual(left.AirAbsorptionOverrideMid, right.AirAbsorptionOverrideMid)) return false;
            if (!NullableEqual(left.AirAbsorptionOverrideHigh, right.AirAbsorptionOverrideHigh)) return false;

            return true;
        }

        private static bool NearlyEqual(float a, float b)
        {
            return Math.Abs(a - b) <= 0.001f;
        }

        private static bool NullableEqual(float? left, float? right)
        {
            if (left.HasValue != right.HasValue)
                return false;
            if (!left.HasValue)
                return true;
            return NearlyEqual(left.Value, right!.Value);
        }

        private MapSnapshot BuildMapSnapshot(Vector3 worldPosition, float headingDegrees)
        {
            var snapshot = new MapSnapshot
            {
                MaterialId = _map.DefaultMaterialId,
                Noise = _map.DefaultNoise,
                WidthMeters = Math.Max(0.5f, _map.DefaultWidthMeters),
                IsSafeZone = IsSafeZone(worldPosition),
                Zone = string.Empty
            };

            ApplyAreaSnapshotOverrides(worldPosition, headingDegrees, ref snapshot);
            ApplySectorSnapshotOverrides(worldPosition, headingDegrees, ref snapshot);
            return snapshot;
        }

        private void ApplySectorSnapshotOverrides(Vector3 worldPosition, float headingDegrees, ref MapSnapshot snapshot)
        {
            if (_sectorManager == null)
                return;

            if (!_sectorManager.TryLocate(worldPosition, headingDegrees, out var sector, out _, out _))
                return;

            snapshot.SectorId = sector.Id;
            snapshot.SectorType = sector.Type;

            if (_sectorRuleManager != null && _sectorRuleManager.TryGetRules(sector.Id, out var rules))
            {
                snapshot.IsClosed = rules.IsClosed;
                snapshot.IsRestricted = rules.IsRestricted;
                snapshot.RequiresStop = rules.RequiresStop;
                snapshot.RequiresYield = rules.RequiresYield;
                snapshot.MinSpeedKph = rules.MinSpeedKph;
                snapshot.MaxSpeedKph = rules.MaxSpeedKph;
            }

            if (_branchManager == null)
                return;

            var branches = _branchManager.GetBranchesForSector(sector.Id);
            if (branches.Count == 0)
                return;

            var branch = branches[0];
            snapshot.BranchId = branch.Id;
            snapshot.BranchRole = branch.Role;
            snapshot.IsIntersection = branch.Role == TrackBranchRole.Intersection ||
                                      branch.Role == TrackBranchRole.Merge ||
                                      branch.Role == TrackBranchRole.Split ||
                                      branch.Role == TrackBranchRole.Branch;

            var position = new Vector2(worldPosition.X, worldPosition.Z);
            snapshot.BranchSummary = BuildBranchSummary(branch, position, headingDegrees);
            snapshot.BranchSuggestion = BuildBranchSuggestion(branch, position, headingDegrees);
        }

        private void ApplyAreaSnapshotOverrides(Vector3 worldPosition, float headingDegrees, ref MapSnapshot snapshot)
        {
            if (_areaManager == null)
                return;

            var areas = _areaManager.FindAreasContaining(worldPosition);
            if (areas.Count == 0)
                return;

            var area = areas[areas.Count - 1];
            if (!string.IsNullOrWhiteSpace(area.MaterialId))
                snapshot.MaterialId = area.MaterialId!;
            if (area.Noise.HasValue)
                snapshot.Noise = area.Noise.Value;
            if (area.WidthMeters.HasValue)
                snapshot.WidthMeters = Math.Max(0.5f, area.WidthMeters.Value);
            if (area.Type == TrackAreaType.SafeZone || (area.Flags & TrackAreaFlags.SafeZone) != 0)
                snapshot.IsSafeZone = true;

            if (!string.IsNullOrWhiteSpace(area.Name))
                snapshot.Zone = area.Name!;
            else if (!string.IsNullOrWhiteSpace(area.Id))
                snapshot.Zone = area.Id;

            TryApplyAreaWidthFromMetadata(area, ref snapshot.WidthMeters);
        }

        private static bool TryApplyAreaWidthFromMetadata(TrackAreaDefinition area, ref float widthMeters)
        {
            if (area.Metadata == null || area.Metadata.Count == 0)
                return false;

            if (TryGetMetadataFloat(area.Metadata, out var widthValue, "intersection_width", "width", "lane_width"))
            {
                widthMeters = Math.Max(0.5f, widthValue);
                return true;
            }

            return false;
        }

        private static bool TryGetMetadataFloat(
            IReadOnlyDictionary<string, string> metadata,
            out float value,
            params string[] keys)
        {
            value = 0f;
            foreach (var key in keys)
            {
                if (!metadata.TryGetValue(key, out var raw))
                    continue;
                if (float.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
                    return true;
            }
            return false;
        }

        private void AnnounceMapChanges(MapSnapshot previous, MapSnapshot current)
        {
            if (!string.Equals(previous.MaterialId, current.MaterialId, StringComparison.OrdinalIgnoreCase))
                _speech.Speak($"{FormatMaterial(current.MaterialId)} material.");

            if (previous.Noise != current.Noise)
                _speech.Speak($"{FormatNoise(current.Noise)} zone.");

            if (previous.IsSafeZone != current.IsSafeZone)
            {
                if (current.IsSafeZone)
                    _speech.Speak("Safe zone.");
                else
                    _speech.Speak("Leaving safe zone.");
            }

            if (previous.IsClosed != current.IsClosed && current.IsClosed)
                _speech.Speak("Closed sector.");
            if (previous.IsRestricted != current.IsRestricted && current.IsRestricted)
                _speech.Speak("Restricted sector.");
            if (previous.RequiresStop != current.RequiresStop && current.RequiresStop)
                _speech.Speak("Stop required.");
            if (previous.RequiresYield != current.RequiresYield && current.RequiresYield)
                _speech.Speak("Yield.");

            if (!string.Equals(previous.Zone, current.Zone, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(current.Zone))
                    _speech.Speak($"{current.Zone}.");
                else if (!string.IsNullOrWhiteSpace(previous.Zone))
                    _speech.Speak("Leaving zone.");
            }

            var wasIntersection = previous.IsIntersection;
            var isIntersection = current.IsIntersection;
            if (wasIntersection != isIntersection)
            {
                if (isIntersection)
                    _speech.Speak("Intersection.");
                else
                    _speech.Speak("Leaving intersection.");
            }

            if (!string.Equals(previous.BranchId, current.BranchId, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(current.BranchSummary))
                    _speech.Speak(current.BranchSummary);
                if (!string.IsNullOrWhiteSpace(current.BranchSuggestion))
                    _speech.Speak(current.BranchSuggestion);
            }
        }

        private void ReportHeight()
        {
            var heightMeters = _worldPosition.Y;
            var heightText = _settings.Units == UnitSystem.Imperial
                ? $"{heightMeters * MetersToFeet:F1} feet"
                : $"{heightMeters:F1} meters";
            var message = $"Height {heightText}";

            if (_surfaceSystem != null &&
                _surfaceSystem.TrySample(_worldPosition, out var sample, new TrackSurfaceQueryOptions
                {
                    PreferClosestHeightToReference = true,
                    ReferenceHeightMeters = _worldPosition.Y
                }))
            {
                var headingForward = MapMovement.HeadingVector(_headingDegrees);
                var details = SurfaceReport.FormatSlopeBank(sample.Normal, headingForward);
                if (!string.IsNullOrWhiteSpace(details))
                    message = $"{message}, {details}";
            }

            _speech.Speak(message);
        }

        private void InitializeBeacon()
        {
            var path = Path.Combine(AssetPaths.SoundsRoot, "Legacy", "beacon.wav");
            if (!File.Exists(path))
                return;
            _soundBeacon = _audio.CreateSpatialSource(path, streamFromDisk: true, allowHrtf: true);
            if (_soundBeacon != null)
            {
                _soundBeacon.SetUseReflections(true);
                _soundBeacon.SetUseBakedReflections(false);
                _soundBeacon.SetDopplerFactor(0f);
            }
        }

        private void UpdateApproachGuidance(float elapsed)
        {
            if (_approachBeacon == null || _soundBeacon == null)
                return;

            var headingDegrees = _headingDegrees;
            if (_approachBeacon.TryGetCue(_worldPosition, headingDegrees, out var cue) && !cue.Passed)
            {
                var position = AudioWorld.ToMeters(cue.BeaconPosition);
                _soundBeacon.SetPosition(position);
                _soundBeacon.SetVelocity(Vector3.Zero);
                ApplyBeaconBakedIdentifier(cue.PortalId);
                _beaconCooldown -= elapsed;
                if (_beaconCooldown <= 0f)
                {
                    _soundBeacon.Stop();
                    _soundBeacon.SeekToStart();
                    _soundBeacon.Play(loop: false);
                    _beaconCooldown = 1.5f;
                }

                var tolerance = cue.ToleranceDegrees ?? DefaultApproachToleranceDegrees;
                var headingText = FormatHeadingShort(cue.TargetHeadingDegrees);
                if (cue.DeltaDegrees > tolerance)
                {
                    if (!string.Equals(_lastApproachPortalId, cue.PortalId, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(_lastApproachHeading, headingText, StringComparison.OrdinalIgnoreCase))
                    {
                        _speech.Speak($"Turn {headingText}.");
                        _lastApproachPortalId = cue.PortalId;
                        _lastApproachHeading = headingText;
                    }
                }

                return;
            }

            _beaconCooldown = 0f;
            if (_soundBeacon.IsPlaying)
                _soundBeacon.Stop();
            _soundBeacon.ClearBakedIdentifier();
            _lastApproachPortalId = null;
            _lastApproachHeading = null;
        }

        private void ApplyBeaconBakedIdentifier(string portalId)
        {
            if (_soundBeacon == null || _steamAudioScene == null)
                return;

            if (_steamAudioScene.TryGetPortalBakedIdentifier(portalId, out var identifier))
                _soundBeacon.SetBakedIdentifier(identifier);
            else
                _soundBeacon.ClearBakedIdentifier();
        }

        private bool IsWithinTrack(Vector3 worldPosition)
        {
            var safeZone = IsSafeZone(worldPosition);

            if (_map.MinX.HasValue && worldPosition.X <= _map.MinX.Value)
                return false;
            if (_map.MinZ.HasValue && worldPosition.Z <= _map.MinZ.Value)
                return false;
            if (_map.MaxX.HasValue && worldPosition.X >= _map.MaxX.Value)
                return false;
            if (_map.MaxZ.HasValue && worldPosition.Z >= _map.MaxZ.Value)
                return false;

            if (_areaManager != null && _areaManager.ContainsTrackArea(worldPosition))
                return true;

            if (_surfaceSystem != null && _surfaceSystem.TrySample(worldPosition, out _))
                return true;

            if (safeZone)
                return !IsBlockedBySectorRules(worldPosition);

            return false;
        }

        private bool IsBlockedBySectorRules(Vector3 worldPosition)
        {
            if (_sectorManager == null || _sectorRuleManager == null)
                return false;

            var sectors = _sectorManager.FindSectorsContaining(worldPosition);
            if (sectors.Count == 0)
                return false;

            foreach (var sector in sectors)
            {
                if (_sectorRuleManager.TryGetRules(sector.Id, out var rules) &&
                    (rules.IsClosed || rules.IsRestricted))
                    return true;
            }

            return false;
        }

        private bool AllowsSectorTransition(Vector3 fromPosition, Vector3 toPosition, float headingDegrees, out string deniedReason)
        {
            deniedReason = "Access denied.";
            if (_sectorManager == null || _sectorRuleManager == null)
                return true;

            var hasFrom = _sectorManager.TryLocate(fromPosition, headingDegrees, out var fromSector, out var fromPortal, out _);
            var hasTo = _sectorManager.TryLocate(toPosition, headingDegrees, out var toSector, out var toPortal, out _);
            if (!hasTo)
                return true;

            if (_sectorRuleManager.TryGetRules(toSector.Id, out var toRules))
            {
                if (toRules.IsClosed)
                {
                    deniedReason = "Closed sector.";
                    return false;
                }
                if (toRules.IsRestricted)
                {
                    deniedReason = "Restricted sector.";
                    return false;
                }
            }

            if (hasFrom && !string.Equals(fromSector.Id, toSector.Id, StringComparison.OrdinalIgnoreCase))
            {
                var direction = MapMovement.ToCardinal(headingDegrees);
                if (!_sectorRuleManager.AllowsExit(fromSector.Id, fromPortal?.Id, direction))
                {
                    deniedReason = "Exit not allowed.";
                    return false;
                }
                if (!_sectorRuleManager.AllowsEntry(toSector.Id, toPortal?.Id, direction))
                {
                    deniedReason = "Entry not allowed.";
                    return false;
                }
            }

            return true;
        }

        private bool IsSafeZone(Vector3 worldPosition)
        {
            if (_areaManager == null)
                return false;

            var areas = _areaManager.FindAreasContaining(worldPosition);
            if (areas.Count == 0)
                return false;

            foreach (var area in areas)
            {
                if (area.Type == TrackAreaType.SafeZone || (area.Flags & TrackAreaFlags.SafeZone) != 0)
                    return true;
            }
            return false;
        }


        private static string FormatHeadingShort(float degrees)
        {
            var normalized = degrees % 360f;
            if (normalized < 0f)
                normalized += 360f;

            if (normalized >= 315f || normalized < 45f)
                return "north";
            if (normalized >= 45f && normalized < 135f)
                return "east";
            if (normalized >= 135f && normalized < 225f)
                return "south";
            return "west";
        }

        private string BuildBranchSummary(TrackBranchDefinition branch, Vector2 position, float headingDegrees)
        {
            if (branch.Exits.Count == 0)
                return string.Empty;

            var exitSummaries = new List<string>();
            foreach (var exit in branch.Exits)
            {
                var desc = DescribeExit(exit, position, headingDegrees);
                if (!string.IsNullOrWhiteSpace(desc))
                    exitSummaries.Add(desc);
            }

            if (exitSummaries.Count == 0)
                return string.Empty;

            return $"Exits: {string.Join(", ", exitSummaries)}.";
        }

        private string BuildBranchSuggestion(
            TrackBranchDefinition branch,
            Vector2 position,
            float headingDegrees)
        {
            if (branch.Exits.Count == 0)
                return string.Empty;

            var preferredPortal = GetPreferredExitPortal(branch);
            TrackBranchExitDefinition? choice = null;
            if (!string.IsNullOrWhiteSpace(preferredPortal))
            {
                foreach (var exit in branch.Exits)
                {
                    if (string.Equals(exit.PortalId, preferredPortal, StringComparison.OrdinalIgnoreCase))
                    {
                        choice = exit;
                        break;
                    }
                }
            }

            if (choice == null)
                choice = ChooseClosestExit(branch.Exits, position, headingDegrees);

            if (choice == null)
                return string.Empty;

            var desc = DescribeExit(choice, position, headingDegrees);
            if (string.IsNullOrWhiteSpace(desc))
                return string.Empty;

            return $"Suggested: {desc}.";
        }

        private string? GetPreferredExitPortal(TrackBranchDefinition branch)
        {
            if (branch.Metadata == null || branch.Metadata.Count == 0)
                return null;

            if (branch.Metadata.TryGetValue("preferred_exit", out var raw) && !string.IsNullOrWhiteSpace(raw))
                return raw.Trim();
            if (branch.Metadata.TryGetValue("preferred_exit_portal", out raw) && !string.IsNullOrWhiteSpace(raw))
                return raw.Trim();
            if (branch.Metadata.TryGetValue("preferred_exit_id", out raw) && !string.IsNullOrWhiteSpace(raw))
                return raw.Trim();
            return null;
        }

        private TrackBranchExitDefinition? ChooseClosestExit(
            IReadOnlyList<TrackBranchExitDefinition> exits,
            Vector2 position,
            float headingDegrees)
        {
            TrackBranchExitDefinition? best = null;
            var bestDelta = float.MaxValue;

            foreach (var exit in exits)
            {
                if (!TryResolveExitHeading(exit, position, out var exitHeading))
                    continue;
                var delta = Math.Abs(NormalizeDegreesDelta(exitHeading - headingDegrees));
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = exit;
                }
            }

            return best;
        }

        private string DescribeExit(TrackBranchExitDefinition exit, Vector2 position, float headingDegrees)
        {
            if (!TryResolveExitHeading(exit, position, out var exitHeading))
                return exit.Name ?? string.Empty;

            var relative = DescribeRelativeDirection(exitHeading, headingDegrees);
            if (string.IsNullOrWhiteSpace(exit.Name))
                return relative;
            return $"{relative} ({exit.Name})";
        }

        private bool TryResolveExitHeading(TrackBranchExitDefinition exit, Vector2 position, out float headingDegrees)
        {
            headingDegrees = 0f;
            if (exit.HeadingDegrees.HasValue)
            {
                headingDegrees = exit.HeadingDegrees.Value;
                return true;
            }

            if (_portalManager != null && _portalManager.TryGetPortal(exit.PortalId, out var portal))
            {
                if (portal.ExitHeadingDegrees.HasValue)
                {
                    headingDegrees = portal.ExitHeadingDegrees.Value;
                    return true;
                }
                if (portal.EntryHeadingDegrees.HasValue)
                {
                    headingDegrees = portal.EntryHeadingDegrees.Value;
                    return true;
                }

                var portalPos = new Vector2(portal.X, portal.Z);
                headingDegrees = HeadingFromVector(portalPos - position);
                return true;
            }

            return false;
        }

        private static float HeadingFromVector(Vector2 delta)
        {
            var radians = (float)Math.Atan2(delta.X, delta.Y);
            var degrees = radians * 180f / (float)Math.PI;
            if (degrees < 0f)
                degrees += 360f;
            return degrees;
        }

        private static string DescribeRelativeDirection(float targetHeadingDegrees, float headingDegrees)
        {
            var delta = NormalizeDegreesDelta(targetHeadingDegrees - headingDegrees);
            var absDelta = Math.Abs(delta);

            if (absDelta <= 30f)
                return "straight";
            if (absDelta >= 150f)
                return "back";
            if (delta > 0f)
                return "right";
            return "left";
        }

        private static float NormalizeDegreesDelta(float degreesDelta)
        {
            degreesDelta %= 360f;
            if (degreesDelta > 180f)
                degreesDelta -= 360f;
            if (degreesDelta < -180f)
                degreesDelta += 360f;
            return degreesDelta;
        }

        private static string FormatTrackName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Track";
            return name.Replace('_', ' ').Replace('-', ' ').Trim();
        }

        private string FormatMaterial(string materialId)
        {
            if (string.IsNullOrWhiteSpace(materialId))
                return "Unknown";

            foreach (var material in _map.Materials)
            {
                if (material != null && string.Equals(material.Id, materialId, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(material.Name))
                        return material.Name!;
                    return material.Id;
                }
            }

            return materialId;
        }

        private static string FormatNoise(TrackNoise noise)
        {
            return noise switch
            {
                TrackNoise.Crowd => "Crowd",
                TrackNoise.Ocean => "Ocean",
                TrackNoise.Trackside => "Trackside",
                TrackNoise.Clock => "Clock",
                TrackNoise.Jet => "Jet",
                TrackNoise.Thunder => "Thunder",
                TrackNoise.Pile => "Construction",
                TrackNoise.Construction => "Construction",
                TrackNoise.River => "River",
                TrackNoise.Helicopter => "Helicopter",
                TrackNoise.Owl => "Owl",
                _ => "Quiet"
            };
        }



        private struct MapSnapshot
        {
            public string MaterialId;
            public TrackNoise Noise;
            public float WidthMeters;
            public bool IsSafeZone;
            public string Zone;
            public string SectorId;
            public TrackSectorType SectorType;
            public bool IsIntersection;
            public string BranchId;
            public TrackBranchRole BranchRole;
            public string BranchSummary;
            public string BranchSuggestion;
            public bool IsClosed;
            public bool IsRestricted;
            public bool RequiresStop;
            public bool RequiresYield;
            public float? MinSpeedKph;
            public float? MaxSpeedKph;
        }
    }
}
