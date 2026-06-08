using System.IO;

namespace AIHelperNET.Infrastructure.Common;

public static class AppPaths
{
    private static readonly string Base = ResolveBase();

    public static string DatabaseFile => Path.Combine(Base, "sessions.db");
    public static string SettingsFile => Path.Combine(Base, "settings.json");
    public static string LogDirectory => Path.Combine(Base, "logs");
    public static string LogFile      => Path.Combine(LogDirectory, "log-.txt");
    public static string ModelsDir       => Path.Combine(Base, "models");
    public static string DiagnosticsDir  => Path.Combine(Base, "diagnostics");

    public static void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(Base);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(ModelsDir);
        Directory.CreateDirectory(DiagnosticsDir);
        MigrateFromLegacy();
    }

    // Prefer D:\AIHelperNET; fall back to %LocalAppData%\AIHelperNET if D is unavailable.
    private static string ResolveBase()
    {
        try
        {
            if (new DriveInfo("D").IsReady)
                return @"D:\AIHelperNET";
        }
        catch { }
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIHelperNET");
    }

    // Copy settings + model files from the old %LocalAppData% location the first time
    // the app runs from the D-drive path.
    private static void MigrateFromLegacy()
    {
        var legacy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIHelperNET");

        if (legacy == Base) return; // already on the legacy path, nothing to do

        // settings.json — preserves backend, device IDs, answer settings, etc.
        var legacySettings = Path.Combine(legacy, "settings.json");
        if (File.Exists(legacySettings) && !File.Exists(SettingsFile))
            File.Copy(legacySettings, SettingsFile);

        // model .bin files — large, only copy if not already present on D
        var legacyModels = Path.Combine(legacy, "models");
        if (Directory.Exists(legacyModels))
        {
            foreach (var src in Directory.GetFiles(legacyModels, "*.bin"))
            {
                var dst = Path.Combine(ModelsDir, Path.GetFileName(src));
                if (!File.Exists(dst))
                    File.Copy(src, dst);
            }
        }
    }
}
