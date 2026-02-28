// Here is the complete implementation strategy:
// 1. In `TopSpeed/Input/RaceInput.cs`:
//    Add `public bool GetToggleComputerControl() => WasPressed(Key.B) && IsAltDown();`
//    Add `private bool IsAltDown() => _lastState.IsDown(Key.LeftAlt) || _lastState.IsDown(Key.RightAlt);`
//
// 2. In `TopSpeed/Vehicles/Car.cs`:
//    Add a private field: `private bool _isComputerControlled;`
//    Add a private field: `private int _computerRandom;`
//    In `Initialize`: `_computerRandom = Algorithm.RandomInt(100);`
//    In `Run(float elapsed)`:
//    Check if `_input.GetToggleComputerControl()` is true, and toggle `_isComputerControlled`.
//    Wait! We can't do it inside `Run()` if it is called constantly, we should be able to toggle it only once per key press. `WasPressed` in `GetToggleComputerControl()` handles this.
//
//    When updating `_currentSteering`, `_currentThrottle`, `_currentBrake`:
//    ```csharp
//    if (_input.GetToggleComputerControl())
//    {
//        _isComputerControlled = !_isComputerControlled;
//        // Maybe play a sound or use Speech to inform player
//    }
//
//    if (_isComputerControlled)
//    {
//        var road = _track.RoadComputer(_positionY);
//        var laneHalfWidth = Math.Max(0.1f, Math.Abs(road.Right - road.Left) * 0.5f);
//        var relPos = BotRaceRules.CalculateRelativeLanePosition(_positionX, road.Left, laneHalfWidth);
//        var nextRoad = _track.RoadComputer(_positionY + 30.0f);
//        var nextLaneHalfWidth = Math.Max(0.1f, Math.Abs(nextRoad.Right - nextRoad.Left) * 0.5f);
//        var nextRelPos = BotRaceRules.CalculateRelativeLanePosition(_positionX, nextRoad.Left, nextLaneHalfWidth);
//
//        BotSharedModel.GetControlInputs(1, _computerRandom, road.Type, nextRoad.Type, relPos, out var throttle, out var steering);
//        _currentThrottle = (int)Math.Round(throttle);
//        _currentSteering = (int)Math.Round(steering);
//        _currentBrake = 0; // The bot apparently doesn't use brake here, or we can copy the bot's braking logic. Let's see how Bot handles braking.
//    }
//    else
//    {
//        _currentSteering = _input.GetSteering();
//        _currentThrottle = _input.GetThrottle();
//        _currentBrake = _input.GetBrake();
//    }
//    ```
