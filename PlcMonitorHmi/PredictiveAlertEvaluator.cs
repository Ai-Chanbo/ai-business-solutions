namespace PlcMonitorHmi;

public enum AlertLevel { Normal, Warning, Critical }

public class PredictiveAlertResult
{
    public AlertLevel Level   { get; init; }
    public string     Message { get; init; } = "";
}

public static class PredictiveAlertEvaluator
{
    private const int FiveMinuteSamples = 300; // 1秒間隔 × 300 = 5分

    // 直近5分の温度上昇率を計算する。
    // サンプルが5分未満の場合は取得可能な最古〜最新で計算する。
    // 2点未満のときは 0.0 を返す。
    public static double CalculateRiseRate(IReadOnlyList<double> temps)
    {
        if (temps.Count < 2) return 0.0;
        int lookback = Math.Min(temps.Count, FiveMinuteSamples);
        return temps[^1] - temps[temps.Count - lookback];
    }

    // 予兆アラートレベルを判定する。Critical → Warning → Normal の順に評価する。
    public static PredictiveAlertResult Evaluate(double riseRate, double maxTemp, int todayAlarmCount)
    {
        if (riseRate >= 5.0 || maxTemp >= 70.0)
            return new PredictiveAlertResult { Level = AlertLevel.Critical, Message = "危険" };

        if (riseRate >= 3.0 || todayAlarmCount >= 2)
            return new PredictiveAlertResult { Level = AlertLevel.Warning, Message = "注意" };

        return new PredictiveAlertResult { Level = AlertLevel.Normal, Message = "正常" };
    }
}
