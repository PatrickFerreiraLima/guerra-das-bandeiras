using System.Runtime.InteropServices;

namespace GuerraDasBandeiras.Core;

/// <summary>Copia texto para a área de transferência do Windows (para colar o código da sala numa conversa).</summary>
public static class Clipboard
{
    [DllImport("user32.dll", SetLastError = true)] static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll", SetLastError = true)] static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetClipboardData(uint format, IntPtr mem);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr GlobalLock(IntPtr mem);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GlobalUnlock(IntPtr mem);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr GlobalFree(IntPtr mem);
    const uint CF_UNICODETEXT = 13, GMEM_MOVEABLE = 2;

    public static bool SetText(string text)
    {
        if (!OperatingSystem.IsWindows()) return false;
        for (int attempt = 0; attempt < 5; attempt++)   // outro programa pode estar usando a área de transferência
        {
            if (OpenClipboard(IntPtr.Zero)) break;
            if (attempt == 4) return false;
            Thread.Sleep(20);
        }
        try
        {
            EmptyClipboard();
            var bytes = (text.Length + 1) * 2;
            var mem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
            if (mem == IntPtr.Zero) return false;
            var p = GlobalLock(mem);
            Marshal.Copy(text.ToCharArray(), 0, p, text.Length);
            Marshal.WriteInt16(p, text.Length * 2, 0);
            GlobalUnlock(mem);
            if (SetClipboardData(CF_UNICODETEXT, mem) == IntPtr.Zero) { GlobalFree(mem); return false; }
            return true;
        }
        finally { CloseClipboard(); }
    }
}
