namespace ColdChainMonitor;

// Storage class: Cold or Frozen
public enum StorageClass
{
    Cold,
    Frozen
}

// Single valid temperature reading
public sealed record Reading(
    string SensorId,
    DateTimeOffset Timestamp,
    StorageClass StorageClass,
    decimal Temperature);

// Line that failed parsing or violated consistency rule
public sealed record ImportError(
    int LineNumber,
    string RawLine,
    string Message);

// Alert for out-of-range temperature or abrupt jump
public sealed record Alert(
    string SensorId,
    DateTimeOffset Timestamp,
    decimal Temperature,
    IReadOnlyList<string> Reasons)
{
    // Simple value equality so alerts can be compared in tests
    public bool Equals(Alert? other) =>
        other is not null &&
        string.Equals(SensorId, other.SensorId, StringComparison.OrdinalIgnoreCase) &&
        Timestamp == other.Timestamp &&
        Temperature == other.Temperature &&
        Reasons.SequenceEqual(other.Reasons);

    public override int GetHashCode() =>
        HashCode.Combine(SensorId.ToLowerInvariant(), Timestamp, Temperature, Reasons.Count);
}

// Result of reading from stream
public sealed record ImportResult(
    IReadOnlyList<Reading> Readings,
    IReadOnlyList<ImportError> Errors);

// Complete monitoring archive saved to JSON
public sealed record MonitoringArchive(
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<Reading> Readings,
    IReadOnlyList<ImportError> Errors,
    IReadOnlyList<Alert> Alerts)
{
    // Compare archives by values and collection contents
    public bool Equals(MonitoringArchive? other) =>
        other is not null && ColdChainProcessor.AreArchivesEquivalent(this, other);

    public override int GetHashCode() =>
        HashCode.Combine(CreatedAtUtc, Readings.Count, Errors.Count, Alerts.Count);
}
