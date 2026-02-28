// To implement Alt-B computer control:
// 1. Add `GetToggleComputerControl` to `RaceInput`
// 2. We can handle it in `Level.cs` or `Car.cs`.
// Since `Car.cs` is already handling bot behavior for actual bots (`ComputerPlayer.cs`), maybe it's cleaner to add an internal `ComputerPlayer _autoPilot` to `Car.cs`, or add a bool `_isComputerControlled` to `Car.cs`.
// However, `ComputerPlayer` needs a player number.
// In `Car.cs`: we can just update `_currentSteering`, `_currentThrottle`, etc. from an internal `ComputerPlayer` logic or just let the `ComputerPlayer` class handle it.
// Let's look at what ComputerPlayer actually does. It has a `Run` method.
