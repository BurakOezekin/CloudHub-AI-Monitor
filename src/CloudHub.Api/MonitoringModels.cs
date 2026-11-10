internal sealed record Sample(DateTimeOffset ReceivedAt, SystemMeasurement Values);
internal sealed record MetricWarning(string Metric, decimal Threshold);
internal sealed record ProcessReading(int Pid, string Name, decimal CpuPercent, decimal RamMb);
internal sealed record SystemMeasurement(decimal? CpuPercent, decimal? RamPercent = null, decimal? DiskUsedPercent = null, ProcessReading[]? Processes = null);
