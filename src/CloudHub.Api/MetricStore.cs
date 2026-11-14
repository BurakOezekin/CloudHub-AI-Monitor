using Microsoft.Data.Sqlite;

/// <summary>
/// Speichert System-Messwerte (CPU, RAM, Speicherplatz) dauerhaft in einer lokalen SQLite-Datei.
/// Prozessnamen und PIDs werden bewusst nicht gespeichert.
/// </summary>
internal sealed class MetricStore
{
    private readonly string connectionString;
    private readonly TimeSpan retention;
    private readonly TimeProvider clock;
    private readonly object gate = new();
    private DateTimeOffset lastPrune = DateTimeOffset.MinValue;

    internal static readonly IReadOnlyDictionary<string, TimeSpan> Ranges = new Dictionary<string, TimeSpan>
    {
        ["1h"] = TimeSpan.FromHours(1),
        ["24h"] = TimeSpan.FromHours(24),
        ["7d"] = TimeSpan.FromDays(7)
    };

    internal string DatabasePath { get; }

    internal MetricStore(string databasePath, TimeSpan retention, TimeProvider? timeProvider = null)
    {
        DatabasePath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = true }.ToString();
        this.retention = retention;
        clock = timeProvider ?? TimeProvider.System;

        using var connection = Open();
        Execute(connection, """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS samples (
                received_at INTEGER NOT NULL,
                cpu_percent REAL,
                ram_percent REAL,
                disk_used_percent REAL
            );
            CREATE INDEX IF NOT EXISTS ix_samples_received_at ON samples (received_at);
            """);
        Prune(connection);
    }

    internal static MetricStore FromConfiguration(IConfiguration configuration)
    {
        var path = configuration["Storage:DatabasePath"];
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.Create), "CloudHub", "cloudhub.db");
        var days = configuration.GetValue("Storage:RetentionDays", 7);
        return new MetricStore(path, TimeSpan.FromDays(Math.Clamp(days, 1, 365)));
    }

    internal void Add(Sample sample)
    {
        lock (gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO samples VALUES ($at, $cpu, $ram, $disk)";
            command.Parameters.AddWithValue("$at", sample.ReceivedAt.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$cpu", (object?)sample.Values.CpuPercent ?? DBNull.Value);
            command.Parameters.AddWithValue("$ram", (object?)sample.Values.RamPercent ?? DBNull.Value);
            command.Parameters.AddWithValue("$disk", (object?)sample.Values.DiskUsedPercent ?? DBNull.Value);
            command.ExecuteNonQuery();
            if (clock.GetUtcNow() - lastPrune > TimeSpan.FromHours(1)) Prune(connection);
        }
    }

    /// <summary>Liefert die Rohmessungen ab einem Zeitpunkt, z. B. um den Live-Verlauf nach einem Neustart zu füllen.</summary>
    internal Sample[] Since(DateTimeOffset from)
    {
        lock (gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT received_at, cpu_percent, ram_percent, disk_used_percent
                FROM samples WHERE received_at >= $from ORDER BY received_at
                """;
            command.Parameters.AddWithValue("$from", from.ToUnixTimeMilliseconds());
            using var reader = command.ExecuteReader();
            var result = new List<Sample>();
            while (reader.Read())
                result.Add(new Sample(DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                    new SystemMeasurement(Read(reader, 1), Read(reader, 2), Read(reader, 3))));
            return result.ToArray();
        }
    }

    /// <summary>Fasst einen Zeitraum in höchstens <paramref name="maxPoints"/> Durchschnittswerte zusammen.</summary>
    internal HistoryPoint[] History(TimeSpan range, int maxPoints = 240)
    {
        var to = clock.GetUtcNow().ToUnixTimeMilliseconds();
        var from = to - (long)range.TotalMilliseconds;
        var bucket = Math.Max(5000, (long)range.TotalMilliseconds / maxPoints);
        lock (gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT (received_at - $from) / $bucket AS slot,
                       AVG(cpu_percent), AVG(ram_percent), AVG(disk_used_percent),
                       MAX(cpu_percent), MAX(ram_percent), COUNT(*)
                FROM samples
                WHERE received_at >= $from AND received_at <= $to
                GROUP BY slot ORDER BY slot
                """;
            command.Parameters.AddWithValue("$from", from);
            command.Parameters.AddWithValue("$to", to);
            command.Parameters.AddWithValue("$bucket", bucket);
            using var reader = command.ExecuteReader();
            var result = new List<HistoryPoint>();
            while (reader.Read())
            {
                var at = DateTimeOffset.FromUnixTimeMilliseconds(from + reader.GetInt64(0) * bucket + bucket / 2);
                result.Add(new HistoryPoint(at, Round(reader, 1), Round(reader, 2), Round(reader, 3),
                    Round(reader, 4), Round(reader, 5), reader.GetInt32(6)));
            }
            return result.ToArray();
        }
    }

    private void Prune(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM samples WHERE received_at < $limit";
        command.Parameters.AddWithValue("$limit", (clock.GetUtcNow() - retention).ToUnixTimeMilliseconds());
        command.ExecuteNonQuery();
        lastPrune = clock.GetUtcNow();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static decimal? Read(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : (decimal)reader.GetDouble(index);

    private static decimal? Round(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : Math.Round((decimal)reader.GetDouble(index), 1);
}

internal sealed record HistoryPoint(DateTimeOffset At, decimal? CpuPercent, decimal? RamPercent,
    decimal? DiskUsedPercent, decimal? CpuMaxPercent, decimal? RamMaxPercent, int Samples);
