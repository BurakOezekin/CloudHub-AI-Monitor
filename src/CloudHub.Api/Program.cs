var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

CpuMeasurement? latest = null;

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/metrics", IResult (CpuMeasurement measurement) =>
{
    if (measurement.CpuPercent is null or < 0 or > 100)
    {
        return Results.BadRequest(new { message = "cpuPercent muss zwischen 0 und 100 liegen." });
    }

    // Auch gleichzeitig eintreffende Anfragen sollen den veröffentlichten Wert sehen.
    Volatile.Write(ref latest, measurement);
    return Results.Ok(measurement);
});

app.MapGet("/api/metrics/latest", IResult () =>
{
    var measurement = Volatile.Read(ref latest);
    return measurement is null
        ? Results.NotFound(new { message = "Es liegt noch kein Messwert vor." })
        : Results.Ok(measurement);
});

app.Run();

record CpuMeasurement(decimal? CpuPercent);
