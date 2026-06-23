using System.Diagnostics;
using System.Text.Json;

namespace PlcMonitorHmi;

public sealed class TelemetryPayload
{
    public string DeviceId { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
    public string ConnectionStatus { get; init; } = "Connected";
    public ushort? RawValue { get; init; }
    public double? Temperature { get; init; }
    public string OperationStatus { get; init; } = string.Empty;
    public string AlarmStatus { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
}

public sealed class AzureIoTHubTelemetryService
{
    private const string DefaultDeviceId = "plc-monitor-demo-001";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly string _deviceId;
    private readonly string _logFilePath;

    public AzureIoTHubTelemetryService(string? deviceId = null)
    {
        _deviceId = string.IsNullOrWhiteSpace(deviceId) ? DefaultDeviceId : deviceId;
        _logFilePath = Path.Combine(AppContext.BaseDirectory, "Logs", "TelemetryLog.jsonl");
    }

    public TelemetryPayload CreatePayload(
        ushort rawValue,
        double temperature,
        string operationStatus,
        string alarmStatus,
        DateTimeOffset? timestamp = null)
    {
        return new TelemetryPayload
        {
            DeviceId = _deviceId,
            Timestamp = timestamp ?? DateTimeOffset.Now,
            RawValue = rawValue,
            Temperature = temperature,
            OperationStatus = operationStatus,
            AlarmStatus = alarmStatus
        };
    }

    public TelemetryPayload CreateDisconnectedPayload(
        string errorMessage,
        DateTimeOffset? timestamp = null)
    {
        return new TelemetryPayload
        {
            DeviceId = _deviceId,
            Timestamp = timestamp ?? DateTimeOffset.Now,
            ConnectionStatus = "Disconnected",
            RawValue = null,
            Temperature = null,
            OperationStatus = "Unknown",
            AlarmStatus = "Unknown",
            ErrorMessage = errorMessage
        };
    }

    public string ToJson(TelemetryPayload payload)
    {
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public async Task WriteTelemetryAsync(TelemetryPayload payload, CancellationToken cancellationToken = default)
    {
        try
        {
            string json = ToJson(payload);
            Debug.WriteLine(json);

            string? logDirectory = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            await File.AppendAllTextAsync(_logFilePath, json + Environment.NewLine, cancellationToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Telemetry JSON output failed: {ex}");
        }
    }
}
