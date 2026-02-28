// To make the player's car use ComputerPlayer's logic without duplicating the code,
// the simplest and cleanest way is to use `BotSharedModel.GetControlInputs(1, _random, currentType, nextType, relPos, out throttle, out steering)`
// in Car.cs, when computer control is enabled.
