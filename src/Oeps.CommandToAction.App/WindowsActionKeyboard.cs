using System.ComponentModel;
using System.Runtime.InteropServices;
using Oeps.CommandToAction.Core.Commands;

namespace Oeps.CommandToAction.App;

/// <summary>Sends layout-aware keystrokes, with Unicode fallback, without activating a window.</summary>
internal sealed class WindowsActionKeyboard : IActionKeyboard
{
    private const uint KeyUp = 0x0002;
    private const uint Unicode = 0x0004;
    private const uint ExtendedKey = 0x0001;
    private const int CharactersPerBatch = 32;
    private bool _waitingForTarget;

    // Raised on the queue worker, so subscribers must dispatch UI changes themselves.
    public event Action<bool>? WaitingForTargetChanged;

    public async Task TypeTextAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        for (var index = 0; index < text.Length;)
        {
            await WaitForTargetAsync(cancellationToken).ConfigureAwait(false);
            var thread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
            var layout = GetKeyboardLayout(thread);
            var capsLock = (GetKeyState(0x14) & 1) != 0;
            var length = index + 1 < text.Length &&
                ((text[index] == '\r' && text[index + 1] == '\n') ||
                 (char.IsHighSurrogate(text[index]) && char.IsLowSurrogate(text[index + 1]))) ? 2 : 1;
            var inputs = EncodeText(text.Substring(index, length), value => ResolveLayoutKey(value, layout, capsLock))
                .SelectMany(batch => batch).ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            SendChecked(inputs);
            index += length;
            // Give browser grids a short interval to enter edit mode after the first key.
            await Task.Delay(5, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task PressKeyAsync(ActionKey key, CancellationToken cancellationToken)
    {
        var inputs = EncodeKey(key);
        await WaitForTargetAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        SendChecked(inputs);
    }

    private async Task WaitForTargetAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var foreground = GetForegroundWindow();
                var thread = GetWindowThreadProcessId(foreground, out var processId);
                // This includes the main window, configuration and information dialogs.
                // Waiting for modifiers also avoids turning Tab into Alt+Tab or Shift+Tab.
                var ready = foreground != IntPtr.Zero && thread != 0 && processId != (uint)Environment.ProcessId
                    && !IsDown(0x10) && !IsDown(0x11) && !IsDown(0x12) && !IsDown(0x5B) && !IsDown(0x5C);
                SetWaiting(!ready);
                if (ready) return;
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            SetWaiting(false);
            throw;
        }
    }

    private static bool IsDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private void SetWaiting(bool waiting)
    {
        if (_waitingForTarget == waiting) return;
        _waitingForTarget = waiting;
        WaitingForTargetChanged?.Invoke(waiting);
    }

    // Pure encoding functions are also used by tests; those tests never call SendInput.
    internal readonly record struct LayoutKey(ushort VirtualKey, ushort ScanCode, bool Shift);

    internal static IEnumerable<NativeInput[]> EncodeText(string text, Func<char, LayoutKey?>? mapKey = null)
    {
        for (var index = 0; index < text.Length;)
        {
            var inputs = new List<NativeInput>(CharactersPerBatch * 2 + 2);
            var characters = 0;
            while (index < text.Length && characters++ < CharactersPerBatch)
            {
                var value = text[index++];
                if (value == '\r' || value == '\n')
                {
                    if (value == '\r' && index < text.Length && text[index] == '\n') index++;
                    inputs.AddRange(EncodeKey(ActionKey.Enter));
                }
                else if (value == '\t') inputs.AddRange(EncodeKey(ActionKey.Tab));
                else
                {
                    var mapped = !char.IsControl(value) && !char.IsSurrogate(value) ? mapKey?.Invoke(value) : null;
                    if (mapped is { } key)
                    {
                        if (key.Shift) inputs.Add(CreateInput(0x10, 0x2A, 0));
                        inputs.Add(CreateInput(key.VirtualKey, key.ScanCode, 0));
                        inputs.Add(CreateInput(key.VirtualKey, key.ScanCode, KeyUp));
                        if (key.Shift) inputs.Add(CreateInput(0x10, 0x2A, KeyUp));
                    }
                    else AddUnicode(inputs, value);
                    // Keep surrogate pairs in one SendInput call, including at a batch boundary.
                    if (char.IsHighSurrogate(value) && index < text.Length && char.IsLowSurrogate(text[index]))
                        AddUnicode(inputs, text[index++]);
                }
            }
            yield return inputs.ToArray();
        }
    }

    // Resolve against the destination window's layout, not the queue worker's layout.
    // Round-trip validation rejects dead keys and accounts for Caps Lock without toggling it.
    internal static LayoutKey? ResolveLayoutKey(char value, IntPtr layout, bool capsLock)
    {
        if (char.IsControl(value) || char.IsSurrogate(value)) return null;
        var mapping = VkKeyScanEx(value, layout);
        if (mapping == -1) return null;
        var modifiers = (mapping >> 8) & 0xFF;
        // Ctrl/Alt mappings could trigger app shortcuts. Preserve those characters as Unicode.
        if ((modifiers & ~1) != 0) return null;
        var virtualKey = (ushort)(mapping & 0xFF);
        var scan = MapVirtualKeyEx(virtualKey, 0, layout);
        if (scan == 0 || scan > 0xFF) return null;
        var requestedShift = (modifiers & 1) != 0;
        foreach (var shift in new[] { requestedShift, !requestedShift })
        {
            var state = new byte[256];
            if (shift) state[0x10] = state[0xA0] = 0x80;
            if (capsLock) state[0x14] = 1;
            var translated = new char[8];
            // Flag 4 (Windows 10+) queries without modifying the keyboard's dead-key buffer.
            if (ToUnicodeEx(virtualKey, scan, state, translated, translated.Length, 4, layout) == 1
                && translated[0] == value)
                return new(virtualKey, (ushort)scan, shift);
        }
        return null;
    }

    private static void AddUnicode(List<NativeInput> inputs, char value)
    {
        inputs.Add(CreateInput(0, value, Unicode));
        inputs.Add(CreateInput(0, value, Unicode | KeyUp));
    }

    internal static NativeInput[] EncodeKey(ActionKey key)
    {
        var virtualKey = key switch
        {
            ActionKey.Tab => 0x09,
            ActionKey.Enter => 0x0D,
            ActionKey.Up => 0x26,
            ActionKey.Right => 0x27,
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        };
        var flags = key is ActionKey.Up or ActionKey.Right ? ExtendedKey : 0;
        return [CreateInput((ushort)virtualKey, 0, flags), CreateInput((ushort)virtualKey, 0, flags | KeyUp)];
    }

    private static NativeInput CreateInput(ushort virtualKey, ushort scanCode, uint flags) => new()
    {
        Type = 1,
        Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = virtualKey, ScanCode = scanCode, Flags = flags } }
    };

    private static void SendChecked(NativeInput[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
        if (sent == inputs.Length) return;
        var error = Marshal.GetLastWin32Error();
        // A partial shifted character can leave both Shift and its character down.
        // Release only accepted downs in reverse order, never replay any text.
        var releases = ReleasesForAcceptedPrefix(inputs, sent);
        if (releases.Length > 0) SendInput((uint)releases.Length, releases, Marshal.SizeOf<NativeInput>());
        throw new InvalidOperationException(
            $"Windows accepted {sent} of {inputs.Length} keyboard events. Check the target application's permissions. The action was not retried.",
            error == 0 ? null : new Win32Exception(error));
    }

    internal static NativeInput[] ReleasesForAcceptedPrefix(NativeInput[] inputs, uint accepted)
    {
        var down = new List<NativeInput>();
        foreach (var input in inputs.Take((int)Math.Min(accepted, (uint)inputs.Length)))
        {
            var key = input.Data.Keyboard;
            if ((key.Flags & KeyUp) == 0) down.Add(input);
            else
            {
                var index = down.FindLastIndex(item => item.Data.Keyboard.VirtualKey == key.VirtualKey
                    && item.Data.Keyboard.ScanCode == key.ScanCode && item.Data.Keyboard.Flags == (key.Flags & ~KeyUp));
                if (index >= 0) down.RemoveAt(index);
            }
        }
        down.Reverse();
        return down.Select(input => { input.Data.Keyboard.Flags |= KeyUp; return input; }).ToArray();
    }

    // INPUT's union must include MOUSEINPUT even though we only emit keyboard input:
    // that largest member determines the native size (40 bytes on x64, 28 on x86).
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeInput
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HardwareInput
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [In] NativeInput[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint threadId);

    [DllImport("user32.dll", EntryPoint = "VkKeyScanExW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern short VkKeyScanEx(char character, IntPtr layout);

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyExW", ExactSpelling = true)]
    private static extern uint MapVirtualKeyEx(uint code, uint mapType, IntPtr layout);

    [DllImport("user32.dll", EntryPoint = "ToUnicodeEx", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint virtualKey, uint scanCode, byte[] state,
        [Out] char[] buffer, int bufferLength, uint flags, IntPtr layout);
}
