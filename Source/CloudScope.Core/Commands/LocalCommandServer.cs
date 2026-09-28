using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace CloudScope.Commands;

/// <summary>Loopback-only, token-authenticated bridge into the application's command runtime.</summary>
public sealed class LocalCommandServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Func<string?, CancellationToken, Task<object>> _dispatch;
    public string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public int Port { get; }
    public string DiscoveryFile { get; }

    public LocalCommandServer(int port, Func<string?, CancellationToken, Task<object>> dispatch)
    {
        if (port < 1024 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        Port = port; _dispatch = dispatch;
        DiscoveryFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CloudScope", "instances", $"{Environment.ProcessId}.json");
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        try
        {
            _listener.Start();
            Directory.CreateDirectory(Path.GetDirectoryName(DiscoveryFile)!);
            var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                if (File.Exists(DiscoveryFile)) File.SetUnixFileMode(DiscoveryFile, options.UnixCreateMode.Value);
            }
            using (var file = new FileStream(DiscoveryFile, options))
                JsonSerializer.Serialize(file, new { pid = Environment.ProcessId, port, token = Token });
            _ = Run();
        }
        catch { _listener.Close(); _stop.Dispose(); throw; }
    }

    private async Task Run()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                HttpListenerContext context = await _listener.GetContextAsync();
                // Serial requests also serialize interactive prompt/answer conversations.
                await Handle(context);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException && _stop.IsCancellationRequested) { break; }
        }
    }
    private async Task Handle(HttpListenerContext context)
    {
        try
        {
            if (context.Request.Headers["Authorization"] != "Bearer " + Token)
            { context.Response.StatusCode = 401; await Reply(new { error = "Authentication required." }); return; }
            string path = context.Request.Url!.AbsolutePath;
            if (context.Request.HttpMethod == "GET" && path == "/health") { await Reply(new { ready = true, pid = Environment.ProcessId }); return; }
            string? command = null;
            if (context.Request.HttpMethod == "POST" && path == "/exec")
            {
                if (context.Request.ContentLength64 is < 1 or > 65536)
                { context.Response.StatusCode = 400; await Reply(new { error = "Request body must be 1..65536 bytes." }); return; }
                using var json = await JsonDocument.ParseAsync(context.Request.InputStream, cancellationToken: _stop.Token);
                command = json.RootElement.GetProperty("command").GetString();
                if (command == null) throw new JsonException("command must be a string.");
            }
            else if (context.Request.HttpMethod != "GET" || path != "/status")
            { context.Response.StatusCode = 404; await Reply(new { error = "Use GET /health, GET /status or POST /exec." }); return; }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            object result = await _dispatch(command, timeout.Token).WaitAsync(timeout.Token);
            await Reply(result);
        }
        catch (Exception ex)
        {
            try
            {
                context.Response.StatusCode = ex is OperationCanceledException ? 503 : 400;
                await Reply(new { error = ex.Message });
            }
            catch (Exception) { /* The API OFF command may already have closed this response. */ }
        }
        finally { try { context.Response.Close(); } catch (ObjectDisposedException) { } }

        async Task Reply(object value)
        {
            context.Response.ContentType = "application/json";
            await JsonSerializer.SerializeAsync(context.Response.OutputStream, value);
        }
    }
    public void Dispose()
    {
        _stop.Cancel(); _listener.Close();
        if (File.Exists(DiscoveryFile)) File.Delete(DiscoveryFile);
    }
}
