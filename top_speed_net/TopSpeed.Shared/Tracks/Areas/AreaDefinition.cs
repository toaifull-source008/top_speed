using System;
using System.Collections.Generic;
using TopSpeed.Data;
using TopSpeed.Tracks.Rooms;

namespace TopSpeed.Tracks.Areas
{
    public sealed class TrackAreaDefinition
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public TrackAreaDefinition(
            string id,
            TrackAreaType type,
            string geometryId,
            float elevationMeters,
            float heightMeters,
            float? ceilingHeightMeters,
            string? roomId,
            TrackRoomOverrides? roomOverrides,
            string? name = null,
            string? materialId = null,
            TrackNoise? noise = null,
            float? widthMeters = null,
            TrackAreaFlags flags = TrackAreaFlags.None,
            IReadOnlyDictionary<string, string>? metadata = null,
            IReadOnlyList<string>? soundSourceIds = null,
            string? volumeId = null,
            string? surfaceId = null,
            float? volumeThicknessMeters = null,
            float? volumeOffsetMeters = null,
            float? volumeMinY = null,
            float? volumeMaxY = null,
            TrackAreaVolumeMode volumeMode = TrackAreaVolumeMode.LocalBand,
            TrackAreaVolumeOffsetMode volumeOffsetMode = TrackAreaVolumeOffsetMode.Bottom,
            TrackAreaVolumeSpace volumeOffsetSpace = TrackAreaVolumeSpace.Inherit,
            TrackAreaVolumeSpace volumeMinMaxSpace = TrackAreaVolumeSpace.Inherit)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Area id is required.", nameof(id));
            if (string.IsNullOrWhiteSpace(geometryId))
                throw new ArgumentException("Geometry id is required.", nameof(geometryId));

            Id = id.Trim();
            Type = type;
            GeometryId = geometryId.Trim();
            if (heightMeters <= 0f)
                throw new ArgumentOutOfRangeException(nameof(heightMeters), "Area height must be greater than zero.");
            if (ceilingHeightMeters.HasValue && ceilingHeightMeters.Value <= elevationMeters)
                throw new ArgumentOutOfRangeException(nameof(ceilingHeightMeters), "Ceiling height must be above the elevation.");
            if (volumeMinY.HasValue && volumeMaxY.HasValue && volumeMaxY.Value <= volumeMinY.Value)
                throw new ArgumentOutOfRangeException(nameof(volumeMaxY), "Area volume max_y must be greater than min_y.");
            ElevationMeters = elevationMeters;
            HeightMeters = heightMeters;
            CeilingHeightMeters = ceilingHeightMeters;
            var trimmedRoom = roomId?.Trim();
            RoomId = string.IsNullOrWhiteSpace(trimmedRoom) ? null : trimmedRoom;
            RoomOverrides = roomOverrides;
            var trimmedName = name?.Trim();
            Name = string.IsNullOrWhiteSpace(trimmedName) ? null : trimmedName;
            var trimmedMaterial = materialId?.Trim();
            MaterialId = string.IsNullOrWhiteSpace(trimmedMaterial) ? null : trimmedMaterial;
            Noise = noise;
            WidthMeters = widthMeters;
            Flags = flags;
            Metadata = NormalizeMetadata(metadata);
            SoundSourceIds = NormalizeIds(soundSourceIds);
            var trimmedVolume = volumeId?.Trim();
            VolumeId = string.IsNullOrWhiteSpace(trimmedVolume) ? null : trimmedVolume;
            var trimmedSurface = surfaceId?.Trim();
            SurfaceId = string.IsNullOrWhiteSpace(trimmedSurface) ? null : trimmedSurface;
            VolumeThicknessMeters = volumeThicknessMeters;
            VolumeOffsetMeters = volumeOffsetMeters;
            VolumeMinY = volumeMinY;
            VolumeMaxY = volumeMaxY;
            VolumeMode = volumeMode;
            VolumeOffsetMode = volumeOffsetMode;
            VolumeOffsetSpace = volumeOffsetSpace;
            VolumeMinMaxSpace = volumeMinMaxSpace;
        }

        public string Id { get; }
        public TrackAreaType Type { get; }
        public string GeometryId { get; }
        public float ElevationMeters { get; }
        public float HeightMeters { get; }
        public float? CeilingHeightMeters { get; }
        public string? RoomId { get; }
        public TrackRoomOverrides? RoomOverrides { get; }
        public string? Name { get; }
        public string? MaterialId { get; }
        public TrackNoise? Noise { get; }
        public float? WidthMeters { get; }
        public TrackAreaFlags Flags { get; }
        public IReadOnlyDictionary<string, string> Metadata { get; }
        public IReadOnlyList<string> SoundSourceIds { get; }
        public string? VolumeId { get; }
        public string? SurfaceId { get; }
        public float? VolumeThicknessMeters { get; }
        public float? VolumeOffsetMeters { get; }
        public float? VolumeMinY { get; }
        public float? VolumeMaxY { get; }
        public TrackAreaVolumeMode VolumeMode { get; }
        public TrackAreaVolumeOffsetMode VolumeOffsetMode { get; }
        public TrackAreaVolumeSpace VolumeOffsetSpace { get; }
        public TrackAreaVolumeSpace VolumeMinMaxSpace { get; }

        private static IReadOnlyDictionary<string, string> NormalizeMetadata(IReadOnlyDictionary<string, string>? metadata)
        {
            if (metadata == null || metadata.Count == 0)
                return EmptyMetadata;
            var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in metadata)
                copy[pair.Key] = pair.Value;
            return copy;
        }

        private static IReadOnlyList<string> NormalizeIds(IReadOnlyList<string>? values)
        {
            if (values == null || values.Count == 0)
                return Array.Empty<string>();
            var list = new List<string>(values.Count);
            foreach (var raw in values)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                list.Add(raw.Trim());
            }
            return list;
        }
    }
}
