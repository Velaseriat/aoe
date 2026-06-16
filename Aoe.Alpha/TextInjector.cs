using System.Runtime.InteropServices;

namespace Aoe.Alpha;

/// <summary>
/// Injects text into the currently focused window, either by clipboard paste (Ctrl+V) or by
/// synthesizing Unicode keystrokes. All public methods must be called on an STA (UI) thread.
/// </summary>
public static class TextInjector
{
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;

    public static void PasteViaClipboard(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        string? previous = null;
        try { if (Clipboard.ContainsText()) previous = Clipboard.GetText(); }
        catch { /* clipboard busy */ }

        try
        {
            Clipboard.SetText(text);
            SendCtrlV();
        }
        finally
        {
            // Restore the prior clipboard shortly after the paste is consumed.
            if (previous is not null)
            {
                var prev = previous;
                _ = Task.Delay(400).ContinueWith(_ =>
                {
                    try { Clipboard.SetText(prev); } catch { }
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
        }
    }

    public static void TypeUnicode(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var inputs = new List<INPUT>(text.Length * 2);
        foreach (char c in text)
        {
            inputs.Add(MakeUnicode(c, keyUp: false));
            inputs.Add(MakeUnicode(c, keyUp: true));
        }
        SendInputs(inputs.ToArray());
    }

    private static void SendCtrlV()
    {
        var inputs = new[]
        {
            MakeVirtual(VK_CONTROL, keyUp: false),
            MakeVirtual(VK_V, keyUp: false),
            MakeVirtual(VK_V, keyUp: true),
            MakeVirtual(VK_CONTROL, keyUp: true),
        };
        SendInputs(inputs);
    }

    private static void SendInputs(INPUT[] inputs)
    {
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
            throw new InvalidOperationException($"SendInput sent {sent}/{inputs.Length} (Win32 error {Marshal.GetLastWin32Error()}).");
    }

    private static INPUT MakeUnicode(char c, bool keyUp) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = c,
                dwFlags = KEYEVENTF_UNICODE | (keyUp ? KEYEVENTF_KEYUP : 0),
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            },
        },
    };

    private static INPUT MakeVirtual(ushort vk, bool keyUp) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = 0,
                dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            },
        },
    };

    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion u;
    }

    // The union must include the largest member (MOUSEINPUT) so Marshal.SizeOf<INPUT> matches
    // the real Win32 INPUT size (40 bytes on x64). A short cbSize makes SendInput fail with 87.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}
