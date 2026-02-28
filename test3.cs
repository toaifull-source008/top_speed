// To implement this feature we need to:
// 1. Add `public bool GetToggleComputerControl() => WasPressed(Key.B) && IsAltDown();` to RaceInput.cs (and an IsAltDown helper)
// 2. Add `private bool _computerControl;` and `private ComputerPlayer _computerController;` in Level.cs or Car.cs?
// It might make more sense to let Car.cs handle this since it's the one consuming _input.GetSteering() etc.
// In Car.cs, we can have a ComputerPlayer instance that represents the bot logic for our car.
