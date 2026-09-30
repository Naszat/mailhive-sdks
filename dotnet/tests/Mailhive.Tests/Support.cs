using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mailhive.Tests;

/// <summary>The shared specification in the repository's spec/ folder.</summary>
internal static class Spec
{
    public static string RepoRoot { get; } = FindRoot();

    public static JsonElement Load(string name) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "spec", name))).RootElement.Clone();

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "spec", "conformance.json")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Couldn't find spec/conformance.json above " + AppContext.BaseDirectory);
    }
}

/// <summary>The shared mock API (mock-server/server.mjs), on a free port, for the whole test class.</summary>
public sealed class MockServerFixture : IDisposable
{
    private readonly Process _process;
    private readonly HttpClient _http = new();

    public MockServerFixture()
    {
        var start = new ProcessStartInfo("node")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(Path.Combine(Spec.RepoRoot, "mock-server", "server.mjs"));
        start.ArgumentList.Add("--port");
        start.ArgumentList.Add("0");
        _process = Process.Start(start) ?? throw new InvalidOperationException("Couldn't start node.");
        var line = _process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("The mock server printed nothing.");
        Url = line.Trim().Split(' ').Last();
    }

    /// <summary>The server's root URL, without /v1.</summary>
    public string Url { get; }

    public async Task ResetAsync() => (await _http.PostAsync(Url + "/__reset", null)).EnsureSuccessStatusCode();

    public async Task<JsonElement[]> RequestsAsync()
    {
        var text = await _http.GetStringAsync(Url + "/__requests");
        return JsonDocument.Parse(text).RootElement.Clone().EnumerateArray().ToArray();
    }

    public void Dispose()
    {
        _http.Dispose();
        try
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(5000);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        _process.Dispose();
    }
}

/// <summary>An HttpMessageHandler that answers from a function and records every request.</summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

    public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) => _respond = respond;

    public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : this((request, _) => Task.FromResult(respond(request))) { }

    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        return await _respond(request, cancellationToken);
    }

    public static HttpResponseMessage Json(int status, object body, params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        foreach (var (name, value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }
        return response;
    }
}
