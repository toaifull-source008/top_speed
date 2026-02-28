// To make the computer control the car, we can use the bot logic directly.
// But the car is fundamentally different from a bot, it has _input.GetSteering() etc.
// In Car.cs, when _input.GetToggleComputerControl() is triggered, we set `_isComputerControlled = !_isComputerControlled;`
// Then if `_isComputerControlled`, instead of getting input from _input, we calculate the steering, throttle and brake using the same logic as the bot.
// Wait, the bot logic is tightly coupled to ComputerPlayer.cs.
