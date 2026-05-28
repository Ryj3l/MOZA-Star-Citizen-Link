using Moza.ScLink.App.Input;

namespace Moza.ScLink.App.Tests;

public sealed class HotkeyCombinationTests
{
    [Fact]
    public void DefaultTextParsesToDefaultCombination()
    {
        Assert.True(HotkeyCombination.TryParse(HotkeyCombination.DefaultText, out var combo));
        Assert.Equal(HotkeyCombination.Default, combo);
        Assert.Equal(HotkeyCombination.ModControl | HotkeyCombination.ModAlt, combo.Modifiers);
        Assert.Equal(0x7Bu, combo.VirtualKey); // VK_F12
    }

    [Theory]
    [InlineData("Ctrl+Alt+F12")]
    [InlineData("control+alt+f12")]
    [InlineData("CTRL + ALT + F12")]
    public void ModifierAndKeyTokensAreCaseAndWhitespaceInsensitive(string text)
    {
        Assert.True(HotkeyCombination.TryParse(text, out var combo));
        Assert.Equal(HotkeyCombination.Default, combo);
    }

    [Fact]
    public void WinAndShiftAliasesCompose()
    {
        Assert.True(HotkeyCombination.TryParse("Windows+Shift+A", out var combo));
        Assert.Equal(HotkeyCombination.ModWin | HotkeyCombination.ModShift, combo.Modifiers);
        Assert.Equal((uint)'A', combo.VirtualKey);
    }

    [Theory]
    [InlineData("F1", 0x70u)]
    [InlineData("F24", 0x87u)]
    [InlineData("A", 0x41u)]
    [InlineData("z", 0x5Au)]
    [InlineData("5", 0x35u)]
    public void SingleKeyTokensMapToVirtualKeyCodes(string key, uint expectedVk)
    {
        Assert.True(HotkeyCombination.TryParse("Ctrl+" + key, out var combo));
        Assert.Equal(HotkeyCombination.ModControl, combo.Modifiers);
        Assert.Equal(expectedVk, combo.VirtualKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+Alt")]   // no key token
    [InlineData("Ctrl+A+B")]   // two key tokens
    [InlineData("Ctrl+Foo")]   // unknown token
    [InlineData("Ctrl++A")]    // empty token
    [InlineData("Ctrl+F0")]    // F-key below range
    [InlineData("Ctrl+F25")]   // F-key above range
    public void InvalidInputFailsToParse(string? text)
    {
        Assert.False(HotkeyCombination.TryParse(text, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("garbage")]
    [InlineData("Ctrl+Alt")]
    public void ParseOrDefaultFallsBackToDefault(string? text)
    {
        Assert.Equal(HotkeyCombination.Default, HotkeyCombination.ParseOrDefault(text));
    }
}
