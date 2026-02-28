// Here is the plan:
// 1. Add `GetToggleComputerControl` to `RaceInput` by mapping it to `WasPressed(Key.B) && IsAltDown()`.
//    - Need to add `IsAltDown()` in `RaceInput` like `IsCtrlDown()` and `IsShiftDown()`.
// 2. In `Car.cs`, add `private bool _computerControl;`
//    - When `_input.GetToggleComputerControl()` is true, flip `_computerControl` and optionally play a sound or just flip it.
//    - Actually, there is a requirement to "bấm Alt B một lần nữa để lấy lại quyền điều khiển xe", meaning "Press Alt B again to regain control of the car".
//    - If `_computerControl` is true, then we should calculate steering, throttle, brake exactly like how `ComputerPlayer.AI()` calculates it:
