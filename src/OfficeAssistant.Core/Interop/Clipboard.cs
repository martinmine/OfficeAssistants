using System.Runtime.InteropServices;

namespace OfficeAssistant.Core.Interop;

/// <summary>Plain-text clipboard access for the balloon's text box.</summary>
internal static class Clipboard
{
    public static string? GetText(nint owner)
    {
        if (!Native.OpenClipboard(owner))
        {
            return null;
        }

        try
        {
            var handle = Native.GetClipboardData(Native.CF_UNICODETEXT);
            var pointer = handle == 0 ? 0 : Native.GlobalLock(handle);
            if (pointer == 0)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                Native.GlobalUnlock(handle);
            }
        }
        finally
        {
            Native.CloseClipboard();
        }
    }

    public static void SetText(nint owner, string text)
    {
        if (!Native.OpenClipboard(owner))
        {
            return;
        }

        try
        {
            Native.EmptyClipboard();
            var bytes = (text.Length + 1) * 2;
            var handle = Native.GlobalAlloc(Native.GMEM_MOVEABLE, (nuint)bytes);
            var pointer = Native.GlobalLock(handle);
            Marshal.Copy(text.ToCharArray(), 0, pointer, text.Length);
            Marshal.WriteInt16(pointer, text.Length * 2, 0);
            Native.GlobalUnlock(handle);

            // On success the clipboard owns the memory.
            if (Native.SetClipboardData(Native.CF_UNICODETEXT, handle) == 0)
            {
                Native.GlobalFree(handle);
            }
        }
        finally
        {
            Native.CloseClipboard();
        }
    }
}
