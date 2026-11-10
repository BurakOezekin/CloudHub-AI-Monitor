using System.ComponentModel;
using System.Net.Http.Json;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Der Agent benötigt Windows.");
    return 1;
}

bool once = args is ["--once"];
if (args.Length > 0 && !once)
{
    Console.Error.WriteLine("Aufruf: CloudHub.Agent [--once]");
    return 1;
}

string portSetting = Environment.GetEnvironmentVariable("CLOUDHUB_API_PORT") ?? "5080";
if (!ushort.TryParse(portSetting, out ushort port) || port == 0)
{
    Console.Error.WriteLine("CLOUDHUB_API_PORT muss zwischen 1 und 65535 liegen.");
    return 1;
}
var endpoint = new Uri($"http://127.0.0.1:{port}/api/metrics");
using var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
{
    Timeout = TimeSpan.FromSeconds(5)
};

do
{
    try
    {
        var measurement = await WindowsMetrics.SampleAsync();
        using var response = await client.PostAsJsonAsync(endpoint, measurement);
        response.EnsureSuccessStatusCode();
        Console.WriteLine($"Lokal gesendet | CPU {measurement.CpuPercent:F1} % | RAM {measurement.RamPercent:F1} % | Laufwerk belegt {measurement.DiskUsedPercent:F1} %");
    }
    catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
    {
        Console.Error.WriteLine($"Senden fehlgeschlagen. Läuft die API auf 127.0.0.1:{port}?");
        if (once) return 1;
    }
    catch (Exception error) when (error is Win32Exception or IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"Windows-Messung fehlgeschlagen: {error.Message}");
        if (once) return 1;
    }

    if (!once) await Task.Delay(TimeSpan.FromSeconds(4));
} while (!once);

return 0;
