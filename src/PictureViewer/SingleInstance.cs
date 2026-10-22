using System.IO.Pipes;
using System.Text;

namespace PictureViewer;

/// <summary>
/// Only one copy runs (it owns the tray icon and the capture hot key). A second launch hands its
/// file argument to the first and exits.
/// </summary>
public static class SingleInstance
{
    const string MutexName = @"Local\PictureViewer.SingleInstance";
    const string PipeName = "PictureViewer.SingleInstance";

    static Mutex? _mutex;

    public static bool TryBecomePrimary()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool created);
        return created;
    }

    /// <summary>Calls <paramref name="onMessage"/> (on a worker thread) with the path another launch sent, or "" to just show the window.</summary>
    public static void StartServer(Action<string> onMessage)
    {
        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync();
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    onMessage(await reader.ReadToEndAsync());
                }
                catch (IOException)
                {
                    await Task.Delay(500); // a client dropped mid-message; listen again
                }
            }
        });
    }

    public static void SendToPrimary(string path)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client, Encoding.UTF8);
            writer.Write(path);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            // The first copy is shutting down; nothing useful left to do.
        }
    }
}
