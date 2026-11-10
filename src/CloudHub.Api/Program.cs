using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

var samples = new Queue<Sample>();
var sync = new object();

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

    lock (sync)
    {
        var now = DateTimeOffset.UtcNow;
        samples.Enqueue(new Sample(now, measurement));
        while (samples.Count > 120 || samples.Peek().ReceivedAt < now.AddMinutes(-2))
            samples.Dequeue();
    }
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

record Sample(DateTimeOffset ReceivedAt, SystemMeasurement Values);
record MetricWarning(string Metric, decimal Threshold);
record SystemMeasurement(decimal? CpuPercent, decimal? RamPercent = null, decimal? DiskUsedPercent = null);
