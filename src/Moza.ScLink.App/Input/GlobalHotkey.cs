using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Moza.ScLink.Core.Safety;

namespace Moza.ScLink.App.Input;

/// <summary>
/// Registers a system-wide hotkey that activates emergency stop, via a dedicated message-only
/// (<c>HWND_MESSAGE</c>) window owned on the WPF UI thread. The window's messages are pumped by the
/// thread's WPF <c>Dispatcher</c>, so the hotkey keeps working even when the main window is hidden or
/// minimized to tray. This is a thin Win32 P/Invoke adapter and is not unit-tested directly (precedent:
/// the Vortice DirectInput device); the testable parsing logic lives in <see cref="HotkeyCombination"/>.
/// Must be constructed and disposed on the UI thread (window thread affinity).
/// </summary>
public sealed partial class GlobalHotkey : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 1;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly IEmergencyStop _emergencyStop;
    private readonly ILogger<GlobalHotkey> _logger;
    private readonly WindowProcedure _wndProc; // GC root for the marshalled function pointer
    private readonly string _className;

    private ushort _classAtom;
    private IntPtr _hwnd;
    private bool _registered;
    private bool _disposed;

    public GlobalHotkey(HotkeyCombination combination, IEmergencyStop emergencyStop, ILogger<GlobalHotkey> logger)
    {
        ArgumentNullException.ThrowIfNull(emergencyStop);
        ArgumentNullException.ThrowIfNull(logger);
        _emergencyStop = emergencyStop;
        _logger = logger;
        _wndProc = WindowProc;
        _className = "MozaScLinkHotkeyWindow_" + Guid.NewGuid().ToString("N");

        var hInstance = GetModuleHandleW(IntPtr.Zero);
        var classNamePtr = Marshal.StringToHGlobalUni(_className);
        try
        {
            var wndClass = new WndClassExW
            {
                cbSize = (uint)Marshal.SizeOf<WndClassExW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = hInstance,
                lpszClassName = classNamePtr,
            };

            _classAtom = RegisterClassExW(ref wndClass);
            if (_classAtom == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterClassEx failed for the emergency-stop hotkey window.");
            }
        }
        finally
        {
            // The system copies the class name on RegisterClassEx, so the buffer can be freed now.
            Marshal.FreeHGlobal(classNamePtr);
        }

        _hwnd = CreateWindowExW(0, _classAtom, IntPtr.Zero, 0, 0, 0, 0, 0, HwndMessage, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            UnregisterClassAtom(hInstance);
            throw new Win32Exception(error, "CreateWindowEx failed for the emergency-stop hotkey window.");
        }

        _registered = RegisterHotKey(_hwnd, HotkeyId, combination.Modifiers, combination.VirtualKey);
        if (_registered)
        {
            Log.Registered(_logger, combination.Modifiers, combination.VirtualKey);
        }
        else
        {
            // Combination claimed by another process: do not crash. The on-screen E-STOP button is the fallback.
            Log.RegistrationFailed(_logger, combination.Modifiers, combination.VirtualKey);
        }
    }

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            try
            {
                // ActivateAsync completes synchronously and is idempotent; fire-and-forget on the UI thread.
                _ = _emergencyStop.ActivateAsync("hotkey");
            }
            catch (Exception ex)
            {
                // The message pump must never throw out of a WndProc.
                Log.ActivationFailed(_logger, ex);
            }

            return IntPtr.Zero;
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_registered && _hwnd != IntPtr.Zero)
        {
            _ = UnregisterHotKey(_hwnd, HotkeyId);
            _registered = false;
        }

        if (_hwnd != IntPtr.Zero)
        {
            _ = DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        UnregisterClassAtom(GetModuleHandleW(IntPtr.Zero));
    }

    private void UnregisterClassAtom(IntPtr hInstance)
    {
        if (_classAtom != 0)
        {
            _ = UnregisterClassW(_classAtom, hInstance);
            _classAtom = 0;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProcedure(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct WndClassExW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public IntPtr lpszMenuName;
        public IntPtr lpszClassName;
        public IntPtr hIconSm;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GetModuleHandleW(IntPtr lpModuleName);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial ushort RegisterClassExW(ref WndClassExW lpwcx);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterClassW(ushort lpClassName, IntPtr hInstance);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr CreateWindowExW(
        uint dwExStyle, ushort lpClassName, IntPtr lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    private static class Log
    {
        private static readonly Action<ILogger, uint, uint, Exception?> _registered =
            LoggerMessage.Define<uint, uint>(
                LogLevel.Information,
                new EventId(1, "HotkeyRegistered"),
                "Emergency-stop hotkey registered (modifiers=0x{Modifiers:X4}, virtualKey=0x{VirtualKey:X2}).");

        private static readonly Action<ILogger, uint, uint, Exception?> _registrationFailed =
            LoggerMessage.Define<uint, uint>(
                LogLevel.Warning,
                new EventId(2, "HotkeyRegistrationFailed"),
                "Emergency-stop hotkey registration failed (modifiers=0x{Modifiers:X4}, virtualKey=0x{VirtualKey:X2}); the combination may be claimed by another process. The on-screen E-STOP button remains available.");

        private static readonly Action<ILogger, Exception?> _activationFailed =
            LoggerMessage.Define(
                LogLevel.Error,
                new EventId(3, "HotkeyActivationFailed"),
                "Emergency-stop hotkey was pressed but activation threw.");

        public static void Registered(ILogger logger, uint modifiers, uint virtualKey) => _registered(logger, modifiers, virtualKey, null);

        public static void RegistrationFailed(ILogger logger, uint modifiers, uint virtualKey) => _registrationFailed(logger, modifiers, virtualKey, null);

        public static void ActivationFailed(ILogger logger, Exception exception) => _activationFailed(logger, exception);
    }
}
