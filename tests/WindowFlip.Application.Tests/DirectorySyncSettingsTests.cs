using WindowFlip.Platform.Windows;
using Xunit;

namespace WindowFlip.Application.Tests;

public sealed class DirectorySyncSettingsTests
{
    [Fact]
    public void SettingsDefaultEnabledAndPersistOptOut()
    {
        string directory = Path.Combine(Path.GetTempPath(), "WindowFlip.SettingsTest." + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        try
        {
            JsonDirectorySyncSettings settings = new(path);
            Assert.True(settings.LoadEnabled());
            Assert.True(settings.SaveEnabled(false));
            Assert.False(new JsonDirectorySyncSettings(path).LoadEnabled());
            Assert.True(settings.SaveEnabled(true));
            Assert.True(settings.LoadEnabled());
            File.WriteAllText(path, "invalid json");
            Assert.True(settings.LoadEnabled());
            File.WriteAllText(path, "{}");
            Assert.True(settings.LoadEnabled());
        }
        finally
        {
            File.Delete(path);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }

    [Fact]
    public void WriteFailureIsReportedWithoutThrowing()
    {
        string path = Path.GetTempFileName();
        try
        {
            JsonDirectorySyncSettings settings = new(Path.Combine(path, "settings.json"));
            Assert.False(settings.SaveEnabled(false));
        }
        finally { File.Delete(path); }
    }
}
