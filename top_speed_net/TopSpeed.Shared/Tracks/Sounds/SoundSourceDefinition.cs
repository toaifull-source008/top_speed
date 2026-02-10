using System;
using System.Collections.Generic;
using System.Numerics;

namespace TopSpeed.Tracks.Sounds
{
    public enum TrackSoundSourceType
    {
        Ambient = 0,
        Static = 1,
        Moving = 2,
        Random = 3
    }

    public enum TrackSoundRandomMode
    {
        OnStart = 0,
        PerArea = 1
    }

    public sealed class TrackSoundSourceDefinition
    {
        private static readonly IReadOnlyList<string> EmptyList = Array.Empty<string>();

        public TrackSoundSourceDefinition(
            string id,
            TrackSoundSourceType type,
            string? path,
            IReadOnlyList<string>? variantPaths,
            IReadOnlyList<string>? variantSourceIds,
            TrackSoundRandomMode randomMode,
            bool loop,
            float volume,
            bool spatial,
            bool allowHrtf,
            bool useReflections,
            bool useBakedReflections,
            float fadeInSeconds,
            float fadeOutSeconds,
            float? crossfadeSeconds,
            float pitch,
            float pan,
            float? minDistance,
            float? maxDistance,
            float? rolloff,
            bool global,
            string? startAreaId,
            string? endAreaId,
            Vector3? startPosition,
            float? startRadiusMeters,
            Vector3? endPosition,
            float? endRadiusMeters,
            Vector3? position,
            string? geometryId,
            string? pathGeometryId,
            float? speedMetersPerSecond)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Sound source id is required.", nameof(id));

            Id = id.Trim();
            Type = type;
            Path = string.IsNullOrWhiteSpace(path) ? null : path?.Trim();
            VariantPaths = variantPaths ?? EmptyList;
            VariantSourceIds = variantSourceIds ?? EmptyList;
            RandomMode = randomMode;
            Loop = loop;
            Volume = Clamp01(volume);
            Spatial = spatial;
            AllowHrtf = allowHrtf;
            UseReflections = useReflections;
            UseBakedReflections = useBakedReflections;
            FadeInSeconds = Math.Max(0f, fadeInSeconds);
            FadeOutSeconds = Math.Max(0f, fadeOutSeconds);
            CrossfadeSeconds = crossfadeSeconds.HasValue ? Math.Max(0f, crossfadeSeconds.Value) : (float?)null;
            Pitch = pitch <= 0f ? 1.0f : pitch;
            Pan = ClampPan(pan);
            MinDistance = minDistance;
            MaxDistance = maxDistance;
            Rolloff = rolloff;
            Global = global;
            StartAreaId = string.IsNullOrWhiteSpace(startAreaId) ? null : startAreaId?.Trim();
            EndAreaId = string.IsNullOrWhiteSpace(endAreaId) ? null : endAreaId?.Trim();
            StartPosition = startPosition;
            StartRadiusMeters = startRadiusMeters;
            EndPosition = endPosition;
            EndRadiusMeters = endRadiusMeters;
            Position = position;
            GeometryId = string.IsNullOrWhiteSpace(geometryId) ? null : geometryId?.Trim();
            PathGeometryId = string.IsNullOrWhiteSpace(pathGeometryId) ? null : pathGeometryId?.Trim();
            SpeedMetersPerSecond = speedMetersPerSecond;
        }

        public string Id { get; }
        public TrackSoundSourceType Type { get; }
        public string? Path { get; }
        public IReadOnlyList<string> VariantPaths { get; }
        public IReadOnlyList<string> VariantSourceIds { get; }
        public TrackSoundRandomMode RandomMode { get; }
        public bool Loop { get; }
        public float Volume { get; }
        public bool Spatial { get; }
        public bool AllowHrtf { get; }
        public bool UseReflections { get; }
        public bool UseBakedReflections { get; }
        public float FadeInSeconds { get; }
        public float FadeOutSeconds { get; }
        public float? CrossfadeSeconds { get; }
        public float Pitch { get; }
        public float Pan { get; }
        public float? MinDistance { get; }
        public float? MaxDistance { get; }
        public float? Rolloff { get; }
        public bool Global { get; }
        public string? StartAreaId { get; }
        public string? EndAreaId { get; }
        public Vector3? StartPosition { get; }
        public float? StartRadiusMeters { get; }
        public Vector3? EndPosition { get; }
        public float? EndRadiusMeters { get; }
        public Vector3? Position { get; }
        public string? GeometryId { get; }
        public string? PathGeometryId { get; }
        public float? SpeedMetersPerSecond { get; }

        private static float Clamp01(float value)
        {
            if (value < 0f)
                return 0f;
            if (value > 1f)
                return 1f;
            return value;
        }

        private static float ClampPan(float value)
        {
            if (value < -1f)
                return -1f;
            if (value > 1f)
                return 1f;
            return value;
        }
    }
}
