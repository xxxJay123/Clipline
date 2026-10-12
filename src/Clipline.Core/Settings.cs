using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clipline.Core;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

/// <summary>A global shortcut: modifiers plus a Windows virtual-key code.</summary>
public sealed record Hotkey(HotkeyModifiers Modifiers, int Key)
{
    /// <summary>Ctrl + Alt + T, the same keys Tendedero uses on the Mac (Control, Option, T).</summary>
    public static Hotkey Default { get; } = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'T');

    /// <summary>A shortcut needs a modifier, or it would swallow plain typing.</summary>
    [JsonIgnore]
    public bool IsValid => Modifiers != HotkeyModifiers.None && Modifiers != HotkeyModifiers.Shift && KeyName(Key) != null;

    public string Display
    {
        get
        {
            var parts = new List<string>();
            if (Modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
            if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
            parts.Add(KeyName(Key) ?? "?");
            return string.Join(" + ", parts);
        }
    }

    /// <summary>The label of a virtual key, or null for keys a shortcut cannot end with.</summary>
    public static string? KeyName(int vk) => vk switch
    {
        >= 'A' and <= 'Z' => ((char)vk).ToString(),
        >= '0' and <= '9' => ((char)vk).ToString(),
        >= 0x70 and <= 0x87 => "F" + (vk - 0x6F),
        >= 0x60 and <= 0x69 => "Num " + (vk - 0x60),
        0x20 => "Space",
        0x0D => "Enter",
        0x09 => "Tab",
        0x21 => "Page Up",
        0x22 => "Page Down",
        0x23 => "End",
        0x24 => "Home",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        0x2D => "Insert",
        0x2E => "Delete",
        0x2C => "Print Screen",
        0x13 => "Pause",
        0xBA => ";",
        0xBB => "=",
        0xBC => ",",
        0xBD => "-",
        0xBE => ".",
        0xBF => "/",
        0xC0 => "`",
        0xDB => "[",
        0xDC => "\\",
        0xDD => "]",
        0xDE => "'",
        _ => null,
    };
}

/// <summary>Everything the user can change, all of it from the tray menu.</summary>
public sealed class Settings
{
    public static readonly int[] KeepChoices = [25, 50, 100];
    public const int MaxExtraFolders = 3;

    public LineSize Size { get; set; } = LineSize.Medium;

    /// <summary>How many photos the line keeps; 0 for as many as fit across the screen.</summary>
    public int KeepOnLine { get; set; }

    /// <summary>Resting the pointer against the top edge brings the line down.</summary>
    public bool RevealAtTopEdge { get; set; } = true;

    /// <summary>Something new hanging shows itself for a moment.</summary>
    public bool PeekAtNew { get; set; } = true;

    /// <summary>Whether the line also comes down over full screen apps.</summary>
    public bool OverFullScreen { get; set; }

    /// <summary>Images copied to the clipboard hang too. Off until turned on.</summary>
    public bool HangCopiedImages { get; set; }

    /// <summary>
    /// Discarding a screenshot from a watched folder also sends the file to
    /// the Recycle Bin, so the Screenshots folder only keeps what you keep.
    /// </summary>
    public bool RecycleDiscarded { get; set; }

    public bool Sounds { get; set; } = true;

    /// <summary>The show and hide shortcut; null for none.</summary>
    public Hotkey? Shortcut { get; set; } = Hotkey.Default;

    /// <summary>Folders chosen in Watch folders, besides the Windows capture folders.</summary>
    public List<string> ExtraFolders { get; set; } = [];

    /// <summary>Windows capture folders the user stopped watching.</summary>
    public List<string> MutedFolders { get; set; } = [];

    public bool Welcomed { get; set; }

    [JsonIgnore]
    public int? Keep => KeepChoices.Contains(KeepOnLine) ? KeepOnLine : null;
}

/// <summary>What hangs on the line, kept between launches.</summary>
public sealed class LineState
{
    public List<string> Hanging { get; set; } = [];
}

/// <summary>Small JSON files, written so that a crash never leaves half a file behind.</summary>
public static class JsonStore
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static T Load<T>(string path) where T : new()
    {
        try
        {
            if (File.Exists(path)) return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? new T();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // A damaged file starts over from the defaults rather than stopping the app.
        }
        return new T();
    }

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, overwrite: true);
    }
}
