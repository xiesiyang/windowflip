namespace WindowFlip.Core.Switching;

public sealed class WindowSwitchSession
{
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(2.5);

    private readonly TimeSpan duration;
    private readonly List<nint> sessionOrder = [];
    private string? sessionApplicationKey;
    private DateTimeOffset lastSelectionTime;

    public WindowSwitchSession(TimeSpan? duration = null)
    {
        this.duration = duration ?? DefaultDuration;
        if (this.duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }
    }

    public SwitchSelectionResult Select(
        string applicationKey,
        nint foregroundHandle,
        IReadOnlyList<WindowDescriptor> windows,
        SwitchDirection direction,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationKey);
        ArgumentNullException.ThrowIfNull(windows);

        if (direction is not SwitchDirection.Next and not SwitchDirection.Previous)
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        if (windows.Count == 0)
        {
            return new SwitchSelectionResult(SwitchSelectionStatus.NoWindows, 0, []);
        }

        if (windows.Count == 1)
        {
            return new SwitchSelectionResult(SwitchSelectionStatus.OnlyOneWindow, 0, windows.ToArray());
        }

        bool continueSession = string.Equals(
            sessionApplicationKey,
            applicationKey,
            StringComparison.OrdinalIgnoreCase)
            && now - lastSelectionTime <= duration;

        if (!continueSession)
        {
            sessionOrder.Clear();
            sessionOrder.AddRange(windows.Select(window => window.Handle));
            sessionApplicationKey = applicationKey;
        }
        else
        {
            HashSet<nint> available = windows.Select(window => window.Handle).ToHashSet();
            sessionOrder.RemoveAll(handle => !available.Contains(handle));

            foreach (WindowDescriptor window in windows)
            {
                if (!sessionOrder.Contains(window.Handle))
                {
                    sessionOrder.Add(window.Handle);
                }
            }
        }

        int currentIndex = sessionOrder.IndexOf(foregroundHandle);
        if (currentIndex < 0)
        {
            currentIndex = direction == SwitchDirection.Next ? sessionOrder.Count - 1 : 0;
        }

        int offset = direction == SwitchDirection.Next ? 1 : -1;
        int targetIndex = WrapIndex(currentIndex + offset, sessionOrder.Count);
        nint targetHandle = sessionOrder[targetIndex];
        lastSelectionTime = now;

        Dictionary<nint, WindowDescriptor> byHandle = windows.ToDictionary(window => window.Handle);
        WindowDescriptor[] ordered = sessionOrder
            .Where(byHandle.ContainsKey)
            .Select(handle => byHandle[handle])
            .ToArray();

        return new SwitchSelectionResult(SwitchSelectionStatus.Selected, targetHandle, ordered);
    }

    public static int WrapIndex(int index, int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        int wrapped = index % count;
        return wrapped < 0 ? wrapped + count : wrapped;
    }
}
