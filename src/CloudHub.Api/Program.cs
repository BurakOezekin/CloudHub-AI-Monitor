using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 65536);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true).AddEnvironmentVariables();
builder.Services.AddHttpClient("OpenRouter", client => client.Timeout = TimeSpan.FromSeconds(120))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

var samples = new Queue<Sample>();
var sync = new object();

MetricStore? store = null;
try
{
    store = MetricStore.FromConfiguration(app.Configuration);
    foreach (var sample in store.Since(DateTimeOffset.UtcNow.AddMinutes(-2)).TakeLast(120))
        samples.Enqueue(sample);
    app.Logger.LogInformation("Messhistorie wird gespeichert in {Path}", store.DatabasePath);
}
catch (Exception error)
{
    app.Logger.LogWarning(error, "Messhistorie kann nicht gespeichert werden. Die API läuft ohne Speicherung weiter.");
}

app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    if (context.Request.Host.Host is not ("localhost" or "127.0.0.1" or "[::1]"))
    {
        context.Response.StatusCode = 400;
        return;
    }
    if (HttpMethods.IsPost(context.Request.Method))
    {
        var origin = context.Request.Headers.Origin.ToString();
        var fetchSite = context.Request.Headers["Sec-Fetch-Site"].ToString();
        if (fetchSite == "cross-site" || (origin.Length > 0 &&
            origin != $"{context.Request.Scheme}://{context.Request.Host}"))
        {
            context.Response.StatusCode = 403;
            return;
        }
    }
    await next();
});

app.MapPcChecks(() => { lock (sync) return samples.ToArray(); });
app.MapInfrastructure();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/metrics", IResult (SystemMeasurement measurement) =>
{
    if (measurement.CpuPercent is null or < 0 or > 100)
    {
        return Results.BadRequest(new { message = "cpuPercent muss zwischen 0 und 100 liegen." });
    }

    if (measurement.RamPercent is < 0 or > 100 || measurement.DiskUsedPercent is < 0 or > 100)
    {
        return Results.BadRequest(new { message = "ramPercent und diskUsedPercent müssen zwischen 0 und 100 liegen." });
    }

    if (measurement.Processes is { } processes &&
        (processes.Length > 10 || processes.Any(process => process is null ||
         process.Pid <= 0 || string.IsNullOrWhiteSpace(process.Name) || process.Name.Length > 128 ||
         process.CpuPercent is < 0 or > 100 || process.RamMb is < 0 or > 10000000)))
        return Results.BadRequest(new { message = "Ungültige Prozessmessung." });

    var sample = new Sample(DateTimeOffset.UtcNow, measurement);
    lock (sync)
    {
        samples.Enqueue(sample);
        while (samples.Count > 120 || samples.Peek().ReceivedAt < sample.ReceivedAt.AddMinutes(-2))
            samples.Dequeue();
    }
    try { store?.Add(sample); }
    catch (Exception error) { app.Logger.LogWarning(error, "Messwert konnte nicht gespeichert werden."); }
    return Results.Ok(measurement);
});

app.MapGet("/api/metrics/latest", IResult () =>
{
    SystemMeasurement? measurement;
    lock (sync) measurement = samples.LastOrDefault()?.Values;
    return measurement is null
        ? Results.NotFound(new { message = "Es liegt noch kein Messwert vor." })
        : Results.Ok(measurement);
});

app.MapGet("/api/dashboard", () =>
{
    lock (sync)
    {
        var now = DateTimeOffset.UtcNow;
        var latest = samples.LastOrDefault();
        var history = samples.Where(sample => sample.ReceivedAt >= now.AddMinutes(-2)).ToArray();
        return Results.Ok(new { now, latest, history, warnings = GetWarnings(latest?.Values) });
    }
});

app.MapGet("/api/history", IResult (string? range) =>
{
    if (store is null)
        return Results.Json(new { message = "Die Messhistorie ist nicht verfügbar." }, statusCode: 503);
    if (!MetricStore.Ranges.TryGetValue(range ?? "1h", out var span))
        return Results.BadRequest(new { message = "range muss 1h, 24h oder 7d sein." });
    return Results.Ok(new { now = DateTimeOffset.UtcNow, range = range ?? "1h", points = store.History(span) });
});

app.Run();

static List<MetricWarning> GetWarnings(SystemMeasurement? values)
{
    var warnings = new List<MetricWarning>();
    AddIfHigh("cpuPercent", values?.CpuPercent, 90);
    AddIfHigh("ramPercent", values?.RamPercent, 85);
    AddIfHigh("diskUsedPercent", values?.DiskUsedPercent, 90);
    return warnings;

    void AddIfHigh(string metric, decimal? value, decimal threshold)
    {
        if (value >= threshold)
            warnings.Add(new MetricWarning(metric, threshold));
    }
}
