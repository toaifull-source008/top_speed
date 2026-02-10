using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using TopSpeed.Audio;
using TopSpeed.Core;
using TopSpeed.Tracks.Areas;
using TopSpeed.Tracks.Geometry;
using TopSpeed.Tracks.Map;
using TS.Audio;

namespace TopSpeed.Tracks.Sounds
{
    internal sealed class TrackSoundSourceSystem : IDisposable
    {
        private const float DefaultRandomCrossfadeSeconds = 0.75f;
        private sealed class SoundSourceState
        {
            public SoundSourceState(TrackSoundSourceDefinition definition)
            {
                Definition = definition;
            }

            public TrackSoundSourceDefinition Definition { get; }
            public TrackSoundSourceDefinition? ActiveDefinition { get; set; }
            public string? ActivePath { get; set; }
            public string? ActivePathFull { get; set; }
            public string? ActiveAreaId { get; set; }
            public AudioSourceHandle? Handle { get; set; }
            public bool IsPlaying { get; set; }
            public float PathDistance { get; set; }
            public PathSampler? PathSampler { get; set; }
        }

        private sealed class PathSampler
        {
            public PathSampler(Vector3[] points, float[] segmentLengths, float totalLength, bool closed)
            {
                Points = points;
                SegmentLengths = segmentLengths;
                TotalLength = totalLength;
                Closed = closed;
            }

            public Vector3[] Points { get; }
            public float[] SegmentLengths { get; }
            public float TotalLength { get; }
            public bool Closed { get; }
        }

        private sealed class PendingStop
        {
            public PendingStop(AudioSourceHandle handle, float remainingSeconds)
            {
                Handle = handle;
                RemainingSeconds = remainingSeconds;
            }

            public AudioSourceHandle Handle { get; }
            public float RemainingSeconds { get; set; }
        }

        private readonly TrackMap _map;
        private readonly TrackAreaManager _areaManager;
        private readonly AudioManager _audio;
        private readonly Dictionary<string, TrackSoundSourceDefinition> _sourceLookup;
        private readonly Dictionary<string, GeometryDefinition> _geometryLookup;
        private readonly Dictionary<string, TrackAreaDefinition> _areaLookup;
        private readonly Dictionary<string, HashSet<string>> _areasBySource;
        private readonly Dictionary<string, Vector3> _geometryCenters;
        private readonly List<SoundSourceState> _states;
        private readonly List<PendingStop> _pendingStops;
        private readonly Random _random;
        private readonly string _rootDirectory;
        private float _fastAccumulator;
        private float _slowAccumulator;
        private HashSet<string>? _lastAreaIds;

        public TrackSoundSourceSystem(TrackMap map, TrackAreaManager areaManager, AudioManager audio)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _areaManager = areaManager ?? throw new ArgumentNullException(nameof(areaManager));
            _audio = audio ?? throw new ArgumentNullException(nameof(audio));
            _random = new Random();

            _sourceLookup = new Dictionary<string, TrackSoundSourceDefinition>(StringComparer.OrdinalIgnoreCase);
            _states = new List<SoundSourceState>();
            if (_map.SoundSources != null)
            {
                foreach (var source in _map.SoundSources)
                {
                    if (source == null)
                        continue;
                    _sourceLookup[source.Id] = source;
                    _states.Add(new SoundSourceState(source));
                }
            }

            _geometryLookup = new Dictionary<string, GeometryDefinition>(StringComparer.OrdinalIgnoreCase);
            _geometryCenters = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
            if (_map.Geometries != null)
            {
                foreach (var geometry in _map.Geometries)
                {
                    if (geometry == null || string.IsNullOrWhiteSpace(geometry.Id))
                        continue;
                    _geometryLookup[geometry.Id] = geometry;
                    if (TryComputeCenter(geometry, out var center))
                        _geometryCenters[geometry.Id] = center;
                }
            }

            _areaLookup = new Dictionary<string, TrackAreaDefinition>(StringComparer.OrdinalIgnoreCase);
            if (_map.Areas != null)
            {
                foreach (var area in _map.Areas)
                {
                    if (area == null || string.IsNullOrWhiteSpace(area.Id))
                        continue;
                    _areaLookup[area.Id] = area;
                }
            }

            _areasBySource = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            if (_map.Areas != null)
            {
                foreach (var area in _map.Areas)
                {
                    if (area == null || area.SoundSourceIds == null)
                        continue;
                    foreach (var sourceId in area.SoundSourceIds)
                    {
                        if (string.IsNullOrWhiteSpace(sourceId))
                            continue;
                        if (!_areasBySource.TryGetValue(sourceId, out var set))
                        {
                            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            _areasBySource[sourceId] = set;
                        }
                        set.Add(area.Id);
                    }
                }
            }

            _rootDirectory = ResolveRootDirectory(_map);
            _pendingStops = new List<PendingStop>();
        }

        public void Initialize()
        {
            for (int i = 0; i < _states.Count; i++)
            {
                ResetState(_states[i]);
            }

            for (int i = 0; i < _states.Count; i++)
            {
                if (_states[i].Definition.Global)
                    StartState(_states[i], activeAreaId: null, listenerPosition: Vector3.Zero, forceNewVariant: true);
            }
        }

        public void Update(Vector3 listenerPosition, float elapsedSeconds)
        {
            var areas = _areaManager.FindAreasContaining(listenerPosition);
            Update(listenerPosition, elapsedSeconds, areas);
        }

        public void Update(Vector3 listenerPosition, float elapsedSeconds, IReadOnlyList<TrackAreaDefinition> areas)
        {
            UpdatePendingStops(elapsedSeconds);

            var currentAreaIds = BuildAreaIdSet(areas);
            var areaChanged = !AreAreaSetsEqual(_lastAreaIds, currentAreaIds);
            _lastAreaIds = currentAreaIds;

            var fastTick = 1f / 30f;
            var slowTick = 1f / 5f;
            _fastAccumulator += Math.Max(0f, elapsedSeconds);
            _slowAccumulator += Math.Max(0f, elapsedSeconds);

            var fastUpdate = areaChanged || _fastAccumulator >= fastTick;
            var slowUpdate = areaChanged || _slowAccumulator >= slowTick;

            if (fastUpdate)
                _fastAccumulator = 0f;
            if (slowUpdate)
                _slowAccumulator = 0f;

            var fastDelta = fastUpdate ? Math.Min(_fastAccumulator > 0f ? _fastAccumulator : fastTick, 0.5f) : 0f;
            var slowDelta = slowUpdate ? Math.Min(_slowAccumulator > 0f ? _slowAccumulator : slowTick, 1.0f) : 0f;

            for (int i = 0; i < _states.Count; i++)
            {
                var state = _states[i];
                if (state == null)
                    continue;
                var isFast = NeedsFastUpdate(state.Definition);
                if (isFast && fastUpdate)
                    UpdateState(state, areas, currentAreaIds, listenerPosition, fastDelta);
                else if (!isFast && slowUpdate)
                    UpdateState(state, areas, currentAreaIds, listenerPosition, slowDelta);
            }
        }

        public void StopAll()
        {
            for (int i = 0; i < _states.Count; i++)
            {
                StopState(_states[i]);
            }

            DisposePendingStops();
        }

        public void Dispose()
        {
            for (int i = 0; i < _states.Count; i++)
            {
                DisposeState(_states[i]);
            }

            DisposePendingStops();
        }

        private void UpdateState(
            SoundSourceState state,
            IReadOnlyList<TrackAreaDefinition> areas,
            HashSet<string>? currentAreaIds,
            Vector3 listenerPosition,
            float elapsedSeconds)
        {
            var definition = state.Definition;
            var activeAreaId = FindActiveAreaId(definition, areas);
            var hasStartEnd = HasStartEnd(definition);

            bool shouldPlay;
            if (definition.Global)
            {
                shouldPlay = true;
            }
            else if (hasStartEnd)
            {
                if (!state.IsPlaying)
                    shouldPlay = IsStartConditionMet(definition, currentAreaIds, listenerPosition);
                else
                    shouldPlay = !IsEndConditionMet(definition, currentAreaIds, listenerPosition);
            }
            else
            {
                shouldPlay = activeAreaId != null;
            }

            var areaChanged = !string.Equals(state.ActiveAreaId, activeAreaId, StringComparison.OrdinalIgnoreCase);
            var needsVariantRefresh = definition.Type == TrackSoundSourceType.Random &&
                                      (state.ActiveDefinition == null ||
                                       state.ActivePath == null ||
                                       (!state.IsPlaying && definition.RandomMode == TrackSoundRandomMode.OnStart) ||
                                       (definition.RandomMode == TrackSoundRandomMode.PerArea && areaChanged));

            if (shouldPlay)
            {
                if (!state.IsPlaying || needsVariantRefresh)
                    StartState(state, activeAreaId, listenerPosition, needsVariantRefresh);
                else if (areaChanged)
                    state.ActiveAreaId = activeAreaId;
            }
            else if (state.IsPlaying)
            {
                StopState(state);
                state.ActiveAreaId = null;
            }

            if (state.IsPlaying)
            {
                UpdateActivePosition(state, activeAreaId, listenerPosition, elapsedSeconds);
            }
        }

        private void StartState(SoundSourceState state, string? activeAreaId, Vector3 listenerPosition, bool forceNewVariant)
        {
            var definition = state.Definition;
            if (definition.Type == TrackSoundSourceType.Random)
            {
                if (forceNewVariant || state.ActiveDefinition == null || state.ActivePath == null)
                {
                    if (!TrySelectVariant(state))
                        return;
                }
            }
            else
            {
                state.ActiveDefinition = definition;
                state.ActivePath = definition.Path;
            }

            if (state.ActiveDefinition == null || string.IsNullOrWhiteSpace(state.ActivePath))
                return;

            if (!TryResolveAudioPath(state.ActivePath!, out var fullPath))
                return;

            var needsNewHandle = state.Handle == null ||
                                 !string.Equals(state.ActivePathFull, fullPath, StringComparison.OrdinalIgnoreCase) ||
                                 forceNewVariant;

            var crossfadeSeconds = 0f;
            if (forceNewVariant && state.Definition.Type == TrackSoundSourceType.Random)
                crossfadeSeconds = ResolveRandomCrossfadeSeconds(state.Definition);

            if (needsNewHandle)
            {
                var previousHandle = state.Handle;
                var previousDefinition = state.ActiveDefinition;
                state.Handle = CreateHandle(state.ActiveDefinition, fullPath);
                state.ActivePathFull = fullPath;
                if (state.Handle == null)
                    return;

                if (previousHandle != null)
                {
                    var fadeOut = previousDefinition?.FadeOutSeconds ?? 0f;
                    if (crossfadeSeconds > 0f && fadeOut <= 0f)
                        fadeOut = crossfadeSeconds;
                    EnqueueFadeOut(previousHandle, fadeOut);
                }
            }

            if (state.Handle == null)
                return;

            ApplySourceSettings(state.Handle, state.ActiveDefinition);

            if (state.ActiveDefinition.Spatial)
            {
                var position = ResolvePosition(state.ActiveDefinition, activeAreaId, listenerPosition);
                state.Handle.SetPosition(position);
                state.Handle.SetVelocity(Vector3.Zero);
            }

            state.Handle.SeekToStart();
            var fadeIn = state.ActiveDefinition.FadeInSeconds;
            if (crossfadeSeconds > 0f && fadeIn <= 0f)
                fadeIn = crossfadeSeconds;
            if (fadeIn > 0f)
                state.Handle.Play(state.ActiveDefinition.Loop, fadeIn);
            else
                state.Handle.Play(state.ActiveDefinition.Loop);

            state.ActiveAreaId = activeAreaId;
            state.IsPlaying = true;
        }

        private void StopState(SoundSourceState state)
        {
            if (state.Handle != null)
            {
                var fadeOut = state.ActiveDefinition?.FadeOutSeconds ?? state.Definition.FadeOutSeconds;
                if (fadeOut > 0f)
                    state.Handle.Stop(fadeOut);
                else
                    state.Handle.Stop();
            }

            state.IsPlaying = false;
        }

        private void ResetState(SoundSourceState state)
        {
            DisposeState(state);
            state.ActiveDefinition = null;
            state.ActivePath = null;
            state.ActivePathFull = null;
            state.ActiveAreaId = null;
            state.PathDistance = 0f;
            state.PathSampler = null;
            state.IsPlaying = false;
        }

        private void DisposeState(SoundSourceState state)
        {
            if (state.Handle == null)
                return;
            state.Handle.Stop();
            state.Handle.Dispose();
            state.Handle = null;
        }

        private void DisposeHandle(SoundSourceState state)
        {
            if (state.Handle == null)
                return;
            state.Handle.Stop();
            state.Handle.Dispose();
            state.Handle = null;
        }

        private void EnqueueFadeOut(AudioSourceHandle handle, float fadeOutSeconds)
        {
            if (handle == null)
                return;
            if (fadeOutSeconds <= 0f || !handle.IsPlaying)
            {
                handle.Stop();
                handle.Dispose();
                return;
            }

            handle.Stop(fadeOutSeconds);
            for (int i = 0; i < _pendingStops.Count; i++)
            {
                if (ReferenceEquals(_pendingStops[i].Handle, handle))
                {
                    if (_pendingStops[i].RemainingSeconds < fadeOutSeconds)
                        _pendingStops[i].RemainingSeconds = fadeOutSeconds;
                    return;
                }
            }

            _pendingStops.Add(new PendingStop(handle, fadeOutSeconds));
        }

        private void UpdatePendingStops(float elapsedSeconds)
        {
            if (_pendingStops.Count == 0)
                return;
            var step = Math.Max(0f, elapsedSeconds);
            for (int i = _pendingStops.Count - 1; i >= 0; i--)
            {
                var pending = _pendingStops[i];
                pending.RemainingSeconds -= step;
                if (pending.RemainingSeconds <= 0f)
                {
                    pending.Handle.Dispose();
                    _pendingStops.RemoveAt(i);
                }
            }
        }

        private void DisposePendingStops()
        {
            for (int i = _pendingStops.Count - 1; i >= 0; i--)
            {
                _pendingStops[i].Handle.Dispose();
            }
            _pendingStops.Clear();
        }

        private void UpdateActivePosition(SoundSourceState state, string? activeAreaId, Vector3 listenerPosition, float elapsedSeconds)
        {
            var definition = state.ActiveDefinition;
            if (definition == null || state.Handle == null || !definition.Spatial)
                return;

            if (definition.Type == TrackSoundSourceType.Moving)
            {
                UpdateMovingPosition(state, definition, elapsedSeconds);
                return;
            }

            var position = ResolvePosition(definition, activeAreaId, listenerPosition);
            state.Handle.SetPosition(position);
        }

        private void UpdateMovingPosition(SoundSourceState state, TrackSoundSourceDefinition definition, float elapsedSeconds)
        {
            if (state.Handle == null)
                return;

            if (state.PathSampler == null)
                state.PathSampler = BuildPathSampler(definition);
            var sampler = state.PathSampler;
            if (sampler == null || sampler.TotalLength <= 0f)
                return;

            var speed = definition.SpeedMetersPerSecond ?? 0f;
            if (speed <= 0f)
                return;

            state.PathDistance += speed * Math.Max(0f, elapsedSeconds);

            if (definition.Loop && sampler.TotalLength > 0f)
            {
                state.PathDistance %= sampler.TotalLength;
            }
            else if (state.PathDistance >= sampler.TotalLength)
            {
                state.PathDistance = sampler.TotalLength;
                StopState(state);
                return;
            }

            var position = SamplePath(sampler, state.PathDistance);
            state.Handle.SetPosition(position);
        }

        private Vector3 ResolvePosition(TrackSoundSourceDefinition definition, string? activeAreaId, Vector3 listenerPosition)
        {
            if (definition.Position.HasValue)
                return definition.Position.Value;

            if (!string.IsNullOrWhiteSpace(definition.GeometryId) &&
                _geometryCenters.TryGetValue(definition.GeometryId!, out var center))
            {
                return center;
            }

            if (!string.IsNullOrWhiteSpace(activeAreaId) &&
                _areaLookup.TryGetValue(activeAreaId!, out var area) &&
                !string.IsNullOrWhiteSpace(area.GeometryId) &&
                _geometryCenters.TryGetValue(area.GeometryId, out center))
            {
                return center;
            }

            if (definition.StartPosition.HasValue)
                return definition.StartPosition.Value;

            return listenerPosition;
        }

        private bool TrySelectVariant(SoundSourceState state)
        {
            var definition = state.Definition;
            var candidates = new List<(TrackSoundSourceDefinition Definition, string Path)>();

            if (!string.IsNullOrWhiteSpace(definition.Path))
                candidates.Add((definition, definition.Path!));

            if (definition.VariantPaths != null)
            {
                foreach (var path in definition.VariantPaths)
                {
                    if (!string.IsNullOrWhiteSpace(path))
                        candidates.Add((definition, path));
                }
            }

            if (definition.VariantSourceIds != null)
            {
                foreach (var id in definition.VariantSourceIds)
                {
                    if (string.IsNullOrWhiteSpace(id))
                        continue;
                    if (_sourceLookup.TryGetValue(id, out var other) &&
                        other.Type != TrackSoundSourceType.Random &&
                        !string.IsNullOrWhiteSpace(other.Path))
                    {
                        candidates.Add((other, other.Path!));
                    }
                }
            }

            if (candidates.Count == 0)
                return false;

            var index = _random.Next(candidates.Count);
            var selected = candidates[index];
            state.ActiveDefinition = selected.Definition;
            state.ActivePath = selected.Path;
            state.ActivePathFull = null;
            state.PathSampler = null;
            state.PathDistance = 0f;
            return true;
        }

        private AudioSourceHandle? CreateHandle(TrackSoundSourceDefinition definition, string fullPath)
        {
            try
            {
                if (definition.Spatial)
                {
                    return definition.Loop
                        ? _audio.CreateLoopingSpatialSource(fullPath, definition.AllowHrtf)
                        : _audio.CreateSpatialSource(fullPath, streamFromDisk: true, allowHrtf: definition.AllowHrtf);
                }

                return definition.Loop
                    ? _audio.CreateLoopingSource(fullPath, useHrtf: false)
                    : _audio.CreateSource(fullPath, streamFromDisk: true, useHrtf: false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Sound source create failed: " + ex);
                return null;
            }
        }

        private static void ApplySourceSettings(AudioSourceHandle handle, TrackSoundSourceDefinition definition)
        {
            handle.SetVolume(definition.Volume);
            handle.SetPitch(definition.Pitch);
            if (!definition.Spatial)
            {
                handle.SetPan(definition.Pan);
                return;
            }

            handle.SetUseReflections(definition.UseReflections);
            handle.SetUseBakedReflections(definition.UseBakedReflections);

            if (definition.MinDistance.HasValue || definition.MaxDistance.HasValue || definition.Rolloff.HasValue)
            {
                var minDistance = definition.MinDistance ?? 1f;
                var maxDistance = definition.MaxDistance ?? 10000f;
                var rolloff = definition.Rolloff ?? 1f;
                handle.SetDistanceModel(DistanceModel.Inverse, minDistance, maxDistance, rolloff);
            }
        }

        private string? FindActiveAreaId(TrackSoundSourceDefinition definition, IReadOnlyList<TrackAreaDefinition> areas)
        {
            if (!_areasBySource.TryGetValue(definition.Id, out var areaIds) || areaIds.Count == 0)
                return null;
            for (int i = 0; i < areas.Count; i++)
            {
                var area = areas[i];
                if (area == null)
                    continue;
                if (areaIds.Contains(area.Id))
                    return area.Id;
            }
            return null;
        }

        private static bool HasStartEnd(TrackSoundSourceDefinition definition)
        {
            return !string.IsNullOrWhiteSpace(definition.StartAreaId) ||
                   !string.IsNullOrWhiteSpace(definition.EndAreaId) ||
                   definition.StartPosition.HasValue ||
                   definition.EndPosition.HasValue;
        }

        private static bool NeedsFastUpdate(TrackSoundSourceDefinition definition)
        {
            if (definition == null)
                return false;
            if (definition.Type == TrackSoundSourceType.Moving)
                return true;
            return HasStartEnd(definition);
        }

        private static HashSet<string>? BuildAreaIdSet(IReadOnlyList<TrackAreaDefinition> areas)
        {
            if (areas == null || areas.Count == 0)
                return null;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < areas.Count; i++)
            {
                var area = areas[i];
                if (area == null)
                    continue;
                set.Add(area.Id);
            }
            return set.Count == 0 ? null : set;
        }

        private static bool AreAreaSetsEqual(HashSet<string>? left, HashSet<string>? right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || left.Count == 0)
                return right == null || right.Count == 0;
            if (right == null || right.Count == 0)
                return false;
            if (left.Count != right.Count)
                return false;
            foreach (var item in left)
            {
                if (!right.Contains(item))
                    return false;
            }
            return true;
        }

        private static bool IsStartConditionMet(TrackSoundSourceDefinition definition, HashSet<string>? currentAreas, Vector3 listenerPosition)
        {
            if (string.IsNullOrWhiteSpace(definition.StartAreaId) && !definition.StartPosition.HasValue)
                return true;

            if (!string.IsNullOrWhiteSpace(definition.StartAreaId) &&
                currentAreas != null &&
                currentAreas.Contains(definition.StartAreaId!))
            {
                return true;
            }

            if (definition.StartPosition.HasValue)
            {
                var radius = definition.StartRadiusMeters ?? 1f;
                if (radius <= 0f)
                    radius = 1f;
                return Vector3.DistanceSquared(listenerPosition, definition.StartPosition.Value) <= radius * radius;
            }

            return false;
        }

        private static bool IsEndConditionMet(TrackSoundSourceDefinition definition, HashSet<string>? currentAreas, Vector3 listenerPosition)
        {
            if (!string.IsNullOrWhiteSpace(definition.EndAreaId) &&
                currentAreas != null &&
                currentAreas.Contains(definition.EndAreaId!))
            {
                return true;
            }

            if (definition.EndPosition.HasValue)
            {
                var radius = definition.EndRadiusMeters ?? 1f;
                if (radius <= 0f)
                    radius = 1f;
                return Vector3.DistanceSquared(listenerPosition, definition.EndPosition.Value) <= radius * radius;
            }

            return false;
        }

        private PathSampler? BuildPathSampler(TrackSoundSourceDefinition definition)
        {
            var geometryId = !string.IsNullOrWhiteSpace(definition.PathGeometryId)
                ? definition.PathGeometryId
                : definition.GeometryId;

            if (string.IsNullOrWhiteSpace(geometryId))
                return null;

            if (!_geometryLookup.TryGetValue(geometryId!, out var geometry))
                return null;

            if (geometry.Points == null || geometry.Points.Count < 2)
                return null;

            var points = geometry.Points as Vector3[] ?? new List<Vector3>(geometry.Points).ToArray();
            var closed = geometry.Type == GeometryType.Polygon;
            var segmentCount = closed ? points.Length : points.Length - 1;
            if (segmentCount <= 0)
                return null;

            var lengths = new float[segmentCount];
            var total = 0f;
            for (int i = 0; i < segmentCount; i++)
            {
                var a = points[i];
                var b = (i == points.Length - 1) ? points[0] : points[i + 1];
                var len = Vector3.Distance(a, b);
                lengths[i] = len;
                total += len;
            }

            return new PathSampler(points, lengths, total, closed);
        }

        private static Vector3 SamplePath(PathSampler sampler, float distance)
        {
            if (sampler.TotalLength <= 0f)
                return sampler.Points[0];

            var remaining = distance;
            for (int i = 0; i < sampler.SegmentLengths.Length; i++)
            {
                var segLen = sampler.SegmentLengths[i];
                if (segLen <= 0f)
                    continue;
                if (remaining <= segLen)
                {
                    var a = sampler.Points[i];
                    var b = (i == sampler.Points.Length - 1) ? sampler.Points[0] : sampler.Points[i + 1];
                    var t = remaining / segLen;
                    return Vector3.Lerp(a, b, t);
                }
                remaining -= segLen;
            }

            return sampler.Points[sampler.Points.Length - 1];
        }

        private static float ResolveRandomCrossfadeSeconds(TrackSoundSourceDefinition definition)
        {
            if (definition.CrossfadeSeconds.HasValue)
                return Math.Max(0f, definition.CrossfadeSeconds.Value);
            return DefaultRandomCrossfadeSeconds;
        }

        private bool TryResolveAudioPath(string relativePath, out string fullPath)
        {
            fullPath = string.Empty;
            if (string.IsNullOrWhiteSpace(relativePath))
                return false;
            if (Path.IsPathRooted(relativePath))
                return false;

            var root = _rootDirectory;
            if (string.IsNullOrWhiteSpace(root))
                root = AppContext.BaseDirectory;

            var rootFull = Path.GetFullPath(root);
            if (!rootFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                rootFull += Path.DirectorySeparatorChar;
            var combined = Path.GetFullPath(Path.Combine(rootFull, relativePath));
            if (!combined.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!File.Exists(combined))
                return false;

            fullPath = combined;
            return true;
        }

        private static string ResolveRootDirectory(TrackMap map)
        {
            if (map == null)
                return AppContext.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(map.RootDirectory))
                return map.RootDirectory;
            if (!string.IsNullOrWhiteSpace(map.MapPath))
                return Path.GetDirectoryName(map.MapPath!) ?? AppContext.BaseDirectory;
            return Path.Combine(AssetPaths.Root, "Tracks");
        }

        private static bool TryComputeCenter(GeometryDefinition geometry, out Vector3 center)
        {
            center = Vector3.Zero;
            if (geometry == null || geometry.Points == null || geometry.Points.Count == 0)
                return false;

            var sum = Vector3.Zero;
            var count = 0;
            foreach (var point in geometry.Points)
            {
                sum += point;
                count++;
            }

            if (count == 0)
                return false;
            center = sum / count;
            return true;
        }
    }
}
