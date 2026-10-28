using System.Runtime.InteropServices;
using Oeps.CommandToAction.App;
using Oeps.CommandToAction.Core.Commands;

namespace Oeps.CommandToAction.Tests;

public static class KeyboardTests
{
    [Test]
    public static void MappedTextUsesNormalLetterKeysAndReleasesShift()
    {
        var events = WindowsActionKeyboard.EncodeText("Aa1", c => c switch
        {
            'A' => new(0x41, 0x1E, true),
            'a' => new(0x41, 0x1E, false),
            '1' => new(0x31, 0x02, false),
            _ => null
        }).SelectMany(batch => batch).ToArray();
        Assert.Equal("16:0,65:0,65:2,16:2,65:0,65:2,49:0,49:2",
            string.Join(",", events.Select(e => $"{e.Data.Keyboard.VirtualKey}:{e.Data.Keyboard.Flags}")));
        Assert.False(events.Any(e => (e.Data.Keyboard.Flags & 4) != 0));
        Assert.Equal((ushort)0x1E, events[1].Data.Keyboard.ScanCode);
    }

    [Test]
    public static void UnmappableCharactersRemainLiteralWithNoShortcutModifiers()
    {
        const string text = "+^%{}中😀";
        var events = WindowsActionKeyboard.EncodeText(text, _ => null).SelectMany(batch => batch).ToArray();
        Assert.Equal(text, new string(events.Where(e => e.Data.Keyboard.Flags == 4)
            .Select(e => (char)e.Data.Keyboard.ScanCode).ToArray()));
        Assert.True(events.All(e => e.Data.Keyboard.VirtualKey == 0));
    }

    [Test]
    public static void PartialShiftedInputReleasesAllAcceptedDownKeysWithoutRetyping()
    {
        var events = WindowsActionKeyboard.EncodeText("A", _ => new(0x41, 0x1E, true))
            .SelectMany(batch => batch).ToArray();
        Assert.Equal(0, WindowsActionKeyboard.ReleasesForAcceptedPrefix(events, 0).Length);
        Assert.Equal("16", Releases(1));
        Assert.Equal("65,16", Releases(2));
        Assert.Equal("16", Releases(3));
        Assert.Equal("", Releases(4));
        string Releases(uint count)
        {
            var releases = WindowsActionKeyboard.ReleasesForAcceptedPrefix(events, count);
            Assert.True(releases.All(e => (e.Data.Keyboard.Flags & 2) != 0));
            return string.Join(",", releases.Select(e => e.Data.Keyboard.VirtualKey));
        }
    }

    [Test]
    public static void NativeLayoutMappingRoundTripsProductLabelsWithCapsLockOnAndOff()
    {
        if (!OperatingSystem.IsWindows()) return;
        var layout = GetKeyboardLayout(0);
        foreach (var caps in new[] { false, true })
        foreach (var value in "OEPS-AB12abcd0123456789")
        {
            var mapped = WindowsActionKeyboard.ResolveLayoutKey(value, layout, caps)
                ?? throw new Exception($"No normal key mapping for {value}, Caps Lock {caps}.");
            var state = new byte[256];
            if (caps) state[0x14] = 1;
            if (mapped.Shift) state[0x10] = state[0xA0] = 0x80;
            var output = new char[8];
            Assert.Equal(1, ToUnicodeEx(mapped.VirtualKey, mapped.ScanCode, state, output, output.Length, 4, layout));
            Assert.Equal(value, output[0]);
        }
        Assert.True(WindowsActionKeyboard.ResolveLayoutKey('\uD83D', layout, false) is null);
        Assert.True(WindowsActionKeyboard.ResolveLayoutKey('\t', layout, false) is null);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint thread);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int ToUnicodeEx(uint key, uint scan, byte[] state, [Out] char[] output,
        int length, uint flags, IntPtr layout);

    [Test]
    public static void KeyboardNativeLayoutMatchesWindowsInputUnion()
    {
        Assert.Equal(IntPtr.Size == 8 ? 40 : 28, Marshal.SizeOf<WindowsActionKeyboard.NativeInput>());
        Assert.Equal(IntPtr.Size == 8 ? 8L : 4L,
            Marshal.OffsetOf<WindowsActionKeyboard.NativeInput>(nameof(WindowsActionKeyboard.NativeInput.Data)).ToInt64());
        Assert.Equal(IntPtr.Size == 8 ? 24 : 16, Marshal.SizeOf<WindowsActionKeyboard.KeyboardInput>());
    }

    [Test]
    public static void KeyboardTextPreservesUnicodeAndSendKeysMetacharactersLiterally()
    {
        const string text = "OEPS +^%{}[]~() café 中文 😀";
        var inputs = WindowsActionKeyboard.EncodeText(text).SelectMany(batch => batch).ToArray();
        Assert.Equal(text.Length * 2, inputs.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var down = inputs[2 * i];
            var up = inputs[2 * i + 1];
            Assert.Equal(1U, down.Type);
            Assert.Equal((ushort)0, down.Data.Keyboard.VirtualKey);
            Assert.Equal((ushort)text[i], down.Data.Keyboard.ScanCode);
            Assert.Equal(4U, down.Data.Keyboard.Flags);
            Assert.Equal((ushort)text[i], up.Data.Keyboard.ScanCode);
            Assert.Equal(6U, up.Data.Keyboard.Flags);
        }
    }

    [Test]
    public static void KeyboardCrLfIsOneEnterAndTabsAreKeys()
    {
        var inputs = WindowsActionKeyboard.EncodeText("a\r\nb\nc\rd\t").SelectMany(batch => batch).ToArray();
        var downs = inputs.Where((_, index) => index % 2 == 0).ToArray();
        Assert.Equal(8, downs.Length);
        Assert.Equal(3, downs.Count(input => input.Data.Keyboard.VirtualKey == 0x0D));
        Assert.Equal(1, downs.Count(input => input.Data.Keyboard.VirtualKey == 0x09));
        Assert.Equal("abcd", new string(downs.Where(input => input.Data.Keyboard.Flags == 4)
            .Select(input => (char)input.Data.Keyboard.ScanCode).ToArray()));
    }

    [Test]
    public static void KeyboardBatchesDoNotSplitSurrogatesOrCrLf()
    {
        var batches = WindowsActionKeyboard.EncodeText(new string('a', 31) + "😀z").ToArray();
        Assert.Equal(2, batches.Length);
        Assert.Equal(66, batches[0].Length);
        Assert.Equal((ushort)'\uD83D', batches[0][62].Data.Keyboard.ScanCode);
        Assert.Equal((ushort)'\uDE00', batches[0][64].Data.Keyboard.ScanCode);
        Assert.Equal((ushort)'z', batches[1][0].Data.Keyboard.ScanCode);
        var lineBatches = WindowsActionKeyboard.EncodeText(new string('a', 31) + "\r\nz").ToArray();
        Assert.Equal(64, lineBatches[0].Length);
        Assert.Equal((ushort)0x0D, lineBatches[0][62].Data.Keyboard.VirtualKey);
        Assert.Equal((ushort)'z', lineBatches[1][0].Data.Keyboard.ScanCode);
        Assert.Equal(0, WindowsActionKeyboard.EncodeText("").Count());
    }

    [Test]
    public static void KeyboardArrowKeysHaveExtendedFlagAndEveryKeyReleases()
    {
        foreach (var (key, code, extended) in new[]
        {
            (ActionKey.Tab, 0x09, false), (ActionKey.Enter, 0x0D, false),
            (ActionKey.Up, 0x26, true), (ActionKey.Right, 0x27, true)
        })
        {
            var inputs = WindowsActionKeyboard.EncodeKey(key);
            Assert.Equal(2, inputs.Length);
            Assert.Equal((ushort)code, inputs[0].Data.Keyboard.VirtualKey);
            Assert.Equal(extended ? 1U : 0U, inputs[0].Data.Keyboard.Flags);
            Assert.Equal(extended ? 3U : 2U, inputs[1].Data.Keyboard.Flags);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowsActionKeyboard.EncodeKey((ActionKey)999));
    }

    [Test]
    public static async Task KeyboardCancelledRequestsNeverReachNativeInput()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var keyboard = new WindowsActionKeyboard();
        await Assert.ThrowsAsync<OperationCanceledException>(() => keyboard.TypeTextAsync("hello", cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => keyboard.PressKeyAsync(ActionKey.Enter, cancellation.Token));
    }
}
