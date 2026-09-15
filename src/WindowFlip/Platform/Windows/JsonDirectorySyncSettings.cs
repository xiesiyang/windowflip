using System.Text.Json;
using WindowFlip.Application;

namespace WindowFlip.Platform.Windows;

internal sealed class JsonDirectorySyncSettings(string? filePath = null) : IDirectorySyncSettings
{
    private readonly string path = filePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowFlip", "settings.json");

    public bool LoadEnabled()
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path))?.DirectorySyncEnabled ?? true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return true;
        }
    }

    public bool SaveEnabled(bool enabled)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Settings(enabled)));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed record Settings(bool DirectorySyncEnabled = true);
}
