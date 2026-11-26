using System.Windows.Threading;
using Windows.System.Power;
using WinSolar.Core.Engine;
using WinSolar.Core.Power;

namespace WinSolar.App.Services;

/// <summary>Mains or battery and the charge, from WinRT <see cref="PowerManager"/>; raises <see cref="Changed"/> on the UI thread.</summary>
public sealed class PowerService : IPower, IDisposable
{
    private readonly Dispatcher dispatcher;

    public PowerService(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        PowerManager.BatteryStatusChanged += OnChanged;
        PowerManager.PowerSupplyStatusChanged += OnChanged;
        PowerManager.RemainingChargePercentChanged += OnChanged;
    }

    public event Action? Changed;

    public PowerStatus Read()
    {
        try
        {
            if (PowerManager.BatteryStatus == BatteryStatus.NotPresent) return PowerStatus.Mains;
            return new PowerStatus(PowerManager.PowerSupplyStatus == PowerSupplyStatus.NotPresent, PowerManager.RemainingChargePercent);
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return PowerStatus.Mains;
        }
    }

    private void OnChanged(object? sender, object e) => dispatcher.BeginInvoke(() => Changed?.Invoke());

    public void Dispose()
    {
        PowerManager.BatteryStatusChanged -= OnChanged;
        PowerManager.PowerSupplyStatusChanged -= OnChanged;
        PowerManager.RemainingChargePercentChanged -= OnChanged;
    }
}
