using System;

namespace TopSpeed.Menu
{
    internal class MenuItem
    {
        private readonly string _text;
        private readonly Func<string>? _textProvider;
        private readonly MenuItemAction[] _actions;
        public string? Hint { get; }

        public string Text => _text;
        public MenuAction Action { get; }
        public string? NextMenuId { get; }
        public Action? OnActivate { get; }
        public bool SuppressPostActivateAnnouncement { get; }
        public MenuItemFlags Flags { get; }
        public bool IsCloseItem => (Flags & MenuItemFlags.Close) != 0;
        public bool HasActions => _actions.Length > 0;
        public int ActionCount => _actions.Length;

        public MenuItem(
            string text,
            MenuAction action,
            string? nextMenuId = null,
            Action? onActivate = null,
            bool suppressPostActivateAnnouncement = false,
            string? hint = null,
            MenuItemFlags flags = MenuItemFlags.None,
            params MenuItemAction[] actions)
        {
            _text = text;
            _textProvider = null;
            Action = action;
            NextMenuId = nextMenuId;
            OnActivate = onActivate;
            SuppressPostActivateAnnouncement = suppressPostActivateAnnouncement;
            Hint = hint;
            Flags = flags;
            _actions = actions ?? Array.Empty<MenuItemAction>();
        }

        public MenuItem(
            Func<string> textProvider,
            MenuAction action,
            string? nextMenuId = null,
            Action? onActivate = null,
            bool suppressPostActivateAnnouncement = false,
            string? hint = null,
            MenuItemFlags flags = MenuItemFlags.None,
            params MenuItemAction[] actions)
        {
            _text = string.Empty;
            _textProvider = textProvider ?? throw new ArgumentNullException(nameof(textProvider));
            Action = action;
            NextMenuId = nextMenuId;
            OnActivate = onActivate;
            SuppressPostActivateAnnouncement = suppressPostActivateAnnouncement;
            Hint = hint;
            Flags = flags;
            _actions = actions ?? Array.Empty<MenuItemAction>();
        }

        public virtual string GetDisplayText()
        {
            return _textProvider?.Invoke() ?? _text;
        }

        public virtual string? ActivateAndGetAnnouncement()
        {
            OnActivate?.Invoke();
            return null;
        }

        public virtual bool Adjust(MenuAdjustAction action, out string? announcement)
        {
            announcement = null;
            return false;
        }

        public bool TryActivateAction(int actionIndex)
        {
            if (actionIndex < 0 || actionIndex >= _actions.Length)
                return false;

            _actions[actionIndex].Activate();
            return true;
        }

        public bool TryGetActionLabel(int actionIndex, out string label)
        {
            label = string.Empty;
            if (actionIndex < 0 || actionIndex >= _actions.Length)
                return false;

            label = _actions[actionIndex].Label ?? string.Empty;
            return true;
        }

        public virtual string? GetHintText()
        {
            if (HasActions)
            {
                var actionsHint = "Actions available, press right arrow to view.";
                if (string.IsNullOrWhiteSpace(Hint))
                    return actionsHint;
                return $"{Hint} {actionsHint}";
            }

            return Hint;
        }

        protected string GetBaseText()
        {
            return _textProvider?.Invoke() ?? _text;
        }
    }
}
