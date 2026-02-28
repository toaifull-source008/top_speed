using System;
using System.Collections.Generic;
using SharpDX.DirectInput;

namespace TopSpeed.Input
{
    internal sealed class RaceInput
    {
        private enum InputScope
        {
            Driving,
            Auxiliary
        }

        private enum TriggerMode
        {
            Hold,
            Press
        }

        private readonly struct InputActionMeta
        {
            public InputActionMeta(InputScope scope, TriggerMode keyboardMode, TriggerMode joystickMode, bool allowNumpadEnterAlias = false)
            {
                Scope = scope;
                KeyboardMode = keyboardMode;
                JoystickMode = joystickMode;
                AllowNumpadEnterAlias = allowNumpadEnterAlias;
            }

            public InputScope Scope { get; }
            public TriggerMode KeyboardMode { get; }
            public TriggerMode JoystickMode { get; }
            public bool AllowNumpadEnterAlias { get; }
        }

        private readonly struct InputActionBinding
        {
            public InputActionBinding(
                string label,
                InputActionMeta meta,
                Func<Key> getKey,
                Action<Key> setKey,
                Func<JoystickAxisOrButton> getAxis,
                Action<JoystickAxisOrButton> setAxis)
            {
                Label = label;
                Meta = meta;
                GetKey = getKey;
                SetKey = setKey;
                GetAxis = getAxis;
                SetAxis = setAxis;
            }

            public string Label { get; }
            public InputActionMeta Meta { get; }
            public Func<Key> GetKey { get; }
            public Action<Key> SetKey { get; }
            public Func<JoystickAxisOrButton> GetAxis { get; }
            public Action<JoystickAxisOrButton> SetAxis { get; }
        }

        private readonly RaceSettings _settings;
        private readonly InputState _lastState;
        private readonly InputState _prevState;
        private readonly List<InputActionDefinition> _actionDefinitions;
        private readonly Dictionary<InputAction, InputActionBinding> _actionBindings;
        private JoystickAxisOrButton _left;
        private JoystickAxisOrButton _right;
        private JoystickAxisOrButton _throttle;
        private JoystickAxisOrButton _brake;
        private JoystickAxisOrButton _gearUp;
        private JoystickAxisOrButton _gearDown;
        private JoystickAxisOrButton _horn;
        private JoystickAxisOrButton _requestInfo;
        private JoystickAxisOrButton _currentGear;
        private JoystickAxisOrButton _currentLapNr;
        private JoystickAxisOrButton _currentRacePerc;
        private JoystickAxisOrButton _currentLapPerc;
        private JoystickAxisOrButton _currentRaceTime;
        private JoystickAxisOrButton _startEngine;
        private JoystickAxisOrButton _reportDistance;
        private JoystickAxisOrButton _reportSpeed;
        private JoystickAxisOrButton _trackName;
        private JoystickAxisOrButton _pause;
        private InputDeviceMode _deviceMode;
        private Key _kbLeft;
        private Key _kbRight;
        private Key _kbThrottle;
        private Key _kbBrake;
        private Key _kbGearUp;
        private Key _kbGearDown;
        private Key _kbHorn;
        private Key _kbRequestInfo;
        private Key _kbCurrentGear;
        private Key _kbCurrentLapNr;
        private Key _kbCurrentRacePerc;
        private Key _kbCurrentLapPerc;
        private Key _kbCurrentRaceTime;
        private Key _kbStartEngine;
        private Key _kbReportDistance;
        private Key _kbReportSpeed;
        private Key _kbPlayer1;
        private Key _kbPlayer2;
        private Key _kbPlayer3;
        private Key _kbPlayer4;
        private Key _kbPlayer5;
        private Key _kbPlayer6;
        private Key _kbPlayer7;
        private Key _kbPlayer8;
        private Key _kbTrackName;
        private Key _kbPlayerNumber;
        private Key _kbPause;
        private Key _kbPlayerPos1;
        private Key _kbPlayerPos2;
        private Key _kbPlayerPos3;
        private Key _kbPlayerPos4;
        private Key _kbPlayerPos5;
        private Key _kbPlayerPos6;
        private Key _kbPlayerPos7;
        private Key _kbPlayerPos8;
        private Key _kbFlush;
        private JoystickStateSnapshot _center;
        private JoystickStateSnapshot _lastJoystick;
        private JoystickStateSnapshot _prevJoystick;
        private bool _hasCenter;
        private bool _hasPrevJoystick;
        private bool _joystickAvailable;
        private bool _allowDrivingInput;
        private bool _allowAuxiliaryInput;
        private bool _overlayInputBlocked;
        private bool UseJoystick => _deviceMode != InputDeviceMode.Keyboard && _joystickAvailable;
        private bool UseKeyboard => _deviceMode != InputDeviceMode.Joystick || !_joystickAvailable;

        public KeyMapManager KeyMap { get; }

        public RaceInput(RaceSettings settings)
        {
            _settings = settings;
            _lastState = new InputState();
            _prevState = new InputState();
            _actionDefinitions = new List<InputActionDefinition>();
            _actionBindings = CreateActionBindings();
            Initialize();
            KeyMap = new KeyMapManager(this);
        }

        public void Initialize()
        {
            _left = JoystickAxisOrButton.AxisNone;
            _right = JoystickAxisOrButton.AxisNone;
            _throttle = JoystickAxisOrButton.AxisNone;
            _brake = JoystickAxisOrButton.AxisNone;
            _gearUp = JoystickAxisOrButton.AxisNone;
            _gearDown = JoystickAxisOrButton.AxisNone;
            _horn = JoystickAxisOrButton.AxisNone;
            _requestInfo = JoystickAxisOrButton.AxisNone;
            _currentGear = JoystickAxisOrButton.AxisNone;
            _currentLapNr = JoystickAxisOrButton.AxisNone;
            _currentRacePerc = JoystickAxisOrButton.AxisNone;
            _currentLapPerc = JoystickAxisOrButton.AxisNone;
            _currentRaceTime = JoystickAxisOrButton.AxisNone;
            _startEngine = JoystickAxisOrButton.AxisNone;
            _reportDistance = JoystickAxisOrButton.AxisNone;
            _reportSpeed = JoystickAxisOrButton.AxisNone;
            _trackName = JoystickAxisOrButton.AxisNone;
            _pause = JoystickAxisOrButton.AxisNone;
            ReadFromSettings();
            _allowDrivingInput = true;
            _allowAuxiliaryInput = true;
            _overlayInputBlocked = false;

            _kbPlayer1 = Key.F1;
            _kbPlayer2 = Key.F2;
            _kbPlayer3 = Key.F3;
            _kbPlayer4 = Key.F4;
            _kbPlayer5 = Key.F5;
            _kbPlayer6 = Key.F6;
            _kbPlayer7 = Key.F7;
            _kbPlayer8 = Key.F8;
            _kbPlayerNumber = Key.F11;
            _kbPlayerPos1 = Key.D1;
            _kbPlayerPos2 = Key.D2;
            _kbPlayerPos3 = Key.D3;
            _kbPlayerPos4 = Key.D4;
            _kbPlayerPos5 = Key.D5;
            _kbPlayerPos6 = Key.D6;
            _kbPlayerPos7 = Key.D7;
            _kbPlayerPos8 = Key.D8;
            _kbFlush = Key.LeftAlt;
        }

        public void Run(InputState input)
        {
            Run(input, null);
        }

        public void Run(InputState input, JoystickStateSnapshot? joystick)      
        {
            _prevState.CopyFrom(_lastState);
            _lastState.CopyFrom(input);
            if (joystick.HasValue)
            {
                if (_hasPrevJoystick)
                    _prevJoystick = _lastJoystick;
                _lastJoystick = joystick.Value;
                if (!_hasCenter)
                {
                    _center = joystick.Value;
                    _hasCenter = true;
                }
                if (!_hasPrevJoystick)
                    _prevJoystick = joystick.Value;
                _hasPrevJoystick = true;
            }
            _joystickAvailable = joystick.HasValue;
            if (!joystick.HasValue)
                _hasPrevJoystick = false;
        }

        public void SetLeft(JoystickAxisOrButton a)
        {
            _left = a;
            _settings.JoystickLeft = a;
        }

        public void SetLeft(Key key)
        {
            _kbLeft = key;
            _settings.KeyLeft = key;
        }

        public void SetRight(JoystickAxisOrButton a)
        {
            _right = a;
            _settings.JoystickRight = a;
        }

        public void SetRight(Key key)
        {
            _kbRight = key;
            _settings.KeyRight = key;
        }

        public void SetThrottle(JoystickAxisOrButton a)
        {
            _throttle = a;
            _settings.JoystickThrottle = a;
        }

        public void SetThrottle(Key key)
        {
            _kbThrottle = key;
            _settings.KeyThrottle = key;
        }

        public void SetBrake(JoystickAxisOrButton a)
        {
            _brake = a;
            _settings.JoystickBrake = a;
        }

        public void SetBrake(Key key)
        {
            _kbBrake = key;
            _settings.KeyBrake = key;
        }

        public void SetGearUp(JoystickAxisOrButton a)
        {
            _gearUp = a;
            _settings.JoystickGearUp = a;
        }

        public void SetGearUp(Key key)
        {
            _kbGearUp = key;
            _settings.KeyGearUp = key;
        }

        public void SetGearDown(JoystickAxisOrButton a)
        {
            _gearDown = a;
            _settings.JoystickGearDown = a;
        }

        public void SetGearDown(Key key)
        {
            _kbGearDown = key;
            _settings.KeyGearDown = key;
        }

        public void SetHorn(JoystickAxisOrButton a)
        {
            _horn = a;
            _settings.JoystickHorn = a;
        }

        public void SetHorn(Key key)
        {
            _kbHorn = key;
            _settings.KeyHorn = key;
        }

        public void SetRequestInfo(JoystickAxisOrButton a)
        {
            _requestInfo = a;
            _settings.JoystickRequestInfo = a;
        }

        public void SetRequestInfo(Key key)
        {
            _kbRequestInfo = key;
            _settings.KeyRequestInfo = key;
        }

        public void SetCurrentGear(JoystickAxisOrButton a)
        {
            _currentGear = a;
            _settings.JoystickCurrentGear = a;
        }

        public void SetCurrentGear(Key key)
        {
            _kbCurrentGear = key;
            _settings.KeyCurrentGear = key;
        }

        public void SetCurrentLapNr(JoystickAxisOrButton a)
        {
            _currentLapNr = a;
            _settings.JoystickCurrentLapNr = a;
        }

        public void SetCurrentLapNr(Key key)
        {
            _kbCurrentLapNr = key;
            _settings.KeyCurrentLapNr = key;
        }

        public void SetCurrentRacePerc(JoystickAxisOrButton a)
        {
            _currentRacePerc = a;
            _settings.JoystickCurrentRacePerc = a;
        }

        public void SetCurrentRacePerc(Key key)
        {
            _kbCurrentRacePerc = key;
            _settings.KeyCurrentRacePerc = key;
        }

        public void SetCurrentLapPerc(JoystickAxisOrButton a)
        {
            _currentLapPerc = a;
            _settings.JoystickCurrentLapPerc = a;
        }

        public void SetCurrentLapPerc(Key key)
        {
            _kbCurrentLapPerc = key;
            _settings.KeyCurrentLapPerc = key;
        }

        public void SetCurrentRaceTime(JoystickAxisOrButton a)
        {
            _currentRaceTime = a;
            _settings.JoystickCurrentRaceTime = a;
        }

        public void SetCurrentRaceTime(Key key)
        {
            _kbCurrentRaceTime = key;
            _settings.KeyCurrentRaceTime = key;
        }

        public void SetStartEngine(JoystickAxisOrButton a)
        {
            _startEngine = a;
            _settings.JoystickStartEngine = a;
        }

        public void SetStartEngine(Key key)
        {
            _kbStartEngine = key;
            _settings.KeyStartEngine = key;
        }

        public void SetReportDistance(JoystickAxisOrButton a)
        {
            _reportDistance = a;
            _settings.JoystickReportDistance = a;
        }

        public void SetReportDistance(Key key)
        {
            _kbReportDistance = key;
            _settings.KeyReportDistance = key;
        }

        public void SetReportSpeed(JoystickAxisOrButton a)
        {
            _reportSpeed = a;
            _settings.JoystickReportSpeed = a;
        }

        public void SetReportSpeed(Key key)
        {
            _kbReportSpeed = key;
            _settings.KeyReportSpeed = key;
        }

        public void SetTrackName(JoystickAxisOrButton a)
        {
            _trackName = a;
            _settings.JoystickTrackName = a;
        }

        public void SetTrackName(Key key)
        {
            _kbTrackName = key;
            _settings.KeyTrackName = key;
        }

        public void SetPause(JoystickAxisOrButton a)
        {
            _pause = a;
            _settings.JoystickPause = a;
        }

        public void SetPause(Key key)
        {
            _kbPause = key;
            _settings.KeyPause = key;
        }

        public void SetCenter(JoystickStateSnapshot center)
        {
            _center = center;
            _hasCenter = true;
            _settings.JoystickCenter = center;
        }

        public void SetDevice(bool useJoystick)
        {
            SetDevice(useJoystick ? InputDeviceMode.Joystick : InputDeviceMode.Keyboard);
        }

        public void SetDevice(InputDeviceMode mode)
        {
            _deviceMode = mode;
            _settings.DeviceMode = mode;
        }

        public int GetSteering()
        {
            if (!_allowDrivingInput || _overlayInputBlocked)
                return 0;

            var joystickSteer = 0;
            if (UseJoystick)
            {
                var left = GetAxis(_left);
                var right = GetAxis(_right);
                joystickSteer = left != 0 ? -left : right;
                if (joystickSteer != 0 || !UseKeyboard)
                    return joystickSteer;
            }

            if (UseKeyboard)
            {
                if (_lastState.IsDown(_kbLeft))
                    return -100;
                if (_lastState.IsDown(_kbRight))
                    return 100;
            }

            return joystickSteer;
        }

        public int GetThrottle()
        {
            if (!_allowDrivingInput || _overlayInputBlocked)
                return 0;

            var joystickThrottle = UseJoystick ? GetAxis(_throttle) : 0;
            if (joystickThrottle != 0 || !UseKeyboard)
                return joystickThrottle;

            return UseKeyboard && _lastState.IsDown(_kbThrottle) ? 100 : 0;
        }

        public int GetBrake()
        {
            if (!_allowDrivingInput || _overlayInputBlocked)
                return 0;

            var joystickBrake = UseJoystick ? -GetAxis(_brake) : 0;
            if (joystickBrake != 0 || !UseKeyboard)
                return joystickBrake;

            return UseKeyboard && _lastState.IsDown(_kbBrake) ? -100 : 0;
        }

        public bool GetReverseRequested() => _allowDrivingInput && UseKeyboard && WasPressed(Key.Z);

        public bool GetForwardRequested() => _allowDrivingInput && UseKeyboard && WasPressed(Key.A);

        public bool GetGearUp() => IsActionTriggered(InputAction.GearUp);

        public bool GetGearDown() => IsActionTriggered(InputAction.GearDown);

        public bool GetHorn() => IsActionTriggered(InputAction.Horn);

        public bool GetRequestInfo() => IsActionTriggered(InputAction.RequestInfo);

        public bool GetCurrentGear() => IsActionTriggered(InputAction.CurrentGear);

        public bool GetCurrentLapNr() => IsActionTriggered(InputAction.CurrentLapNr);

        public bool GetCurrentRacePerc() => IsActionTriggered(InputAction.CurrentRacePerc);

        public bool GetCurrentLapPerc() => IsActionTriggered(InputAction.CurrentLapPerc);

        public bool GetCurrentRaceTime() => IsActionTriggered(InputAction.CurrentRaceTime);

        public bool GetMappedAction(InputAction action) => IsActionTriggered(action);

        public bool TryGetPlayerInfo(out int player)
        {
            if (!_allowAuxiliaryInput)
            {
                player = 0;
                return false;
            }

            if (WasPressed(_kbPlayer1)) { player = 0; return true; }
            if (WasPressed(_kbPlayer2)) { player = 1; return true; }
            if (WasPressed(_kbPlayer3)) { player = 2; return true; }
            if (WasPressed(_kbPlayer4)) { player = 3; return true; }
            if (WasPressed(_kbPlayer5)) { player = 4; return true; }
            if (WasPressed(_kbPlayer6)) { player = 5; return true; }
            if (WasPressed(_kbPlayer7)) { player = 6; return true; }
            if (WasPressed(_kbPlayer8)) { player = 7; return true; }
            player = 0;
            return false;
        }

        public bool TryGetPlayerPosition(out int player)
        {
            if (!_allowAuxiliaryInput)
            {
                player = 0;
                return false;
            }

            if (WasPressed(_kbPlayerPos1)) { player = 0; return true; }
            if (WasPressed(_kbPlayerPos2)) { player = 1; return true; }
            if (WasPressed(_kbPlayerPos3)) { player = 2; return true; }
            if (WasPressed(_kbPlayerPos4)) { player = 3; return true; }
            if (WasPressed(_kbPlayerPos5)) { player = 4; return true; }
            if (WasPressed(_kbPlayerPos6)) { player = 5; return true; }
            if (WasPressed(_kbPlayerPos7)) { player = 6; return true; }
            if (WasPressed(_kbPlayerPos8)) { player = 7; return true; }
            player = 0;
            return false;
        }

        public bool GetTrackName() => IsActionTriggered(InputAction.TrackName);

        public bool GetPlayerNumber() => _allowAuxiliaryInput && WasPressed(_kbPlayerNumber);

        public bool GetPause() => IsActionTriggered(InputAction.Pause);

        public bool GetStartEngine() => IsActionTriggered(InputAction.StartEngine);

        public bool GetFlush() => !_overlayInputBlocked && _lastState.IsDown(_kbFlush);

        // Speed and distance reporting hotkeys
        public bool GetSpeedReport() => IsActionTriggered(InputAction.ReportSpeed);
        public bool GetDistanceReport() => IsActionTriggered(InputAction.ReportDistance);

        public bool GetNextPanelRequest() => WasPressed(Key.Tab) && IsCtrlDown() && !IsShiftDown();

        public bool GetPreviousPanelRequest() => WasPressed(Key.Tab) && IsCtrlDown() && IsShiftDown();

        public bool GetOpenRadioMediaRequest() => WasPressed(Key.O);

        public bool GetToggleRadioPlaybackRequest() => WasPressed(Key.P);

        public bool GetToggleComputerControl() => WasPressed(Key.B) && IsAltDown();

        public void SetPanelInputAccess(bool allowDrivingInput, bool allowAuxiliaryInput)
        {
            _allowDrivingInput = allowDrivingInput;
            _allowAuxiliaryInput = allowAuxiliaryInput;
        }

        public void SetOverlayInputBlocked(bool blocked)
        {
            _overlayInputBlocked = blocked;
        }

        internal IReadOnlyList<InputActionDefinition> GetActionDefinitions()
        {
            return _actionDefinitions;
        }

        internal string GetActionLabel(InputAction action)
        {
            return _actionBindings.TryGetValue(action, out var binding)
                ? binding.Label
                : "Action";
        }

        internal Key GetKeyMapping(InputAction action)
        {
            return _actionBindings.TryGetValue(action, out var binding)
                ? binding.GetKey()
                : Key.Unknown;
        }

        internal JoystickAxisOrButton GetAxisMapping(InputAction action)
        {
            return _actionBindings.TryGetValue(action, out var binding)
                ? binding.GetAxis()
                : JoystickAxisOrButton.AxisNone;
        }

        internal void ApplyKeyMapping(InputAction action, Key key)
        {
            if (_actionBindings.TryGetValue(action, out var binding))
                binding.SetKey(key);
        }

        internal void ApplyAxisMapping(InputAction action, JoystickAxisOrButton axis)
        {
            if (_actionBindings.TryGetValue(action, out var binding))
                binding.SetAxis(axis);
        }

        private int GetAxis(JoystickAxisOrButton axis)
        {
            return GetAxis(axis, _lastJoystick);
        }

        private int GetAxis(JoystickAxisOrButton axis, JoystickStateSnapshot state)
        {
            switch (axis)
            {
                case JoystickAxisOrButton.AxisNone:
                    return 0;
                case JoystickAxisOrButton.AxisXNeg:
                    if (_center.X - state.X > 0)
                        return Math.Min(_center.X - state.X, 100);
                    break;
                case JoystickAxisOrButton.AxisXPos:
                    if (state.X - _center.X > 0)
                        return Math.Min(state.X - _center.X, 100);
                    break;
                case JoystickAxisOrButton.AxisYNeg:
                    if (_center.Y - state.Y > 0)
                        return Math.Min(_center.Y - state.Y, 100);
                    break;
                case JoystickAxisOrButton.AxisYPos:
                    if (state.Y - _center.Y > 0)
                        return Math.Min(state.Y - _center.Y, 100);
                    break;
                case JoystickAxisOrButton.AxisZNeg:
                    if (_center.Z - state.Z > 0)
                        return Math.Min(_center.Z - state.Z, 100);
                    break;
                case JoystickAxisOrButton.AxisZPos:
                    if (state.Z - _center.Z > 0)
                        return Math.Min(state.Z - _center.Z, 100);
                    break;
                case JoystickAxisOrButton.AxisRxNeg:
                    if (_center.Rx - state.Rx > 0)
                        return Math.Min(_center.Rx - state.Rx, 100);
                    break;
                case JoystickAxisOrButton.AxisRxPos:
                    if (state.Rx - _center.Rx > 0)
                        return Math.Min(state.Rx - _center.Rx, 100);
                    break;
                case JoystickAxisOrButton.AxisRyNeg:
                    if (_center.Ry - state.Ry > 0)
                        return Math.Min(_center.Ry - state.Ry, 100);
                    break;
                case JoystickAxisOrButton.AxisRyPos:
                    if (state.Ry - _center.Ry > 0)
                        return Math.Min(state.Ry - _center.Ry, 100);
                    break;
                case JoystickAxisOrButton.AxisRzNeg:
                    if (_center.Rz - state.Rz > 0)
                        return Math.Min(_center.Rz - state.Rz, 100);
                    break;
                case JoystickAxisOrButton.AxisRzPos:
                    if (state.Rz - _center.Rz > 0)
                        return Math.Min(state.Rz - _center.Rz, 100);
                    break;
                case JoystickAxisOrButton.AxisSlider1Neg:
                    if (_center.Slider1 - state.Slider1 > 0)
                        return Math.Min(_center.Slider1 - state.Slider1, 100);
                    break;
                case JoystickAxisOrButton.AxisSlider1Pos:
                    if (state.Slider1 - _center.Slider1 > 0)
                        return Math.Min(state.Slider1 - _center.Slider1, 100);
                    break;
                case JoystickAxisOrButton.AxisSlider2Neg:
                    if (_center.Slider2 - state.Slider2 > 0)
                        return Math.Min(_center.Slider2 - state.Slider2, 100);
                    break;
                case JoystickAxisOrButton.AxisSlider2Pos:
                    if (state.Slider2 - _center.Slider2 > 0)
                        return Math.Min(state.Slider2 - _center.Slider2, 100);
                    break;
                case JoystickAxisOrButton.Button1:
                    return state.B1 ? 100 : 0;
                case JoystickAxisOrButton.Button2:
                    return state.B2 ? 100 : 0;
                case JoystickAxisOrButton.Button3:
                    return state.B3 ? 100 : 0;
                case JoystickAxisOrButton.Button4:
                    return state.B4 ? 100 : 0;
                case JoystickAxisOrButton.Button5:
                    return state.B5 ? 100 : 0;
                case JoystickAxisOrButton.Button6:
                    return state.B6 ? 100 : 0;
                case JoystickAxisOrButton.Button7:
                    return state.B7 ? 100 : 0;
                case JoystickAxisOrButton.Button8:
                    return state.B8 ? 100 : 0;
                case JoystickAxisOrButton.Button9:
                    return state.B9 ? 100 : 0;
                case JoystickAxisOrButton.Button10:
                    return state.B10 ? 100 : 0;
                case JoystickAxisOrButton.Button11:
                    return state.B11 ? 100 : 0;
                case JoystickAxisOrButton.Button12:
                    return state.B12 ? 100 : 0;
                case JoystickAxisOrButton.Button13:
                    return state.B13 ? 100 : 0;
                case JoystickAxisOrButton.Button14:
                    return state.B14 ? 100 : 0;
                case JoystickAxisOrButton.Button15:
                    return state.B15 ? 100 : 0;
                case JoystickAxisOrButton.Button16:
                    return state.B16 ? 100 : 0;
                case JoystickAxisOrButton.Pov1:
                    return state.Pov1 ? 100 : 0;
                case JoystickAxisOrButton.Pov2:
                    return state.Pov2 ? 100 : 0;
                case JoystickAxisOrButton.Pov3:
                    return state.Pov3 ? 100 : 0;
                case JoystickAxisOrButton.Pov4:
                    return state.Pov4 ? 100 : 0;
                case JoystickAxisOrButton.Pov5:
                    return state.Pov5 ? 100 : 0;
                case JoystickAxisOrButton.Pov6:
                    return state.Pov6 ? 100 : 0;
                case JoystickAxisOrButton.Pov7:
                    return state.Pov7 ? 100 : 0;
                case JoystickAxisOrButton.Pov8:
                    return state.Pov8 ? 100 : 0;
                default:
                    return 0;
            }

            return 0;
        }

        private bool WasPressed(Key key)
        {
            if (_overlayInputBlocked)
                return false;
            return _lastState.IsDown(key) && !_prevState.IsDown(key);
        }

        private bool IsActionTriggered(InputAction action)
        {
            if (_overlayInputBlocked)
                return false;
            var meta = GetActionMeta(action);
            if (!IsScopeEnabled(meta.Scope))
                return false;

            var keyboard = IsActionActiveOnKeyboard(action, meta);
            var joystick = IsActionActiveOnJoystick(action, meta);
            return keyboard || joystick;
        }

        private bool IsActionActiveOnKeyboard(InputAction action, InputActionMeta meta)
        {
            if (!UseKeyboard)
                return false;

            var key = GetKeyMapping(action);
            if (key == Key.Unknown)
                return false;

            var active = meta.KeyboardMode == TriggerMode.Hold
                ? _lastState.IsDown(key)
                : WasPressed(key);
            if (!active && meta.AllowNumpadEnterAlias && key == Key.Return)
                active = WasPressed(Key.NumberPadEnter);

            return active;
        }

        private bool IsActionActiveOnJoystick(InputAction action, InputActionMeta meta)
        {
            if (!UseJoystick)
                return false;

            var axis = GetAxisMapping(action);
            if (axis == JoystickAxisOrButton.AxisNone)
                return false;

            return meta.JoystickMode == TriggerMode.Hold
                ? GetAxis(axis) > 50
                : AxisPressed(axis);
        }

        private bool IsScopeEnabled(InputScope scope)
        {
            return scope switch
            {
                InputScope.Driving => _allowDrivingInput,
                InputScope.Auxiliary => _allowAuxiliaryInput,
                _ => false
            };
        }

        private InputActionMeta GetActionMeta(InputAction action)
        {
            if (_actionBindings.TryGetValue(action, out var binding))
                return binding.Meta;

            return new InputActionMeta(InputScope.Auxiliary, TriggerMode.Press, TriggerMode.Press);
        }

        private Dictionary<InputAction, InputActionBinding> CreateActionBindings()
        {
            var bindings = new Dictionary<InputAction, InputActionBinding>();

            void Add(
                InputAction action,
                string label,
                InputScope scope,
                TriggerMode keyboardMode,
                TriggerMode joystickMode,
                Func<Key> getKey,
                Action<Key> setKey,
                Func<JoystickAxisOrButton> getAxis,
                Action<JoystickAxisOrButton> setAxis,
                bool allowNumpadEnterAlias = false)
            {
                bindings[action] = new InputActionBinding(
                    label,
                    new InputActionMeta(scope, keyboardMode, joystickMode, allowNumpadEnterAlias),
                    getKey,
                    setKey,
                    getAxis,
                    setAxis);
                _actionDefinitions.Add(new InputActionDefinition(action, label));
            }

            Add(InputAction.SteerLeft, "Steer left", InputScope.Driving, TriggerMode.Hold, TriggerMode.Hold, () => _kbLeft, key => SetLeft(key), () => _left, axis => SetLeft(axis));
            Add(InputAction.SteerRight, "Steer right", InputScope.Driving, TriggerMode.Hold, TriggerMode.Hold, () => _kbRight, key => SetRight(key), () => _right, axis => SetRight(axis));
            Add(InputAction.Throttle, "Throttle", InputScope.Driving, TriggerMode.Hold, TriggerMode.Hold, () => _kbThrottle, key => SetThrottle(key), () => _throttle, axis => SetThrottle(axis));
            Add(InputAction.Brake, "Brake", InputScope.Driving, TriggerMode.Hold, TriggerMode.Hold, () => _kbBrake, key => SetBrake(key), () => _brake, axis => SetBrake(axis));
            Add(InputAction.GearUp, "Shift gear up", InputScope.Driving, TriggerMode.Hold, TriggerMode.Hold, () => _kbGearUp, key => SetGearUp(key), () => _gearUp, axis => SetGearUp(axis));
            Add(InputAction.GearDown, "Shift gear down", InputScope.Driving, TriggerMode.Hold, TriggerMode.Hold, () => _kbGearDown, key => SetGearDown(key), () => _gearDown, axis => SetGearDown(axis));
            Add(InputAction.Horn, "Use horn", InputScope.Driving, TriggerMode.Hold, TriggerMode.Hold, () => _kbHorn, key => SetHorn(key), () => _horn, axis => SetHorn(axis));
            Add(InputAction.RequestInfo, "Request position information", InputScope.Auxiliary, TriggerMode.Hold, TriggerMode.Hold, () => _kbRequestInfo, key => SetRequestInfo(key), () => _requestInfo, axis => SetRequestInfo(axis));
            Add(InputAction.CurrentGear, "Current gear", InputScope.Auxiliary, TriggerMode.Press, TriggerMode.Press, () => _kbCurrentGear, key => SetCurrentGear(key), () => _currentGear, axis => SetCurrentGear(axis));
            Add(InputAction.CurrentLapNr, "Current lap number", InputScope.Auxiliary, TriggerMode.Press, TriggerMode.Press, () => _kbCurrentLapNr, key => SetCurrentLapNr(key), () => _currentLapNr, axis => SetCurrentLapNr(axis));
            Add(InputAction.CurrentRacePerc, "Current race percentage", InputScope.Auxiliary, TriggerMode.Press, TriggerMode.Press, () => _kbCurrentRacePerc, key => SetCurrentRacePerc(key), () => _currentRacePerc, axis => SetCurrentRacePerc(axis));
            Add(InputAction.CurrentLapPerc, "Current lap percentage", InputScope.Auxiliary, TriggerMode.Press, TriggerMode.Press, () => _kbCurrentLapPerc, key => SetCurrentLapPerc(key), () => _currentLapPerc, axis => SetCurrentLapPerc(axis));
            Add(InputAction.CurrentRaceTime, "Current race time", InputScope.Auxiliary, TriggerMode.Press, TriggerMode.Press, () => _kbCurrentRaceTime, key => SetCurrentRaceTime(key), () => _currentRaceTime, axis => SetCurrentRaceTime(axis));
            Add(InputAction.StartEngine, "Start the engine", InputScope.Auxiliary, TriggerMode.Press, TriggerMode.Press, () => _kbStartEngine, key => SetStartEngine(key), () => _startEngine, axis => SetStartEngine(axis), allowNumpadEnterAlias: true);
            Add(InputAction.ReportDistance, "Report distance", InputScope.Auxiliary, TriggerMode.Press, TriggerMode.Press, () => _kbReportDistance, key => SetReportDistance(key), () => _reportDistance, axis => SetReportDistance(axis));
            Add(InputAction.ReportSpeed, "Report speed", InputScope.Auxiliary, TriggerMode.Press, TriggerMode.Press, () => _kbReportSpeed, key => SetReportSpeed(key), () => _reportSpeed, axis => SetReportSpeed(axis));
            Add(InputAction.TrackName, "Report track name", InputScope.Auxiliary, TriggerMode.Press, TriggerMode.Press, () => _kbTrackName, key => SetTrackName(key), () => _trackName, axis => SetTrackName(axis));
            Add(InputAction.Pause, "Pause", InputScope.Auxiliary, TriggerMode.Hold, TriggerMode.Hold, () => _kbPause, key => SetPause(key), () => _pause, axis => SetPause(axis));

            return bindings;
        }

        private bool AxisPressed(JoystickAxisOrButton axis)
        {
            if (!UseJoystick)
                return false;
            var current = GetAxis(axis, _lastJoystick);
            var previous = _hasPrevJoystick ? GetAxis(axis, _prevJoystick) : 0;
            return current > 50 && previous <= 50;
        }

        private bool IsAltDown()
        {
            return _lastState.IsDown(Key.LeftAlt) || _lastState.IsDown(Key.RightAlt);
        }

        private bool IsCtrlDown()
        {
            return _lastState.IsDown(Key.LeftControl) || _lastState.IsDown(Key.RightControl);
        }

        private bool IsShiftDown()
        {
            return _lastState.IsDown(Key.LeftShift) || _lastState.IsDown(Key.RightShift);
        }

        private void ReadFromSettings()
        {
            _left = _settings.JoystickLeft;
            _right = _settings.JoystickRight;
            _throttle = _settings.JoystickThrottle;
            _brake = _settings.JoystickBrake;
            _gearUp = _settings.JoystickGearUp;
            _gearDown = _settings.JoystickGearDown;
            _horn = _settings.JoystickHorn;
            _requestInfo = _settings.JoystickRequestInfo;
            _currentGear = _settings.JoystickCurrentGear;
            _currentLapNr = _settings.JoystickCurrentLapNr;
            _currentRacePerc = _settings.JoystickCurrentRacePerc;
            _currentLapPerc = _settings.JoystickCurrentLapPerc;
            _currentRaceTime = _settings.JoystickCurrentRaceTime;
            _startEngine = _settings.JoystickStartEngine;
            _reportDistance = _settings.JoystickReportDistance;
            _reportSpeed = _settings.JoystickReportSpeed;
            _trackName = _settings.JoystickTrackName;
            _pause = _settings.JoystickPause;
            _center = _settings.JoystickCenter;
            _hasCenter = true;
            _kbLeft = _settings.KeyLeft;
            _kbRight = _settings.KeyRight;
            _kbThrottle = _settings.KeyThrottle;
            _kbBrake = _settings.KeyBrake;
            _kbGearUp = _settings.KeyGearUp;
            _kbGearDown = _settings.KeyGearDown;
            _kbHorn = _settings.KeyHorn;
            _kbRequestInfo = _settings.KeyRequestInfo;
            _kbCurrentGear = _settings.KeyCurrentGear;
            _kbCurrentLapNr = _settings.KeyCurrentLapNr;
            _kbCurrentRacePerc = _settings.KeyCurrentRacePerc;
            _kbCurrentLapPerc = _settings.KeyCurrentLapPerc;
            _kbCurrentRaceTime = _settings.KeyCurrentRaceTime;
            _kbStartEngine = _settings.KeyStartEngine;
            _kbReportDistance = _settings.KeyReportDistance;
            _kbReportSpeed = _settings.KeyReportSpeed;
            _kbTrackName = _settings.KeyTrackName;
            _kbPause = _settings.KeyPause;
            _deviceMode = _settings.DeviceMode;
        }
    }
}
