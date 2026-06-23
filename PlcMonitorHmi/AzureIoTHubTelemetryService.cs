using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Devices.Client;

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

public sealed class AzureIoTHubTelemetryService : IDisposable
{
    private const string DefaultDeviceId = "plc-monitor-demo-001";
    private const string DeviceConnectionStringEnvironmentVariable = "AZURE_IOT_HUB_DEVICE_CONNECTION_STRING";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly string _deviceId;
    private readonly string _logFilePath;
    private readonly DeviceClient? _deviceClient;

    public AzureIoTHubTelemetryService(string? deviceId = null)
    {
        _deviceId = string.IsNullOrWhiteSpace(deviceId) ? DefaultDeviceId : deviceId;
        _logFilePath = Path.Combine(AppContext.BaseDirectory, "Logs", "TelemetryLog.jsonl");

        string? connectionString = Environment.GetEnvironmentVariable(DeviceConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Debug.WriteLine("Azure IoT Hub connection string is not set. Telemetry will be written to JSONL only.");
            return;
        }

        try
        {
            _deviceClient = DeviceClient.CreateFromConnectionString(connectionString, TransportType.Mqtt);
            Debug.WriteLine("Azure IoT Hub DeviceClient initialized.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Azure IoT Hub DeviceClient initialization failed: {ex}");
        }
    }

    public TelemetryPayload CreatePayload(
        ushort rawValue,
        double temperature,
        string operationStatus,
        string alarmStatus,
        string connectionStatus = "Connected",
        DateTimeOffset? timestamp = null)
    {
        return new TelemetryPayload
        {
            DeviceId = _deviceId,
            Timestamp = timestamp ?? DateTimeOffset.Now,
            ConnectionStatus = connectionStatus,
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
        string json = ToJson(payload);
        Debug.WriteLine(json);

        try
        {
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

        if (_deviceClient is null)
        {
            Debug.WriteLine("Azure IoT Hub send skipped: connection string is not configured or client initialization failed.");
            return;
        }

        try
        {
            using var message = new Microsoft.Azure.Devices.Client.Message(Encoding.UTF8.GetBytes(json))
            {
                ContentType = "application/json",
                ContentEncoding = "utf-8"
            };

            await _deviceClient.SendEventAsync(message, cancellationToken);
            Debug.WriteLine("Azure IoT Hub telemetry send succeeded.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Azure IoT Hub telemetry send failed: {ex}");
        }
    }

    public void Dispose()
    {
        _deviceClient?.Dispose();
    }
}
