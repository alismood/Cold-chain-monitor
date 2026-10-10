using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ColdChainMonitor;

public static class ColdChainProcessor
{
    // JSON options: camelCase property names, indented output, string enums
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    // Pure helper: checks if temperature violates safe boundaries (uses switch)
    public static bool IsOutsideRange(Reading reading) => reading.StorageClass switch
    {
        StorageClass.Cold => reading.Temperature < 2.0m || reading.Temperature > 8.0m,
        StorageClass.Frozen => reading.Temperature < -22.0m || reading.Temperature > -15.0m,
        _ => false
    };

    // Reads and materializes readings from stream (leaves caller-owned stream open)
    public static ImportResult ReadReadings(Stream input)
    {
        var readings = new List<Reading>();
        var errors = new List<ImportError>();
        var knownSensorClasses = new Dictionary<string, StorageClass>(StringComparer.OrdinalIgnoreCase);

        using var reader = new StreamReader(input, Encoding.UTF8, true, 1024, leaveOpen: true);

        int lineNumber = 0;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!ReadingParser.TryParseReading(line, out var reading, out var error) || reading is null)
            {
                errors.Add(new ImportError(lineNumber, line, error ?? "Unknown error"));
                continue;
            }

            // A sensor must maintain the same StorageClass as its first accepted reading
            if (knownSensorClasses.TryGetValue(reading.SensorId, out var expectedClass))
            {
                if (reading.StorageClass != expectedClass)
                {
                    errors.Add(new ImportError(
                        lineNumber,
                        line,
                        $"Storage class mismatch for sensor '{reading.SensorId}': expected {expectedClass}, but found {reading.StorageClass}."));
                    continue;
                }
            }
            else
            {
                knownSensorClasses[reading.SensorId] = reading.StorageClass;
            }

            readings.Add(reading);
        }

        return new ImportResult(readings, errors);
    }

    // Pure analysis function: groups by sensor, sorts chronologically, and compares adjacent pairs
    public static IReadOnlyList<Alert> AnalyzeReadings(IReadOnlyList<Reading> readings)
    {
        if (readings.Count == 0)
        {
            return Array.Empty<Alert>();
        }

        // Store first casing of each sensor ID for consistent alert naming
        var firstCasing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in readings)
        {
            if (!firstCasing.ContainsKey(r.SensorId))
            {
                firstCasing[r.SensorId] = r.SensorId;
            }
        }

        // Track original line index for deterministic sorting
        var indexed = readings
            .Select((reading, index) => (Reading: reading, OriginalIndex: index))
            .ToList();

        var alerts = new List<(Alert Alert, int Order)>();

        // 1. Group by SensorId using OrdinalIgnoreCase 
        var sensorGroups = indexed.GroupBy(x => x.Reading.SensorId, StringComparer.OrdinalIgnoreCase);

        foreach (var group in sensorGroups)
        {
            string canonicalId = firstCasing[group.Key];

            // 2. Sort chronologically by Timestamp, tie-break by input order
            var sorted = group
                .OrderBy(x => x.Reading.Timestamp)
                .ThenBy(x => x.OriginalIndex)
                .ToList();

            // First reading: check outside range only (cannot be abrupt)
            var first = sorted[0];
            if (IsOutsideRange(first.Reading))
            {
                alerts.Add((new Alert(canonicalId, first.Reading.Timestamp, first.Reading.Temperature, new[] { "Outside range" }), first.OriginalIndex));
            }

            // 3. Adjacent pairs using Zip and Skip(1)
            var pairs = sorted.Zip(sorted.Skip(1), (previous, current) => (Previous: previous, Current: current));

            foreach (var (prev, curr) in pairs)
            {
                bool outsideRange = IsOutsideRange(curr.Reading);
                bool abrupt = Math.Abs(curr.Reading.Temperature - prev.Reading.Temperature) > 4.0m;

                if (outsideRange || abrupt)
                {
                    var reasons = new List<string>();
                    if (outsideRange) reasons.Add("Outside range");
                    if (abrupt) reasons.Add("Abrupt change");

                    alerts.Add((new Alert(canonicalId, curr.Reading.Timestamp, curr.Reading.Temperature, reasons), curr.OriginalIndex));
                }
            }
        }

        // 4. Deterministic order: by SensorId then Timestamp
        return alerts
            .OrderBy(x => x.Alert.SensorId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Alert.Timestamp)
            .ThenBy(x => x.Order)
            .Select(x => x.Alert)
            .ToList();
    }

    // Serializes archive to output stream (leaves stream open)
    public static void WriteArchive(Stream output, MonitoringArchive archive)
    {
        using var writer = new StreamWriter(output, Encoding.UTF8, 1024, leaveOpen: true);
        var json = JsonSerializer.Serialize(archive, SerializerOptions);
        writer.Write(json);
        writer.Flush();
    }

    // Deserializes archive from input stream (leaves stream open)
    public static MonitoringArchive ReadArchive(Stream input)
    {
        using var reader = new StreamReader(input, Encoding.UTF8, true, 1024, leaveOpen: true);
        var json = reader.ReadToEnd();

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new JsonException("JSON archive cannot be empty.");
        }

        var archive = JsonSerializer.Deserialize<MonitoringArchive>(json, SerializerOptions);
        if (archive is null)
        {
            throw new JsonException("Deserialized archive cannot be null.");
        }

        return archive;
    }

    // Pure comparison: checks value equality across all fields and collections
    public static bool AreArchivesEquivalent(MonitoringArchive a, MonitoringArchive b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;

        if (a.CreatedAtUtc != b.CreatedAtUtc) return false;

        if (a.Readings.Count != b.Readings.Count) return false;
        for (int i = 0; i < a.Readings.Count; i++)
        {
            var r1 = a.Readings[i];
            var r2 = b.Readings[i];
            if (r1.SensorId != r2.SensorId ||
                r1.Timestamp != r2.Timestamp ||
                r1.StorageClass != r2.StorageClass ||
                r1.Temperature != r2.Temperature)
            {
                return false;
            }
        }

        if (a.Errors.Count != b.Errors.Count) return false;
        for (int i = 0; i < a.Errors.Count; i++)
        {
            var e1 = a.Errors[i];
            var e2 = b.Errors[i];
            if (e1.LineNumber != e2.LineNumber ||
                e1.RawLine != e2.RawLine ||
                e1.Message != e2.Message)
            {
                return false;
            }
        }

        if (a.Alerts.Count != b.Alerts.Count) return false;
        for (int i = 0; i < a.Alerts.Count; i++)
        {
            var al1 = a.Alerts[i];
            var al2 = b.Alerts[i];
            if (!string.Equals(al1.SensorId, al2.SensorId, StringComparison.OrdinalIgnoreCase) ||
                al1.Timestamp != al2.Timestamp ||
                al1.Temperature != al2.Temperature ||
                !al1.Reasons.SequenceEqual(al2.Reasons))
            {
                return false;
            }
        }

        return true;
    }
}
