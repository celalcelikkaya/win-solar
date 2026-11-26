using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinSolar.Core.Settings;

/// <summary>
/// Small JSON files in one folder. Saves are atomic (write .tmp, then replace), so a power cut leaves either the old
/// or the new file. A damaged file loads as defaults and is kept as <c>name.bad</c> for diagnosis.
/// </summary>
public sealed class JsonFileStore(string folder)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Folder { get; } = folder;

    public T Load<T>(string name, Func<T> defaults)
    {
        var path = Path.Combine(Folder, name);
        if (!File.Exists(path)) return defaults();
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? defaults();
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or IOException or ArgumentException)
        {
            try { File.Copy(path, path + ".bad", overwrite: true); }
            catch (IOException) { /* keeping the copy is best effort */ }
            return defaults();
        }
    }

    public void Save<T>(string name, T value)
    {
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, name);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, overwrite: true);
    }
}
