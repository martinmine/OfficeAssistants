using System.Runtime.InteropServices;
using OfficeAssistant.Core.Platform;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;
using Windows.Win32.System.Ole;

namespace OfficeAssistant.Platform.Windows;

/// <summary>
/// The Windows clipboard, as Unicode text. Setting it needs an owner window (with a null
/// owner SetClipboardData fails), so the platform passes the balloon's window.
/// </summary>
public sealed unsafe class Win32Clipboard(Func<nint> owner) : IClipboard
{
    public string? GetText()
    {
        if (!PInvoke.OpenClipboard((HWND)owner()))
        {
            return null;
        }

        try
        {
            var handle = (HGLOBAL)(void*)PInvoke.GetClipboardData((uint)CLIPBOARD_FORMAT.CF_UNICODETEXT);
            var pointer = handle.IsNull ? null : PInvoke.GlobalLock(handle);
            if (pointer is null)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni((nint)pointer);
            }
            finally
            {
                PInvoke.GlobalUnlock(handle);
            }
        }
        finally
        {
            PInvoke.CloseClipboard();
        }
    }

    public void SetText(string text)
    {
        if (!PInvoke.OpenClipboard((HWND)owner()))
        {
            return;
        }

        try
        {
            PInvoke.EmptyClipboard();
            var handle = PInvoke.GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE, (nuint)((text.Length + 1) * sizeof(char)));
            var pointer = (char*)PInvoke.GlobalLock(handle);
            text.CopyTo(new Span<char>(pointer, text.Length));
            pointer[text.Length] = '\0';
            PInvoke.GlobalUnlock(handle);

            // On success the clipboard owns the memory.
            if (PInvoke.SetClipboardData((uint)CLIPBOARD_FORMAT.CF_UNICODETEXT, (HANDLE)(void*)handle).IsNull)
            {
                PInvoke.GlobalFree(handle);
            }
        }
        finally
        {
            PInvoke.CloseClipboard();
        }
    }
}
