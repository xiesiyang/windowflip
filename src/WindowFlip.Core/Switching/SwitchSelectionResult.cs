namespace WindowFlip.Core.Switching;

public sealed record SwitchSelectionResult(
    SwitchSelectionStatus Status,
    nint SelectedHandle,
    IReadOnlyList<WindowDescriptor> OrderedWindows);
