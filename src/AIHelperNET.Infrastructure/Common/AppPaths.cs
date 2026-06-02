using System.IO;

namespace AIHelperNET.Infrastructure.Common;

public static class AppPaths
{
    private static readonly string Base =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIHelperNET");

    public static string DatabaseFile  => Path.Combine(Base, "sessions.db");
    public static string SettingsFile  => Path.Combine(Base, "settings.json");
    public static string LogDirectory  => Path.Combine(Base, "logs");
    public static string LogFile       => Path.Combine(LogDirectory, "log-.txt");
    public static string ModelsDir     => Path.Combine(Base, "models");

    public static void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(Base);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(ModelsDir);
    }
}
