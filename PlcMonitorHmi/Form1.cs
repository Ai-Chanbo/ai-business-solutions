using System.Drawing;
using System.Text;
using System.Windows.Forms;
using HslCommunication.ModBus;
using ScottPlot.WinForms;

namespace PlcMonitorHmi;

public partial class Form1 : Form
{
    // ──────────────────────────────────────────────────
    //  既存フィールド（変更なし）
    // ──────────────────────────────────────────────────
    private readonly ModbusTcpNet _modbus = new("127.0.0.1", 502);
    private readonly System.Windows.Forms.Timer _timer = new();

    private DataGridView gridLog        = new();
    private Label        lblConnection  = new();
    private Label        lblTemperature = new();
    private Label        lblOperation   = new();
    private Label        lblAlarm       = new();
    private Button       btnConnect     = new();
    private Button       btnStop        = new();

    private const double TemperatureScale = 1000.0;
    private const double AlarmTemperature = 60.0;

    // ──────────────────────────────────────────────────
    //  ScottPlot フィールド（Version 2、変更なし）
    // ──────────────────────────────────────────────────
    private FormsPlot _chart = new();
    private readonly List<double> _chartTimes = new(MaxDataPoints + 4);
    private readonly List<double> _chartTemps = new(MaxDataPoints + 4);
    private const int MaxDataPoints = 600; // 10 分 × 60 秒

    // ──────────────────────────────────────────────────
    //  アラーム履歴フィールド（Version 3 追加）
    // ──────────────────────────────────────────────────
    private bool     _isAlarm       = false;
    private DateTime _alarmStartTime;
    private double   _alarmMaxTemp  = 0.0;
    private readonly List<AlarmHistory> _alarmHistories = new();
    private DataGridView gridAlarm = new();
    private const int MaxAlarmHistory = 100;

    private static readonly string AlarmCsvPath =
        Path.Combine(AppContext.BaseDirectory, "Logs", "AlarmHistory.csv");

    // ──────────────────────────────────────────────────
    //  コンストラクタ
    // ──────────────────────────────────────────────────
    public Form1()
    {
        InitializeComponent();
        InitializeHmi();
    }

    // ──────────────────────────────────────────────────
    //  UI 初期化
    // ──────────────────────────────────────────────────
    private void InitializeHmi()
    {
        Text        = "PLC温度監視HMI v3";
        Width       = 1050;
        Height      = 950;          // v3: 710 → 950（アラーム履歴グリッド追加）
        MinimumSize = new Size(900, 750);
        BackColor   = Color.FromArgb(22, 22, 36);

        // ── 左側ステータスパネル (265px) ──────────────
        var pnlStatus = new Panel
        {
            Bounds    = new Rectangle(10, 10, 265, 890), // v3: 650 → 890
            BackColor = Color.FromArgb(30, 30, 46),
        };

        // 接続ラベル
        lblConnection.SetBounds(10, 15, 245, 28);
        lblConnection.Text      = "接続状態：未接続";
        lblConnection.ForeColor = Color.Silver;
        lblConnection.Font      = new Font("Yu Gothic UI", 10);

        // 温度ラベル（大フォント）
        lblTemperature.SetBounds(10, 55, 245, 55);
        lblTemperature.Text      = "現在温度：--- ℃";
        lblTemperature.ForeColor = Color.White;
        lblTemperature.Font      = new Font("Yu Gothic UI", 18, FontStyle.Bold);

        // 稼働状態ラベル
        lblOperation.SetBounds(10, 120, 245, 28);
        lblOperation.Text      = "稼働状態：---";
        lblOperation.ForeColor = Color.Silver;
        lblOperation.Font      = new Font("Yu Gothic UI", 10);

        // 異常状態ラベル
        lblAlarm.SetBounds(10, 158, 245, 46);
        lblAlarm.Text      = "異常状態：---";
        lblAlarm.ForeColor = Color.Silver;
        lblAlarm.Font      = new Font("Yu Gothic UI", 14, FontStyle.Bold);

        // ボタン
        btnConnect.SetBounds(10, 218, 120, 36);
        btnConnect.Text      = "接続開始";
        btnConnect.BackColor = Color.FromArgb(50, 110, 70);
        btnConnect.ForeColor = Color.White;
        btnConnect.FlatStyle = FlatStyle.Flat;
        btnConnect.FlatAppearance.BorderColor = Color.FromArgb(70, 140, 90);

        btnStop.SetBounds(140, 218, 115, 36);
        btnStop.Text      = "停止";
        btnStop.BackColor = Color.FromArgb(100, 40, 40);
        btnStop.ForeColor = Color.White;
        btnStop.FlatStyle = FlatStyle.Flat;
        btnStop.FlatAppearance.BorderColor = Color.FromArgb(140, 60, 60);

        // DataGridView（通信ログ）
        gridLog.SetBounds(10, 270, 245, 610); // v3: 370 → 610（パネル拡張に合わせて伸長）
        gridLog.AllowUserToAddRows  = false;
        gridLog.ReadOnly            = true;
        gridLog.RowHeadersVisible   = false;
        gridLog.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        gridLog.EnableHeadersVisualStyles             = false;
        gridLog.BackgroundColor                       = Color.FromArgb(22, 22, 36);
        gridLog.ForeColor                             = Color.Silver;
        gridLog.GridColor                             = Color.FromArgb(55, 55, 75);
        gridLog.DefaultCellStyle.BackColor            = Color.FromArgb(28, 28, 44);
        gridLog.DefaultCellStyle.ForeColor            = Color.Silver;
        gridLog.DefaultCellStyle.SelectionBackColor   = Color.FromArgb(60, 80, 120);
        gridLog.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(38, 38, 58);
        gridLog.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;

        gridLog.Columns.Add("Time",            "時刻");
        gridLog.Columns.Add("RawValue",        "PLC値");
        gridLog.Columns.Add("Temperature",     "温度");
        gridLog.Columns.Add("OperationStatus", "稼働");
        gridLog.Columns.Add("AlarmStatus",     "異常");

        pnlStatus.Controls.Add(lblConnection);
        pnlStatus.Controls.Add(lblTemperature);
        pnlStatus.Controls.Add(lblOperation);
        pnlStatus.Controls.Add(lblAlarm);
        pnlStatus.Controls.Add(btnConnect);
        pnlStatus.Controls.Add(btnStop);
        pnlStatus.Controls.Add(gridLog);

        // ── 右側チャート（v3: 高さ 650 → 420 に縮小してアラームグリッドの領域を確保）
        _chart.Bounds    = new Rectangle(285, 10, 750, 420); // v3: 650 → 420
        _chart.BackColor = Color.FromArgb(22, 22, 36);

        Controls.Add(pnlStatus);
        Controls.Add(_chart);

        // ── イベント・タイマー ──────────────────────────
        btnConnect.Click += BtnConnect_Click;
        btnStop.Click    += BtnStop_Click;

        _timer.Interval = 1000;
        _timer.Tick    += Timer_Tick;

        SetupChart();
        SetupAlarmGrid(); // v3 追加
    }

    // ──────────────────────────────────────────────────
    //  チャート初期設定（変更なし）
    // ──────────────────────────────────────────────────
    private void SetupChart()
    {
        var plot = _chart.Plot;

        plot.FigureBackground.Color = ScottPlot.Color.FromColor(Color.FromArgb(22, 22, 36));
        plot.DataBackground.Color   = ScottPlot.Color.FromColor(Color.FromArgb(28, 28, 44));
        plot.Axes.Color(ScottPlot.Color.FromColor(Color.Silver));

        plot.Title("温度トレンド（直近10分）");
        plot.YLabel("温度 (℃)");
        plot.Axes.DateTimeTicksBottom();

        var alarm = plot.Add.HorizontalLine(AlarmTemperature);
        alarm.Color       = ScottPlot.Color.FromColor(Color.OrangeRed);
        alarm.LineWidth   = 1.5f;
        alarm.LinePattern = ScottPlot.LinePattern.Dashed;

        _chart.Refresh();
    }

    // ──────────────────────────────────────────────────
    //  チャート更新（変更なし）
    // ──────────────────────────────────────────────────
    private void UpdateChart(double temperature)
    {
        _chartTimes.Add(DateTime.Now.ToOADate());
        _chartTemps.Add(temperature);

        if (_chartTimes.Count > MaxDataPoints)
        {
            _chartTimes.RemoveAt(0);
            _chartTemps.RemoveAt(0);
        }

        var plot = _chart.Plot;
        plot.Clear();

        var alarm = plot.Add.HorizontalLine(AlarmTemperature);
        alarm.Color       = ScottPlot.Color.FromColor(Color.OrangeRed);
        alarm.LineWidth   = 1.5f;
        alarm.LinePattern = ScottPlot.LinePattern.Dashed;

        if (_chartTimes.Count >= 2)
        {
            var scatter = plot.Add.Scatter(_chartTimes.ToArray(), _chartTemps.ToArray());
            scatter.Color      = ScottPlot.Color.FromColor(Color.CornflowerBlue);
            scatter.LineWidth  = 2f;
            scatter.MarkerSize = 0f;
        }

        plot.Axes.DateTimeTicksBottom();
        _chart.Refresh();
    }

    // ──────────────────────────────────────────────────
    //  アラーム履歴グリッド初期設定（Version 3）
    // ──────────────────────────────────────────────────
    private void SetupAlarmGrid()
    {
        // セクションラベル
        var lblAlarmSection = new Label
        {
            Bounds    = new Rectangle(285, 440, 750, 22),
            Text      = "■ アラーム履歴",
            ForeColor = Color.FromArgb(255, 120, 80),
            BackColor = Color.FromArgb(22, 22, 36),
            Font      = new Font("Yu Gothic UI", 10, FontStyle.Bold),
        };
        Controls.Add(lblAlarmSection);

        // アラーム履歴グリッド
        gridAlarm.Bounds            = new Rectangle(285, 465, 750, 425);
        gridAlarm.AllowUserToAddRows  = false;
        gridAlarm.ReadOnly            = true;
        gridAlarm.RowHeadersVisible   = false;
        gridAlarm.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        gridAlarm.EnableHeadersVisualStyles             = false;
        gridAlarm.BackgroundColor                       = Color.FromArgb(22, 22, 36);
        gridAlarm.ForeColor                             = Color.FromArgb(255, 190, 170);
        gridAlarm.GridColor                             = Color.FromArgb(70, 40, 40);
        gridAlarm.DefaultCellStyle.BackColor            = Color.FromArgb(35, 20, 20);
        gridAlarm.DefaultCellStyle.ForeColor            = Color.FromArgb(255, 190, 170);
        gridAlarm.DefaultCellStyle.SelectionBackColor   = Color.FromArgb(80, 40, 40);
        gridAlarm.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(55, 28, 28);
        gridAlarm.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(255, 140, 100);

        gridAlarm.Columns.Add("StartTime",       "発生時刻");
        gridAlarm.Columns.Add("EndTime",         "復旧時刻");
        gridAlarm.Columns.Add("DurationSeconds", "継続時間(秒)");
        gridAlarm.Columns.Add("MaxTemperature",  "最大温度(℃)");

        Controls.Add(gridAlarm);
    }

    // ──────────────────────────────────────────────────
    //  アラーム状態管理（Version 3）
    // ──────────────────────────────────────────────────

    // Timer_Tick から毎秒呼び出される。60℃の立ち上がり/立ち下がりエッジを検出する。
    private void UpdateAlarmHistory(double temperature, string alarmStatus)
    {
        if (alarmStatus == "異常")
        {
            if (!_isAlarm)
            {
                // アラーム開始（立ち上がりエッジ）
                _isAlarm        = true;
                _alarmStartTime = DateTime.Now;
                _alarmMaxTemp   = temperature;
            }
            else if (temperature > _alarmMaxTemp)
            {
                // 継続中：最大温度を更新
                _alarmMaxTemp = temperature;
            }
        }
        else
        {
            if (_isAlarm)
            {
                // アラーム復旧（立ち下がりエッジ）
                _isAlarm = false;
                var endTime = DateTime.Now;
                RegisterAlarm(new AlarmHistory
                {
                    StartTime       = _alarmStartTime,
                    EndTime         = endTime,
                    DurationSeconds = (endTime - _alarmStartTime).TotalSeconds,
                    MaxTemperature  = _alarmMaxTemp,
                });
            }
        }
    }

    private void RegisterAlarm(AlarmHistory alarm)
    {
        // メモリリスト（最大 100 件、新しい順）
        _alarmHistories.Insert(0, alarm);
        if (_alarmHistories.Count > MaxAlarmHistory)
            _alarmHistories.RemoveAt(_alarmHistories.Count - 1);

        // DataGridView 更新（先頭行に挿入）
        gridAlarm.Rows.Insert(0,
            alarm.StartTime.ToString("HH:mm:ss"),
            alarm.EndTime.HasValue ? alarm.EndTime.Value.ToString("HH:mm:ss") : "-",
            $"{alarm.DurationSeconds:F1}",
            $"{alarm.MaxTemperature:F1}");

        if (gridAlarm.Rows.Count > MaxAlarmHistory)
            gridAlarm.Rows.RemoveAt(gridAlarm.Rows.Count - 1);

        // CSV 保存
        SaveAlarmToCsv(alarm);
    }

    private void SaveAlarmToCsv(AlarmHistory alarm)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AlarmCsvPath)!);

            bool writeHeader = !File.Exists(AlarmCsvPath);
            using var sw = new StreamWriter(
                AlarmCsvPath, append: true,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            if (writeHeader)
                sw.WriteLine("発生時刻,復旧時刻,継続時間(秒),最大温度(℃)");

            sw.WriteLine(string.Join(",",
                alarm.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                alarm.EndTime.HasValue
                    ? alarm.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss")
                    : "",
                alarm.DurationSeconds.ToString("F1"),
                alarm.MaxTemperature.ToString("F1")));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AlarmCsv] 書込み失敗: {ex.Message}");
        }
    }

    // ──────────────────────────────────────────────────
    //  既存メソッド（ロジック変更なし）
    // ──────────────────────────────────────────────────

    private void BtnConnect_Click(object? sender, EventArgs e)
    {
        var result = _modbus.ConnectServer();

        if (result.IsSuccess)
        {
            lblConnection.Text      = "接続状態：接続成功";
            lblConnection.ForeColor = Color.LimeGreen;
            _timer.Start();
        }
        else
        {
            lblConnection.Text      = "接続状態：接続失敗";
            lblConnection.ForeColor = Color.Red;
        }
    }

    private void BtnStop_Click(object? sender, EventArgs e)
    {
        _timer.Stop();
        lblConnection.Text      = "接続状態：停止中";
        lblConnection.ForeColor = Color.Black;
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        var result = _modbus.ReadUInt16("1", 1);

        if (!result.IsSuccess)
        {
            lblConnection.Text      = "接続状態：読取失敗";
            lblConnection.ForeColor = Color.Red;
            return;
        }

        ushort rawValue    = result.Content[0];
        double temperature = rawValue / TemperatureScale;

        string operationStatus = rawValue == 0 ? "停止中" : "稼働中";
        string alarmStatus     = temperature > AlarmTemperature ? "異常" : "正常";

        lblTemperature.Text = $"現在温度：{temperature:F1} ℃";
        lblOperation.Text   = $"稼働状態：{operationStatus}";
        lblAlarm.Text       = $"異常状態：{alarmStatus}";
        lblAlarm.ForeColor  = alarmStatus == "異常" ? Color.Red : Color.Green;

        gridLog.Rows.Insert(
            0,
            DateTime.Now.ToString("HH:mm:ss"),
            rawValue,
            $"{temperature:F1}℃",
            operationStatus,
            alarmStatus);

        if (alarmStatus == "異常")
            gridLog.Rows[0].DefaultCellStyle.BackColor = Color.LightPink;

        if (gridLog.Rows.Count > 100)
            gridLog.Rows.RemoveAt(gridLog.Rows.Count - 1);

        UpdateChart(temperature);
        UpdateAlarmHistory(temperature, alarmStatus); // v3 追加
    }
}
