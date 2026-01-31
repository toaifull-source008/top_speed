using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using SteamAudio;

namespace TS.Audio
{
    public sealed class SteamAudioContext : IDisposable
    {
        internal sealed class ListenerState
        {
            public readonly IPL.Vector3 Right;
            public readonly IPL.Vector3 Up;
            public readonly IPL.Vector3 Ahead;
            public readonly IPL.Vector3 Origin;

            public ListenerState(IPL.Vector3 right, IPL.Vector3 up, IPL.Vector3 ahead, IPL.Vector3 origin)
            {
                Right = right;
                Up = up;
                Ahead = ahead;
                Origin = origin;
            }
        }

        public IPL.Context Context;
        public IPL.Hrtf Hrtf;
        public readonly int SampleRate;
        public readonly int FrameSize;
        private volatile ListenerState _listenerState;
        internal ListenerState ListenerSnapshot => _listenerState;
        private IPL.Simulator _simulator;
        private IPL.Scene _scene;
        private readonly Dictionary<AudioSourceHandle, IPL.Source> _sources = new Dictionary<AudioSourceHandle, IPL.Source>();
        private readonly object _simLock = new object();

        public SteamAudioContext(int sampleRate, int frameSize, string? hrtfSofaPath)
        {
            SampleRate = sampleRate;
            FrameSize = frameSize;
            _listenerState = CreateIdentityState();

            var contextSettings = new IPL.ContextSettings
            {
                Version = IPL.Version,
                LogCallback = null,
                AllocateCallback = null,
                FreeCallback = null,
                SimdLevel = IPL.SimdLevel.Avx2,
                Flags = 0
            };

            var error = IPL.ContextCreate(in contextSettings, out Context);
            if (error != IPL.Error.Success)
            {
                throw new InvalidOperationException("Failed to create SteamAudio context: " + error);
            }

            var hrtfSettings = new IPL.HrtfSettings
            {
                Type = string.IsNullOrWhiteSpace(hrtfSofaPath) ? IPL.HrtfType.Default : IPL.HrtfType.Sofa,
                SofaFileName = string.IsNullOrWhiteSpace(hrtfSofaPath) ? null : hrtfSofaPath,
                SofaData = IntPtr.Zero,
                SofaDataSize = 0,
                Volume = 1.0f,
                NormType = IPL.HrtfNormType.None
            };

            var audioSettings = new IPL.AudioSettings
            {
                SamplingRate = sampleRate,
                FrameSize = frameSize
            };

            error = IPL.HrtfCreate(Context, in audioSettings, in hrtfSettings, out Hrtf);
            if (error != IPL.Error.Success)
            {
                IPL.ContextRelease(ref Context);
                Context = default;
                throw new InvalidOperationException("Failed to create SteamAudio HRTF: " + error);
            }
        }

        public void SetScene(IPL.Scene scene)
        {
            if (scene.Handle == IntPtr.Zero || Context.Handle == IntPtr.Zero)
                return;

            lock (_simLock)
            {
                if (_simulator.Handle == IntPtr.Zero)
                    CreateSimulator();

                _scene = scene;
                if (_simulator.Handle != IntPtr.Zero)
                {
                    IPL.SimulatorSetScene(_simulator, scene);
                    IPL.SimulatorCommit(_simulator);
                }
            }
        }

        public void UpdateSimulation(IReadOnlyList<AudioSourceHandle> sources)
        {
            if (sources == null || sources.Count == 0)
                return;
            if (_simulator.Handle == IntPtr.Zero || _scene.Handle == IntPtr.Zero)
                return;

            lock (_simLock)
            {
                var active = new HashSet<AudioSourceHandle>();
                foreach (var source in sources)
                {
                    if (source == null || !source.IsSpatialized || !source.UsesSteamAudio)
                        continue;
                    active.Add(source);
                    EnsureSource(source);
                    SetSourceInputs(source);
                }

                RemoveInactiveSources(active);

                var shared = BuildSharedInputs();
                var flags = IPL.SimulationFlags.Direct | IPL.SimulationFlags.Reflections;
                IPL.SimulatorSetSharedInputs(_simulator, flags, in shared);
                IPL.SimulatorCommit(_simulator);
                IPL.SimulatorRunDirect(_simulator);
                IPL.SimulatorRunReflections(_simulator);

                foreach (var source in active)
                {
                    if (!_sources.TryGetValue(source, out var simSource) || simSource.Handle == IntPtr.Zero)
                        continue;
                    IPL.SourceGetOutputs(simSource, flags, out var outputs);
                    ApplyDirectOutputs(source, in outputs.Direct);
                    ApplyReflectionOutputs(source, in outputs.Reflections);
                }
            }
        }

        public void UpdateListener(Vector3 position, Vector3 forward, Vector3 up)
        {
            if (Context.Handle == IntPtr.Zero)
                return;

            var normForward = Vector3.Normalize(forward);
            var normUp = Vector3.Normalize(up);
            var right = Vector3.Normalize(Vector3.Cross(normUp, normForward));

            _listenerState = new ListenerState(
                ToIpl(right),
                ToIpl(normUp),
                new IPL.Vector3 { X = -normForward.X, Y = -normForward.Y, Z = -normForward.Z },
                ToIpl(position));
        }

        public void Dispose()
        {
            lock (_simLock)
            {
                foreach (var entry in _sources.Values)
                {
                    var source = entry;
                    if (source.Handle != IntPtr.Zero)
                    {
                        IPL.SourceRemove(source, _simulator);
                        IPL.SourceRelease(ref source);
                    }
                }
                _sources.Clear();

                if (_simulator.Handle != IntPtr.Zero)
                {
                    IPL.SimulatorRelease(ref _simulator);
                    _simulator = default;
                }
            }

            if (Hrtf.Handle != IntPtr.Zero)
            {
                IPL.HrtfRelease(ref Hrtf);
                Hrtf = default;
            }

            if (Context.Handle != IntPtr.Zero)
            {
                IPL.ContextRelease(ref Context);
                Context = default;
            }
        }

        public static IPL.Vector3 ToIpl(Vector3 v)
        {
            return new IPL.Vector3 { X = v.X, Y = v.Y, Z = v.Z };
        }

        private void CreateSimulator()
        {
            var settings = new IPL.SimulationSettings
            {
                Flags = IPL.SimulationFlags.Direct | IPL.SimulationFlags.Reflections,
                SceneType = IPL.SceneType.Default,
                ReflectionType = IPL.ReflectionEffectType.Parametric,
                MaxNumOcclusionSamples = 32,
                MaxNumRays = 256,
                NumDiffuseSamples = 64,
                MaxDuration = 1.5f,
                MaxOrder = 1,
                MaxNumSources = 128,
                NumThreads = Math.Max(1, Environment.ProcessorCount - 1),
                RayBatchSize = 64,
                NumVisSamples = 8,
                SamplingRate = SampleRate,
                FrameSize = FrameSize
            };

            var error = IPL.SimulatorCreate(Context, in settings, out _simulator);
            if (error != IPL.Error.Success)
                _simulator = default;
        }

        private void EnsureSource(AudioSourceHandle source)
        {
            if (_sources.TryGetValue(source, out var existing) && existing.Handle != IntPtr.Zero)
                return;

            var settings = new IPL.SourceSettings
            {
                Flags = IPL.SimulationFlags.Direct | IPL.SimulationFlags.Reflections
            };

            var error = IPL.SourceCreate(_simulator, in settings, out var simSource);
            if (error != IPL.Error.Success)
                return;

            IPL.SourceAdd(simSource, _simulator);
            _sources[source] = simSource;
        }

        private void RemoveInactiveSources(HashSet<AudioSourceHandle> active)
        {
            if (_sources.Count == 0)
                return;

            var toRemove = new List<AudioSourceHandle>();
            foreach (var entry in _sources)
            {
                if (!active.Contains(entry.Key))
                    toRemove.Add(entry.Key);
            }

            foreach (var handle in toRemove)
            {
                if (_sources.TryGetValue(handle, out var simSource) && simSource.Handle != IntPtr.Zero)
                {
                    IPL.SourceRemove(simSource, _simulator);
                    IPL.SourceRelease(ref simSource);
                }
                _sources.Remove(handle);
            }
        }

        private void SetSourceInputs(AudioSourceHandle handle)
        {
            if (!_sources.TryGetValue(handle, out var source) || source.Handle == IntPtr.Zero)
                return;

            var spatial = handle.SpatialParams;
            var position = new IPL.Vector3
            {
                X = Volatile.Read(ref spatial.PosX),
                Y = Volatile.Read(ref spatial.PosY),
                Z = Volatile.Read(ref spatial.PosZ)
            };

            var coord = new IPL.CoordinateSpace3
            {
                Origin = position,
                Right = new IPL.Vector3 { X = 1f, Y = 0f, Z = 0f },
                Up = new IPL.Vector3 { X = 0f, Y = 1f, Z = 0f },
                Ahead = new IPL.Vector3 { X = 0f, Y = 0f, Z = 1f }
            };

            var inputs = new IPL.SimulationInputs
            {
                Flags = IPL.SimulationFlags.Direct | IPL.SimulationFlags.Reflections,
                DirectFlags = IPL.DirectSimulationFlags.Occlusion | IPL.DirectSimulationFlags.Transmission | IPL.DirectSimulationFlags.AirAbsorption,
                Source = coord,
                DistanceAttenuationModel = new IPL.DistanceAttenuationModel { Type = IPL.DistanceAttenuationModelType.Default, MinDistance = 1.0f },
                AirAbsorptionModel = new IPL.AirAbsorptionModel { Type = IPL.AirAbsorptionModelType.Default },
                Directivity = new IPL.Directivity { DipoleWeight = 0f, DipolePower = 1f },
                OcclusionType = IPL.OcclusionType.Raycast,
                OcclusionRadius = 0.5f,
                NumOcclusionSamples = 8,
                HybridReverbTransitionTime = 1.0f,
                HybridReverbOverlapPercent = 0.25f,
                NumTransmissionRays = 4,
                Baked = false
            };

            unsafe
            {
                inputs.ReverbScale[0] = 1.0f;
                inputs.ReverbScale[1] = 1.0f;
                inputs.ReverbScale[2] = 1.0f;
            }

            IPL.SourceSetInputs(source, inputs.Flags, in inputs);
        }

        private IPL.SimulationSharedInputs BuildSharedInputs()
        {
            var listener = _listenerState;
            var shared = new IPL.SimulationSharedInputs
            {
                Listener = new IPL.CoordinateSpace3
                {
                    Origin = listener.Origin,
                    Right = listener.Right,
                    Up = listener.Up,
                    Ahead = listener.Ahead
                },
                NumRays = 256,
                NumBounces = 2,
                Duration = 1.5f,
                Order = 1,
                IrradianceMinDistance = 1.0f
            };

            return shared;
        }

        private static unsafe void ApplyDirectOutputs(AudioSourceHandle handle, in IPL.DirectEffectParams direct)
        {
            var spatial = handle.SpatialParams;
            var roomFlags = Volatile.Read(ref spatial.RoomFlags);
            var hasRoom = (roomFlags & AudioSourceSpatialParams.RoomHasProfile) != 0;

            float airLow;
            float airMid;
            float airHigh;
            float transLow;
            float transMid;
            float transHigh;

            airLow = direct.AirAbsorption[0];
            airMid = direct.AirAbsorption[1];
            airHigh = direct.AirAbsorption[2];
            transLow = direct.Transmission[0];
            transMid = direct.Transmission[1];
            transHigh = direct.Transmission[2];

            var occlusion = direct.Occlusion;
            var occlusionOverride = Volatile.Read(ref spatial.RoomOcclusionOverride);
            if (!float.IsNaN(occlusionOverride))
            {
                occlusion = Clamp01(occlusionOverride);
            }
            else if (hasRoom)
            {
                var scale = Clamp01(Volatile.Read(ref spatial.RoomOcclusionScale));
                occlusion = Lerp(1f, occlusion, scale);
            }

            var transOverrideLow = Volatile.Read(ref spatial.RoomTransmissionOverrideLow);
            var transOverrideMid = Volatile.Read(ref spatial.RoomTransmissionOverrideMid);
            var transOverrideHigh = Volatile.Read(ref spatial.RoomTransmissionOverrideHigh);
            if (!float.IsNaN(transOverrideLow) || !float.IsNaN(transOverrideMid) || !float.IsNaN(transOverrideHigh))
            {
                if (!float.IsNaN(transOverrideLow)) transLow = Clamp01(transOverrideLow);
                if (!float.IsNaN(transOverrideMid)) transMid = Clamp01(transOverrideMid);
                if (!float.IsNaN(transOverrideHigh)) transHigh = Clamp01(transOverrideHigh);
            }
            else if (hasRoom)
            {
                var scale = Clamp01(Volatile.Read(ref spatial.RoomTransmissionScale));
                transLow = Lerp(1f, transLow, scale);
                transMid = Lerp(1f, transMid, scale);
                transHigh = Lerp(1f, transHigh, scale);
            }

            var airOverrideLow = Volatile.Read(ref spatial.RoomAirAbsorptionOverrideLow);
            var airOverrideMid = Volatile.Read(ref spatial.RoomAirAbsorptionOverrideMid);
            var airOverrideHigh = Volatile.Read(ref spatial.RoomAirAbsorptionOverrideHigh);
            if (!float.IsNaN(airOverrideLow) || !float.IsNaN(airOverrideMid) || !float.IsNaN(airOverrideHigh))
            {
                if (!float.IsNaN(airOverrideLow)) airLow = Clamp01(airOverrideLow);
                if (!float.IsNaN(airOverrideMid)) airMid = Clamp01(airOverrideMid);
                if (!float.IsNaN(airOverrideHigh)) airHigh = Clamp01(airOverrideHigh);
            }
            else if (hasRoom)
            {
                var scale = Clamp01(Volatile.Read(ref spatial.RoomAirAbsorptionScale));
                airLow = Lerp(1f, airLow, scale);
                airMid = Lerp(1f, airMid, scale);
                airHigh = Lerp(1f, airHigh, scale);
            }

            handle.ApplyDirectSimulation(occlusion, airLow, airMid, airHigh, transLow, transMid, transHigh);
        }

        private static unsafe void ApplyReflectionOutputs(AudioSourceHandle handle, in IPL.ReflectionEffectParams reflections)
        {
            var spatial = handle.SpatialParams;
            var roomFlags = Volatile.Read(ref spatial.RoomFlags);
            var hasRoom = (roomFlags & AudioSourceSpatialParams.RoomHasProfile) != 0;

            if (!hasRoom)
            {
                if (reflections.Type != IPL.ReflectionEffectType.Parametric &&
                    reflections.Type != IPL.ReflectionEffectType.Hybrid)
                    return;

                var timeLow = reflections.ReverbTimes[0];
                var timeMid = reflections.ReverbTimes[1];
                var timeHigh = reflections.ReverbTimes[2];
                var eqLow = reflections.Eq[0];
                var eqMid = reflections.Eq[1];
                var eqHigh = reflections.Eq[2];
                var delay = reflections.Delay;
                handle.ApplyReflectionSimulation(timeLow, timeMid, timeHigh, eqLow, eqMid, eqHigh, delay);
                return;
            }

            var timeMidRoom = Math.Max(0f, Volatile.Read(ref spatial.RoomReverbTimeSeconds));
            var hfRatio = Clamp01(Volatile.Read(ref spatial.RoomHfDecayRatio));
            var roomTimeLow = timeMidRoom;
            var roomTimeMid = timeMidRoom;
            var roomTimeHigh = timeMidRoom * hfRatio;

            var roomEqHigh = Clamp01(Volatile.Read(ref spatial.RoomEarlyReflectionsGain));
            var roomEqLow = Clamp01(Volatile.Read(ref spatial.RoomLateReverbGain));
            var roomEqMid = Clamp01((roomEqLow + roomEqHigh) * 0.5f);
            var diffusion = Clamp01(Volatile.Read(ref spatial.RoomDiffusion));
            roomEqLow = Lerp(roomEqLow, roomEqMid, diffusion);
            roomEqHigh = Lerp(roomEqHigh, roomEqMid, diffusion);
            var roomDelay = 0;
            handle.ApplyReflectionSimulation(roomTimeLow, roomTimeMid, roomTimeHigh, roomEqLow, roomEqMid, roomEqHigh, roomDelay);
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        private static float Lerp(float from, float to, float t)
        {
            return from + (to - from) * t;
        }

        private static ListenerState CreateIdentityState()
        {
            return new ListenerState(
                new IPL.Vector3 { X = 1, Y = 0, Z = 0 },
                new IPL.Vector3 { X = 0, Y = 1, Z = 0 },
                new IPL.Vector3 { X = 0, Y = 0, Z = -1 },
                new IPL.Vector3 { X = 0, Y = 0, Z = 0 });
        }
    }
}
