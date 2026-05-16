namespace PulseNetHotkeyService;

using System;
using System.Runtime.InteropServices;

/// <summary>
/// Raw Win32 imports for the keyboard hook + message loop. Kept hand-written
/// rather than via CsWin32 since the surface is small enough that pulling in
/// the source generator and a NativeMethods.txt would be heavier than this file.
/// </summary>
internal static class NativeMethods
{
    public const int  WH_KEYBOARD_LL = 13;
    public const int  WM_KEYDOWN     = 0x0100;
    public const int  WM_KEYUP       = 0x0101;
    public const int  WM_SYSKEYDOWN  = 0x0104;
    public const int  WM_SYSKEYUP    = 0x0105;
    public const uint WM_QUIT        = 0x0012;
    public const uint ASFW_ANY       = 0xFFFFFFFF;

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint    vkCode;
        public uint    scanCode;
        public uint    flags;
        public uint    time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint   message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint   time;
        public int    pt_x;
        public int    pt_y;
    }

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(in MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(in MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern bool AllowSetForegroundWindow(uint dwProcessId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);
}
