namespace PlcMonitorHmi;

public class DiagnosticsStats
{
    public bool   IsCommunicationLost { get; init; }
    public int    ConsecutiveErrors   { get; init; }
    public double OperationRatePct    { get; init; }
    public double AvgTemperature      { get; init; }
    public double MaxTemperature      { get; init; }
    public int    TodayAlarmCount     { get; init; }
}
