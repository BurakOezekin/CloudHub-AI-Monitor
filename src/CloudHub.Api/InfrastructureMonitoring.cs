internal static class InfrastructureMonitoring
{
    private const int Limit = 25;

    public static void MapInfrastructure(this WebApplication app)
    {
        var sync = new object();
        InfrastructureSnapshot? latest = null;
        DateTimeOffset receivedAt = default;

        app.MapPost("/api/infrastructure", IResult (InfrastructureSnapshot snapshot) =>
        {
            if (!Valid(snapshot.Docker) || !Valid(snapshot.Pods) || !Valid(snapshot.Nodes))
                return Results.BadRequest(new { message = "Ungültige Infrastrukturmessung." });
            lock (sync)
            {
                latest = snapshot;
                receivedAt = DateTimeOffset.UtcNow;
            }
            return Results.Ok(new { status = "accepted" });
        });

        app.MapGet("/api/infrastructure", IResult () =>
        {
            lock (sync)
            {
                if (latest is null)
                    return Results.NotFound(new { message = "Noch keine Infrastrukturmessung." });
                return Results.Ok(new
                {
                    receivedAt,
                    stale = DateTimeOffset.UtcNow - receivedAt > TimeSpan.FromSeconds(90),
                    latest.Docker, latest.Pods, latest.Nodes
                });
            }
        });
    }

    private static bool Valid(InfrastructureSource? source)
    {
        if (source is null || source.Status is not ("ok" or "disabled" or "unavailable") ||
            source.Metrics is not ("available" or "unavailable" or "notRequested") ||
            source.Rows is null || source.Rows.Length > Limit || source.Total < source.Rows.Length ||
            source.Rows.Length != Math.Min(source.Total, Limit))
            return false;
        if (source.Status != "ok" && (source.Total != 0 || source.Metrics != "notRequested"))
            return false;
        return source.Rows.All(row => row is not null &&
            !string.IsNullOrWhiteSpace(row.Name) && row.Name.Length <= 253 &&
            !string.IsNullOrWhiteSpace(row.State) && row.State.Length <= 80 &&
            row.Detail is { Length: <= 120 } &&
            (row.Cpu is null || row.Cpu.Length <= 32) &&
            (row.Memory is null || row.Memory.Length <= 32));
    }
}

internal sealed record InfrastructureSnapshot(InfrastructureSource? Docker, InfrastructureSource? Pods, InfrastructureSource? Nodes);
internal sealed record InfrastructureSource(string? Status, string? Metrics, int Total, InfrastructureRow?[]? Rows);
internal sealed record InfrastructureRow(string? Name, string? State, string? Detail, string? Cpu, string? Memory);
