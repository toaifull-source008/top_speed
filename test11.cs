// Looking at ComputerPlayer.AI(), it only sets _currentThrottle and _currentSteering.
// _currentBrake is seemingly never set! Wait, maybe BotSharedModel.GetControlInputs sets throttle to negative values? Let's check TopSpeed.Shared/Bots/BotSharedModel.cs again.
