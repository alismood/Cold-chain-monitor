# ColdChainMonitor — Assignment 3
## Zhabaikhan Ali IT-2503

---

### Q1: What makes `TryParseReading` a pure function?
> **Answer:**  
> It does not perform any I/O (no file reading), does not touch mutable global variables, and does not read the system clock (`DateTime.Now`). It only takes an input string and returns a result deterministically. Given the same string, it will always return the exact same output.

### Q2: Why is `Enum.TryParse` alone not enough for `99`, and how did you fix it?
> **Answer:**  
> In C#, `Enum.TryParse<StorageClass>("99", ...)` actually returns `true` because C# allows any integer to be converted to an enum type under the hood. Even `"0"` would parse into `Cold`!  
> To reject it, we check:
> 1. `!storageClassStr.Any(char.IsDigit)` (ensures no numbers are allowed).
> 2. `Enum.TryParse` to check it matches a name.
> 3. `Enum.IsDefined` to ensure it is a declared enum constant.

### Q3: Who owns the stream, and why do helper methods use `leaveOpen: true`?
> **Answer:**  
> The **caller** (`Program.cs` or test) creates and owns the stream with `using`.  
> Helper methods (`ReadReadings`, `WriteArchive`, `ReadArchive`) use `leaveOpen: true` on `StreamReader`/`StreamWriter` so that disposing the reader does NOT close the caller's stream.

### Q4: Why is returning a lazy sequence (`yield return`) with a `using StreamReader` dangerous?
> **Answer:**  
> Because the `using` block disposes the `StreamReader` as soon as the method returns. When the caller later tries to iterate over the lazy sequence (`foreach`), the reader is already closed, throwing an `ObjectDisposedException`.  
> **Fix:** We eagerly materialize results into a `List<Reading>` before returning.

### Q5: Why do we `GroupBy` sensor before comparing adjacent pairs?
> **Answer:**  
> In the text file, readings from different sensors (`S1` and `S2`) can be interleaved. If we compared chronologically across the whole file without grouping, we would compare S1's temperature with S2's temperature. Different sensors are in different locations/storage classes, so comparing them would create false abrupt change alerts.

### Q6: Why do we sort by Timestamp inside each group, and why `i > 0` for abrupt changes?
> **Answer:**  
> 1. The input lines might not arrive in chronological order. We sort by timestamp so adjacent means adjacent in time.  
> 2. We check `i > 0` because the **first reading** of a sensor has no previous reading to compare against, so it can never be an abrupt change.

### Q7: Why must `stream.Position = 0` be set before deserializing a MemoryStream?
> **Answer:**  
> When you serialize data into a stream, the stream's write cursor (`Position`) advances to the very end. If you immediately call `Deserialize`, it starts reading from that position and finds 0 bytes (EOF). Setting `Position = 0` rewinds the cursor back to the beginning so all bytes can be read.

### Q8: Why does round-trip verification compare values instead of reference identity (`==`)?
> **Answer:**  
> Deserialization creates completely new object instances in different memory locations. In C#, record default equality checks references for collections (`IReadOnlyList`), which would return `false`. We must compare the actual field values and collection contents sequentially.

### Q9: Why is `CreatedAtUtc` injected from `Program.cs` instead of calling `DateTime.UtcNow` inside analysis?
> **Answer:**  
> To keep analysis pure and deterministic. If analysis called `DateTime.UtcNow`, tests would get different millisecond values every run and couldn't verify identical JSON output. Injecting a fixed timestamp makes tests 100% reproducible.

---

### Results Summary
* **Input lines:** 10
* **Valid readings:** 6 (in original order in archive)
* **Import errors:** 4 (line 7: bad date; line 8: numeric enum 99; line 9: comma decimal 5,5; line 10: S1 changed from Cold to Frozen)
* **Alerts:** 2
  * `S1` at 08:05: 8.7°C (Outside range [2.0, 8.0] and Abrupt change from 4.0)
  * `S2` at 08:06: -12.0°C (Outside range [-22.0, -15.0] and Abrupt change from -18.0)
* **Safe boundary:** `S1` at 08:15 with 8.0°C is safe (inclusive boundary).
* **Round-trip check:** Verified by value equality.
