using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TPFan.Shared.Models;

namespace TPFan.GUI.Services;

/// <summary>
/// Named Pipe client mirroring TPFan.UWP's FanServiceClient. Talks to
/// the background service at \\.\pipe\TPFan.Pipe over a length-prefixed
/// JSON envelope.
/// </summary>
public sealed class FanServiceClient : IDisposable
{
    private const string PipeName = "TPFan.Pipe";
    private const int TimeoutMs = 2000;
    private bool _disposed;

    public async Task<bool> IsServiceRunningAsync()
    {
        try
        {
            using var pipe = CreatePipe();
            await pipe.ConnectAsync(TimeoutMs).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<FanCurve> GetFanCurveAsync()
    {
        var response = await SendRequestAsync("GetFanCurve").ConfigureAwait(false);
        return JsonSerializer.Deserialize<FanCurve>(response) ?? new FanCurve();
    }

    public async Task<FanStatus> GetFanStatusAsync()
    {
        var response = await SendRequestAsync("GetFanStatus").ConfigureAwait(false);
        return JsonSerializer.Deserialize<FanStatus>(response) ?? new FanStatus();
    }

    public async Task<bool> SetFanSpeedOverrideAsync(int speedPercent)
    {
        var payload = JsonSerializer.Serialize(new { Method = "SetFanSpeedOverride", SpeedPercent = speedPercent });
        var response = await SendRequestAsync(payload).ConfigureAwait(false);
        return bool.TryParse(response, out var result) && result;
    }

    public async Task<bool> ResetFanOverrideAsync()
    {
        var response = await SendRequestAsync("ResetFanOverride").ConfigureAwait(false);
        return bool.TryParse(response, out var result) && result;
    }

    private static NamedPipeClientStream CreatePipe()
    {
        return new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    }

    private async Task<string> SendRequestAsync(string requestJson)
    {
        using var pipe = CreatePipe();
        await pipe.ConnectAsync(TimeoutMs).ConfigureAwait(false);

        var requestBytes = Encoding.UTF8.GetBytes(requestJson);
        var lengthBytes = BitConverter.GetBytes(requestBytes.Length);

        await pipe.WriteAsync(lengthBytes.AsMemory(0, 4)).ConfigureAwait(false);
        await pipe.WriteAsync(requestBytes.AsMemory(0, requestBytes.Length)).ConfigureAwait(false);
        await pipe.FlushAsync().ConfigureAwait(false);

        var responseLengthBuffer = new byte[4];
        var totalRead = 0;
        while (totalRead < 4)
        {
            var r = await pipe.ReadAsync(responseLengthBuffer.AsMemory(totalRead, 4 - totalRead)).ConfigureAwait(false);
            if (r == 0) return string.Empty;
            totalRead += r;
        }
        var responseLength = BitConverter.ToInt32(responseLengthBuffer, 0);
        if (responseLength is <= 0 or > 65536) return string.Empty;

        var responseBuffer = new byte[responseLength];
        totalRead = 0;
        while (totalRead < responseLength)
        {
            var r = await pipe.ReadAsync(responseBuffer.AsMemory(totalRead, responseLength - totalRead)).ConfigureAwait(false);
            if (r == 0) break;
            totalRead += r;
        }
        return Encoding.UTF8.GetString(responseBuffer, 0, totalRead);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}