using System;
using System.IO;
using System.Text;
using System.Windows.Input;

namespace Kronix.Services;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8
}

/// <summary>A global hotkey: modifier flags (Win32 MOD_* values) plus a key.</summary>
public sealed record Hotkey(HotkeyModifiers Modifiers, Key Key)
{
    /// <summary>Easy to press and not used by Windows itself.</summary>
    public static Hotkey Default { get; } = new(
        HotkeyModifiers.Control | HotkeyModifiers.Alt, Key.K);

    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt
        or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System
        or Key.None or Key.DeadCharProcessed or Key.ImeProcessed;

    public override string ToString() => Format(german: true);

    /// <summary>Stable, language-independent form used for persistence.</summary>
    public string Serialize() => Format(german: false);

    private string Format(bool german)
    {
        var builder = new StringBuilder();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) builder.Append(german ? "Strg+" : "Ctrl+");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) builder.Append("Alt+");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) builder.Append(german ? "Umschalt+" : "Shift+");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) builder.Append("Win+");
        builder.Append(Key);
        return builder.ToString();
    }

    public static bool TryParse(string? text, out Hotkey? hotkey)
    {
        hotkey = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        HotkeyModifiers modifiers = HotkeyModifiers.None;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl": modifiers |= HotkeyModifiers.Control; break;
                case "alt": modifiers |= HotkeyModifiers.Alt; break;
                case "shift": modifiers |= HotkeyModifiers.Shift; break;
                case "win": modifiers |= HotkeyModifiers.Win; break;
                default: return false;
            }
        }

        if (!Enum.TryParse(parts[^1], ignoreCase: true, out Key key) || !Enum.IsDefined(key)
            || IsModifierKey(key) || KeyInterop.VirtualKeyFromKey(key) == 0)
        {
            return false;
        }

        if (modifiers == HotkeyModifiers.None)
        {
            return false;
        }

        hotkey = new Hotkey(modifiers, key);
        return true;
    }
}

/// <summary>Stores the hotkey per PC (not in the shared database).</summary>
public sealed class HotkeyStore
{
    private const string DisabledMarker = "off";
    private readonly string _path;

    public HotkeyStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kronix", "hotkey.txt");
    }

    /// <summary>Returns the saved hotkey; null means disabled. Missing or invalid files give the default.</summary>
    public Hotkey? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return Hotkey.Default;
            }

            string text = File.ReadAllText(_path).Trim();
            if (string.Equals(text, DisabledMarker, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return Hotkey.TryParse(text, out Hotkey? hotkey) ? hotkey : Hotkey.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Hotkey.Default;
        }
    }

    public void Save(Hotkey? hotkey)
    {
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_path, hotkey?.Serialize() ?? DisabledMarker);
    }
}
