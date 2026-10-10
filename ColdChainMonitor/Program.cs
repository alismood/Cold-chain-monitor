namespace ColdChainMonitor;

internal class Program
{
    private static void Main(string[] args)
    {
        string inputPath = args.Length > 0 ? args[0] : "readings.txt";
        string outputPath = args.Length > 1 ? args[1] : "archive.json";

        // Find file in current folder or parent folder
        if (!File.Exists(inputPath) && File.Exists(Path.Combine("..", inputPath)))
        {
            inputPath = Path.Combine("..", inputPath);
        }

        if (!File.Exists(inputPath))
        {
            Console.WriteLine($"Error: Input file '{inputPath}' not found.");
            return;
        }

        Console.WriteLine("=== ColdChainMonitor ===");
        Console.WriteLine($"Reading file: {inputPath}");

        // 1. Program.cs owns the FileStream and disposes it with using
        ImportResult importResult;
        using (var inputStream = new FileStream(inputPath, FileMode.Open, FileAccess.Read))
        {
            importResult = ColdChainProcessor.ReadReadings(inputStream);
        }

        Console.WriteLine($"\nValid readings: {importResult.Readings.Count}");
        Console.WriteLine($"Import errors:  {importResult.Errors.Count}");

        foreach (var err in importResult.Errors)
        {
            Console.WriteLine($"  [Line {err.LineNumber}] \"{err.RawLine}\" -> {err.Message}");
        }

        // 2. Pure adjacent query analysis
        var alerts = ColdChainProcessor.AnalyzeReadings(importResult.Readings);

        Console.WriteLine($"\nAlerts detected: {alerts.Count}");
        foreach (var alert in alerts)
        {
            string reasons = string.Join("; ", alert.Reasons);
            Console.WriteLine($"  Sensor {alert.SensorId} at {alert.Timestamp:HH:mm} UTC: {alert.Temperature}°C -> {reasons}");
        }

        // 3. Inject CreatedAtUtc from Program.cs as required by task
        var createdAtUtc = DateTimeOffset.UtcNow;
        var archive = new MonitoringArchive(
            createdAtUtc,
            importResult.Readings,
            importResult.Errors,
            alerts);

        // 4. Program.cs owns output FileStream and disposes it with using
        using (var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
        {
            ColdChainProcessor.WriteArchive(outputStream, archive);
        }
        Console.WriteLine($"\nArchive saved to: {outputPath}");

        // 5. Read back archive to verify round-trip
        MonitoringArchive restored;
        using (var readBackStream = new FileStream(outputPath, FileMode.Open, FileAccess.Read))
        {
            restored = ColdChainProcessor.ReadArchive(readBackStream);
        }

        // 6. Verify round trip by comparing values
        if (ColdChainProcessor.AreArchivesEquivalent(archive, restored))
        {
            Console.WriteLine("Round-trip check: SUCCEEDED (restored archive matches original values).");
        }
        else
        {
            Console.WriteLine("Round-trip check: FAILED.");
        }
    }
}
