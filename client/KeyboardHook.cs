using System.Runtime.InteropServices;

namespace Swittcs;

/// <summary>Global WH_KEYBOARD_LL hook. Needs a message loop on the installing thread (WinForms provides it).</summary>
public sealed class KeyboardHook : IDisposable
{
    delegate nint HookProc(int nCode, nint wParam, nint lParam);

    readonly HookProc _proc;              // held so the GC can't collect the delegate Windows calls into
    readonly HashSet<int> _down = [];     // filters auto-repeat: one Down per physical press
    nint _hook;

    /// <summary>(virtualKey, isDown). Raised on the thread pool, never on the hook thread.</summary>
    public event Action<int, bool>? Key;

    public KeyboardHook() => _proc = Callback;

    public void Install()
    {
        _hook = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, _proc, GetModuleHandle(null), 0);
        if (_hook == 0) throw new InvalidOperationException($"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
    }

    public void Dispose()
    {
        if (_hook != 0) UnhookWindowsHookEx(_hook);
        _hook = 0;
    }

    nint Callback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            int vk = Marshal.ReadInt32(lParam); // KBDLLHOOKSTRUCT.vkCode is the first field
            bool? down = (int)wParam switch
            {
                0x100 or 0x104 => _down.Add(vk) ? true : null,     // WM_KEYDOWN / WM_SYSKEYDOWN
                0x101 or 0x105 => _down.Remove(vk) ? false : null, // WM_KEYUP / WM_SYSKEYUP
                _ => null,
            };
            // Windows silently unhooks callbacks that are slow, so the real work runs off-thread.
            if (down is bool d && Key is { } handler)
                ThreadPool.UnsafeQueueUserWorkItem(static s => s.handler(s.vk, s.d), (handler, vk, d), preferLocal: false);
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern nint SetWindowsHookEx(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern nint GetModuleHandle(string? lpModuleName);
}
