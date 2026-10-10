using System.Text;
using System.Text.Json;
using Xunit;

namespace ColdChainMonitor.Tests;

public class ColdChainMonitorTests
{
    private const string SampleData =
        "S1|2026-09-22T08:10:00Z|Cold|5.2\n" +
        "S2|2026-09-22T08:01:00Z|Frozen|-18.0\n" +
        "S1|2026-09-22T08:00:00Z|Cold|4.0\n" +
        "S1|2026-09-22T08:05:00Z|Cold|8.7\n" +
        "S2|2026-09-22T08:06:00Z|Frozen|-12.0\n" +
        "S1|2026-09-22T08:15:00Z|Cold|8.0\n" +
        "S3|bad-date|Cold|3.5\n" +
        "S2|2026-09-22T08:11:00Z|99|-17.0\n" +
        "S1|2026-09-22T08:20:00Z|Cold|5,5\n" +
        "S1|2026-09-22T08:25:00Z|Frozen|-17.0\n";

    // 1. The supplied file produces 6 valid readings, 4 errors, and 2 alerts
    [Fact]
    public void Test01_SuppliedFile_ProducesExpectedCounts()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(SampleData));
        var result = ColdChainProcessor.ReadReadings(stream);
        var alerts = ColdChainProcessor.AnalyzeReadings(result.Readings);

        Assert.Equal(6, result.Readings.Count);
        Assert.Equal(4, result.Errors.Count);
        Assert.Equal(2, alerts.Count);
    }

    // 2. Numeric enum text 99 is rejected even when Enum.TryParse succeeds
    [Fact]
    public void Test02_NumericEnum99_IsRejected()
    {
        var line = "S2|2026-09-22T08:11:00Z|99|-17.0";
        bool ok = ReadingParser.TryParseReading(line, out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    // 3. 5,5 is rejected under invariant parsing
    [Fact]
    public void Test03_CommaDecimal_IsRejected()
    {
        var line = "S1|2026-09-22T08:20:00Z|Cold|5,5";
        bool ok = ReadingParser.TryParseReading(line, out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    // 4. A StorageClass change for the same sensor becomes an import error
    [Fact]
    public void Test04_StorageClassChange_BecomesImportError()
    {
        var data = "S1|2026-09-22T08:00:00Z|Cold|4.0\nS1|2026-09-22T08:25:00Z|Frozen|-17.0\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
        var result = ColdChainProcessor.ReadReadings(stream);

        Assert.Single(result.Readings);
        Assert.Single(result.Errors);
    }

    // 5. Cold 8.0 and Frozen -15.0 are safe boundary values
    [Fact]
    public void Test05_SafeBoundaryValues_ProduceNoAlerts()
    {
        var readings = new List<Reading>
        {
            new("S1", DateTimeOffset.Parse("2026-09-22T08:00:00Z"), StorageClass.Cold, 8.0m),
            new("S2", DateTimeOffset.Parse("2026-09-22T08:00:00Z"), StorageClass.Frozen, -15.0m)
        };
        var alerts = ColdChainProcessor.AnalyzeReadings(readings);

        Assert.Empty(alerts);
    }

    // 6. Adjacent comparisons never cross sensor boundaries
    [Fact]
    public void Test06_ComparisonsNeverCrossSensors()
    {
        var readings = new List<Reading>
        {
            new("S1", DateTimeOffset.Parse("2026-09-22T08:00:00Z"), StorageClass.Cold, 4.0m),
            new("S2", DateTimeOffset.Parse("2026-09-22T08:01:00Z"), StorageClass.Frozen, -18.0m)
        };
        var alerts = ColdChainProcessor.AnalyzeReadings(readings);

        Assert.Empty(alerts);
    }

    // 7. Input order may differ from timestamp order without changing the alerts
    [Fact]
    public void Test07_InputOrderDoesNotAffectAlerts()
    {
        var list1 = new List<Reading>
        {
            new("S1", DateTimeOffset.Parse("2026-09-22T08:00:00Z"), StorageClass.Cold, 4.0m),
            new("S1", DateTimeOffset.Parse("2026-09-22T08:05:00Z"), StorageClass.Cold, 8.7m)
        };
        var list2 = new List<Reading>
        {
            new("S1", DateTimeOffset.Parse("2026-09-22T08:05:00Z"), StorageClass.Cold, 8.7m),
            new("S1", DateTimeOffset.Parse("2026-09-22T08:00:00Z"), StorageClass.Cold, 4.0m)
        };

        var alerts1 = ColdChainProcessor.AnalyzeReadings(list1);
        var alerts2 = ColdChainProcessor.AnalyzeReadings(list2);

        Assert.Equal(alerts1, alerts2);
    }

    // 8. ReadReadings leaves a caller-owned stream open
    [Fact]
    public void Test08_ReadReadings_LeavesStreamOpen()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("S1|2026-09-22T08:00:00Z|Cold|4.0\n"));
        ColdChainProcessor.ReadReadings(stream);

        Assert.True(stream.CanRead);
    }

    // 9. WriteArchive leaves a caller-owned stream open
    [Fact]
    public void Test09_WriteArchive_LeavesStreamOpen()
    {
        var archive = new MonitoringArchive(DateTimeOffset.UtcNow, new List<Reading>(), new List<ImportError>(), new List<Alert>());
        using var stream = new MemoryStream();
        ColdChainProcessor.WriteArchive(stream, archive);

        Assert.True(stream.CanWrite);
    }

    // 10. A MemoryStream round trip preserves archive values and collection contents, and serialized JSON represents StorageClass values as enum names rather than numbers
    [Fact]
    public void Test10_RoundTrip_PreservesValues_AndEnumsAreStrings()
    {
        var readings = new List<Reading>
        {
            new("S1", DateTimeOffset.Parse("2026-09-22T08:00:00Z"), StorageClass.Cold, 4.0m)
        };
        var archive = new MonitoringArchive(DateTimeOffset.Parse("2026-09-22T10:00:00Z"), readings, new List<ImportError>(), new List<Alert>());

        using var stream = new MemoryStream();
        ColdChainProcessor.WriteArchive(stream, archive);

        // Check JSON contains "Cold" instead of 0
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, leaveOpen: true);
        string json = reader.ReadToEnd();
        Assert.Contains("\"storageClass\": \"Cold\"", json);

        // Rewind and read back
        stream.Position = 0;
        var restored = ColdChainProcessor.ReadArchive(stream);

        Assert.True(ColdChainProcessor.AreArchivesEquivalent(archive, restored));
    }

    // 11. Enumerating stored results after the source stream is disposed still works
    [Fact]
    public void Test11_ResultsWorkAfterStreamDisposed()
    {
        ImportResult result;
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("S1|2026-09-22T08:00:00Z|Cold|4.0\n")))
        {
            result = ColdChainProcessor.ReadReadings(stream);
        } // stream is disposed here

        Assert.Single(result.Readings);
        Assert.Equal("S1", result.Readings[0].SensorId);
    }

    // 12. ReadArchive leaves a caller-owned stream open
    [Fact]
    public void Test12_ReadArchive_LeavesStreamOpen()
    {
        var archive = new MonitoringArchive(DateTimeOffset.UtcNow, new List<Reading>(), new List<ImportError>(), new List<Alert>());
        using var stream = new MemoryStream();
        ColdChainProcessor.WriteArchive(stream, archive);
        stream.Position = 0;

        ColdChainProcessor.ReadArchive(stream);

        Assert.True(stream.CanRead);
    }

    // 13. ReadReadings starts from current stream position without silently resetting it to zero
    [Fact]
    public void Test13_ReadReadings_StartsFromCurrentPosition()
    {
        var data = "SKIP_ME\nS1|2026-09-22T08:00:00Z|Cold|4.0\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
        stream.Position = Encoding.UTF8.GetBytes("SKIP_ME\n").Length;

        var result = ColdChainProcessor.ReadReadings(stream);

        Assert.Single(result.Readings);
        Assert.Equal("S1", result.Readings[0].SensorId);
    }

    // 14. Additional test: safe lower boundaries (Cold 2.0 and Frozen -22.0)
    [Fact]
    public void Test14_SafeLowerBoundaries_ProduceNoAlerts()
    {
        var readings = new List<Reading>
        {
            new("S1", DateTimeOffset.Parse("2026-09-22T08:00:00Z"), StorageClass.Cold, 2.0m),
            new("S2", DateTimeOffset.Parse("2026-09-22T08:00:00Z"), StorageClass.Frozen, -22.0m)
        };
        var alerts = ColdChainProcessor.AnalyzeReadings(readings);

        Assert.Empty(alerts);
    }
}
