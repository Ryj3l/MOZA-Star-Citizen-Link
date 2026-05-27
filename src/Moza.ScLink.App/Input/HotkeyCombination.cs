using System.Globalization;

namespace Moza.ScLink.App.Input;

/// <summary>
/// An immutable global-hotkey combination: a set of Win32 modifier flags (<c>MOD_*</c>) plus a single
/// virtual-key code, suitable for <c>User32.RegisterHotKey</c>. Parsing is pure and Win32-free — the
/// <c>GlobalHotkey</c> adapter (T-16 PR2 E2) consumes the result. The pure/adapter split keeps this
/// logic unit-testable while the P/Invoke plumbing stays a thin, untested adapter.
/// </summary>
public readonly record struct HotkeyCombination(uint Modifiers, uint VirtualKey)
{
    // Win32 RegisterHotKey modifier flags (winuser.h).
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    /// <summary>The canonical text form of <see cref="Default"/>, matching the default of
    /// <c>AppSettings.EmergencyStopHotkey</c>.</summary>
    public const string DefaultText = "Ctrl+Alt+F12";

    /// <summary>The default combination, <c>Ctrl+Alt+F12</c> (VK_F12 = 0x7B).</summary>
    public static HotkeyCombination Default { get; } = new(ModControl | ModAlt, 0x7B);

    /// <summary>
    /// Parses a human-readable combo such as <c>"Ctrl+Alt+F12"</c>. Tolerates Ctrl/Control, Alt,
    /// Shift, Win/Windows modifier aliases (case-insensitive, whitespace-trimmed) and exactly one key
    /// token from F1–F24, A–Z, or 0–9. Returns <see langword="false"/> for null/blank input, unknown
    /// tokens, an empty token, or anything other than exactly one key token.
    /// </summary>
    public static bool TryParse(string? text, out HotkeyCombination combination)
    {
        combination = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        uint modifiers = 0;
        uint? virtualKey = null;

        foreach (var raw in text.Split('+'))
        {
            var token = raw.Trim();
            if (token.Length == 0)
            {
                return false;
            }

            switch (token.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= ModControl;
                    continue;
                case "ALT":
                    modifiers |= ModAlt;
                    continue;
                case "SHIFT":
                    modifiers |= ModShift;
                    continue;
                case "WIN":
                case "WINDOWS":
                    modifiers |= ModWin;
                    continue;
            }

            if (!TryParseKey(token, out var vk))
            {
                return false;
            }

            if (virtualKey is not null)
            {
                return false; // more than one non-modifier key
            }

            virtualKey = vk;
        }

        if (virtualKey is null)
        {
            return false; // no key token
        }

        combination = new HotkeyCombination(modifiers, virtualKey.Value);
        return true;
    }

    /// <summary>Parses <paramref name="text"/>, returning <see cref="Default"/> when it is null,
    /// blank, or malformed. Callers that need to surface the fallback should use
    /// <see cref="TryParse"/> and log on a <see langword="false"/> result.</summary>
    public static HotkeyCombination ParseOrDefault(string? text) =>
        TryParse(text, out var combination) ? combination : Default;

    private static bool TryParseKey(string token, out uint virtualKey)
    {
        virtualKey = 0;

        // Function keys F1–F24 → VK_F1 (0x70) .. VK_F24 (0x87).
        if ((token[0] is 'F' or 'f') && token.Length >= 2 &&
            int.TryParse(token.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var fn) &&
            fn is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + (fn - 1));
            return true;
        }

        if (token.Length == 1)
        {
            var c = token[0];
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                virtualKey = c; // VK for A–Z and 0–9 equals the ASCII code.
                return true;
            }
            if (c is >= 'a' and <= 'z')
            {
                virtualKey = char.ToUpperInvariant(c);
                return true;
            }
        }

        return false;
    }
}
