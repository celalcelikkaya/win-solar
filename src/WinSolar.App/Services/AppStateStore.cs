using System.IO;
using WinSolar.Core.Engine;
using WinSolar.Core.Settings;

namespace WinSolar.App.Services;

/// <summary>Settings and state in %AppData%\Win Solar.</summary>
public sealed class AppStateStore : IStateStore
{
    private const string SettingsFile = "settings.json";
    private const string StateFile = "state.json";

    private readonly JsonFileStore files;

    public AppStateStore()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Win Solar");
        files = new JsonFileStore(folder);
        IsFirstRun = !File.Exists(Path.Combine(folder, SettingsFile));
        Settings = files.Load(SettingsFile, () => new AppSettings());
        State = files.Load(StateFile, () => new AppState());
    }

    public string Folder => files.Folder;

    /// <summary>No settings file existed at start: Win Solar runs for the first time (the welcome sheet shows).</summary>
    public bool IsFirstRun { get; }

    public AppSettings Settings { get; private set; }

    public AppState State { get; private set; }

    public void Save(AppState state)
    {
        State = state;
        files.Save(StateFile, state);
    }

    public void UpdateSettings(Func<AppSettings, AppSettings> change)
    {
        Settings = change(Settings);
        files.Save(SettingsFile, Settings);
    }
}
