using System;
using System.Collections.Generic;
using System.Numerics;

namespace TopSpeed.Tracks.Volumes
{
    public sealed class TrackVolumeDefinition
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public TrackVolumeDefinition(
            string id,
            TrackVolumeType type,
            Vector3 center,
            bool hasCenter,
            Vector3 size,
            float radius,
            float height,
            string? geometryId,
            float? minY,
            float? maxY,
            Vector3 rotationDegrees,
            IReadOnlyDictionary<string, string>? metadata = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Volume id is required.", nameof(id));

            Id = id.Trim();
            Type = type;
            Center = center;
            HasCenter = hasCenter;
            Size = size;
            Radius = radius;
            Height = height;
            GeometryId = string.IsNullOrWhiteSpace(geometryId) ? null : geometryId!.Trim();
            MinY = minY;
            MaxY = maxY;
            RotationDegrees = rotationDegrees;
            Metadata = NormalizeMetadata(metadata);
        }

        public string Id { get; }
        public TrackVolumeType Type { get; }
        public Vector3 Center { get; }
        public bool HasCenter { get; }
        public Vector3 Size { get; }
        public float Radius { get; }
        public float Height { get; }
        public string? GeometryId { get; }
        public float? MinY { get; }
        public float? MaxY { get; }
        public Vector3 RotationDegrees { get; }
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
