// Namespace must match the generated PInvoke class to extend it.
// ReSharper disable once CheckNamespace
namespace Windows.Win32;

using global::System.Runtime.InteropServices;
using Foundation;
using UI.WindowsAndMessaging;

internal partial class PInvoke
{
    public static bool Guard(BOOL result, string? methodName = null)
    {
        if (result == true) return true;

        var errorCode = Marshal.GetLastWin32Error();
        var errorMessage = Marshal.GetLastPInvokeErrorMessage();
        global::System.Diagnostics.Debug.WriteLine(
            $"[{methodName ?? "PInvoke"}] failed — code {errorCode}: {errorMessage}");
        return false;
    }

    public static string? GetClassName(HWND hWnd)
    {
        if (hWnd == HWND.Null) return null;
        Span<char> buffer = stackalloc char[256];
        var length = GetClassName(hWnd, buffer);
        return SpanToString(buffer, length);
    }

    public static string? GetWindowText(HWND hWnd)
    {
        if (hWnd == HWND.Null) return null;
        var length = GetWindowTextLength(hWnd) + 1;
        Span<char> buffer = stackalloc char[length];
        GetWindowText(hWnd, buffer);
        return SpanToString(buffer, length)?.Trim('\0');
    }

    public static bool IsTopLevelWindow(HWND hWnd)
    {
        if (hWnd == HWND.Null) return false;
        return GetAncestor(hWnd, GET_ANCESTOR_FLAGS.GA_ROOT) == hWnd;
    }

    public static string? GetWindowProcessName(HWND hWnd)
    {
        _ = GetWindowThreadProcessId(hWnd, out var processId);

        using var handle = OpenProcess_SafeHandle(
            global::Windows.Win32.System.Threading.PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_INFORMATION,
            false,
            processId);

        if (handle.IsInvalid) return null;

        Span<char> buffer = stackalloc char[1024];
        var length = GetModuleFileNameEx(handle, null, buffer);
        return SpanToString(buffer, length);
    }

    private static string? SpanToString(Span<char> buffer, uint length)
    {
        if (length == 0 || buffer.IsEmpty) return null;
        var safeLength = (int)Math.Min((uint)buffer.Length, length);
        return new string(buffer[..safeLength]);
    }

    private static string? SpanToString(Span<char> buffer, int length)
    {
        if (length == 0 || buffer.IsEmpty) return null;
        var safeLength = Math.Min(buffer.Length, length);
        return new string(buffer[..safeLength]);
    }
}
