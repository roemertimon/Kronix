using System;
using System.ComponentModel;
using System.Windows.Interop;
using Kronix.Logging;

namespace Kronix.Services;

/// <summary>
/// Registers one system-wide hotkey on a hidden message-only window, so it works while the
/// dashboard is hidden in the tray.
/// </summary>
public static class GlobalHotkeyService
{
    private const int HotkeyId = 0x4B58;
    private const uint ModNoRepeat = 0x4000;

    private static readonly HotkeyStore Store = new();
    private static HwndSource? _source;
    private static Action? _onPressed;

    public static Hotkey? Current { get; private set; }

    /// <summary>Starts the service with the stored hotkey. Returns an error message if it could not be registered.</summary>
    public static string? Start(Action onPressed)
    {
        _onPressed = onPressed;
        _source ??= CreateMessageWindow();
        return Apply(Store.Load(), save: false);
    }

    /// <summary>Changes (or disables, when null) the hotkey and saves it. Returns an error message on failure.</summary>
    public static string? Change(Hotkey? hotkey) => Apply(hotkey, save: true);

    public static void Stop()
    {
        if (_source is null)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        _source.RemoveHook(WndProc);
        _source.Dispose();
        _source = null;
        Current = null;
    }

    private static string? Apply(Hotkey? hotkey, bool save)
    {
        if (_source is null)
        {
            return "Der Hotkey-Dienst läuft nicht.";
        }

        Hotkey? previous = Current;
        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        Current = null;

        if (hotkey is not null && !NativeMethods.RegisterHotKey(
                _source.Handle, HotkeyId, (uint)hotkey.Modifiers | ModNoRepeat, hotkey.VirtualKey))
        {
            string reason = new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error()).Message;
            AppLogger.Warning("hotkey.register_failed", $"Could not register global hotkey: {reason}");
            if (previous is not null)
            {
                NativeMethods.RegisterHotKey(
                    _source.Handle, HotkeyId, (uint)previous.Modifiers | ModNoRepeat, previous.VirtualKey);
                Current = previous;
            }

            return $"Die Tastenkombination {hotkey} ist bereits von Windows oder einem anderen Programm belegt.";
        }

        Current = hotkey;
        if (save)
        {
            try
            {
                Store.Save(hotkey);
            }
            catch (Exception ex)
            {
                AppLogger.Error("hotkey.save_failed", "Could not save the hotkey setting.", ex);
                return "Der Hotkey ist aktiv, konnte aber nicht dauerhaft gespeichert werden.";
            }
        }

        return null;
    }

    private static HwndSource CreateMessageWindow()
    {
        var parameters = new HwndSourceParameters("KronixHotkeyWindow")
        {
            ParentWindow = NativeMethods.HWND_MESSAGE,
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };
        var source = new HwndSource(parameters);
        source.AddHook(WndProc);
        return source;
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _onPressed?.Invoke();
        }

        return IntPtr.Zero;
    }
}
