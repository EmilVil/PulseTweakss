using System.Text.Json;
using System.Text.Json.Serialization;
using Pulse.Models;

namespace Pulse.Services;

public sealed class JsonStore(string directory)
{
    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static JsonStore ForCurrentUser() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PULSE"));

    public Preferences ReadPreferences() => Read<Preferences>("preferences.json") ?? new();
    public bool IsInternalPath(string path)
    {
        var relative = Path.GetRelativePath(directory, Path.GetFullPath(path));
        return !Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    public void SavePreferences(Preferences value) => Write(Path.Combine(directory, "preferences.json"), value);
    public List<ChangeRecord> ReadHistory() => Read<List<ChangeRecord>>("history.json") ?? [];
    internal void SaveHistory(List<ChangeRecord> value) => Write(Path.Combine(directory, "history.json"), value);

    public FileStream LockHistory()
    {
        Directory.CreateDirectory(directory);
        return new FileStream(Path.Combine(directory, "journal.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public static void Export(string path, Preferences preferences, object settings, IReadOnlyList<ChangeRecord> history) =>
        Write(path, new { schemaVersion = 1, exportedAt = DateTimeOffset.UtcNow, preferences, settings, history });

    private T? Read<T>(string file) where T : class
    {
        try
        {
            using var stream = new FileStream(Path.Combine(directory, file), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            return JsonSerializer.Deserialize<T>(stream, Format) ?? throw new InvalidDataException($"{file} is empty.");
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (JsonException ex) { throw new InvalidDataException($"{file} could not be read. Preserve the file for recovery.", ex); }
    }

    private static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, Format);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
