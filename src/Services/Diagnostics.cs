namespace ProjectDock.Services;

internal static class Diagnostics
{
    private static readonly bool Enabled =
        Environment.GetEnvironmentVariable("PROJECTDOCK_DEBUG") == "1";

    public static void Log(string message)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "pd-diag.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
        }
        catch
        {
            // 诊断日志失败时忽略
        }
    }
}
