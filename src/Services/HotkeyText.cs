namespace ProjectDock.Services;

public static class HotkeyText
{
    private static readonly Dictionary<string, uint> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Esc"] = 0x1B,
        ["Escape"] = 0x1B,
        ["Space"] = 0x20,
        ["Tab"] = 0x09,
        ["Enter"] = 0x0D,
        ["Return"] = 0x0D,
        ["Backspace"] = 0x08,
        ["Delete"] = 0x2E,
        ["Insert"] = 0x2D,
        ["Home"] = 0x24,
        ["End"] = 0x23,
        ["PgUp"] = 0x21,
        ["PageUp"] = 0x21,
        ["PgDn"] = 0x22,
        ["PageDown"] = 0x22,
        ["Left"] = 0x25,
        ["Up"] = 0x26,
        ["Right"] = 0x27,
        ["Down"] = 0x28,
        ["`"] = 0xC0,
        ["-"] = 0xBD,
        ["="] = 0xBB,
        ["["] = 0xDB,
        ["]"] = 0xDD,
        ["\\"] = 0xDC,
        [";"] = 0xBA,
        ["'"] = 0xDE,
        [","] = 0xBC,
        ["."] = 0xBE,
        ["/"] = 0xBF,
    };

    public static bool TryParse(string? text, out uint modifiers, out uint key, out string? error)
    {
        modifiers = 0;
        key = 0;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "快捷键不能为空";
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            error = "至少需要一个修饰键（Ctrl / Alt / Shift / Win）";
            return false;
        }

        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= NativeMethods.MOD_CONTROL;
                    break;
                case "alt":
                    modifiers |= NativeMethods.MOD_ALT;
                    break;
                case "shift":
                    modifiers |= NativeMethods.MOD_SHIFT;
                    break;
                case "win":
                case "windows":
                    modifiers |= NativeMethods.MOD_WIN;
                    break;
                default:
                    error = $"无法识别的修饰键：{parts[i]}";
                    return false;
            }
        }

        if (modifiers == 0)
        {
            error = "至少需要一个修饰键（Ctrl / Alt / Shift / Win）";
            return false;
        }

        if (!TryParseKey(parts[^1], out key))
        {
            error = $"无法识别的按键：{parts[^1]}";
            return false;
        }

        return true;
    }

    public static string Format(uint modifiers, uint key)
    {
        var parts = new List<string>(4);

        if ((modifiers & NativeMethods.MOD_CONTROL) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((modifiers & NativeMethods.MOD_ALT) != 0)
        {
            parts.Add("Alt");
        }

        if ((modifiers & NativeMethods.MOD_SHIFT) != 0)
        {
            parts.Add("Shift");
        }

        if ((modifiers & NativeMethods.MOD_WIN) != 0)
        {
            parts.Add("Win");
        }

        parts.Add(KeyName(key));
        return string.Join("+", parts);
    }

    private static bool TryParseKey(string token, out uint key)
    {
        key = 0;
        token = token.Trim();

        if (token.Length == 0)
        {
            return false;
        }

        if (token.Length == 1)
        {
            var c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                key = c;
                return true;
            }
        }

        if (token.Length is 2 or 3 &&
            char.ToUpperInvariant(token[0]) == 'F' &&
            int.TryParse(token[1..], out var number) &&
            number is >= 1 and <= 24)
        {
            key = (uint)(0x70 + number - 1);
            return true;
        }

        if (Aliases.TryGetValue(token, out var alias))
        {
            key = alias;
            return true;
        }

        return false;
    }

    private static string KeyName(uint key)
    {
        if (key is >= 'A' and <= 'Z' or >= '0' and <= '9')
        {
            return ((char)key).ToString();
        }

        if (key is >= 0x70 and <= 0x87)
        {
            return "F" + (key - 0x70 + 1);
        }

        return key switch
        {
            0x1B => "Esc",
            0x20 => "Space",
            0x09 => "Tab",
            0x0D => "Enter",
            0x08 => "Backspace",
            0x2E => "Delete",
            0x2D => "Insert",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PgUp",
            0x22 => "PgDn",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0xC0 => "`",
            0xBD => "-",
            0xBB => "=",
            0xDB => "[",
            0xDD => "]",
            0xDC => "\\",
            0xBA => ";",
            0xDE => "'",
            0xBC => ",",
            0xBE => ".",
            0xBF => "/",
            _ => "0x" + key.ToString("X2"),
        };
    }
}
