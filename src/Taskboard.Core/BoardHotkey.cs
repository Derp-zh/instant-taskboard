namespace Taskboard.Core;

public sealed class BoardHotkey
{
    public bool Enabled { get; set; } = true;
    public uint Modifiers { get; set; } = 2; // Control; independent of IME characters.
    public uint VirtualKey { get; set; } = 0xC0;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => (Modifiers & ~15u) == 0 && (!Enabled ||
        (Modifiers != 0 && VirtualKey is >= 8 and <= 254 && VirtualKey is not (16 or 17 or 18 or 91 or 92 or 123 or 160 or 161 or 162 or 163 or 164 or 165 or 229)));
}
