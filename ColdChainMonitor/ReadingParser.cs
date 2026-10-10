using System.Globalization;

namespace ColdChainMonitor;

public static class ReadingParser
{
    // Pure function: parses one line without touching any files, clock, or global variables
    public static bool TryParseReading(string rawLine, out Reading? reading, out string? errorMessage)
    {
        reading = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(rawLine))
        {
            errorMessage = "Line is empty.";
            return false;
        }

        // Each line must have exactly 4 fields separated by '|'
        var parts = rawLine.Split('|');
        if (parts.Length != 4)
        {
            errorMessage = $"Expected 4 fields separated by '|', but got {parts.Length}.";
            return false;
        }

        var sensorId = parts[0].Trim();
        var timestampStr = parts[1].Trim();
        var storageClassStr = parts[2].Trim();
        var tempStr = parts[3].Trim();

        // Check 1: Sensor ID cannot be empty
        if (string.IsNullOrEmpty(sensorId))
        {
            errorMessage = "Sensor ID cannot be empty.";
            return false;
        }

        // Check 2: Timestamp format must be yyyy-MM-ddTHH:mm:ssZ with UTC offset (+00:00)
        if (!DateTimeOffset.TryParseExact(
                timestampStr,
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestamp) || timestamp.Offset != TimeSpan.Zero)
        {
            errorMessage = $"Invalid timestamp: '{timestampStr}'. Expected yyyy-MM-ddTHH:mm:ssZ in UTC.";
            return false;
        }

        // Check 3: Storage class must be Cold or Frozen (reject numbers like 99 or 0)
        if (storageClassStr.Any(char.IsDigit) ||
            !Enum.TryParse<StorageClass>(storageClassStr, true, out var storageClass) ||
            !Enum.IsDefined(typeof(StorageClass), storageClass))
        {
            errorMessage = $"Invalid storage class: '{storageClassStr}'. Must be Cold or Frozen.";
            return false;
        }

        // Check 4: Temperature must be invariant decimal (period separator only, reject comma like 5,5)
        if (!decimal.TryParse(
                tempStr,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var temperature))
        {
            errorMessage = $"Invalid temperature: '{tempStr}'. Must use '.' as decimal separator.";
            return false;
        }

        reading = new Reading(sensorId, timestamp, storageClass, temperature);
        return true;
    }
}
