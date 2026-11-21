using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Codirva;

internal static class WindowsProcesses
{
    private static readonly HashSet<string> AgentExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "codex.exe", "claude.exe", "pi.exe", "dsh.exe",
    };

    public static uint? FindSingleAgent(int shellPid)
    {
        using var snapshot = CreateToolhelp32Snapshot(2, 0);
        var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
        if (snapshot.IsInvalid || !Process32First(snapshot, ref entry))
            return null;

        var processes = new List<(uint Id, uint ParentId, string Name)>();
        do
        {
            processes.Add((entry.Id, entry.ParentId, entry.Name));
        } while (Process32Next(snapshot, ref entry));
        return FindSingleAgent((uint)shellPid, processes);
    }

    internal static uint? FindSingleAgent(uint shellPid, IEnumerable<(uint Id, uint ParentId, string Name)> processes)
    {
        var children = processes.ToLookup(process => process.ParentId);

        var pending = new Queue<uint>();
        var visited = new HashSet<uint>();
        uint? agent = null;
        pending.Enqueue(shellPid);
        while (pending.TryDequeue(out var parent))
        {
            if (!visited.Add(parent))
                continue;
            foreach (var child in children[parent])
            {
                if (AgentExecutables.Contains(child.Name))
                {
                    if (agent is not null)
                        return null;
                    agent = child.Id;
                }
                pending.Enqueue(child.Id);
            }
        }
        return agent;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, Id;
        public nuint DefaultHeapId;
        public uint ModuleId, Threads, ParentId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string Name;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true)]
    private static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true)]
    private static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry entry);
}
