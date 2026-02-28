// To make the Computer control work, let's copy the AI part of `ComputerPlayer`.
// ComputerPlayer.cs uses `BotRaceRules.CalculateRelativeLanePosition` and `BotSharedModel.GetControlInputs` which need some state:
// - `_difficulty` (we can just use 1.0f or 0.5f for the car, maybe 1.0f which means a good bot)
// - `_random` (an instance of `System.Random`)
// Let's see what `ComputerPlayer` imports.
