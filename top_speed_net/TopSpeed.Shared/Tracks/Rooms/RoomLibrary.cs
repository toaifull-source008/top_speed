using System;
using System.Collections.Generic;

namespace TopSpeed.Tracks.Rooms
{
    public static class TrackRoomLibrary
    {
        private struct RoomValues
        {
            public float ReverbTimeSeconds;
            public float ReverbGain;
            public float HfDecayRatio;
            public float EarlyReflectionsGain;
            public float LateReverbGain;
            public float Diffusion;
            public float AirAbsorption;
            public float OcclusionScale;
            public float TransmissionScale;
        }

        private static readonly Dictionary<string, RoomValues> Presets =
            new Dictionary<string, RoomValues>(StringComparer.OrdinalIgnoreCase)
            {
                ["outdoor_open"] = new RoomValues
                {
                    ReverbTimeSeconds = 0.4f,
                    ReverbGain = 0.10f,
                    HfDecayRatio = 0.80f,
                    EarlyReflectionsGain = 0.10f,
                    LateReverbGain = 0.10f,
                    Diffusion = 0.20f,
                    AirAbsorption = 0.60f,
                    OcclusionScale = 0.40f,
                    TransmissionScale = 0.70f
                },
                ["outdoor_urban"] = new RoomValues
                {
                    ReverbTimeSeconds = 0.8f,
                    ReverbGain = 0.20f,
                    HfDecayRatio = 0.70f,
                    EarlyReflectionsGain = 0.30f,
                    LateReverbGain = 0.20f,
                    Diffusion = 0.40f,
                    AirAbsorption = 0.50f,
                    OcclusionScale = 0.50f,
                    TransmissionScale = 0.50f
                },
                ["outdoor_forest"] = new RoomValues
                {
                    ReverbTimeSeconds = 0.6f,
                    ReverbGain = 0.15f,
                    HfDecayRatio = 0.50f,
                    EarlyReflectionsGain = 0.20f,
                    LateReverbGain = 0.15f,
                    Diffusion = 0.30f,
                    AirAbsorption = 0.80f,
                    OcclusionScale = 0.60f,
                    TransmissionScale = 0.60f
                },
                ["tunnel_short"] = new RoomValues
                {
                    ReverbTimeSeconds = 1.2f,
                    ReverbGain = 0.50f,
                    HfDecayRatio = 0.60f,
                    EarlyReflectionsGain = 0.60f,
                    LateReverbGain = 0.50f,
                    Diffusion = 0.70f,
                    AirAbsorption = 0.20f,
                    OcclusionScale = 0.80f,
                    TransmissionScale = 0.30f
                },
                ["tunnel_long"] = new RoomValues
                {
                    ReverbTimeSeconds = 2.4f,
                    ReverbGain = 0.70f,
                    HfDecayRatio = 0.50f,
                    EarlyReflectionsGain = 0.70f,
                    LateReverbGain = 0.70f,
                    Diffusion = 0.80f,
                    AirAbsorption = 0.20f,
                    OcclusionScale = 0.90f,
                    TransmissionScale = 0.20f
                },
                ["garage_small"] = new RoomValues
                {
                    ReverbTimeSeconds = 1.0f,
                    ReverbGain = 0.40f,
                    HfDecayRatio = 0.60f,
                    EarlyReflectionsGain = 0.50f,
                    LateReverbGain = 0.40f,
                    Diffusion = 0.60f,
                    AirAbsorption = 0.30f,
                    OcclusionScale = 0.70f,
                    TransmissionScale = 0.30f
                },
                ["garage_large"] = new RoomValues
                {
                    ReverbTimeSeconds = 1.8f,
                    ReverbGain = 0.55f,
                    HfDecayRatio = 0.60f,
                    EarlyReflectionsGain = 0.60f,
                    LateReverbGain = 0.60f,
                    Diffusion = 0.70f,
                    AirAbsorption = 0.30f,
                    OcclusionScale = 0.70f,
                    TransmissionScale = 0.30f
                },
                ["underpass"] = new RoomValues
                {
                    ReverbTimeSeconds = 1.4f,
                    ReverbGain = 0.45f,
                    HfDecayRatio = 0.50f,
                    EarlyReflectionsGain = 0.60f,
                    LateReverbGain = 0.50f,
                    Diffusion = 0.60f,
                    AirAbsorption = 0.25f,
                    OcclusionScale = 0.80f,
                    TransmissionScale = 0.30f
                },
                ["canyon"] = new RoomValues
                {
                    ReverbTimeSeconds = 2.8f,
                    ReverbGain = 0.60f,
                    HfDecayRatio = 0.40f,
                    EarlyReflectionsGain = 0.50f,
                    LateReverbGain = 0.60f,
                    Diffusion = 0.50f,
                    AirAbsorption = 0.35f,
                    OcclusionScale = 0.60f,
                    TransmissionScale = 0.40f
                },
                ["stadium_open"] = new RoomValues
                {
                    ReverbTimeSeconds = 1.5f,
                    ReverbGain = 0.45f,
                    HfDecayRatio = 0.60f,
                    EarlyReflectionsGain = 0.70f,
                    LateReverbGain = 0.50f,
                    Diffusion = 0.70f,
                    AirAbsorption = 0.40f,
                    OcclusionScale = 0.40f,
                    TransmissionScale = 0.60f
                },
                ["hall_medium"] = new RoomValues
                {
                    ReverbTimeSeconds = 1.6f,
                    ReverbGain = 0.50f,
                    HfDecayRatio = 0.60f,
                    EarlyReflectionsGain = 0.50f,
                    LateReverbGain = 0.50f,
                    Diffusion = 0.80f,
                    AirAbsorption = 0.30f,
                    OcclusionScale = 0.70f,
                    TransmissionScale = 0.30f
                },
                ["hall_large"] = new RoomValues
                {
                    ReverbTimeSeconds = 2.6f,
                    ReverbGain = 0.60f,
                    HfDecayRatio = 0.50f,
                    EarlyReflectionsGain = 0.60f,
                    LateReverbGain = 0.60f,
                    Diffusion = 0.80f,
                    AirAbsorption = 0.25f,
                    OcclusionScale = 0.80f,
                    TransmissionScale = 0.20f
                },
                ["room_small"] = new RoomValues
                {
                    ReverbTimeSeconds = 0.7f,
                    ReverbGain = 0.30f,
                    HfDecayRatio = 0.70f,
                    EarlyReflectionsGain = 0.40f,
                    LateReverbGain = 0.30f,
                    Diffusion = 0.60f,
                    AirAbsorption = 0.35f,
                    OcclusionScale = 0.60f,
                    TransmissionScale = 0.40f
                },
                ["room_medium"] = new RoomValues
                {
                    ReverbTimeSeconds = 1.1f,
                    ReverbGain = 0.40f,
                    HfDecayRatio = 0.60f,
                    EarlyReflectionsGain = 0.50f,
                    LateReverbGain = 0.40f,
                    Diffusion = 0.70f,
                    AirAbsorption = 0.30f,
                    OcclusionScale = 0.60f,
                    TransmissionScale = 0.30f
                },
                ["room_large"] = new RoomValues
                {
                    ReverbTimeSeconds = 1.8f,
                    ReverbGain = 0.50f,
                    HfDecayRatio = 0.50f,
                    EarlyReflectionsGain = 0.60f,
                    LateReverbGain = 0.50f,
                    Diffusion = 0.70f,
                    AirAbsorption = 0.25f,
                    OcclusionScale = 0.70f,
                    TransmissionScale = 0.30f
                }
            };

        public static bool IsPreset(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;
            return Presets.ContainsKey(name.Trim());
        }

        public static bool TryGetPreset(string name, out TrackRoomDefinition room)
        {
            room = null!;
            if (string.IsNullOrWhiteSpace(name))
                return false;
            if (!Presets.TryGetValue(name.Trim(), out var values))
                return false;

            var id = name.Trim();
            room = new TrackRoomDefinition(
                id,
                id,
                values.ReverbTimeSeconds,
                values.ReverbGain,
                values.HfDecayRatio,
                values.EarlyReflectionsGain,
                values.LateReverbGain,
                values.Diffusion,
                values.AirAbsorption,
                values.OcclusionScale,
                values.TransmissionScale);
            return true;
        }
    }
}
