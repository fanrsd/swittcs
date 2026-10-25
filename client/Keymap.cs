namespace Swittcs;

// Specific groups fall back to their parent when a profile doesn't ship them: see Keymap.Parent.
public enum KeyGroup { Alpha, Space, Enter, Backspace, Modifier, Tab, Arrow, Number, Escape, Function, CapsLock, Shift, Control, Option, Command }

public readonly record struct KeyPosition(float Pan, KeyGroup Group);

/// <summary>Virtual-key → stereo position + sound group, derived from a US ANSI layout.</summary>
public static class Keymap
{
    // Physical rows, left to right. A key's pan is its position within its row, -0.95 (left) .. +0.95 (right).
    static readonly int[][] Rows =
    [
        [0x1B, 0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x7B],                 // Esc F1–F12
        [0xC0, '1', '2', '3', '4', '5', '6', '7', '8', '9', '0', 0xBD, 0xBB, 0x08],                      // ` 1–0 - = Backspace
        [0x09, 'Q', 'W', 'E', 'R', 'T', 'Y', 'U', 'I', 'O', 'P', 0xDB, 0xDD, 0xDC],                      // Tab Q–P [ ] \
        [0x14, 'A', 'S', 'D', 'F', 'G', 'H', 'J', 'K', 'L', 0xBA, 0xDE, 0x0D],                           // Caps A–L ; ' Enter
        [0xA0, 'Z', 'X', 'C', 'V', 'B', 'N', 'M', 0xBC, 0xBE, 0xBF, 0xA1],                               // LShift Z–/ RShift
        [0xA2, 0x5B, 0xA4, 0x20, 0xA5, 0x5C, 0x5D, 0xA3],                                                // Ctrl Win Alt Space Alt Win Menu Ctrl
    ];

    // Ins Home PgUp Del End PgDn ← ↑ → ↓ sit right of the main block.
    static readonly int[] RightCluster = [0x2D, 0x24, 0x21, 0x2E, 0x23, 0x22, 0x25, 0x26, 0x27, 0x28];

    static readonly Dictionary<int, KeyPosition> Map = Build();

    public static KeyPosition Lookup(int vk) => Map.TryGetValue(vk, out var p) ? p : new(0f, KeyGroup.Alpha);

    static Dictionary<int, KeyPosition> Build()
    {
        var map = new Dictionary<int, KeyPosition>();
        foreach (var row in Rows)
            for (int i = 0; i < row.Length; i++)
                map[row[i]] = new(-0.95f + 1.9f * i / (row.Length - 1), GroupOf(row[i]));
        foreach (int vk in RightCluster)
            map[vk] = new(0.95f, GroupOf(vk));
        return map;
    }

    /// <summary>Next group to try when a profile has no sound for <paramref name="g"/>; null once at Alpha.</summary>
    public static KeyGroup? Parent(KeyGroup g) => g switch
    {
        KeyGroup.Alpha => null,
        KeyGroup.Number => KeyGroup.Alpha,
        KeyGroup.Escape or KeyGroup.Function or KeyGroup.CapsLock or KeyGroup.Shift
            or KeyGroup.Control or KeyGroup.Option or KeyGroup.Command => KeyGroup.Modifier,
        _ => KeyGroup.Alpha,
    };

    static KeyGroup GroupOf(int vk) => vk switch
    {
        0x20 => KeyGroup.Space,
        0x0D => KeyGroup.Enter,
        0x08 => KeyGroup.Backspace,
        0x09 => KeyGroup.Tab,
        0x1B => KeyGroup.Escape,
        0x14 => KeyGroup.CapsLock,
        0xA0 or 0xA1 => KeyGroup.Shift,
        0xA2 or 0xA3 => KeyGroup.Control,
        0xA4 or 0xA5 => KeyGroup.Option,   // Alt
        0x5B or 0x5C => KeyGroup.Command,  // Win
        >= 0x25 and <= 0x28 => KeyGroup.Arrow,
        >= 0x30 and <= 0x39 => KeyGroup.Number,
        >= 0x41 and <= 0x5A => KeyGroup.Alpha,  // letters
        >= 0x70 and <= 0x87 => KeyGroup.Function, // F1–F24
        >= 0xBA and <= 0xDE => KeyGroup.Alpha,  // OEM punctuation
        _ => KeyGroup.Modifier,
    };
}
