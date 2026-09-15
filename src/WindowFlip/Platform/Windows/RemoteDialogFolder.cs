using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using WindowFlip.Platform.Windows.Interop;

namespace WindowFlip.Platform.Windows;

// CDM_GETFOLDERPATH is above WM_USER: Windows does not marshal its buffer between processes.
internal sealed class RemoteDialogFolder : IDisposable
{
    private const int Capacity = 32768;
    private static readonly List<RemoteDialogFolder> Quarantined = [];
    private readonly uint processId;
    private readonly SafeProcessHandle process;
    private readonly nint buffer;
    private bool inFlight;
    private bool disposed;

    public RemoteDialogFolder(uint processId)
    {
        this.processId = processId;
        lock (Quarantined)
        {
            for (int i = Quarantined.Count - 1; i >= 0; i--)
            {
                RemoteDialogFolder old = Quarantined[i];
                if (WaitForSingleObject(old.process, 0) == 0)
                {
                    old.process.Dispose();
                    Quarantined.RemoveAt(i);
                }
            }
            if (Quarantined.Any(old => old.processId == processId))
                throw new InvalidOperationException("A folder query is still pending in this process.");
        }
        process = OpenProcess(0x100000 | 0x8 | 0x10, false, processId);
        if (!process.IsInvalid) buffer = VirtualAllocEx(process, 0, Capacity * 2, 0x3000, 4);
    }

    public string? Read(nint dialog)
    {
        if (disposed || buffer == 0 || inFlight) return null;
        NativeMethods.GetWindowThreadProcessId(dialog, out uint currentProcess);
        if (currentProcess != processId) return null;
        inFlight = true;
        if (NativeMethods.SendMessageTimeout(dialog, 0x466, Capacity, buffer, 2 | 0x20, 200, out nuint count) == 0)
            return null;
        inFlight = false;
        if (count == 0 || count >= Capacity) return null;
        byte[] bytes = new byte[(int)count * 2];
        if (!ReadProcessMemory(process, buffer, bytes, (nuint)bytes.Length, out nuint read) || read != (nuint)bytes.Length)
            return null;
        string path = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        return Path.IsPathFullyQualified(path) ? path : null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (inFlight)
        {
            // After a timeout the receiver may still use the buffer. Keep one per hung process,
            // refuse further queries there, and let Windows reclaim it when that process exits.
            lock (Quarantined) Quarantined.Add(this);
            return;
        }
        if (buffer != 0) VirtualFreeEx(process, buffer, 0, 0x8000);
        process.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint VirtualAllocEx(SafeProcessHandle process, nint address, nuint size, uint type, uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFreeEx(SafeProcessHandle process, nint address, nuint size, uint type);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(SafeProcessHandle process, nint address, byte[] bytes, nuint size, out nuint read);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
}
