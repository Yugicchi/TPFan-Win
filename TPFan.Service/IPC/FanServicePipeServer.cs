using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TPFan.Service.Hardware;
using TPFan.Shared.Contracts;
using TPFan.Shared.Models;

namespace TPFan.Service.IPC;

/// <summary>
/// Named Pipe server for IPC between UWP / client apps and the elevated background service.
/// Listens on \\.\pipe\TPFan.Pipe with ACL configured for UWP AppContainer access.
/// </summary>
public class FanServicePipeServer : IFanServiceContract, IDisposable
{
    private const string PipeName = "TPFan.Pipe";
    private readonly T480FanProvider _fanProvider;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenerTask;
    private bool _disposed;

    public FanServicePipeServer(T480FanProvider fanProvider)
    {
        _fanProvider = fanProvider;
    }

    public void Start()
    {
        if (_listenerTask is not null) return;
        _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token));
        Console.WriteLine($"[IPC] Named Pipe server listening on \\\\.\\pipe\\{PipeName}");
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                // Create pipe with security that allows UWP AppContainer access
                server = CreateServerStream();
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                // Handle the client request asynchronously
                _ = HandleClientAsync(server, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                server?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IPC] Connection error: {ex.Message}");
                server?.Dispose();
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
        }
    }

    private static NamedPipeServerStream CreateServerStream()
    {
        var pipeSecurity = new PipeSecurity();

        // Allow Administrators full control
        var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(adminSid, PipeAccessRights.FullControl, AccessControlType.Allow));

        // Allow Authenticated Users Read/Write
        var authUserSid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(authUserSid, PipeAccessRights.ReadWrite, AccessControlType.Allow));

        // Allow UWP AppContainer (ALL APPLICATION PACKAGES) Read/Write
        // S-1-15-2-1 = ALL APPLICATION PACKAGES
        var appContainerSid = new SecurityIdentifier("S-1-15-2-1");
        pipeSecurity.AddAccessRule(new PipeAccessRule(appContainerSid, PipeAccessRights.ReadWrite, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 4096,
            outBufferSize: 4096,
            pipeSecurity);
    }

    private async Task HandleClientAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        using (server)
        {
            try
            {
                // Read 4-byte length prefix
                var lengthBuffer = new byte[4];
                var read = await server.ReadAsync(lengthBuffer.AsMemory(0, 4), ct).ConfigureAwait(false);
                if (read < 4) return;

                var length = BitConverter.ToInt32(lengthBuffer, 0);
                if (length is <= 0 or > 65536) return;

                var payloadBuffer = new byte[length];
                var totalRead = 0;
                while (totalRead < length)
                {
                    var chunk = await server.ReadAsync(payloadBuffer.AsMemory(totalRead, length - totalRead), ct).ConfigureAwait(false);
                    if (chunk == 0) break;
                    totalRead += chunk;
                }

                var requestJson = Encoding.UTF8.GetString(payloadBuffer, 0, totalRead);
                var responseJson = await ProcessRequestAsync(requestJson).ConfigureAwait(false);

                // Write 4-byte length prefix + response payload
                var responseBytes = Encoding.UTF8.GetBytes(responseJson);
                var responseLengthBytes = BitConverter.GetBytes(responseBytes.Length);

                await server.WriteAsync(responseLengthBytes.AsMemory(0, 4), ct).ConfigureAwait(false);
                await server.WriteAsync(responseBytes.AsMemory(0, responseBytes.Length), ct).ConfigureAwait(false);
                await server.FlushAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IPC] Client handle error: {ex.Message}");
            }
        }
    }

    private async Task<string> ProcessRequestAsync(string requestJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(requestJson);
            var root = doc.RootElement;

            // Request can be a plain string: "GetFanCurve", "GetFanStatus", "ResetFanOverride"
            // or an object: { "Method": "SetFanSpeedOverride", "SpeedPercent": 80 }
            string? methodName = null;
            if (root.ValueKind == JsonValueKind.String)
            {
                methodName = root.GetString();
            }
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Method", out var methodProp))
            {
                methodName = methodProp.GetString();
            }

            Console.WriteLine($"[IPC] Request received: Method='{methodName}'");

            switch (methodName)
            {
                case "GetFanCurve":
                    var curve = await GetFanCurveAsync().ConfigureAwait(false);
                    return JsonSerializer.Serialize(curve);

                case "GetFanStatus":
                    var status = await GetFanStatusAsync().ConfigureAwait(false);
                    return JsonSerializer.Serialize(status);

                case "SetFanSpeedOverride":
                    var speed = 0;
                    if (root.TryGetProperty("SpeedPercent", out var speedProp))
                    {
                        speed = speedProp.GetInt32();
                    }
                    var setOk = await SetFanSpeedOverrideAsync(speed).ConfigureAwait(false);
                    return JsonSerializer.Serialize(setOk);

                case "ResetFanOverride":
                    var resetOk = await ResetFanOverrideAsync().ConfigureAwait(false);
                    return JsonSerializer.Serialize(resetOk);

                case "IsServiceRunning":
                    return JsonSerializer.Serialize(true);

                default:
                    Console.WriteLine($"[IPC] Unknown method: '{methodName}'");
                    return JsonSerializer.Serialize(false);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[IPC] Request dispatch error: {ex.Message}");
            return JsonSerializer.Serialize(false);
        }
    }

    public async Task<FanCurve> GetFanCurveAsync() => await _fanProvider.DetectFanCurveAsync();

    public async Task<FanStatus> GetFanStatusAsync() => await _fanProvider.GetFanStatusAsync();

    public async Task<bool> SetFanSpeedOverrideAsync(int speedPercent)
    {
        if (speedPercent is < 0 or > 100) return false;
        return await _fanProvider.SetFanSpeedOverrideAsync(speedPercent);
    }

    public async Task<bool> ResetFanOverrideAsync() => await _fanProvider.ResetFanOverrideAsync();

    public async Task<bool> IsServiceRunningAsync() => await Task.FromResult(true);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
