    private bool IsAltDown()
    {
        return _lastState.IsDown(Key.LeftAlt) || _lastState.IsDown(Key.RightAlt);
    }
