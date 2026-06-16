using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Aoe.Alpha;

/// <summary>
/// Global low-level keyboard hook (WH_KEYBOARD_LL) that reports clean key-down / key-up for a
/// single virtual-key. Must be installed on a thread that pumps Windows messages (the UI thread).
/// </summary>
public sealed class PushToTalkHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int HC_ACTION = 0;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private readonly int _targetVk;
    private readonly LowLevelKeyboardProc _proc; // kept alive to prevent GC of the callback
    private IntPtr _hook = IntPtr.Zero;
    private bool _isDown;

    /// <summary>Raised once when the key transitions up-&gt;down (auto-repeat is suppressed).</summary>
    public event Action? Pressed;

    /// <summary>Raised when the key is released.</summary>
    public event Action? Released;

    public PushToTalkHook(int targetVk)
    {
        _targetVk = targetVk;
        _proc = HookCallback;
    }

    public void Install()
    {
        using Process curProcess = Process.GetCurrentProcess();
        using ProcessModule curModule = curProcess.MainModule!;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException($"Failed to install keyboard hook (Win32 error {Marshal.GetLastWin32Error()}).");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == HC_ACTION)
        {
            int msg = (int)wParam;
            int vk = Marshal.ReadInt32(lParam); // first field of KBDLLHOOKSTRUCT is vkCode
            if (vk == _targetVk)
            {
                if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
                {
                    if (!_isDown)
                    {
                        _isDown = true;
                        Pressed?.Invoke();
                    }
                }
                else if (msg is WM_KEYUP or WM_SYSKEYUP)
                {
                    if (_isDown)
                    {
                        _isDown = false;
                        Released?.Invoke();
                    }
                }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
}
