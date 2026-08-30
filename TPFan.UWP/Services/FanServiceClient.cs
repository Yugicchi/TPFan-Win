using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TPFan.Shared.Contracts;
using TPFan.Shared.Models;

namespace TPFan.UWP.Services;

/// <summary>
/// Client untuk communicate dengan background service via Named Pipes
/// </summary>
public class FanServiceClient : IFanServiceContract
{
    private readonly string _pipeName = "TPFan.Pipe";
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

    public async Task<FanCurve> GetFanCurveAsync()
    {
        return await SendRequestAsync<FanCurve>("GetFanCurve");
    }

    public async Task<FanStatus> GetFanStatusAsync()
    {
        return await SendRequestAsync<FanStatus>("GetFanStatus");
    }

    public async Task<bool> SetFanSpeedOverrideAsync(int speedPercent)
    {
        var request = new { Method = "SetFanSpeedOverride", SpeedPercent = speedPercent };
        return await SendRequestAsync<bool>(request);
    }

    public async Task<bool> ResetFanOverrideAsync()
    {
        return await SendRequestAsync<bool>("ResetFanOverride");
    }

    public async Task<bool> IsServiceRunningAsync()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
            await client.ConnectAsync(500); // Quick check
            return client.IsConnected;
        }
        catch
        {
            return false;
        }
    }

    private async Task<T> SendRequestAsync<T>(object request)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
            await client.ConnectAsync((int)_timeout.TotalMilliseconds);

            var json = JsonSerializer.Serialize(request);
            var bytes = Encoding.UTF8.GetBytes(json);

            // Send request
            await client.WriteAsync(BitConverter.GetBytes(bytes.Length), 0, 4);
            await client.WriteAsync(bytes, 0, bytes.Length);
            await client.FlushAsync();

            // Read response
            var lengthBytes = new byte[4];
            await client.ReadAsync(lengthBytes, 0, 4);
            var length = BitConverter.ToInt32(lengthBytes, 0);

            var responseBytes = new byte[length];
            await client.ReadAsync(responseBytes, 0, length);
            var responseJson = Encoding.UTF8.GetString(responseBytes);

            return JsonSerializer.Deserialize<T>(responseJson)!;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"IPC error: {ex.Message}");
            return default!;
        }
    }
}
