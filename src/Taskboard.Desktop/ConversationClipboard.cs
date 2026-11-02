using System;
using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Taskboard.Desktop;

// Materialize before copying: an OLE proxy to the previous clipboard can become
// invalid after its owner is replaced, especially for multiple formats.
internal sealed class ConversationClipboard
{
    private readonly DataObject snapshot = new();
    private readonly string[] formats;
    internal uint Sequence { get; }
    internal ConversationClipboard()
    {
        Sequence = GetClipboardSequenceNumber();
        var source = Clipboard.GetDataObject(); formats = source?.GetFormats(false) ?? [];
        foreach (var format in formats)
        {
            var value = source!.GetData(format, false) ?? throw new InvalidOperationException("无法完整缓存剪贴板格式");
            object copy = value switch
            {
                string text => text,
                byte[] bytes => bytes.ToArray(),
                string[] files => files.ToArray(),
                StringCollection files => CopyFiles(files),
                MemoryStream stream => new MemoryStream(stream.ToArray()),
                Image image => image.Clone(),
                int or uint or long or ulong or short or ushort or byte or sbyte or bool or char or double or float or decimal => value,
                _ => throw new InvalidOperationException("当前剪贴板格式不能完整缓存")
            };
            snapshot.SetData(format, false, copy);
        }
        if (Sequence != GetClipboardSequenceNumber()) throw new InvalidOperationException("剪贴板正在变化");
    }
    private static StringCollection CopyFiles(StringCollection source)
    { var result = new StringCollection(); result.AddRange(source.Cast<string>().ToArray()); return result; }
    internal bool Restore(uint expectedSequence)
    {
        if (GetClipboardSequenceNumber() != expectedSequence) return false;
        if (formats.Length == 0) Clipboard.Clear(); else Clipboard.SetDataObject(snapshot, true, 10, 100);
        var restored = Clipboard.GetDataObject();
        if (formats.Any(format => restored?.GetDataPresent(format, false) != true)) return false;
        foreach (var format in formats)
            if (snapshot.GetData(format, false) is string text && !Equals(restored!.GetData(format, false), text)) return false;
        return true;
    }
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
}
