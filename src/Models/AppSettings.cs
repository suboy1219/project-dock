using System.Text.Encodings.Web;
using System.Text.Json;

namespace ProjectDock.Models;

public sealed class AppSettings
{
    public const string DefaultHotkey = "Alt+F2";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string Hotkey { get; set; } = DefaultHotkey;

    public static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ProjectDock",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(StorePath), Json);
                if (loaded is not null && !string.IsNullOrWhiteSpace(loaded.Hotkey))
                {
                    return loaded;
                }
            }
        }
        catch
        {
            // 配置损坏时回退到默认值
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var temp = StorePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
            File.Move(temp, StorePath, true);
        }
        catch
        {
            // 写失败不影响运行
        }
    }
}
