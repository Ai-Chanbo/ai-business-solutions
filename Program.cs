using HslCommunication.ModBus;

const string IpAddress = "127.0.0.1";
const int Port = 502;
const string ReadAddress = "1";
const double Scale = 1000.0;
const double AlarmTemperature = 60.0;
const int IntervalMs = 1000;

string logDirectory = Path.Combine(AppContext.BaseDirectory, "Logs");
Directory.CreateDirectory(logDirectory);

string logFilePath = Path.Combine(
    logDirectory,
    $"plc_log_{DateTime.Now:yyyyMMdd}.csv"
);

if (!File.Exists(logFilePath))
{
    File.AppendAllText(
        logFilePath,
        "時刻,PLC値,温度,稼働状態,異常状態,通信状態" + Environment.NewLine
    );
}

var modbus = new ModbusTcpNet(IpAddress, Port);

Console.WriteLine("PLC温度監視アプリを起動します。");
Console.WriteLine($"接続先: {IpAddress}:{Port}");
Console.WriteLine($"ログ出力先: {logFilePath}");
Console.WriteLine("停止する場合は Ctrl + C を押してください。");

var connectResult = modbus.ConnectServer();

if (!connectResult.IsSuccess)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"接続失敗: {connectResult.Message}");
    Console.ResetColor();
    return;
}

Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("接続成功");
Console.ResetColor();

while (true)
{
    try
    {
        var result = modbus.ReadUInt16(ReadAddress, 1);
        DateTime now = DateTime.Now;

        if (result.IsSuccess)
        {
            ushort rawValue = result.Content[0];

            // PLC値を温度として扱う想定
            // 例：8050 → 8.05℃
            // 例：60000 → 60.0℃を超えると異常
            double temperature = rawValue / Scale;

            string operationStatus = rawValue == 0 ? "停止中" : "稼働中";
            string alarmStatus = temperature > AlarmTemperature ? "異常" : "正常";
            string communicationStatus = "正常";

            Console.WriteLine(
                $"[{now:yyyy/MM/dd HH:mm:ss}] PLC値:{rawValue} 温度:{temperature:F1}℃ {operationStatus} {alarmStatus}"
            );

            if (alarmStatus == "異常")
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("温度異常発生！");
                Console.ResetColor();
            }

            string log =
                $"{now:yyyy/MM/dd HH:mm:ss},{rawValue},{temperature:F1},{operationStatus},{alarmStatus},{communicationStatus}";

            File.AppendAllText(logFilePath, log + Environment.NewLine);
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[{now:yyyy/MM/dd HH:mm:ss}] 読取失敗: {result.Message}");
            Console.ResetColor();

            string log =
                $"{now:yyyy/MM/dd HH:mm:ss},,,不明,不明,読取失敗";

            File.AppendAllText(logFilePath, log + Environment.NewLine);
        }
    }
    catch (Exception ex)
    {
        DateTime now = DateTime.Now;

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[{now:yyyy/MM/dd HH:mm:ss}] 例外発生: {ex.Message}");
        Console.ResetColor();

        string log =
            $"{now:yyyy/MM/dd HH:mm:ss},,,不明,不明,例外発生:{ex.Message}";

        File.AppendAllText(logFilePath, log + Environment.NewLine);
    }

    Thread.Sleep(IntervalMs);
}