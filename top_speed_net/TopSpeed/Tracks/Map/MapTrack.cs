using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using TopSpeed.Audio;
using TopSpeed.Core;
using TopSpeed.Data;
using TopSpeed.Tracks.Areas;
using TopSpeed.Tracks.Acoustics;
using TopSpeed.Tracks.Guidance;
using TopSpeed.Tracks.Collisions;
using TopSpeed.Tracks.Geometry;
using TopSpeed.Tracks.Surfaces;
using TopSpeed.Tracks.Rooms;
using TopSpeed.Tracks.Sectors;
using TopSpeed.Tracks.Topology;
using TopSpeed.Tracks.Sounds;
using TopSpeed.Tracks.Volumes;
using TopSpeed.Tracks.Walls;
using TS.Audio;

namespace TopSpeed.Tracks.Map
{
    internal readonly struct TrackTurnGuidance
    {
        public TrackTurnGuidance(
            string sectorId,
            string? entryPortalId,
            string? exitPortalId,
            float turnHeadingDegrees,
            float distanceMeters,
            float guidanceRangeMeters,
            bool passed)
        {
            SectorId = sectorId;
            EntryPortalId = entryPortalId;
            ExitPortalId = exitPortalId;
            TurnHeadingDegrees = turnHeadingDegrees;
            DistanceMeters = distanceMeters;
            GuidanceRangeMeters = guidanceRangeMeters;
            Passed = passed;
        }

        public string SectorId { get; }
        public string? EntryPortalId { get; }
        public string? ExitPortalId { get; }
        public float TurnHeadingDegrees { get; }
        public float DistanceMeters { get; }
        public float GuidanceRangeMeters { get; }
        public bool Passed { get; }
    }

    internal sealed class MapTrack : IDisposable
    {
        private const float CallLengthMeters = 30.0f;
        private const float WallProbeStepMeters = 0.5f;
        private const float WallProbeMinDot = 0.17f;

        private readonly AudioManager _audio;
        private readonly TrackMap _map;
        private readonly TrackAreaManager _areaManager;
        private readonly TrackSectorManager _sectorManager;
        private readonly TrackSectorRuleManager _sectorRuleManager;
        private readonly TrackPortalManager _portalManager;
        private readonly TrackApproachManager _approachManager;
        private readonly TrackApproachBeacon _approachBeacon;
        private readonly TrackBranchManager _branchManager;
        private readonly TrackWallManager _wallManager;
        private readonly TrackMeshCollisionManager _meshCollisionManager;
        private readonly TrackSurfaceSystem _surfaceSystem;
        private readonly TrackSoundSourceSystem _soundSourceSystem;
        private readonly bool _hasSurfaces;
        private readonly string _trackName;
        private readonly bool _userDefined;
        private TrackNoise _currentNoise;
        private readonly float _trackLength;
        private TrackSteamAudioScene? _steamAudioScene;
        private RoomAcoustics _currentRoomAcoustics;
        private bool _hasRoomAcoustics;

        private AudioSourceHandle? _soundCrowd;
        private AudioSourceHandle? _soundOcean;
        private AudioSourceHandle? _soundRain;
        private AudioSourceHandle? _soundWind;
        private AudioSourceHandle? _soundStorm;
        private AudioSourceHandle? _soundDesert;
        private AudioSourceHandle? _soundAirport;
        private AudioSourceHandle? _soundAirplane;
        private AudioSourceHandle? _soundClock;
        private AudioSourceHandle? _soundJet;
        private AudioSourceHandle? _soundThunder;
        private AudioSourceHandle? _soundPile;
        private AudioSourceHandle? _soundConstruction;
        private AudioSourceHandle? _soundRiver;
        private AudioSourceHandle? _soundHelicopter;
        private AudioSourceHandle? _soundOwl;
        private AudioSourceHandle? _soundBeacon;
        private float _beaconCooldown;

        private MapTrack(string trackName, TrackMap map, AudioManager audio, bool userDefined)
        {
            _trackName = string.IsNullOrWhiteSpace(trackName) ? "Track" : trackName.Trim();
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _audio = audio ?? throw new ArgumentNullException(nameof(audio));
            _userDefined = userDefined;
            _currentNoise = TrackNoise.NoNoise;
            _areaManager = map.BuildAreaManager();
            _portalManager = map.BuildPortalManager();
            _sectorManager = new TrackSectorManager(map.Sectors, _areaManager, _portalManager);
            _sectorRuleManager = map.BuildSectorRuleManager();
            _approachManager = new TrackApproachManager(map.Sectors, map.Approaches, _portalManager);
            _approachBeacon = new TrackApproachBeacon(map);
            _branchManager = map.BuildBranchManager();
            _wallManager = new TrackWallManager(map.Geometries, map.Walls);
            _meshCollisionManager = new TrackMeshCollisionManager(map.Geometries, map.Materials);
            _surfaceSystem = map.BuildSurfaceSystem();
            _hasSurfaces = _surfaceSystem.Surfaces.Count > 0;
            _soundSourceSystem = new TrackSoundSourceSystem(map, _areaManager, _audio);
            _trackLength = ResolveTrackLength();
            InitializeSounds();
            InitializeSteamAudioScene();
        }

        public static MapTrack Load(string nameOrPath, AudioManager audio)
        {
            if (!TrackMapLoader.TryResolvePath(nameOrPath, out var path))
                throw new FileNotFoundException("Track map not found.", nameOrPath);

            var map = TrackMapLoader.Load(path);
            var name = map.Name;
            var userDefined = path.IndexOfAny(new[] { '\\', '/' }) >= 0;
            return new MapTrack(name, map, audio, userDefined);
        }

        public string TrackName => _trackName;
        public TrackWeather Weather => _map.Weather;
        public TrackAmbience Ambience => _map.Ambience;
        public string InitialMaterialId => _map.DefaultMaterialId;
        public bool UserDefined => _userDefined;
        public float LaneWidth => Math.Max(0.5f, _map.DefaultWidthMeters * 0.5f);
        public TrackMap Map => _map;
        public float Length => _trackLength;
        public TrackSectorRuleManager SectorRules => _sectorRuleManager;
        public TrackBranchManager Branches => _branchManager;
        public bool HasFinishArea => !string.IsNullOrWhiteSpace(_map.FinishAreaId);
        public bool HasSurfaces => _hasSurfaces;

        public int Lap(float distanceMeters)
        {
            var length = Length;
            if (length <= 0f)
                return 1;
            return (int)(distanceMeters / length) + 1;
        }

        public void SetLaneWidth(float laneWidth)
        {
            _map.DefaultWidthMeters = Math.Max(0.5f, laneWidth * 2f);
        }

        public MapMovementState CreateStartState()
        {
            var state = MapMovement.CreateStart(_map);
            if (TryConstrainToSurface(state.WorldPosition, out var constrained, out _))
                state.WorldPosition = constrained;
            return state;
        }

        public MapMovementState CreateStateFromWorld(Vector3 worldPosition, float headingDegrees)
        {
            return new MapMovementState
            {
                WorldPosition = worldPosition,
                HeadingDegrees = MapMovement.NormalizeDegrees(headingDegrees),
                DistanceMeters = 0f
            };
        }

        public TrackPose GetPose(MapMovementState state)
        {
            var forward = MapMovement.HeadingVector(state.HeadingDegrees);
            var up = Vector3.UnitY;

            if (TryGetSurfaceOrientation(state.WorldPosition, state.HeadingDegrees, out var surfaceForward, out var surfaceUp))
            {
                forward = surfaceForward;
                up = surfaceUp;
            }

            var right = Vector3.Cross(forward, up);
            if (right.LengthSquared() > 0.000001f)
                right = Vector3.Normalize(right);
            else
                right = new Vector3(forward.Z, 0f, -forward.X);

            var heading = state.HeadingDegrees * (float)Math.PI / 180f;
            return new TrackPose(state.WorldPosition, forward, right, up, heading, 0f);
        }

        public TrackRoad RoadAt(MapMovementState state)
        {
            var road = BuildDefaultRoad();
            var safeZone = false;
            var length = _map.CellSizeMeters;
            var width = Math.Max(0.5f, _map.DefaultWidthMeters);
            var materialId = _map.DefaultMaterialId;
            var noise = _map.DefaultNoise;

            ApplyAreaOverrides(state.WorldPosition, state.HeadingDegrees, ref width, ref length, ref materialId, ref noise, ref safeZone);

            var forwardAxis = MapMovement.HeadingVector(state.HeadingDegrees);
            var upAxis = Vector3.UnitY;
            if (TryGetSurfaceOrientation(state.WorldPosition, state.HeadingDegrees, out var surfaceForward, out var surfaceUp))
            {
                forwardAxis = surfaceForward;
                upAxis = surfaceUp;
            }

            if (forwardAxis.LengthSquared() > 0.000001f)
                forwardAxis = Vector3.Normalize(forwardAxis);
            else
                forwardAxis = Vector3.UnitZ;

            if (upAxis.LengthSquared() > 0.000001f)
                upAxis = Vector3.Normalize(upAxis);
            else
                upAxis = Vector3.UnitY;

            var rightAxis = Vector3.Cross(upAxis, forwardAxis);
            if (rightAxis.LengthSquared() > 0.000001f)
                rightAxis = Vector3.Normalize(rightAxis);
            else
                rightAxis = new Vector3(forwardAxis.Z, 0f, -forwardAxis.X);

            road.Left = -width * 0.5f;
            road.Right = width * 0.5f;
            road.ForwardAxis = forwardAxis;
            road.RightAxis = rightAxis;
            road.UpAxis = upAxis;
            road.Length = length;
            road.MaterialId = materialId;
            road.Type = TrackType.Straight;
            road.IsSafeZone = safeZone;
            road.IsOutOfBounds = !IsWithinTrackInternal(state.WorldPosition, safeZone);
            ApplySectorRuleFields(state.WorldPosition, state.HeadingDegrees, ref road);
            return road;
        }

        private float ResolveTrackLength()
        {
            var best = 0f;
            if (_map.Approaches != null)
            {
                foreach (var approach in _map.Approaches)
                {
                    if (approach?.LengthMeters is { } len && len > best)
                        best = len;
                }
            }

            if (best > 0f)
                return best;

            return Math.Max(0f, best);
        }

        public bool IsInsideFinishArea(Vector3 worldPosition)
        {
            if (_areaManager == null || string.IsNullOrWhiteSpace(_map.FinishAreaId))
                return false;
            if (!_areaManager.TryGetArea(_map.FinishAreaId!, out var area))
                return false;
            return _areaManager.Contains(area, worldPosition);
        }

        public bool TryGetCeilingHeight(Vector3 worldPosition, out float ceilingHeight)
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

        public bool TryGetSectorRules(
            Vector3 worldPosition,
            float headingDegrees,
            out TrackSectorDefinition sector,
            out TrackSectorRules rules,
            out PortalDefinition? portal,
            out float? portalDeltaDegrees)
        {
            sector = null!;
            rules = null!;
            portal = null;
            portalDeltaDegrees = null;

            if (_sectorManager == null || _sectorRuleManager == null)
                return false;

            if (!_sectorManager.TryLocate(worldPosition, headingDegrees, out var foundSector, out var foundPortal, out var delta))
                return false;

            if (!_sectorRuleManager.TryGetRules(foundSector.Id, out var foundRules))
                return false;

            sector = foundSector;
            rules = foundRules;
            portal = foundPortal;
            portalDeltaDegrees = delta;
            return true;
        }

        public bool TryMove(ref MapMovementState state, float distanceMeters, float headingDegrees, out TrackRoad road, out bool boundaryHit)
        {
            boundaryHit = false;
            road = BuildDefaultRoad();
            if (Math.Abs(distanceMeters) < 0.001f)
            {
                road = RoadAt(state);
                return false;
            }

            var previousPosition = state.WorldPosition;
            var heading = MapMovement.NormalizeDegrees(headingDegrees);
            var direction = MapMovement.HeadingVector(heading);
            var nextPosition = previousPosition + (direction * distanceMeters);

            if (_hasSurfaces)
            {
                if (!TryConstrainToSurface(nextPosition, out var constrained, out _))
                {
                    boundaryHit = true;
                    road = RoadAt(state);
                    return false;
                }
                nextPosition = constrained;
            }

            if (TryGetWallCollision(previousPosition, nextPosition, out _))
            {
                boundaryHit = true;
                road = RoadAt(state);
                return false;
            }

            if (TryGetMeshCollision(previousPosition, nextPosition, out _))
            {
                boundaryHit = true;
                road = RoadAt(state);
                return false;
            }

            if (!IsWithinTrack(nextPosition))
            {
                boundaryHit = true;
                road = RoadAt(state);
                return false;
            }

            if (!IsSectorTransitionAllowed(previousPosition, nextPosition, heading))
            {
                boundaryHit = true;
                road = RoadAt(state);
                return false;
            }

            state.WorldPosition = nextPosition;
            state.HeadingDegrees = heading;
            var traveled = distanceMeters;
            if (_hasSurfaces)
                traveled = Vector3.Distance(previousPosition, nextPosition);
            state.DistanceMeters += traveled;
            road = RoadAt(state);
            return true;
        }

        public bool TryGetTurnGuidance(Vector3 worldPosition, float headingDegrees, out TrackTurnGuidance guidance)
        {
            guidance = default;
            if (_approachManager == null)
                return false;

            var position = worldPosition;
            var best = default(TurnCandidate);
            var hasBest = false;

            foreach (var approach in _approachManager.Approaches)
            {
                if (approach == null)
                    continue;

                var guidanceRange = 0f;
                if (approach.Metadata != null && approach.Metadata.Count > 0 &&
                    TryGetMetadataFloat(approach.Metadata, out var rangeValue, "turn_range", "guidance_range", "turn_guidance_range"))
                {
                    guidanceRange = Math.Max(1f, rangeValue);
                }

                if (IsApproachSideEnabled(approach, TrackApproachSide.Entry))
                    TryBuildTurnCandidate(approach, TrackApproachSide.Entry, position, headingDegrees, guidanceRange, ref best, ref hasBest);
                if (IsApproachSideEnabled(approach, TrackApproachSide.Exit))
                    TryBuildTurnCandidate(approach, TrackApproachSide.Exit, position, headingDegrees, guidanceRange, ref best, ref hasBest);
            }

            if (!hasBest)
                return false;

            var sectorId = best.SectorId ?? string.Empty;
            guidance = new TrackTurnGuidance(
                sectorId,
                best.EntryPortalId,
                best.ExitPortalId,
                best.TurnHeadingDegrees,
                best.DistanceMeters,
                best.GuidanceRangeMeters,
                best.Passed);
            return true;
        }

        public bool TryGetAvailableHeadings(Vector3 worldPosition, float headingDegrees, out IReadOnlyList<float> headings)
        {
            var list = new List<float>();

            if (_sectorManager != null &&
                _sectorManager.TryLocate(worldPosition, headingDegrees, out var sector, out _, out _))
            {
                var branches = _branchManager.GetBranchesForSector(sector.Id);
                foreach (var branch in branches)
                {
                    if (branch == null)
                        continue;
                    foreach (var exit in branch.Exits)
                    {
                        if (exit == null)
                            continue;
                        var exitHeading = exit.HeadingDegrees;
                        if (!exitHeading.HasValue && _portalManager.TryGetPortal(exit.PortalId, out var portal))
                            exitHeading = portal.ExitHeadingDegrees ?? portal.EntryHeadingDegrees;
                        if (exitHeading.HasValue)
                            AddHeading(list, exitHeading.Value);
                    }
                }
            }

            headings = list;
            return list.Count > 0;
        }

        public bool NextRoad(MapMovementState state, float speed, int curveAnnouncementMode, out TrackRoad road)
        {
            road = RoadAt(state);
            if (!TryGetTurnGuidance(state.WorldPosition, state.HeadingDegrees, out var guidance))
                return false;
            if (guidance.Passed)
                return false;

            if (guidance.GuidanceRangeMeters > 0f && guidance.DistanceMeters > guidance.GuidanceRangeMeters)
                return false;

            road.Type = ResolveTurnType(state.HeadingDegrees, guidance.TurnHeadingDegrees);
            return true;
        }

        public void Initialize()
        {
            _currentNoise = TrackNoise.NoNoise;
            _beaconCooldown = 0f;
            _soundSourceSystem.Initialize();
        }

        public void Run(MapMovementState state, float elapsed)
        {
            var noise = _map.DefaultNoise;
            var materialId = _map.DefaultMaterialId;
            var safeZone = false;
            var length = _map.CellSizeMeters;
            var width = Math.Max(0.5f, _map.DefaultWidthMeters);
            var areas = _areaManager != null
                ? _areaManager.FindAreasContaining(state.WorldPosition)
                : (IReadOnlyList<TrackAreaDefinition>)Array.Empty<TrackAreaDefinition>();
            ApplyAreaOverrides(state.WorldPosition, state.HeadingDegrees, areas, ref width, ref length, ref materialId, ref noise, ref safeZone);
            if (noise != _currentNoise)
            {
                StopNoise(_currentNoise);
                _currentNoise = noise;
            }

            UpdateNoiseLoop(noise);
            UpdateApproachBeacon(state, elapsed);
            UpdateRoomAcoustics(areas);
            _soundSourceSystem.Update(state.WorldPosition, elapsed, areas);
            if (_map.Weather == TrackWeather.Rain)
                PlayIfNotPlaying(_soundRain);
            else
                StopSound(_soundRain);

            if (_map.Weather == TrackWeather.Wind)
                PlayIfNotPlaying(_soundWind);
            else
                StopSound(_soundWind);

            if (_map.Weather == TrackWeather.Storm)
                PlayIfNotPlaying(_soundStorm);
            else
                StopSound(_soundStorm);

            if (_map.Ambience == TrackAmbience.Desert)
                PlayIfNotPlaying(_soundDesert);
            else if (_map.Ambience == TrackAmbience.Airport)
                PlayIfNotPlaying(_soundAirport);
            else
            {
                StopSound(_soundDesert);
                StopSound(_soundAirport);
            }
        }

        public void FinalizeTrack()
        {
            StopAllSounds();
            _soundSourceSystem.StopAll();
        }

        public void Dispose()
        {
            FinalizeTrack();
            _soundSourceSystem.Dispose();
            _audio.SteamAudio?.ClearScene();
            _steamAudioScene?.Dispose();
            DisposeSound(_soundCrowd);
            DisposeSound(_soundOcean);
            DisposeSound(_soundRain);
            DisposeSound(_soundWind);
            DisposeSound(_soundStorm);
            DisposeSound(_soundDesert);
            DisposeSound(_soundAirport);
            DisposeSound(_soundAirplane);
            DisposeSound(_soundClock);
            DisposeSound(_soundJet);
            DisposeSound(_soundThunder);
            DisposeSound(_soundPile);
            DisposeSound(_soundConstruction);
            DisposeSound(_soundRiver);
            DisposeSound(_soundHelicopter);
            DisposeSound(_soundOwl);
            DisposeSound(_soundBeacon);
        }

        private TrackRoad BuildDefaultRoad()
        {
            var width = Math.Max(0.5f, _map.DefaultWidthMeters);
            return new TrackRoad
            {
                Left = -width * 0.5f,
                Right = width * 0.5f,
                ForwardAxis = Vector3.UnitZ,
                RightAxis = Vector3.UnitX,
                UpAxis = Vector3.UnitY,
                MaterialId = _map.DefaultMaterialId,
                Type = TrackType.Straight,
                Length = _map.CellSizeMeters
            };
        }

        private void ApplyAreaOverrides(
            Vector3 worldPosition,
            float headingDegrees,
            ref float width,
            ref float length,
            ref string materialId,
            ref TrackNoise noise,
            ref bool safeZone)
        {
            var heading = MapMovement.ToCardinal(headingDegrees);
            ApplySectorOverrides(worldPosition, heading, ref width, ref length, ref materialId, ref noise, ref safeZone);

            if (_areaManager == null)
                return;

            var areas = _areaManager.FindAreasContaining(worldPosition);
            ApplyAreaOverrides(worldPosition, headingDegrees, areas, ref width, ref length, ref materialId, ref noise, ref safeZone);
        }

        private void ApplyAreaOverrides(
            Vector3 worldPosition,
            float headingDegrees,
            IReadOnlyList<TrackAreaDefinition> areas,
            ref float width,
            ref float length,
            ref string materialId,
            ref TrackNoise noise,
            ref bool safeZone)
        {
            var heading = MapMovement.ToCardinal(headingDegrees);
            ApplySectorOverrides(worldPosition, heading, ref width, ref length, ref materialId, ref noise, ref safeZone);

            if (areas == null || areas.Count == 0)
                return;

            TrackAreaDefinition? surfaceArea = null;
            TrackAreaDefinition? widthArea = null;
            foreach (var candidate in areas)
            {
                if (candidate == null)
                    continue;

                if (candidate.Type == TrackAreaType.SafeZone || (candidate.Flags & TrackAreaFlags.SafeZone) != 0)
                    safeZone = true;

                surfaceArea = candidate;
                if (IsWidthAffectingArea(candidate))
                    widthArea = candidate;
            }

            if (surfaceArea != null)
            {
                if (!string.IsNullOrWhiteSpace(surfaceArea.MaterialId))
                    materialId = surfaceArea.MaterialId!;
                if (surfaceArea.Noise.HasValue)
                    noise = surfaceArea.Noise.Value;
            }

            if (widthArea != null)
            {
                if (widthArea.WidthMeters.HasValue)
                    width = Math.Max(0.5f, widthArea.WidthMeters.Value);

                TryApplyMetadataDimensions(widthArea.Metadata, ref width, ref length);
            }
        }

        private void UpdateRoomAcoustics(Vector3 worldPosition)
        {
            if (_areaManager == null)
            {
                UpdateRoomAcoustics(Array.Empty<TrackAreaDefinition>());
                return;
            }

            var areas = _areaManager.FindAreasContaining(worldPosition);
            UpdateRoomAcoustics(areas);
        }

        private void UpdateRoomAcoustics(IReadOnlyList<TrackAreaDefinition> areas)
        {
            var acoustics = ResolveRoomAcoustics(areas);
            if (!_hasRoomAcoustics || !RoomAcousticsEquals(_currentRoomAcoustics, acoustics))
            {
                _audio.SetRoomAcoustics(acoustics);
                _currentRoomAcoustics = acoustics;
                _hasRoomAcoustics = true;
            }
        }

        private RoomAcoustics ResolveRoomAcoustics(Vector3 worldPosition)
        {
            if (_areaManager == null)
                return RoomAcoustics.Default;

            var areas = _areaManager.FindAreasContaining(worldPosition);
            return ResolveRoomAcoustics(areas);
        }

        private RoomAcoustics ResolveRoomAcoustics(IReadOnlyList<TrackAreaDefinition> areas)
        {
            if (areas == null || areas.Count == 0)
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

        private void ApplySectorOverrides(
            Vector3 worldPosition,
            MapDirection heading,
            ref float width,
            ref float length,
            ref string materialId,
            ref TrackNoise noise,
            ref bool safeZone)
        {
            if (_sectorManager == null)
                return;

            var sectors = _sectorManager.FindSectorsContaining(worldPosition);
            if (sectors.Count == 0)
                return;

            var sector = sectors[sectors.Count - 1];
            if (!string.IsNullOrWhiteSpace(sector.MaterialId))
                materialId = sector.MaterialId!;
            if (sector.Noise.HasValue)
                noise = sector.Noise.Value;
            if ((sector.Flags & TrackSectorFlags.SafeZone) != 0)
                safeZone = true;

            TryApplyMetadataDimensions(sector.Metadata, ref width, ref length);
        }

        private static bool TryApplyMetadataDimensions(
            IReadOnlyDictionary<string, string> metadata,
            ref float width,
            ref float length)
        {
            if (metadata == null || metadata.Count == 0)
                return false;

            var hadAny = false;
            if (TryGetMetadataFloat(metadata, out var widthValue, "intersection_width", "width", "lane_width"))
            {
                width = Math.Max(0.5f, widthValue);
                hadAny = true;
            }
            if (TryGetMetadataFloat(metadata, out var lengthValue, "intersection_length", "length"))
            {
                length = Math.Max(0.1f, lengthValue);
                hadAny = true;
            }
            return hadAny;
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

        private static TrackType ResolveTurnType(float currentHeading, float targetHeading)
        {
            var delta = NormalizeDegrees(targetHeading - currentHeading);
            if (delta > 180f)
                delta -= 360f;
            var abs = Math.Abs(delta);
            if (abs < 5f)
                return TrackType.Straight;

            TrackType left;
            TrackType right;
            if (abs < 30f)
            {
                left = TrackType.EasyLeft;
                right = TrackType.EasyRight;
            }
            else if (abs < 70f)
            {
                left = TrackType.Left;
                right = TrackType.Right;
            }
            else if (abs < 120f)
            {
                left = TrackType.HardLeft;
                right = TrackType.HardRight;
            }
            else
            {
                left = TrackType.HairpinLeft;
                right = TrackType.HairpinRight;
            }

            return delta < 0f ? left : right;
        }


        private void TryBuildTurnCandidate(
            TrackApproachDefinition approach,
            TrackApproachSide side,
            Vector3 position,
            float currentHeadingDegrees,
            float guidanceRangeMeters,
            ref TurnCandidate best,
            ref bool hasBest)
        {
            var portalId = side == TrackApproachSide.Entry ? approach.EntryPortalId : approach.ExitPortalId;
            var portalHeading = side == TrackApproachSide.Entry ? approach.EntryHeadingDegrees : approach.ExitHeadingDegrees;
            if (string.IsNullOrWhiteSpace(portalId) || !portalHeading.HasValue)
                return;
            if (!_portalManager.TryGetPortal(portalId!, out var portal))
                return;
            if (!IsWithinVolume(approach, portal, position))
                return;

            var portalPos = new Vector3(portal.X, portal.Y, portal.Z);
            var distance = Vector3.Distance(position, portalPos);
            var passed = false;
            var inTurnWindow = false;
            var hasTurnGeometry = TryGetTurnGeometry(approach, out var turnGeometry);
            if (hasTurnGeometry)
            {
                distance = DistanceToGeometry(turnGeometry, position, approach.WidthMeters);

                // Enter turn window at/after the gate crossing in entry travel direction.
                if (approach.EntryHeadingDegrees.HasValue &&
                    TryGetClosestPointOnGeometry2D(turnGeometry, new Vector2(position.X, position.Z), out var gatePoint))
                {
                    var forward = HeadingToVector(approach.EntryHeadingDegrees.Value);
                    var toPlayer = new Vector2(position.X, position.Z) - gatePoint;
                    if (Vector2.Dot(forward, toPlayer) >= 0f)
                    {
                        inTurnWindow = true;
                        distance = 0f;
                    }
                }
            }

            if (inTurnWindow)
            {
                var tolerance = approach.AlignmentToleranceDegrees ?? 20f;
                tolerance = Clamp(tolerance, 5f, 45f);
                var headingDelta = DeltaDegrees(currentHeadingDegrees, portalHeading.Value);
                passed = headingDelta <= tolerance;
            }
            else if (portalHeading.HasValue)
            {
                var forward = HeadingToVector(portalHeading.Value);
                var toPlayer = new Vector2(position.X - portalPos.X, position.Z - portalPos.Z);
                // Fallback for approaches without usable gate geometry.
                passed = !hasTurnGeometry && Vector2.Dot(forward, toPlayer) > 0f;
            }

            if (passed)
                return;

            if (!hasBest || distance < best.DistanceMeters)
            {
                best = new TurnCandidate
                {
                    SectorId = approach.SectorId,
                    EntryPortalId = approach.EntryPortalId,
                    ExitPortalId = approach.ExitPortalId,
                    TurnHeadingDegrees = portalHeading.Value,
                    DistanceMeters = distance,
                    PortalPosition = portalPos,
                    PortalHeadingDegrees = portalHeading,
                    GuidanceRangeMeters = guidanceRangeMeters,
                    Passed = false
                };
                hasBest = true;
            }
        }

        private static bool IsApproachSideEnabled(TrackApproachDefinition approach, TrackApproachSide side)
        {
            if (approach?.Metadata == null || approach.Metadata.Count == 0)
                return true;

            if (TryGetMetadataBool(approach.Metadata, out var enabled, "enabled") && !enabled)
                return false;
            if (TryGetMetadataBool(approach.Metadata, out var turnEnabled, "turn_enabled") && !turnEnabled)
                return false;

            if (TryGetMetadataString(approach.Metadata, out var raw, "approach_side", "approach_sides", "side"))
            {
                var trimmed = raw.Trim().ToLowerInvariant();
                if (trimmed.Contains("none") || trimmed.Contains("off") || trimmed.Contains("disabled"))
                    return false;
                var hasEntry = trimmed.Contains("entry");
                var hasExit = trimmed.Contains("exit");
                if (hasEntry || hasExit)
                    return side == TrackApproachSide.Entry ? hasEntry : hasExit;
            }

            if (TryGetMetadataBool(approach.Metadata, out var entryEnabled, "approach_entry", "entry_enabled", "entry_beacon"))
            {
                if (side == TrackApproachSide.Entry)
                    return entryEnabled;
            }
            if (TryGetMetadataBool(approach.Metadata, out var exitEnabled, "approach_exit", "exit_enabled", "exit_beacon"))
            {
                if (side == TrackApproachSide.Exit)
                    return exitEnabled;
            }

            return true;
        }

        private static bool TryGetMetadataBool(
            IReadOnlyDictionary<string, string> metadata,
            out bool value,
            params string[] keys)
        {
            value = false;
            foreach (var key in keys)
            {
                if (!metadata.TryGetValue(key, out var raw))
                    continue;
                if (bool.TryParse(raw, out value))
                    return true;
                var trimmed = raw.Trim().ToLowerInvariant();
                if (trimmed is "1" or "yes" or "true" or "on" or "enabled")
                {
                    value = true;
                    return true;
                }
                if (trimmed is "0" or "no" or "false" or "off" or "disabled")
                {
                    value = false;
                    return true;
                }
            }
            return false;
        }

        private struct TurnCandidate
        {
            public string SectorId;
            public string? EntryPortalId;
            public string? ExitPortalId;
            public float TurnHeadingDegrees;
            public float DistanceMeters;
            public Vector3 PortalPosition;
            public float? PortalHeadingDegrees;
            public float GuidanceRangeMeters;
            public bool Passed;
        }

        private bool TryGetTurnGeometry(TrackApproachDefinition approach, out GeometryDefinition geometry)
        {
            geometry = null!;
            if (approach?.Metadata == null || approach.Metadata.Count == 0)
                return false;
            if (!TryGetMetadataString(approach.Metadata, out var geometryId, "turn_geometry", "centerline_geometry", "beacon_geometry", "approach_geometry"))
                return false;
            return _areaManager.TryGetGeometry(geometryId, out geometry);
        }

        private static bool TryGetMetadataString(
            IReadOnlyDictionary<string, string> metadata,
            out string value,
            params string[] keys)
        {
            value = string.Empty;
            foreach (var key in keys)
            {
                if (metadata.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
                {
                    value = raw.Trim();
                    return true;
                }
            }
            return false;
        }

        private static float DistanceToGeometry(GeometryDefinition geometry, Vector3 position, float? widthOverride)
        {
            if (geometry == null)
                return float.MaxValue;

            var points3D = geometry.Points;
            switch (geometry.Type)
            {
                case GeometryType.Polyline:
                case GeometryType.Spline:
                    var width = widthOverride ?? 0f;
                    return Math.Max(0f, DistanceToPolyline(points3D, position) - (width * 0.5f));
                case GeometryType.Polygon:
                    return DistanceToPolygon(points3D, position);
                case GeometryType.Mesh:
                case GeometryType.Undefined:
                default:
                    return DistanceToPoints(points3D, position);
            }
        }

        private static float DistanceToPoints(IReadOnlyList<Vector3> points, Vector3 position)
        {
            if (points == null || points.Count == 0)
                return float.MaxValue;
            var best = float.MaxValue;
            for (var i = 0; i < points.Count; i++)
            {
                var dist = Vector3.Distance(points[i], position);
                if (dist < best)
                    best = dist;
            }
            return best;
        }

        private static float DistanceToPolyline(IReadOnlyList<Vector3> points, Vector3 position)
        {
            if (points == null || points.Count == 0)
                return float.MaxValue;
            if (points.Count == 1)
                return Vector3.Distance(points[0], position);

            var best = float.MaxValue;
            for (var i = 0; i < points.Count - 1; i++)
            {
                var distance = DistanceToSegment(points[i], points[i + 1], position);
                if (distance < best)
                    best = distance;
            }
            return best;
        }

        private static float DistanceToPolygon(IReadOnlyList<Vector3> points, Vector3 position)
        {
            if (points == null || points.Count == 0)
                return float.MaxValue;

            var points2D = ProjectToXZ(points);
            var position2D = new Vector2(position.X, position.Z);
            if (IsPointInPolygon(points2D, position2D))
            {
                if (TryGetPolygonPlane(points, out var planePoint, out var planeNormal))
                    return Math.Abs(Vector3.Dot(position - planePoint, planeNormal));
                return Math.Abs(position.Y - ResolveAverageY(points));
            }

            var best = float.MaxValue;
            for (var i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                var distance = DistanceToSegment(a, b, position);
                if (distance < best)
                    best = distance;
            }
            return best;
        }

        private static bool TryGetClosestPointOnGeometry2D(GeometryDefinition geometry, Vector2 position, out Vector2 closest)
        {
            closest = position;
            if (geometry == null || geometry.Points == null || geometry.Points.Count == 0)
                return false;

            var points = geometry.Points;
            switch (geometry.Type)
            {
                case GeometryType.Polyline:
                case GeometryType.Spline:
                    return TryGetClosestPointOnPath2D(points, position, closed: false, out closest);
                case GeometryType.Polygon:
                    return TryGetClosestPointOnPath2D(points, position, closed: true, out closest);
                case GeometryType.Mesh:
                case GeometryType.Undefined:
                default:
                    var best = float.MaxValue;
                    for (var i = 0; i < points.Count; i++)
                    {
                        var p = new Vector2(points[i].X, points[i].Z);
                        var d = Vector2.DistanceSquared(p, position);
                        if (d < best)
                        {
                            best = d;
                            closest = p;
                        }
                    }

                    return best < float.MaxValue;
            }
        }

        private static bool TryGetClosestPointOnPath2D(IReadOnlyList<Vector3> points, Vector2 position, bool closed, out Vector2 closest)
        {
            closest = position;
            if (points == null || points.Count == 0)
                return false;
            if (points.Count == 1)
            {
                closest = new Vector2(points[0].X, points[0].Z);
                return true;
            }

            var best = float.MaxValue;
            var segmentCount = points.Count - 1;
            for (var i = 0; i < segmentCount; i++)
            {
                var a = new Vector2(points[i].X, points[i].Z);
                var b = new Vector2(points[i + 1].X, points[i + 1].Z);
                var point = ClosestPointOnSegment2D(a, b, position);
                var d = Vector2.DistanceSquared(point, position);
                if (d < best)
                {
                    best = d;
                    closest = point;
                }
            }

            if (closed)
            {
                var a = new Vector2(points[points.Count - 1].X, points[points.Count - 1].Z);
                var b = new Vector2(points[0].X, points[0].Z);
                var point = ClosestPointOnSegment2D(a, b, position);
                var d = Vector2.DistanceSquared(point, position);
                if (d < best)
                {
                    best = d;
                    closest = point;
                }
            }

            return best < float.MaxValue;
        }

        private static Vector2 ClosestPointOnSegment2D(Vector2 a, Vector2 b, Vector2 p)
        {
            var ab = b - a;
            var abLenSq = Vector2.Dot(ab, ab);
            if (abLenSq <= float.Epsilon)
                return a;

            var t = Vector2.Dot(p - a, ab) / abLenSq;
            if (t <= 0f)
                return a;
            if (t >= 1f)
                return b;
            return a + (ab * t);
        }

        private static List<Vector2> ProjectToXZ(IReadOnlyList<Vector3> points)
        {
            var projected = new List<Vector2>();
            if (points == null || points.Count == 0)
                return projected;

            projected.Capacity = points.Count;
            foreach (var point in points)
                projected.Add(new Vector2(point.X, point.Z));
            return projected;
        }

        private static bool IsPointInPolygon(IReadOnlyList<Vector2> points, Vector2 position)
        {
            var inside = false;
            var j = points.Count - 1;
            for (var i = 0; i < points.Count; i++)
            {
                var pi = points[i];
                var pj = points[j];
                var intersect = ((pi.Y > position.Y) != (pj.Y > position.Y)) &&
                                (position.X < (pj.X - pi.X) * (position.Y - pi.Y) / (pj.Y - pi.Y + float.Epsilon) + pi.X);
                if (intersect)
                    inside = !inside;
                j = i;
            }
            return inside;
        }

        private static float DistanceToSegment(Vector3 a, Vector3 b, Vector3 position)
        {
            var ab = b - a;
            var ap = position - a;
            var abLenSq = Vector3.Dot(ab, ab);
            if (abLenSq <= float.Epsilon)
                return Vector3.Distance(a, position);
            var t = Vector3.Dot(ap, ab) / abLenSq;
            if (t <= 0f)
                return Vector3.Distance(a, position);
            if (t >= 1f)
                return Vector3.Distance(b, position);
            var closest = a + (ab * t);
            return Vector3.Distance(closest, position);
        }

        private static float ResolveAverageY(IReadOnlyList<Vector3> points)
        {
            if (points == null || points.Count == 0)
                return 0f;
            var sum = 0f;
            for (var i = 0; i < points.Count; i++)
                sum += points[i].Y;
            return sum / points.Count;
        }

        private static bool TryGetPolygonPlane(IReadOnlyList<Vector3> points, out Vector3 planePoint, out Vector3 planeNormal)
        {
            planePoint = Vector3.Zero;
            planeNormal = Vector3.UnitY;
            if (points == null || points.Count < 3)
                return false;

            for (var i = 0; i < points.Count - 2; i++)
            {
                var a = points[i];
                var b = points[i + 1];
                for (var j = i + 2; j < points.Count; j++)
                {
                    var c = points[j];
                    var ab = b - a;
                    var ac = c - a;
                    var normal = Vector3.Cross(ab, ac);
                    if (normal.LengthSquared() <= 0.000001f)
                        continue;
                    planePoint = a;
                    planeNormal = Vector3.Normalize(normal);
                    return true;
                }
            }

            return false;
        }

        private bool IsWithinVolume(TrackApproachDefinition? approach, PortalDefinition? portal, Vector3 position)
        {
            if (approach != null &&
                !string.IsNullOrWhiteSpace(approach.VolumeId) &&
                !_areaManager.ContainsVolume(approach.VolumeId!, position))
            {
                return false;
            }

            if (portal != null &&
                !string.IsNullOrWhiteSpace(portal.VolumeId) &&
                !_areaManager.ContainsVolume(portal.VolumeId!, position))
            {
                return false;
            }

            var y = position.Y;
            var anchorY = portal?.Y ?? 0f;
            if (approach != null && (approach.VolumeThicknessMeters.HasValue ||
                                     approach.VolumeOffsetMeters.HasValue ||
                                     approach.VolumeMinY.HasValue ||
                                     approach.VolumeMaxY.HasValue))
            {
                if (!VolumeBounds.TryResolve(
                        anchorY,
                        approach.VolumeMode,
                        approach.VolumeOffsetMode,
                        approach.VolumeOffsetSpace,
                        approach.VolumeMinMaxSpace,
                        approach.VolumeThicknessMeters,
                        approach.VolumeOffsetMeters,
                        approach.VolumeMinY,
                        approach.VolumeMaxY,
                        out var minY,
                        out var maxY))
                {
                    return true;
                }

                return y >= minY && y <= maxY;
            }

            if (portal != null && (portal.VolumeThicknessMeters.HasValue ||
                                   portal.VolumeOffsetMeters.HasValue ||
                                   portal.VolumeMinY.HasValue ||
                                   portal.VolumeMaxY.HasValue))
            {
                if (!VolumeBounds.TryResolve(
                        portal.Y,
                        portal.VolumeMode,
                        portal.VolumeOffsetMode,
                        portal.VolumeOffsetSpace,
                        portal.VolumeMinMaxSpace,
                        portal.VolumeThicknessMeters,
                        portal.VolumeOffsetMeters,
                        portal.VolumeMinY,
                        portal.VolumeMaxY,
                        out var minY,
                        out var maxY))
                {
                    return true;
                }

                return y >= minY && y <= maxY;
            }

            return true;
        }


        private void InitializeSounds()
        {
            var root = Path.Combine(AssetPaths.SoundsRoot, "Legacy");
            _soundCrowd = CreateLoop(root, "crowd.wav");
            _soundOcean = CreateLoop(root, "ocean.wav");
            _soundRain = CreateLoop(root, "rain.wav");
            _soundWind = CreateLoop(root, "wind.wav");
            _soundStorm = CreateLoop(root, "storm.wav");
            _soundDesert = CreateLoop(root, "desert.wav");
            _soundAirport = CreateLoop(root, "airport.wav");
            _soundAirplane = CreateLoop(root, "airplane.wav");
            _soundClock = CreateLoop(root, "clock.wav");
            _soundJet = CreateLoop(root, "jet.wav");
            _soundThunder = CreateLoop(root, "thunder.wav");
            _soundPile = CreateLoop(root, "pile.wav");
            _soundConstruction = CreateLoop(root, "const.wav");
            _soundRiver = CreateLoop(root, "river.wav");
            _soundHelicopter = CreateLoop(root, "helicopter.wav");
            _soundOwl = CreateLoop(root, "owl.wav");
            _soundBeacon = CreateSpatial(root, "beacon.wav");
            if (_soundBeacon != null)
            {
                _soundBeacon.SetUseReflections(true);
                _soundBeacon.SetUseBakedReflections(false);
                _soundBeacon.SetDopplerFactor(0f);
            }
        }

        private void InitializeSteamAudioScene()
        {
            var steam = _audio.SteamAudio;
            if (steam == null)
                return;

            try
            {
                _steamAudioScene = SteamAudioSceneBuilder.Build(_map, steam);
                if (_steamAudioScene != null)
                    steam.SetScene(_steamAudioScene.Scene, _steamAudioScene.ProbeBatch, _steamAudioScene.BakedIdentifier, _steamAudioScene.HasBakedReflections);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Steam Audio scene build failed: " + ex);
            }
        }

        private AudioSourceHandle? CreateLoop(string root, string file)
        {
            var path = Path.Combine(root, file);
            if (!File.Exists(path))
                return null;
            return _audio.CreateLoopingSource(path);
        }

        private AudioSourceHandle? CreateSpatial(string root, string file)
        {
            var path = Path.Combine(root, file);
            if (!File.Exists(path))
                return null;
            return _audio.CreateSpatialSource(path, streamFromDisk: true, allowHrtf: true);
        }

        private void UpdateNoiseLoop(TrackNoise noise)
        {
            switch (noise)
            {
                case TrackNoise.Crowd:
                    PlayIfNotPlaying(_soundCrowd);
                    break;
                case TrackNoise.Ocean:
                    PlayIfNotPlaying(_soundOcean);
                    break;
                case TrackNoise.Trackside:
                    PlayIfNotPlaying(_soundAirplane);
                    break;
                case TrackNoise.Clock:
                    PlayIfNotPlaying(_soundClock);
                    break;
                case TrackNoise.Jet:
                    PlayIfNotPlaying(_soundJet);
                    break;
                case TrackNoise.Thunder:
                    PlayIfNotPlaying(_soundThunder);
                    break;
                case TrackNoise.Pile:
                case TrackNoise.Construction:
                    PlayIfNotPlaying(_soundPile);
                    PlayIfNotPlaying(_soundConstruction);
                    break;
                case TrackNoise.River:
                    PlayIfNotPlaying(_soundRiver);
                    break;
                case TrackNoise.Helicopter:
                    PlayIfNotPlaying(_soundHelicopter);
                    break;
                case TrackNoise.Owl:
                    PlayIfNotPlaying(_soundOwl);
                    break;
            }
        }

        private void StopNoise(TrackNoise noise)
        {
            switch (noise)
            {
                case TrackNoise.Crowd:
                    StopSound(_soundCrowd);
                    break;
                case TrackNoise.Ocean:
                    StopSound(_soundOcean);
                    break;
                case TrackNoise.Trackside:
                    StopSound(_soundAirplane);
                    break;
                case TrackNoise.Clock:
                    StopSound(_soundClock);
                    break;
                case TrackNoise.Jet:
                    StopSound(_soundJet);
                    break;
                case TrackNoise.Thunder:
                    StopSound(_soundThunder);
                    break;
                case TrackNoise.Pile:
                case TrackNoise.Construction:
                    StopSound(_soundPile);
                    StopSound(_soundConstruction);
                    break;
                case TrackNoise.River:
                    StopSound(_soundRiver);
                    break;
                case TrackNoise.Helicopter:
                    StopSound(_soundHelicopter);
                    break;
                case TrackNoise.Owl:
                    StopSound(_soundOwl);
                    break;
            }
        }

        private static void PlayIfNotPlaying(AudioSourceHandle? sound)
        {
            if (sound == null)
                return;
            if (!sound.IsPlaying)
                sound.Play(loop: true);
        }

        private void UpdateApproachBeacon(MapMovementState state, float elapsed)
        {
            if (_soundBeacon == null)
                return;

            var headingDegrees = state.HeadingDegrees;
            if (_approachBeacon.TryGetCue(state.WorldPosition, headingDegrees, out var cue) && !cue.Passed)
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
                return;
            }

            _beaconCooldown = 0f;
            StopSound(_soundBeacon);
            _soundBeacon.ClearBakedIdentifier();
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

        public bool IsWithinTrack(Vector3 worldPosition)
        {
            var safeZone = IsSafeZone(worldPosition);
            return IsWithinTrackInternal(worldPosition, safeZone);
        }

        private bool IsWithinTrackInternal(Vector3 worldPosition, bool isSafeZone)
        {
            if (_map.MinX.HasValue && worldPosition.X <= _map.MinX.Value)
                return false;
            if (_map.MinZ.HasValue && worldPosition.Z <= _map.MinZ.Value)
                return false;
            if (_map.MaxX.HasValue && worldPosition.X >= _map.MaxX.Value)
                return false;
            if (_map.MaxZ.HasValue && worldPosition.Z >= _map.MaxZ.Value)
                return false;

            var position = new Vector2(worldPosition.X, worldPosition.Z);
            if (IsBlockedBySectorRules(worldPosition))
                return false;
            if (IsBlockedByWall(position))
                return false;
            if (_areaManager != null && _areaManager.ContainsTrackArea(worldPosition))
                return true;
            if (_hasSurfaces && _surfaceSystem.TrySample(worldPosition, out _))
                return true;
            return isSafeZone;
        }

        public bool TryGetWallCollision(Vector3 fromWorld, Vector3 toWorld, out TrackWallDefinition wall)
        {
            wall = null!;
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
                if (TryFindWallAt(pos, out wall))
                    return true;
                pos += step;
            }

            return false;
        }

        public bool TryGetWallProximity(
            Vector3 worldPosition,
            Vector3 forward,
            float maxDistanceMeters,
            out TrackWallDefinition wall,
            out float distanceMeters,
            out Vector3 hitWorldPosition,
            out bool isBoundary)
        {
            wall = null!;
            distanceMeters = 0f;
            hitWorldPosition = worldPosition;
            isBoundary = false;

            var wallManager = _wallManager;
            var hasWalls = wallManager != null && wallManager.HasWalls;

            var forward2 = new Vector2(forward.X, forward.Z);
            if (forward2.LengthSquared() <= 0.000001f)
                return false;

            var maxDistance = Math.Max(0f, maxDistanceMeters);
            if (maxDistance <= 0.001f)
                return false;

            forward2 = Vector2.Normalize(forward2);
            var origin = new Vector2(worldPosition.X, worldPosition.Z);
            TrackWallDefinition? bestWall = null;
            Vector2 bestHit = default;
            var bestDistance = float.MaxValue;
            var bestIsBoundary = false;

            if (hasWalls)
            {
                foreach (var candidate in wallManager!.Walls)
                {
                    if (candidate == null)
                        continue;
                    if (candidate.CollisionMode == TrackWallCollisionMode.Pass)
                        continue;
                    if (!wallManager!.TryGetGeometry(candidate.GeometryId, out var geometry) || geometry == null)
                        continue;
                    if (!wallManager.TryGetGeometryPoints2D(geometry.Id, out var points2D))
                        continue;
                    if (!TryGetClosestPointOnGeometry(points2D, geometry.Type, origin, out var closest, out var rawDistance))
                        continue;

                    var to = closest - origin;
                    var toLen = to.Length();
                    if (toLen <= 0.0001f)
                    {
                        bestWall = candidate;
                        bestHit = closest;
                        bestDistance = 0f;
                        bestIsBoundary = false;
                        break;
                    }

                    var dirTo = to / toLen;
                    var dot = Vector2.Dot(forward2, dirTo);
                    if (dot <= WallProbeMinDot)
                        continue;

                    var width = Math.Max(0f, candidate.WidthMeters);
                    var adjustedDistance = Math.Max(0f, rawDistance - (width * 0.5f));
                    if (adjustedDistance > maxDistance)
                        continue;

                    if (adjustedDistance < bestDistance)
                    {
                        bestDistance = adjustedDistance;
                        bestWall = candidate;
                        bestHit = closest;
                        bestIsBoundary = false;
                    }
                }
            }

            if (TryFindBoundaryAlongRay(origin, forward2, worldPosition.Y, maxDistance, out var boundaryDistance, out var boundaryHit) &&
                boundaryDistance < bestDistance)
            {
                bestDistance = boundaryDistance;
                bestWall = null;
                bestHit = boundaryHit;
                bestIsBoundary = true;
            }

            if (bestWall == null)
            {
                if (!bestIsBoundary)
                    return false;
            }

            wall = bestWall!;
            distanceMeters = bestDistance;
            hitWorldPosition = new Vector3(bestHit.X, worldPosition.Y, bestHit.Y);
            isBoundary = bestIsBoundary;
            return true;
        }

        private bool TryFindBoundaryAlongRay(
            Vector2 origin,
            Vector2 direction,
            float worldY,
            float maxDistance,
            out float distanceMeters,
            out Vector2 hitPosition)
        {
            distanceMeters = 0f;
            hitPosition = origin;

            if (direction.LengthSquared() <= 0.000001f)
                return false;

            var startWorld = new Vector3(origin.X, worldY, origin.Y);
            if (!IsWithinTrack(startWorld))
                return false;

            var stepSize = Math.Max(0.1f, WallProbeStepMeters);
            var steps = Math.Max(1, (int)Math.Ceiling(maxDistance / stepSize));
            var step = maxDistance / steps;
            var pos = origin;
            for (var i = 1; i <= steps; i++)
            {
                pos += direction * step;
                var world = new Vector3(pos.X, worldY, pos.Y);
                if (!IsWithinTrack(world))
                {
                    distanceMeters = i * step;
                    hitPosition = pos;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetClosestPointOnGeometry(
            IReadOnlyList<Vector2> points,
            GeometryType geometryType,
            Vector2 position,
            out Vector2 closest,
            out float distance)
        {
            closest = position;
            distance = float.MaxValue;
            if (points == null || points.Count < 2)
                return false;
            var closed = geometryType == GeometryType.Polygon;
            if (!TryGetClosestPointOnPath(points, position, closed, out closest, out distance))
                return false;

            return true;
        }

        private static bool TryGetClosestPointOnPath(
            IReadOnlyList<Vector2> points,
            Vector2 position,
            bool closed,
            out Vector2 closest,
            out float distance)
        {
            closest = position;
            distance = float.MaxValue;
            if (points == null || points.Count < 2)
                return false;

            var count = points.Count;
            var lastIndex = count - 1;
            var segmentCount = lastIndex;
            var lastEqualsFirst = Vector2.DistanceSquared(points[0], points[lastIndex]) <= 0.0001f;

            for (var i = 0; i < segmentCount; i++)
            {
                var a = points[i];
                var b = points[i + 1];
                var c = ClosestPointOnSegment(a, b, position);
                var d = Vector2.DistanceSquared(position, c);
                if (d < distance)
                {
                    distance = d;
                    closest = c;
                }
            }

            if (closed && !lastEqualsFirst)
            {
                var a = points[lastIndex];
                var b = points[0];
                var c = ClosestPointOnSegment(a, b, position);
                var d = Vector2.DistanceSquared(position, c);
                if (d < distance)
                {
                    distance = d;
                    closest = c;
                }
            }

            if (distance < float.MaxValue)
            {
                distance = (float)Math.Sqrt(distance);
                return true;
            }

            return false;
        }

        private static Vector2 ClosestPointOnSegment(Vector2 a, Vector2 b, Vector2 p)
        {
            var ab = b - a;
            var abLenSq = Vector2.Dot(ab, ab);
            if (abLenSq <= float.Epsilon)
                return a;
            var t = Vector2.Dot(p - a, ab) / abLenSq;
            if (t <= 0f)
                return a;
            if (t >= 1f)
                return b;
            return a + ab * t;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        public bool TryGetMeshCollision(Vector3 fromWorld, Vector3 toWorld, out TrackMeshCollision collision)
        {
            collision = default;
            if (_meshCollisionManager == null || !_meshCollisionManager.HasColliders)
                return false;
            return _meshCollisionManager.TryGetCollision(fromWorld, toWorld, out collision);
        }

        public bool TryConstrainToSurface(Vector3 position, out Vector3 constrainedPosition, out TrackSurfaceSample sample)
        {
            constrainedPosition = position;
            sample = default;
            if (!_hasSurfaces)
                return false;

            var options = new TrackSurfaceQueryOptions
            {
                PreferClosestHeightToReference = true,
                ReferenceHeightMeters = position.Y
            };
            if (!_surfaceSystem.TrySample(position, out var hit, options))
                return false;

            var normal = hit.Normal;
            if (normal.LengthSquared() > 0.000001f)
                normal = Vector3.Normalize(normal);
            if (normal.Y <= 0.0001f)
                return false;

            constrainedPosition = new Vector3(position.X, hit.Position.Y, position.Z);
            sample = hit;
            return true;
        }

        public bool TryGetSurfaceOrientation(Vector3 position, float headingDegrees, out Vector3 forward, out Vector3 up)
        {
            forward = MapMovement.HeadingVector(headingDegrees);
            up = Vector3.UnitY;
            if (!_hasSurfaces)
                return false;

            var options = new TrackSurfaceQueryOptions
            {
                PreferClosestHeightToReference = true,
                ReferenceHeightMeters = position.Y
            };
            if (!_surfaceSystem.TrySample(position, out var sample, options))
                return false;

            var normal = sample.Normal;
            if (normal.LengthSquared() > 0.000001f)
                normal = Vector3.Normalize(normal);
            if (normal.Y <= 0.0001f)
                return false;

            var heading = forward;
            var projected = heading - (normal * Vector3.Dot(heading, normal));
            if (projected.LengthSquared() > 0.000001f)
                forward = Vector3.Normalize(projected);

            up = normal;
            return true;
        }

        private bool IsBlockedByWall(Vector2 position)
        {
            if (_wallManager == null || !_wallManager.HasWalls)
                return false;
            return TryFindWallAt(position, out _);
        }

        private bool TryFindWallAt(Vector2 position, out TrackWallDefinition wall)
        {
            wall = null!;
            foreach (var candidate in _wallManager.Walls)
            {
                if (candidate == null)
                    continue;
                if (candidate.CollisionMode == TrackWallCollisionMode.Pass)
                    continue;
                if (_wallManager.Contains(candidate, position))
                {
                    wall = candidate;
                    return true;
                }
            }
            return false;
        }

        private static bool IsWidthAffectingArea(TrackAreaDefinition area)
        {
            if (area == null)
                return false;
            if (area.Type == TrackAreaType.Boundary || area.Type == TrackAreaType.OffTrack)
                return false;
            if (area.Type == TrackAreaType.Start || area.Type == TrackAreaType.Finish ||
                area.Type == TrackAreaType.Checkpoint || area.Type == TrackAreaType.Intersection)
                return false;
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

        private static float HeadingDegrees(MapDirection heading)
        {
            return heading switch
            {
                MapDirection.North => 0f,
                MapDirection.East => 90f,
                MapDirection.South => 180f,
                MapDirection.West => 270f,
                _ => 0f
            };
        }

        private static Vector2 HeadingToVector(float headingDegrees)
        {
            var radians = headingDegrees * (float)Math.PI / 180f;
            return new Vector2((float)Math.Sin(radians), (float)Math.Cos(radians));
        }

        private static void AddHeading(List<float> list, float headingDegrees)
        {
            var normalized = NormalizeDegrees(headingDegrees);
            for (var i = 0; i < list.Count; i++)
            {
                var delta = Math.Abs(NormalizeDegrees(list[i]) - normalized);
                if (delta < 1f || Math.Abs(delta - 360f) < 1f)
                    return;
            }
            list.Add(normalized);
        }

        private static float NormalizeDegrees(float degrees)
        {
            var result = degrees % 360f;
            if (result < 0f)
                result += 360f;
            return result;
        }

        private static float DeltaDegrees(float current, float target)
        {
            var diff = Math.Abs(NormalizeDegrees(current - target));
            return diff > 180f ? 360f - diff : diff;
        }

        internal bool IsSectorTransitionAllowed(Vector3 fromPosition, Vector3 toPosition, float headingDegrees)
        {
            if (_sectorRuleManager == null || _sectorManager == null)
                return true;

            var hasFrom = _sectorManager.TryLocate(fromPosition, headingDegrees, out var fromSector, out var fromPortal, out _);
            var hasTo = _sectorManager.TryLocate(toPosition, headingDegrees, out var toSector, out var toPortal, out _);

            if (!hasTo)
                return true;

            if (!_sectorRuleManager.TryGetRules(toSector.Id, out var toRules))
                return true;

            if (toRules.IsClosed || toRules.IsRestricted)
                return false;

            if (hasFrom && !string.Equals(fromSector.Id, toSector.Id, StringComparison.OrdinalIgnoreCase))
            {
                var entryPortalId = toPortal?.Id;
                var exitPortalId = fromPortal?.Id;
                var direction = MapMovement.ToCardinal(headingDegrees);
                if (!_sectorRuleManager.AllowsExit(fromSector.Id, exitPortalId, direction))
                    return false;
                if (!_sectorRuleManager.AllowsEntry(toSector.Id, entryPortalId, direction))
                    return false;
            }

            return true;
        }

        private void ApplySectorRuleFields(Vector3 worldPosition, float headingDegrees, ref TrackRoad road)
        {
            if (_sectorRuleManager == null || _sectorManager == null)
                return;

            if (!_sectorManager.TryLocate(worldPosition, headingDegrees, out var sector, out _, out _))
                return;
            if (!_sectorRuleManager.TryGetRules(sector.Id, out var rules))
                return;

            road.IsClosed = rules.IsClosed;
            road.IsRestricted = rules.IsRestricted;
            road.RequiresStop = rules.RequiresStop;
            road.RequiresYield = rules.RequiresYield;
            road.MinSpeedKph = rules.MinSpeedKph;
            road.MaxSpeedKph = rules.MaxSpeedKph;
        }

        private bool IsBlockedBySectorRules(Vector3 worldPosition)
        {
            if (_sectorRuleManager == null || _sectorManager == null)
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

        private static void StopSound(AudioSourceHandle? sound)
        {
            if (sound == null)
                return;
            sound.Stop();
        }

        private void StopAllSounds()
        {
            StopSound(_soundCrowd);
            StopSound(_soundOcean);
            StopSound(_soundRain);
            StopSound(_soundWind);
            StopSound(_soundStorm);
            StopSound(_soundDesert);
            StopSound(_soundAirport);
            StopSound(_soundAirplane);
            StopSound(_soundClock);
            StopSound(_soundJet);
            StopSound(_soundThunder);
            StopSound(_soundPile);
            StopSound(_soundConstruction);
            StopSound(_soundRiver);
            StopSound(_soundHelicopter);
            StopSound(_soundOwl);
            StopSound(_soundBeacon);
        }

        private static void DisposeSound(AudioSourceHandle? sound)
        {
            if (sound == null)
                return;
            sound.Stop();
            sound.Dispose();
        }
    }
}
