using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Configuration["OpenRouter:ApiKey"] = "local-test-placeholder";
var handler = new FakeRouter();
builder.Services.AddHttpClient("OpenRouter").ConfigurePrimaryHttpMessageHandler(() => handler);
await using var app = builder.Build();
var clock = new TestClock();
Sample[] samples = [];
app.MapPcChecks(() => samples, clock);
await app.StartAsync();
using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(app.Urls.Single()) };
try
{
    var check = await Start();
    await Expect(await client.PostAsJsonAsync($"/api/checks/{check}/preview",
        new { question = "Testfrage", model = "openai/gpt-5-mini" }), HttpStatusCode.BadRequest);
    await Expect(await Preview(check), HttpStatusCode.Conflict);
    clock.Advance(61);
    await Expect(await Preview(check), HttpStatusCode.Conflict);
    var started = clock.GetUtcNow().AddSeconds(-61);
    samples = Enumerable.Range(0, 12).Select(index => new Sample(started.AddSeconds(1 + index * 5),
        new SystemMeasurement(20 + index, 86, 50, [new ProcessReading(987654, "PRIVATE-PROCESS-XYZ", 10, 500)]))).ToArray();
    var previewResponse = await Preview(check);
    await Expect(previewResponse, HttpStatusCode.OK);
    using var preview = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
    string expectedBody = preview.RootElement.GetProperty("body").GetRawText();
    if (preview.RootElement.GetProperty("body").GetProperty("max_tokens").GetInt32() < 8192)
        throw new Exception("Reasoning-Budget für Flint ist zu klein.");
    var prices = preview.RootElement.GetProperty("body").GetProperty("provider").GetProperty("max_price");
    if (prices.GetProperty("prompt").GetInt32() != 0 || prices.GetProperty("completion").GetInt32() != 0 ||
        prices.GetProperty("request").GetInt32() != 0)
        throw new Exception("Kostenlose Preisgrenze fehlt.");
    if (expectedBody.Contains("PRIVATE-PROCESS-XYZ") || expectedBody.Contains("987654"))
        throw new Exception("Private Prozessdaten sind in der KI-Vorschau.");
    if (!preview.RootElement.GetProperty("aliases").GetRawText().Contains("PRIVATE-PROCESS-XYZ"))
        throw new Exception("Lokale Alias-Zuordnung fehlt.");
    if (!expectedBody.Contains("Testfrage") || !expectedBody.Contains("Prozess 1"))
        throw new Exception("Frage oder Alias fehlt.");
    var provider = preview.RootElement.GetProperty("body").GetProperty("provider");
    if (!provider.GetProperty("zdr").GetBoolean() || provider.GetProperty("data_collection").GetString() != "deny")
        throw new Exception("Standard-Datenschutz fehlt.");
    handler.Status = HttpStatusCode.TooManyRequests;
    await Expect(await client.PostAsJsonAsync($"/api/checks/{check}/analyze", new { }), HttpStatusCode.BadGateway);
    handler.Status = HttpStatusCode.OK;
    handler.ResponseBody = """{"choices":[{"finish_reason":"length","message":{"content":""}}]}""";
    var exhausted = await client.PostAsJsonAsync($"/api/checks/{check}/analyze", new { });
    await Expect(exhausted, HttpStatusCode.BadGateway);
    if (!(await exhausted.Content.ReadAsStringAsync()).Contains("Tokenlimit"))
        throw new Exception("Tokenlimit wird nicht erklärt.");
    handler.ResponseBody = """{"choices":[{"finish_reason":"stop","message":{"content":"Simulierte Antwort."}}]}""";
    await Expect(await client.PostAsJsonAsync($"/api/checks/{check}/analyze", new { }), HttpStatusCode.OK);
    if (handler.Calls != 3 || !System.Text.Json.Nodes.JsonNode.DeepEquals(
        System.Text.Json.Nodes.JsonNode.Parse(handler.Body!), System.Text.Json.Nodes.JsonNode.Parse(expectedBody)))
        throw new Exception("Gesendeter Inhalt weicht von der Vorschau ab.");
    await Expect(await client.PostAsJsonAsync($"/api/checks/{check}/analyze", new { }), HttpStatusCode.Conflict);
    if (handler.Calls != 3) throw new Exception("Doppelter Modellaufruf nach Erfolg.");

    var standard = await Start();
    started = clock.GetUtcNow();
    samples = Enumerable.Range(0, 12).Select(index => new Sample(started.AddSeconds(1 + index * 5),
        new SystemMeasurement(20, 50, 60))).ToArray();
    clock.Advance(61);
    var standardResponse = await client.PostAsJsonAsync($"/api/checks/{standard}/preview",
        new { question = "Testfrage", model = "recursive/flint-v1.0:free", strictPrivacy = false });
    await Expect(standardResponse, HttpStatusCode.OK);
    using var standardPreview = JsonDocument.Parse(await standardResponse.Content.ReadAsStringAsync());
    var standardProvider = standardPreview.RootElement.GetProperty("body").GetProperty("provider");
    if (standardProvider.GetProperty("zdr").GetBoolean() ||
        standardProvider.GetProperty("data_collection").GetString() != "deny" ||
        standardProvider.GetProperty("max_price").GetProperty("completion").GetInt32() != 0)
        throw new Exception("Explizite Datenschutzwahl oder Preisgrenze falsch.");

    builder.Configuration["OpenRouter:ApiKey"] = "";
    await Expect(await client.PostAsJsonAsync($"/api/checks/{Guid.NewGuid()}/analyze", new { }), HttpStatusCode.ServiceUnavailable);
    if (handler.Calls != 3) throw new Exception("Aufruf ohne Key.");
    var expired = await Start();
    clock.Advance(601);
    await Expect(await Preview(expired), HttpStatusCode.NotFound);
    Console.WriteLine("PASS: Messfenster, Datenlücken, Anonymisierung, exakte Vorschau, Einmalversand, fehlender Key und Ablauf.");
}
finally { await app.StopAsync(); }

async Task<Guid> Start()
{
    using var response = await client.PostAsJsonAsync("/api/checks", new { });
    response.EnsureSuccessStatusCode();
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return json.RootElement.GetProperty("id").GetGuid();
}
Task<HttpResponseMessage> Preview(Guid id) => client.PostAsJsonAsync($"/api/checks/{id}/preview", new { question = "Testfrage", model = "xingchen-agi/xing4.0-29b-a4b:free" });
static async Task Expect(HttpResponseMessage response, HttpStatusCode status)
{
    if (response.StatusCode != status)
        throw new Exception($"Erwartet {status}, erhalten {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
}
sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    internal void Advance(int seconds) => now = now.AddSeconds(seconds);
}
sealed class FakeRouter : HttpMessageHandler
{
    internal int Calls;
    internal string? Body;
    internal HttpStatusCode Status = HttpStatusCode.OK;
    internal string ResponseBody = """{"choices":[{"message":{"content":"Simulierte Antwort."}}]}""";
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        if (request.RequestUri?.ToString() != "https://openrouter.ai/api/v1/chat/completions" ||
            request.Headers.Authorization?.Parameter != "local-test-placeholder")
            throw new Exception("Falsches Ziel oder Authentifizierung.");
        Body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.ToString();
        return new HttpResponseMessage(Status) {
            Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json")
        };
    }
}
