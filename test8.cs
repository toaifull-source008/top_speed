// To make the Car driven by a computer, we just add the following code into Car.cs's Run method:
// ```csharp
// if (_input.GetToggleComputerControl())
// {
//     _isComputerControlled = !_isComputerControlled;
//     // Maybe we can also Speak a text:
//     if (_isComputerControlled)
//         AudioWorld.SpeakText("Computer Control Enabled"); // actually we can't easily access the SpeechService here... Let's check how the Car speaks? Wait, Car doesn't speak.
// }
// if (_isComputerControlled)
// {
//     var road = _track.RoadComputer(_positionY);
//     var laneHalfWidth = Math.Max(0.1f, Math.Abs(road.Right - road.Left) * 0.5f);
//     var relPos = BotRaceRules.CalculateRelativeLanePosition(_positionX, road.Left, laneHalfWidth);
//     var nextRoad = _track.RoadComputer(_positionY + 30.0f); // CallLength
//     var nextLaneHalfWidth = Math.Max(0.1f, Math.Abs(nextRoad.Right - nextRoad.Left) * 0.5f);
//     var nextRelPos = BotRaceRules.CalculateRelativeLanePosition(_positionX, nextRoad.Left, nextLaneHalfWidth);
//     BotSharedModel.GetControlInputs(1.0f, _random, road.Type, nextRoad.Type, relPos, out var throttle, out var steering);
//     _currentThrottle = (int)Math.Round(throttle);
//     _currentSteering = (int)Math.Round(steering);
//     _currentBrake = 0; // The GetControlInputs doesn't do braking apparently, wait, how does bot brake?
//     // wait, bot braking logic:
// }
// else
// {
//    _currentSteering = _input.GetSteering();
//    _currentThrottle = _input.GetThrottle();
//    _currentBrake = _input.GetBrake();
// }
// ```
