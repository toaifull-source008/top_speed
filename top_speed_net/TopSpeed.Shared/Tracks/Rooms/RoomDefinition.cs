using System;

namespace TopSpeed.Tracks.Rooms
{
    public sealed class TrackRoomDefinition
    {
        public TrackRoomDefinition(
            string id,
            string? name,
            float reverbTimeSeconds,
            float reverbGain,
            float hfDecayRatio,
            float earlyReflectionsGain,
            float lateReverbGain,
            float diffusion,
            float airAbsorption,
            float occlusionScale,
            float transmissionScale)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Room id is required.", nameof(id));

            Id = id.Trim();
            var trimmedName = name?.Trim();
            Name = string.IsNullOrWhiteSpace(trimmedName) ? null : trimmedName;
            ReverbTimeSeconds = Math.Max(0f, reverbTimeSeconds);
            ReverbGain = Clamp01(reverbGain);
            HfDecayRatio = Clamp01(hfDecayRatio);
            EarlyReflectionsGain = Clamp01(earlyReflectionsGain);
            LateReverbGain = Clamp01(lateReverbGain);
            Diffusion = Clamp01(diffusion);
            AirAbsorption = Clamp01(airAbsorption);
            OcclusionScale = Clamp01(occlusionScale);
            TransmissionScale = Clamp01(transmissionScale);
        }

        public string Id { get; }
        public string? Name { get; }
        public float ReverbTimeSeconds { get; }
        public float ReverbGain { get; }
        public float HfDecayRatio { get; }
        public float EarlyReflectionsGain { get; }
        public float LateReverbGain { get; }
        public float Diffusion { get; }
        public float AirAbsorption { get; }
        public float OcclusionScale { get; }
        public float TransmissionScale { get; }

        private static float Clamp01(float value)
        {
            if (value < 0f)
                return 0f;
            if (value > 1f)
                return 1f;
            return value;
        }
    }

    public sealed class TrackRoomOverrides
    {
        public float? ReverbTimeSeconds { get; set; }
        public float? ReverbGain { get; set; }
        public float? HfDecayRatio { get; set; }
        public float? EarlyReflectionsGain { get; set; }
        public float? LateReverbGain { get; set; }
        public float? Diffusion { get; set; }
        public float? AirAbsorption { get; set; }
        public float? OcclusionScale { get; set; }
        public float? TransmissionScale { get; set; }

        public bool HasAny =>
            ReverbTimeSeconds.HasValue ||
            ReverbGain.HasValue ||
            HfDecayRatio.HasValue ||
            EarlyReflectionsGain.HasValue ||
            LateReverbGain.HasValue ||
            Diffusion.HasValue ||
            AirAbsorption.HasValue ||
            OcclusionScale.HasValue ||
            TransmissionScale.HasValue;
    }
}
