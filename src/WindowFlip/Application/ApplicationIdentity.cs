namespace WindowFlip.Application;

internal sealed record ApplicationIdentity(
    string Key,
    string ProcessName,
    string? ExecutablePath,
    string DisplayName);
