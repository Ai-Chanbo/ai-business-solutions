using HslCommunication.ModBus;

namespace PlcMonitorHmi;

public partial class Form1 : Form
{
    private readonly ModbusTcpNet _modbus = new("127.0.0.1", 502);
    private readonly System.Windows.Forms.Timer _timer = new();

    private DataGridView gridLog = new();
    private Label lblConnection = new();
    private Label lblTemperature = new();
    private Label lblOperation = new();
    private Label lblAlarm = new();
    private Button btnConnect = new();
    private Button btnStop = new();

    private const double TemperatureScale = 1000.0;
    private const double AlarmTemperature = 60.0;

    public Form1()
    {
        InitializeComponent();
        InitializeHmi();
    }

    private void InitializeHmi()
    {
        Text = "PLC温度監視HMI";
        Width = 500;
        Height = 350;

        lblConnection.SetBounds(30, 30, 400, 40);
        lblTemperature.SetBounds(30, 80, 400, 50);
        lblOperation.SetBounds(30, 150, 400, 40);
        lblAlarm.SetBounds(30, 210, 400, 50);

        btnConnect.SetBounds(30, 290, 120, 40);
        btnStop.SetBounds(170, 290, 120, 40);

        lblConnection.Text = "接続状態：未接続";
        lblTemperature.Text = "現在温度：--- ℃";
        lblOperation.Text = "稼働状態：---";
        lblAlarm.Text = "異常状態：---";

        btnConnect.Text = "接続開始";
        btnStop.Text = "停止";

        lblTemperature.Font = new Font("Yu Gothic UI", 18, FontStyle.Bold);
        lblAlarm.Font = new Font("Yu Gothic UI", 16, FontStyle.Bold);

        Controls.Add(lblConnection);
        Controls.Add(lblTemperature);
        Controls.Add(lblOperation);
        Controls.Add(lblAlarm);
        Controls.Add(btnConnect);
        Controls.Add(btnStop);

        gridLog.SetBounds(30, 380, 700, 220);
        gridLog.AllowUserToAddRows = false;
        gridLog.ReadOnly = true;
        gridLog.RowHeadersVisible = false;
        gridLog.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        gridLog.Columns.Add("Time", "時刻");
        gridLog.Columns.Add("RawValue", "PLC値");
        gridLog.Columns.Add("Temperature", "温度");
        gridLog.Columns.Add("OperationStatus", "稼働状態");
        gridLog.Columns.Add("AlarmStatus", "異常状態");

        Controls.Add(gridLog);

        Width = 800;
        Height = 680;

        btnConnect.Click += BtnConnect_Click;
        btnStop.Click += BtnStop_Click;

        _timer.Interval = 1000;
        _timer.Tick += Timer_Tick;
    }

    private void BtnConnect_Click(object? sender, EventArgs e)
    {
        var result = _modbus.ConnectServer();

        if (result.IsSuccess)
        {
            lblConnection.Text = "接続状態：接続成功";
            lblConnection.ForeColor = Color.Green;
            _timer.Start();
        }
        else
        {
            lblConnection.Text = "接続状態：接続失敗";
            lblConnection.ForeColor = Color.Red;
        }
    }

    private void BtnStop_Click(object? sender, EventArgs e)
    {
        _timer.Stop();
        lblConnection.Text = "接続状態：停止中";
        lblConnection.ForeColor = Color.Black;
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        var result = _modbus.ReadUInt16("1", 1);

        if (!result.IsSuccess)
        {
            lblConnection.Text = "接続状態：読取失敗";
            lblConnection.ForeColor = Color.Red;
            return;
        }

        ushort rawValue = result.Content[0];
        double temperature = rawValue / TemperatureScale;

        string operationStatus = rawValue == 0 ? "停止中" : "稼働中";
        string alarmStatus = temperature > AlarmTemperature ? "異常" : "正常";

        lblTemperature.Text = $"現在温度：{temperature:F1} ℃";
        lblOperation.Text = $"稼働状態：{operationStatus}";
        lblAlarm.Text = $"異常状態：{alarmStatus}";

        lblAlarm.ForeColor = alarmStatus == "異常"
            ? Color.Red
            : Color.Green;

        gridLog.Rows.Insert(
        0,
        DateTime.Now.ToString("HH:mm:ss"),
        rawValue,
        $"{temperature:F1}℃",
        operationStatus,
        alarmStatus
        );

        int rowIndex = 0;

        if (alarmStatus == "異常")
        {
            gridLog.Rows[rowIndex].DefaultCellStyle.BackColor = Color.LightPink;
        }

        if (gridLog.Rows.Count > 100)
        {
            gridLog.Rows.RemoveAt(gridLog.Rows.Count - 1);
        }
    }
}