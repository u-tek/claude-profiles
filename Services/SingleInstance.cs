using System.IO;
using System.IO.Pipes;

namespace ClaudeProfiles.Services;

/// <summary>One launcher per user; later launches (e.g. desktop shortcuts) pass their arguments to it.</summary>
public sealed class SingleInstance : IDisposable
{
    const string Name = "ClaudeProfiles-3f9c2b";
    readonly Mutex _mutex;
    readonly CancellationTokenSource _cts = new();

    public bool IsFirst { get; }

    public SingleInstance()
    {
        _mutex = new Mutex(true, @"Local\" + Name, out var created);
        IsFirst = created;
    }

    public static bool Send(string message)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", Name, PipeDirection.Out);
            client.Connect(3000);
            using var w = new StreamWriter(client) { AutoFlush = true };
            w.WriteLine(message);
            return true;
        }
        catch { return false; }
    }

    public void Listen(Action<string> onMessage)
    {
        Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(Name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(_cts.Token);
                    using var r = new StreamReader(server);
                    var line = await r.ReadLineAsync();
                    if (!string.IsNullOrWhiteSpace(line)) onMessage(line.Trim());
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Log.Write("pipe: " + ex.Message); await Task.Delay(500); }
            }
        });
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (IsFirst) { try { _mutex.ReleaseMutex(); } catch { } }
        _mutex.Dispose();
    }
}
