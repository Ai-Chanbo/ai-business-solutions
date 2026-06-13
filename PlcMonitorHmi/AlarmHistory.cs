namespace PlcMonitorHmi;

public class AlarmHistory
{
    public DateTime  StartTime       { get; set; }
    public DateTime? EndTime         { get; set; }
    public double    DurationSeconds { get; set; }
    public double    MaxTemperature  { get; set; }
}
