using System.IO;
using WinSolar.Core.Settings;

namespace WinSolar.App.Services;

/// <summary>learned.json: what Win Solar learns about this PC (light-sensor calibration).</summary>
public sealed class LearnedStore
{
    private const string FileName = "learned.json";
    private readonly JsonFileStore files;

    public LearnedStore(string folder)
    {
        files = new JsonFileStore(folder);
        State = files.Load(FileName, () => LearnedState.Empty);
    }

    public LearnedState State { get; private set; }

    public void Update(Func<LearnedState, LearnedState> change)
    {
        State = change(State);
        try
        {
            files.Save(FileName, State);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not save {FileName}: {e.Message}");
        }
    }
}
