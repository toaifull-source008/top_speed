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
            string shapeId,
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
            IReadOnlyDictionary<string, string>? metadata = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Area id is required.", nameof(id));
            if (string.IsNullOrWhiteSpace(shapeId))
                throw new ArgumentException("Shape id is required.", nameof(shapeId));

            Id = id.Trim();
            Type = type;
            ShapeId = shapeId.Trim();
            if (heightMeters <= 0f)
                throw new ArgumentOutOfRangeException(nameof(heightMeters), "Area height must be greater than zero.");
            if (ceilingHeightMeters.HasValue && ceilingHeightMeters.Value <= elevationMeters)
                throw new ArgumentOutOfRangeException(nameof(ceilingHeightMeters), "Ceiling height must be above the elevation.");
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
        }

        public string Id { get; }
        public TrackAreaType Type { get; }
        public string ShapeId { get; }
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

        private static IReadOnlyDictionary<string, string> NormalizeMetadata(IReadOnlyDictionary<string, string>? metadata)
        {
            if (metadata == null || metadata.Count == 0)
                return EmptyMetadata;
            var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in metadata)
                copy[pair.Key] = pair.Value;
            return copy;
        }
    }
}
