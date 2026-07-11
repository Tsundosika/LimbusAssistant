using System.Windows.Input;

namespace Tsundosika.LimbusAssistant;

public sealed record HotkeyBinding(ModifierKeys Modifiers, Key Key)
{
    public static HotkeyBinding? Parse(string text)
    {
        var modifiers = ModifierKeys.None;
        Key? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "alt":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "shift":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "win" or "windows":
                    modifiers |= ModifierKeys.Windows;
                    break;
                default:
                    if (Enum.TryParse<Key>(part, true, out var parsed))
                    {
                        key = parsed;
                    }
                    break;
            }
        }
        return key is null ? null : new HotkeyBinding(modifiers, key.Value);
    }
}
