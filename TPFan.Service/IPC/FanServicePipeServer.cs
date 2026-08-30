using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TPFan.Service.Hardware;
using TPFan.Shared.Contracts;
using TPFan.Shared.Models;

namespace TPFan.Service.IPC;

/// <summary>
/// Named Pipe server untuk IPC komunikasi antara UWP app dan background service
/// </summary>
public class FanServicePipeServer : IFanServiceContract, IDisposable
{
    private readonly string _pipeName = "TPFan.Pipe";
    private bool _disposed = false;

    private readonly T480FanProvider _fanProvider;

    public FanServicePipeServer(T480FanProvider fanProvider)
    {
        _fanProvider = fanProvider;
    }

    public async Task<FanCurve> GetFanCurveAsync()
    {
        return await _fanProvider.DetectFanCurveAsync();
    }

    public async Task<FanStatus> GetFanStatusAsync()
    {
        return await _fanProvider.GetFanStatusAsync();
    }

    public async Task<bool> SetFanSpeedOverrideAsync(int speedPercent)
    {
        // TODO: Implement ACPI fan control
        // This requires direct EC (Embedded Controller) access
        // which is currently not implemented
        await Task.Delay(100); // Simulate operation
        return true;
    }

    public async Task<bool> ResetFanOverrideAsync()
    {
        // TODO: Reset fan to automatic control
        await Task.Delay(100);
        return true;
    }

    public async Task<bool> IsServiceRunningAsync()
    {
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
