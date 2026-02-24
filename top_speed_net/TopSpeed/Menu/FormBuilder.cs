using System;
using System.Collections.Generic;

namespace TopSpeed.Menu
{
    internal sealed class MenuFormControl
    {
        public MenuFormControl(Func<string> label, Action onActivate, string? hint = null)
        {
            Label = label ?? throw new ArgumentNullException(nameof(label));
            OnActivate = onActivate ?? throw new ArgumentNullException(nameof(onActivate));
            Hint = hint;
        }

        public Func<string> Label { get; }
        public Action OnActivate { get; }
        public string? Hint { get; }
    }

    internal static class MenuFormBuilder
    {
        public static List<MenuItem> BuildItems(
            IEnumerable<MenuFormControl> controls,
            string saveLabel,
            Action onSave,
            string closeLabel)
        {
            var items = new List<MenuItem>();
            if (controls != null)
            {
                foreach (var control in controls)
                {
                    items.Add(new MenuItem(control.Label, MenuAction.None, onActivate: control.OnActivate, hint: control.Hint));
                }
            }

            items.Add(new MenuItem(saveLabel, MenuAction.None, onActivate: onSave));
            items.Add(new MenuItem(closeLabel, MenuAction.None, flags: MenuItemFlags.Close));
            return items;
        }
    }
}
