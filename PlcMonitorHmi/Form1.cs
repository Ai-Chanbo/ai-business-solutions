using System.Drawing;
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

    private DataGridView gridLog     = new();
    private Label        lblConnection  = new();
    private Label        lblTemperature = new();
    private Label        lblOperation   = new();
    private Label        lblAlarm       = new();
    private Button       btnConnect     = new();
    private Button       btnStop        = new();

    private const double TemperatureScale = 1000.0;
    private const double AlarmTemperature = 60.0;

    // ──────────────────────────────────────────────────
    //  追加フィールド（ScottPlot）
    // ──────────────────────────────────────────────────
    private FormsPlot _chart = new();
    private readonly List<double> _chartTimes = new(MaxDataPoints + 4);
    private readonly List<double> _chartTemps = new(MaxDataPoints + 4);
    private const int MaxDataPoints = 600; // 10 分 × 60 秒

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
        Text        = "PLC温度監視HMI v2";
        Width       = 1050;
        Height      = 710;
        MinimumSize = new Size(900, 600);
        BackColor   = Color.FromArgb(22, 22, 36);

        // ── 左側ステータスパネル (270px) ──────────────
        var pnlStatus = new Panel
        {
            Bounds    = new Rectangle(10, 10, 265, 650),
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

        // DataGridView（ログ）
        gridLog.SetBounds(10, 270, 245, 370);
        gridLog.AllowUserToAddRows  = false;
        gridLog.ReadOnly            = true;
        gridLog.RowHeadersVisible   = false;
        gridLog.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        gridLog.EnableHeadersVisualStyles   = false;
        gridLog.BackgroundColor             = Color.FromArgb(22, 22, 36);
        gridLog.ForeColor                   = Color.Silver;
        gridLog.GridColor                   = Color.FromArgb(55, 55, 75);
        gridLog.DefaultCellStyle.BackColor        = Color.FromArgb(28, 28, 44);
        gridLog.DefaultCellStyle.ForeColor        = Color.Silver;
        gridLog.DefaultCellStyle.SelectionBackColor = Color.FromArgb(60, 80, 120);
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

        // ── 右側チャート ──────────────────────────────
        _chart.Bounds    = new Rectangle(285, 10, 750, 650);
        _chart.BackColor = Color.FromArgb(22, 22, 36);

        Controls.Add(pnlStatus);
        Controls.Add(_chart);

        // ── イベント・タイマー ──────────────────────────
        btnConnect.Click += BtnConnect_Click;
        btnStop.Click    += BtnStop_Click;

        _timer.Interval = 1000;
        _timer.Tick    += Timer_Tick;

        SetupChart();
    }

    // ──────────────────────────────────────────────────
    //  チャート初期設定
    // ──────────────────────────────────────────────────
    private void SetupChart()
    {
        var plot = _chart.Plot;

        // 背景をダーク設定
        plot.FigureBackground.Color = ScottPlot.Color.FromColor(Color.FromArgb(22, 22, 36));
        plot.DataBackground.Color   = ScottPlot.Color.FromColor(Color.FromArgb(28, 28, 44));

        // 軸全体をシルバーに
        plot.Axes.Color(ScottPlot.Color.FromColor(Color.Silver));

        // タイトル・軸ラベル
        plot.Title("温度トレンド（直近10分）");
        plot.YLabel("温度 (℃)");

        // X 軸を DateTime 表示
        plot.Axes.DateTimeTicksBottom();

        // アラーム閾値ライン（初期表示用）
        var alarm = plot.Add.HorizontalLine(AlarmTemperature);
        alarm.Color       = ScottPlot.Color.FromColor(Color.OrangeRed);
        alarm.LineWidth   = 1.5f;
        alarm.LinePattern = ScottPlot.LinePattern.Dashed;

        _chart.Refresh();
    }

    // ──────────────────────────────────────────────────
    //  チャート更新（毎秒 Timer_Tick から呼び出し）
    // ──────────────────────────────────────────────────
    private void UpdateChart(double temperature)
    {
        _chartTimes.Add(DateTime.Now.ToOADate());
        _chartTemps.Add(temperature);

        // 直近 MaxDataPoints 件に制限
        if (_chartTimes.Count > MaxDataPoints)
        {
            _chartTimes.RemoveAt(0);
            _chartTemps.RemoveAt(0);
        }

        var plot = _chart.Plot;
        plot.Clear();

        // アラーム閾値ライン（Clear 後に再描画）
        var alarm = plot.Add.HorizontalLine(AlarmTemperature);
        alarm.Color       = ScottPlot.Color.FromColor(Color.OrangeRed);
        alarm.LineWidth   = 1.5f;
        alarm.LinePattern = ScottPlot.LinePattern.Dashed;

        // 温度折れ線グラフ
        if (_chartTimes.Count >= 2)
        {
            var scatter = plot.Add.Scatter(_chartTimes.ToArray(), _chartTemps.ToArray());
            scatter.Color      = ScottPlot.Color.FromColor(Color.CornflowerBlue);
            scatter.LineWidth  = 2f;
            scatter.MarkerSize = 0f;
        }

        // X 軸を DateTime 表示（Clear 後に再設定）
        plot.Axes.DateTimeTicksBottom();

        _chart.Refresh();
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

        ushort rawValue     = result.Content[0];
        double temperature  = rawValue / TemperatureScale;

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

        // チャート更新（追加）
        UpdateChart(temperature);
    }
}
