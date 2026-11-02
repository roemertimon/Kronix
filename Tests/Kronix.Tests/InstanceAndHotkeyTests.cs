using System.Windows.Input;
using Kronix.Services;

namespace Kronix.Tests;

public class InstanceAndHotkeyTests
{
    [Fact]
    public void SecondGuard_CannotAcquire_AndSignalsFirst()
    {
        string name = "Kronix.Test." + Guid.NewGuid().ToString("N");
        using var first = new SingleInstanceGuard(name);
        Assert.True(first.TryAcquire());

        using var activated = new ManualResetEventSlim(false);
        first.StartListening(() => activated.Set());

        using var second = new SingleInstanceGuard(name);
        Assert.False(second.TryAcquire());
        Assert.True(second.SignalExistingInstance());
        Assert.True(activated.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Guard_CanBeReacquiredAfterDispose()
    {
        string name = "Kronix.Test." + Guid.NewGuid().ToString("N");
        var first = new SingleInstanceGuard(name);
        Assert.True(first.TryAcquire());
        first.Dispose();

        using var again = new SingleInstanceGuard(name);
        Assert.True(again.TryAcquire());
    }

    [Fact]
    public void Hotkey_SerializeAndParse_RoundTrips()
    {
        var hotkey = Hotkey.Default;
        Assert.Equal("Ctrl+Alt+K", hotkey.Serialize());
        Assert.True(Hotkey.TryParse(hotkey.Serialize(), out Hotkey? parsed));
        Assert.Equal(hotkey, parsed);
        Assert.Equal("Strg+Alt+K", hotkey.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("K")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Foo+K")]
    [InlineData("Ctrl+NotAKey")]
    public void Hotkey_TryParse_RejectsInvalid(string text)
    {
        Assert.False(Hotkey.TryParse(text, out _));
    }

    [Fact]
    public void HotkeyStore_DefaultsPersistsAndDisables()
    {
        string path = Path.Combine(Path.GetTempPath(), "kx-hotkey-" + Guid.NewGuid().ToString("N"), "hotkey.txt");
        try
        {
            var store = new HotkeyStore(path);
            Assert.Equal(Hotkey.Default, store.Load());

            var custom = new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Win, Key.F9);
            store.Save(custom);
            Assert.Equal(custom, store.Load());

            store.Save(null);
            Assert.Null(store.Load());

            File.WriteAllText(path, "garbage");
            Assert.Equal(Hotkey.Default, store.Load());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }
}
