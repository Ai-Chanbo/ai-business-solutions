using System.Drawing;
using System.Text;
using System.Windows.Forms;
using HslCommunication.ModBus;
using ScottPlot.WinForms;

namespace PlcMonitorHmi;

public partial class Form1 : Form
{
    // ── 既存フィールド（変更なし） ────────────────────────
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

    // ── ScottPlot フィールド（Version 2、変更なし） ────────
    private FormsPlot _chart = new();
    private readonly List<double> _chartTimes = new(MaxDataPoints + 4);
    private readonly List<double> _chartTemps = new(MaxDataPoints + 4);
    private const int MaxDataPoints = 600;

    // ── アラーム履歴フィールド（Version 3、変更なし） ───────
    private bool     _isAlarm       = false;
    private DateTime _alarmStartTime;
    private double   _alarmMaxTemp  = 0.0;
    private readonly List<AlarmHistory> _alarmHistories = new();
    private DataGridView gridAlarm = new();
    private const int MaxAlarmHistory = 100;

    private static readonly string AlarmCsvPath =
        Path.Combine(AppContext.BaseDirectory, "Logs", "AlarmHistory.csv");

    // ── Version 4 Phase 4a: 設備診断フィールド ───────────
    private int _commFailCount = 0;
    private const int CommFailThreshold = 5;
    private int    _totalSamples   = 0;
    private int    _runningSamples = 0;
    private double _sessionMaxTemp = 0.0;

    private Panel pnlDiagnostics     = new();
    private Label lblDiagCommStatus  = new();
    private Label lblDiagOpRate      = new();
    private Label lblDiagAvgTemp     = new();
    private Label lblDiagMaxTemp     = new();
    private Label lblDiagTodayAlarms = new();

    // Version 4 Phase 4b: 予兆保全フィールド
    private Label lblDiagRiseRate   = new();
    private Label lblDiagPredictive = new();

    // ── コンストラクタ ─────────────────────────────────
    public Form1()
    {
        InitializeComponent();
        InitializeHmi();
    }

    // ── UI 初期化 ─────────────────────────────────────
    private void InitializeHmi()
    {
        Text        = "PLC温度監視HMI v4";
        Width       = 1050;
        Height      = 950;
        MinimumSize = new Size(900, 750);
        BackColor   = Color.FromArgb(22, 22, 36);

        // 左側ステータスパネル (265px)
        var pnlStatus = new Panel
        {
            Bounds    = new Rectangle(10, 10, 265, 890),
            BackColor = Color.FromArgb(30, 30, 46),
        };

        lblConnection.SetBounds(10, 15, 245, 28);
        lblConnection.Text      = "接続状態：未接続";
        lblConnection.ForeColor = Color.Silver;
        lblConnection.Font      = new Font("Yu Gothic UI", 10);

        lblTemperature.SetBounds(10, 55, 245, 55);
        lblTemperature.Text      = "現在温度：--- ℃";
        lblTemperature.ForeColor = Color.White;
        lblTemperature.Font      = new Font("Yu Gothic UI", 18, FontStyle.Bold);

        lblOperation.SetBounds(10, 120, 245, 28);
        lblOperation.Text      = "稼働状態：---";
        lblOperation.ForeColor = Color.Silver;
        lblOperation.Font      = new Font("Yu Gothic UI", 10);

        lblAlarm.SetBounds(10, 158, 245, 46);
        lblAlarm.Text      = "異常状態：---";
        lblAlarm.ForeColor = Color.Silver;
        lblAlarm.Font      = new Font("Yu Gothic UI", 14, FontStyle.Bold);

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

        gridLog.SetBounds(10, 270, 245, 610);
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

        // 右側チャート（v4: 420 → 350 に縮小して診断パネル領域を確保）
        _chart.Bounds    = new Rectangle(285, 10, 750, 350);
        _chart.BackColor = Color.FromArgb(22, 22, 36);

        Controls.Add(pnlStatus);
        Controls.Add(_chart);

        btnConnect.Click += BtnConnect_Click;
        btnStop.Click    += BtnStop_Click;
        _timer.Interval = 1000;
        _timer.Tick    += Timer_Tick;

        SetupChart();
        SetupDiagnosticsPanel(); // v4 追加
        SetupAlarmGrid();
    }

    // ── チャート初期設定（変更なし） ──────────────────────
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

    // ── チャート更新（変更なし） ──────────────────────────
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

    // ── 設備診断パネル初期設定（Version 4） ───────────────
    private void SetupDiagnosticsPanel()
    {
        // セクションラベル: chart 下端(360) + 8px 隙間
        var lblSection = new Label
        {
            Bounds    = new Rectangle(285, 368, 750, 22),
            Text      = "■ 設備診断サマリ",
            ForeColor = Color.FromArgb(100, 200, 255),
            BackColor = Color.FromArgb(22, 22, 36),
            Font      = new Font("Yu Gothic UI", 10, FontStyle.Bold),
        };
        Controls.Add(lblSection);

        // 診断パネル本体: ラベル下端(390) + 3px 隙間
        pnlDiagnostics.Bounds    = new Rectangle(285, 393, 750, 175); // v4b: 120→175
        pnlDiagnostics.BackColor = Color.FromArgb(25, 28, 48);
        Controls.Add(pnlDiagnostics);

        var labelFont = new Font("Yu Gothic UI", 10);

        // 上段3列 (y=10, h=45)
        lblDiagCommStatus.SetBounds(10, 10, 235, 45);
        lblDiagCommStatus.Text      = "通信状態\n---";
        lblDiagCommStatus.ForeColor = Color.Silver;
        lblDiagCommStatus.Font      = labelFont;

        lblDiagOpRate.SetBounds(255, 10, 235, 45);
        lblDiagOpRate.Text      = "稼働率\n---";
        lblDiagOpRate.ForeColor = Color.Silver;
        lblDiagOpRate.Font      = labelFont;

        lblDiagTodayAlarms.SetBounds(500, 10, 235, 45);
        lblDiagTodayAlarms.Text      = "本日アラーム\n---";
        lblDiagTodayAlarms.ForeColor = Color.Silver;
        lblDiagTodayAlarms.Font      = labelFont;

        // 下段2列 (y=65, h=45)
        lblDiagAvgTemp.SetBounds(10, 65, 235, 45);
        lblDiagAvgTemp.Text      = "平均温度\n---";
        lblDiagAvgTemp.ForeColor = Color.Silver;
        lblDiagAvgTemp.Font      = labelFont;

        lblDiagMaxTemp.SetBounds(255, 65, 235, 45);
        lblDiagMaxTemp.Text      = "最大温度\n---";
        lblDiagMaxTemp.ForeColor = Color.Silver;
        lblDiagMaxTemp.Font      = labelFont;

        // 第3行 (y=120, h=45): 温度上昇率 | 予兆状態（v4b）
        lblDiagRiseRate.SetBounds(10, 120, 235, 45);
        lblDiagRiseRate.Text      = "温度上昇率\n---";
        lblDiagRiseRate.ForeColor = Color.Silver;
        lblDiagRiseRate.Font      = labelFont;

        lblDiagPredictive.SetBounds(255, 120, 235, 45);
        lblDiagPredictive.Text      = "予兆状態\n---";
        lblDiagPredictive.ForeColor = Color.Silver;
        lblDiagPredictive.Font      = labelFont;

        pnlDiagnostics.Controls.Add(lblDiagCommStatus);
        pnlDiagnostics.Controls.Add(lblDiagOpRate);
        pnlDiagnostics.Controls.Add(lblDiagTodayAlarms);
        pnlDiagnostics.Controls.Add(lblDiagAvgTemp);
        pnlDiagnostics.Controls.Add(lblDiagMaxTemp);
        pnlDiagnostics.Controls.Add(lblDiagRiseRate);
        pnlDiagnostics.Controls.Add(lblDiagPredictive);
    }

    // ── アラーム履歴グリッド初期設定（v4: y座標調整） ─────
    private void SetupAlarmGrid()
    {
        // セクションラベル: pnlDiagnostics 下端(513) + 8px 隙間
        var lblAlarmSection = new Label
        {
            Bounds    = new Rectangle(285, 576, 750, 22),  // v4b: 521→576 (+55)
            Text      = "■ アラーム履歴",
            ForeColor = Color.FromArgb(255, 120, 80),
            BackColor = Color.FromArgb(22, 22, 36),
            Font      = new Font("Yu Gothic UI", 10, FontStyle.Bold),
        };
        Controls.Add(lblAlarmSection);

        // アラーム履歴グリッド: ラベル下端(543) + 3px 隙間
        gridAlarm.Bounds              = new Rectangle(285, 601, 750, 285); // v4b: 546→601, 340→285
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

    // ── 設備診断更新（Version 4） ─────────────────────────

    private void UpdateDiagnostics(double temperature, ushort rawValue)
    {
        _commFailCount  = 0;
        _totalSamples++;
        if (rawValue != 0) _runningSamples++;
        _sessionMaxTemp = Math.Max(_sessionMaxTemp, temperature);

        var stats   = BuildDiagnosticsStats(temperature, rawValue);
        double rise = PredictiveAlertEvaluator.CalculateRiseRate(_chartTemps);
        var    alert = PredictiveAlertEvaluator.Evaluate(rise, _sessionMaxTemp, stats.TodayAlarmCount);
        UpdateDiagnosticsUI(stats, rise, alert);
    }

    private void UpdateDiagnosticsOnCommunicationFailure()
    {
        _commFailCount++;
        var stats = new DiagnosticsStats
        {
            IsCommunicationLost = _commFailCount >= CommFailThreshold,
            ConsecutiveErrors   = _commFailCount,
            OperationRatePct    = _totalSamples > 0
                                    ? (double)_runningSamples / _totalSamples * 100
                                    : 0.0,
            AvgTemperature      = _chartTemps.Count > 0 ? _chartTemps.Average() : 0.0,
            MaxTemperature      = _sessionMaxTemp,
            TodayAlarmCount     = _alarmHistories.Count(h => h.StartTime.Date == DateTime.Today),
        };
        double rise  = PredictiveAlertEvaluator.CalculateRiseRate(_chartTemps);
        var    alert = PredictiveAlertEvaluator.Evaluate(rise, _sessionMaxTemp, stats.TodayAlarmCount);
        UpdateDiagnosticsUI(stats, rise, alert);
    }

    private DiagnosticsStats BuildDiagnosticsStats(double temperature, ushort rawValue)
    {
        return new DiagnosticsStats
        {
            IsCommunicationLost = false,
            ConsecutiveErrors   = 0,
            OperationRatePct    = _totalSamples > 0
                                    ? (double)_runningSamples / _totalSamples * 100
                                    : 0.0,
            AvgTemperature      = _chartTemps.Count > 0 ? _chartTemps.Average() : 0.0,
            MaxTemperature      = _sessionMaxTemp,
            TodayAlarmCount     = _alarmHistories.Count(h => h.StartTime.Date == DateTime.Today),
        };
    }

    private void UpdateDiagnosticsUI(DiagnosticsStats stats, double riseRate, PredictiveAlertResult alert)
    {
        // 通信状態
        if (stats.IsCommunicationLost)
        {
            lblDiagCommStatus.Text      = "通信状態\n通信断";
            lblDiagCommStatus.ForeColor = Color.OrangeRed;
        }
        else if (stats.ConsecutiveErrors > 0)
        {
            lblDiagCommStatus.Text      = $"通信状態\n不安定({stats.ConsecutiveErrors}回)";
            lblDiagCommStatus.ForeColor = Color.Orange;
        }
        else
        {
            lblDiagCommStatus.Text      = "通信状態\n✓ 正常";
            lblDiagCommStatus.ForeColor = Color.LimeGreen;
        }

        // 稼働率
        lblDiagOpRate.Text      = $"稼働率\n{stats.OperationRatePct:F1} %";
        lblDiagOpRate.ForeColor = stats.OperationRatePct >= 80 ? Color.LimeGreen : Color.Orange;

        // 本日アラーム
        lblDiagTodayAlarms.Text      = $"本日アラーム\n{stats.TodayAlarmCount} 回";
        lblDiagTodayAlarms.ForeColor = stats.TodayAlarmCount == 0 ? Color.LimeGreen : Color.OrangeRed;

        // 平均温度
        lblDiagAvgTemp.Text      = $"平均温度\n{stats.AvgTemperature:F1} ℃";
        lblDiagAvgTemp.ForeColor = Color.CornflowerBlue;

        // 最大温度
        lblDiagMaxTemp.Text      = $"最大温度\n{stats.MaxTemperature:F1} ℃";
        lblDiagMaxTemp.ForeColor = stats.MaxTemperature > AlarmTemperature
                                    ? Color.OrangeRed
                                    : Color.CornflowerBlue;

        // 温度上昇率（v4b）
        if (_chartTemps.Count >= 2)
        {
            string sign = riseRate >= 0 ? "+" : "";
            lblDiagRiseRate.Text      = $"温度上昇率\n{sign}{riseRate:F1} ℃ / 5分";
            lblDiagRiseRate.ForeColor = riseRate >= 5.0 ? Color.OrangeRed
                                      : riseRate >= 3.0 ? Color.Orange
                                      : Color.CornflowerBlue;
        }
        else
        {
            lblDiagRiseRate.Text      = "温度上昇率\n---";
            lblDiagRiseRate.ForeColor = Color.Silver;
        }

        // 予兆状態（v4b）
        lblDiagPredictive.Text      = $"予兆状態\n{alert.Message}";
        lblDiagPredictive.ForeColor = alert.Level switch
        {
            AlertLevel.Critical => Color.OrangeRed,
            AlertLevel.Warning  => Color.Orange,
            _                   => Color.LimeGreen,
        };
    }

    // ── アラーム状態管理（Version 3、変更なし） ────────────

    private void UpdateAlarmHistory(double temperature, string alarmStatus)
    {
        if (alarmStatus == "異常")
        {
            if (!_isAlarm)
            {
                _isAlarm        = true;
                _alarmStartTime = DateTime.Now;
                _alarmMaxTemp   = temperature;
            }
            else if (temperature > _alarmMaxTemp)
            {
                _alarmMaxTemp = temperature;
            }
        }
        else
        {
            if (_isAlarm)
            {
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
        _alarmHistories.Insert(0, alarm);
        if (_alarmHistories.Count > MaxAlarmHistory)
            _alarmHistories.RemoveAt(_alarmHistories.Count - 1);

        gridAlarm.Rows.Insert(0,
            alarm.StartTime.ToString("HH:mm:ss"),
            alarm.EndTime.HasValue ? alarm.EndTime.Value.ToString("HH:mm:ss") : "-",
            $"{alarm.DurationSeconds:F1}",
            $"{alarm.MaxTemperature:F1}");

        if (gridAlarm.Rows.Count > MaxAlarmHistory)
            gridAlarm.Rows.RemoveAt(gridAlarm.Rows.Count - 1);

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

    // ── ボタン・タイマーハンドラ（v4: 修正あり） ────────────

    private void BtnConnect_Click(object? sender, EventArgs e)
    {
        // v4: 接続開始時に診断カウンタをリセット
        _totalSamples   = 0;
        _runningSamples = 0;
        _sessionMaxTemp = 0.0;
        _commFailCount  = 0;

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
            UpdateDiagnosticsOnCommunicationFailure(); // v4 追加
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
        UpdateAlarmHistory(temperature, alarmStatus);
        UpdateDiagnostics(temperature, rawValue); // v4 追加
    }
}
