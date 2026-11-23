using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using RoyalTerminal.Terminal;

namespace Codirva;

internal static class WindowsSessions
{
    public static readonly WslTarget Target = new("local", "Windows", IsWindows: true);
    private const byte Ping = 0;
    private const byte Resize = 1;
    private const byte Submit = 2;
    private const byte AgentTreeSubmitSupported = 3;
    // ConPTY Win32 key records. Ctrl+J inserts a newline in Codex without submitting.
    private const string NewLineKey = "\u001b[74;36;10;1;8;1_\u001b[74;36;0;0;0;1_";
    private const string EnterKey = "\u001b[13;28;13;1;0;1_\u001b[13;28;0;0;0;1_";
    private static string Shell => Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    private static string PipeName(string id) => "Codirva.ConPTY." + id;
    private static string MutexName(string id) => @"Local\" + PipeName(id);
    public static bool IsSessionId(string id) => id.Length == 64 && id.All(char.IsAsciiHexDigit);
    private static string Id(SavedSession session) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(ConnectionConfig.PathName + "\0"
            + (session.Target.IsWindowsCodex ? "codex\0" : "") + session.FullName)));

    private static bool IsRunning(string id)
    {
        if (!Mutex.TryOpenExisting(MutexName(id), out var mutex))
            return false;
        mutex.Dispose();
        return true;
    }

    public static SavedSession NormalizeSavedSession(SavedSession session) =>
        session.Target.IsWindowsCodex && !IsRunning(Id(session)) ? session with { Target = Target } : session;

    public static IReadOnlyList<TmuxProject> ListProjects(IEnumerable<SavedSession> saved) =>
        saved.Where(session => session.Target.IsWindows && IsRunning(Id(session)))
            .Select(session =>
            {
                var parts = session.FullName.Split('/', 2);
                return new TmuxSession(parts[1], session.FullName, parts[0], session.Target, Id(session));
            })
            .GroupBy(session => session.ProjectName)
            .Select(group => new TmuxProject(group.Key, group.ToArray())).ToArray();

    public static async Task StartAsync(SavedSession session)
    {
        var id = Id(session);
        if (!IsRunning(id))
        {
            if (session.Target.IsWindowsCodex)
                throw new InvalidOperationException("Refresh the project list to restart this as a Windows PowerShell session.");
            if (!File.Exists(Shell))
                throw new InvalidOperationException($"Windows PowerShell was not found at {Shell}.");
            using var process = Process.Start(
                StartInfo(PrepareHostFiles(), "--windows-session-host", id));
        }
        await ControlAsync(id, Guid.Empty, Ping);
    }

    public static TerminalCommandSpec RelayCommand(string id, Guid client, TerminalSessionDimensions dimensions)
    {
        var info = StartInfo(typeof(Program).Assembly.Location, "--windows-session-relay", id, client.ToString("N"),
            dimensions.Columns.ToString(System.Globalization.CultureInfo.InvariantCulture),
            dimensions.Rows.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return new TerminalCommandSpec(info.FileName, info.ArgumentList.ToArray());
    }

    private static string PrepareHostFiles()
    {
        var assembly = typeof(Program).Assembly.Location;
        var source = Path.GetDirectoryName(assembly)!;
        using var stream = File.OpenRead(assembly);
        var version = Convert.ToHexString(SHA256.HashData(stream));
        var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Codirva", "hosts");
        var destination = Path.Combine(cache, version);
        if (!Directory.Exists(destination))
        {
            // Keep the background host out of the build directory so open sessions do not lock a rebuild.
            var staging = Path.Combine(cache, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                {
                    var copy = Path.Combine(staging, Path.GetRelativePath(source, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                    File.Copy(file, copy);
                }
                try { Directory.Move(staging, destination); }
                catch (IOException) when (Directory.Exists(destination)) { }
            }
            finally
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
            }
        }
        return Path.Combine(destination, Path.GetFileName(assembly));
    }

    private static ProcessStartInfo StartInfo(string assembly, params string[] args)
    {
        var executable = Path.ChangeExtension(assembly, ".exe");
        var appHost = File.Exists(executable);
        var info = new ProcessStartInfo(appHost ? executable : "dotnet.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (!appHost)
            info.ArgumentList.Add(assembly);
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        return info;
    }

    public static Task ResizeAsync(string id, Guid client, int cols, int rows) =>
        ControlAsync(id, client, Resize, writer => { writer.Write(cols); writer.Write(rows); });

    public static async Task SubmitAsync(string id, Guid client, string prompt, bool legacyCodexSession)
    {
        if (prompt.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
            throw new InvalidOperationException("The prompt contains unsupported control characters.");
        var text = Encoding.UTF8.GetBytes(prompt.ReplaceLineEndings("\n"));
        if (text.Length > 1024 * 1024)
            throw new InvalidOperationException("The prompt is too large for the Windows session.");
        if (!legacyCodexSession)
        {
            await ControlAsync(id, client, AgentTreeSubmitSupported, rejectedMessage:
                "This session is using an older host. Exit the agent and PowerShell, then right-click the session name and choose Start.")
                .ConfigureAwait(false);
        }
        await ControlAsync(id, client, Submit, writer => { writer.Write(text.Length); writer.Write(text); })
            .ConfigureAwait(false);
    }

    private static async Task ControlAsync(string id, Guid client, byte operation, Action<BinaryWriter>? payload = null,
        string? rejectedMessage = null)
    {
        using var pipe = new NamedPipeClientStream(".", PipeName(id) + ".control", PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            using var request = new MemoryStream();
            using var writer = new BinaryWriter(request);
            writer.Write(operation);
            writer.Write(client.ToByteArray());
            payload?.Invoke(writer);
            await pipe.WriteAsync(request.GetBuffer().AsMemory(0, (int)request.Length), timeout.Token).ConfigureAwait(false);
            var response = new byte[1];
            await pipe.ReadExactlyAsync(response, timeout.Token).ConfigureAwait(false);
            if (response[0] != 1)
                throw new InvalidOperationException(rejectedMessage ??
                    "The Windows session is disconnected or does not have exactly one supported agent process.");
        }
        catch (OperationCanceledException)
        {
            throw new IOException("The Windows session host did not respond.");
        }
    }

    public static async Task<int> RelayAsync(string id, Guid client, int columns, int rows)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName(id), PipeDirection.InOut, PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await pipe.ConnectAsync(timeout.Token);
            await pipe.WriteAsync(client.ToByteArray(), timeout.Token);
            var accepted = new byte[1];
            await pipe.ReadExactlyAsync(accepted, timeout.Token);
            if (accepted[0] != 1)
                return 1;
            await ResizeAsync(id, client, columns, rows);
            var input = Console.OpenStandardInput().CopyToAsync(pipe);
            var output = pipe.CopyToAsync(Console.OpenStandardOutput());
            await await Task.WhenAny(input, output);
            return 0;
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or InvalidOperationException)
        {
            return 1;
        }
    }

    public static async Task<int> HostAsync(string id)
    {
        using var mutex = new Mutex(false, MutexName(id), out var created);
        if (!created)
            return 0;
        using var pty = new DefaultPtyFactory().Create();
        using var lifetime = new CancellationTokenSource();
        var host = new SessionHost(id, pty, lifetime.Cancel);
        pty.DataReceived += host.ReceiveOutput;
        pty.ProcessExited += _ => lifetime.Cancel();
        try
        {
            await Task.WhenAll(host.AcceptClientsAsync(lifetime.Token), host.HandleControlAsync(lifetime.Token));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        return 0;
    }

    private sealed class SessionHost(string id, IPty pty, Action stop)
    {
        private readonly object _gate = new();
        private readonly object _inputGate = new();
        private readonly Queue<byte[]> _history = new();
        private int _historyBytes;
        private Channel<byte[]>? _output;
        private Guid _client;
        private readonly WindowsConsoleSize _consoleSize = new((columns, rows) =>
            pty.Start(Shell, columns, rows, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                arguments: ["-NoLogo"]), pty.Resize);

        public void ReceiveOutput(byte[] bytes, int length)
        {
            var chunk = bytes.AsSpan(0, length).ToArray();
            lock (_gate)
            {
                _history.Enqueue(chunk);
                _historyBytes += length;
                while (_historyBytes > 1024 * 1024)
                    _historyBytes -= _history.Dequeue().Length;
                if (_output is { } output && !output.Writer.TryWrite(chunk))
                    output.Writer.TryComplete(new IOException("Terminal client is too slow."));
            }
        }

        public async Task AcceptClientsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var pipe = Server(PipeName(id));
                await pipe.WaitForConnectionAsync(cancellationToken);
                using var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var client = new byte[16];
                try
                {
                    await pipe.ReadExactlyAsync(client, connection.Token);
                    var queue = Channel.CreateBounded<byte[]>(256);
                    byte[][] history;
                    lock (_gate)
                    {
                        _client = new Guid(client);
                        history = _history.ToArray();
                        _output = queue;
                    }
                    await pipe.WriteAsync(new byte[] { 1 }, connection.Token);
                    var input = ReadInputAsync(pipe, connection.Token);
                    var output = WriteOutputAsync(pipe, history, queue.Reader, connection.Token);
                    await await Task.WhenAny(input, output);
                    connection.Cancel();
                    try { await Task.WhenAll(input, output); }
                    catch (OperationCanceledException) when (connection.IsCancellationRequested) { }
                }
                catch (IOException)
                {
                    // A detached client leaves PowerShell running.
                }
                finally
                {
                    connection.Cancel();
                    lock (_gate)
                    {
                        _client = Guid.Empty;
                        _output = null;
                    }
                }
            }
        }

        private async Task ReadInputAsync(Stream pipe, CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            int count;
            while ((count = await pipe.ReadAsync(buffer, cancellationToken)) != 0)
            {
                lock (_inputGate)
                    pty.Write(buffer, 0, count);
            }
        }

        private static async Task WriteOutputAsync(Stream pipe, byte[][] history, ChannelReader<byte[]> output, CancellationToken cancellationToken)
        {
            foreach (var chunk in history)
                await pipe.WriteAsync(chunk, cancellationToken);
            await foreach (var chunk in output.ReadAllAsync(cancellationToken))
                await pipe.WriteAsync(chunk, cancellationToken);
        }

        public async Task HandleControlAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var pipe = Server(PipeName(id) + ".control");
                await pipe.WaitForConnectionAsync(cancellationToken);
                try
                {
                    var header = new byte[17];
                    await pipe.ReadExactlyAsync(header, cancellationToken);
                    var operation = header[0];
                    var client = new Guid(header.AsSpan(1));
                    var accepted = operation is Ping or AgentTreeSubmitSupported
                        || await ApplyControlAsync(pipe, operation, client, cancellationToken);
                    await pipe.WriteAsync(new byte[] { accepted ? (byte)1 : (byte)0 }, cancellationToken);
                }
                catch (IOException)
                {
                    // The client can disappear during a request.
                }
            }
        }

        private async Task<bool> ApplyControlAsync(Stream pipe, byte operation, Guid client, CancellationToken cancellationToken)
        {
            if (operation == Resize)
            {
                var size = new byte[8];
                await pipe.ReadExactlyAsync(size, cancellationToken);
                lock (_inputGate)
                {
                    if (!IsActiveClient(client))
                        return false;
                    try
                    {
                        _consoleSize.Apply(BitConverter.ToInt32(size), BitConverter.ToInt32(size, 4));
                    }
                    catch (Exception error) when (error is InvalidOperationException or IOException
                        or System.ComponentModel.Win32Exception)
                    {
                        Trace.TraceWarning("Could not size Windows terminal: {0}", error.Message);
                        stop();
                        return false;
                    }
                    return true;
                }
            }
            if (operation != Submit)
                return false;

            var length = new byte[4];
            await pipe.ReadExactlyAsync(length, cancellationToken);
            var count = BitConverter.ToInt32(length);
            if (count is < 1 or > 1024 * 1024)
                return false;
            var text = new byte[count];
            await pipe.ReadExactlyAsync(text, cancellationToken);
            var paste = new StringBuilder();
            var lines = Encoding.UTF8.GetString(text).Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                if (index > 0)
                    paste.Append(NewLineKey);
                if (lines[index].Length > 0)
                    paste.Append("\u001b[200~").Append(lines[index]).Append("\u001b[201~");
            }
            uint? targetPid;
            lock (_inputGate)
            {
                targetPid = GetAgentPid();
                if (targetPid is null || !IsActiveClient(client))
                    return false;
                pty.Write(paste.ToString());
            }

            // Codex handles the paste asynchronously before it can accept Enter.
            await Task.Delay(250, cancellationToken);
            lock (_inputGate)
            {
                if (!IsActiveClient(client) || GetAgentPid() != targetPid)
                    return false;
                pty.Write(EnterKey);
                return true;
            }
        }

        private uint? GetAgentPid()
        {
            if (!pty.IsRunning)
                return null;

            return WindowsProcesses.FindSingleAgent(pty.ChildPid);
        }

        private bool IsActiveClient(Guid client)
        {
            lock (_gate)
                return client != Guid.Empty && _client == client;
        }

        private static NamedPipeServerStream Server(string name) => new(name, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    }
}
