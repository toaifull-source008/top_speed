import re

with open('top_speed_net/TopSpeed/Vehicles/Car.cs', 'r') as f:
    content = f.read()

# Add _isComputerControlled and _computerRandom
content = re.sub(
    r'(private CarState _state;\s+private TrackSurface _surface;\s+private int _gear;)',
    r'\1\n        private bool _isComputerControlled;\n        private int _computerRandom;',
    content,
    count=1
)

# Initialize _computerRandom
content = re.sub(
    r'(_frame = 1;)',
    r'\1\n            _computerRandom = Algorithm.RandomInt(100);',
    content,
    count=1
)

# Replace the input block in Run
input_block = """                _currentSteering = _input.GetSteering();
                _currentThrottle = _input.GetThrottle();
                _currentBrake = _input.GetBrake();"""

replacement = """                if (_input.GetToggleComputerControl())
                {
                    _isComputerControlled = !_isComputerControlled;
                }

                if (_isComputerControlled)
                {
                    var road = _track.RoadComputer(_positionY);
                    var laneHalfWidth = Math.Max(0.1f, Math.Abs(road.Right - road.Left) * 0.5f);
                    var relPos = TopSpeed.Bots.BotRaceRules.CalculateRelativeLanePosition(_positionX, road.Left, laneHalfWidth);
                    var nextRoad = _track.RoadComputer(_positionY + 30.0f);
                    var nextLaneHalfWidth = Math.Max(0.1f, Math.Abs(nextRoad.Right - nextRoad.Left) * 0.5f);

                    TopSpeed.Bots.BotSharedModel.GetControlInputs(1, _computerRandom, road.Type, nextRoad.Type, relPos, out var botThrottle, out var botSteering);
                    _currentThrottle = (int)Math.Round(botThrottle);
                    _currentSteering = (int)Math.Round(botSteering);
                    _currentBrake = 0;
                }
                else
                {
                    _currentSteering = _input.GetSteering();
                    _currentThrottle = _input.GetThrottle();
                    _currentBrake = _input.GetBrake();
                }"""

content = content.replace(input_block, replacement)

with open('top_speed_net/TopSpeed/Vehicles/Car.cs', 'w') as f:
    f.write(content)
