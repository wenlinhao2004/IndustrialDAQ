using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IndustrialDAQ.Models;
using IndustrialDAQ.Services;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace IndustrialDAQ.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ModbusService _modbusDriver;
    private readonly OpcUaDriver _opcUaDriver;
    private readonly S7Driver _s7Driver;
    private IDeviceDriver? _currentDriver;
    private readonly AlarmService _alarm = new();
    private readonly SerialPortService _serialPort = new();
    private readonly SettingsService _settings;
    private DataLogger? _logger;

    private DataPipeline<Dictionary<string, double>>? _pipeline;
    private CancellationTokenSource? _timerCts;

    // ==================== UI 绑定属性 ====================

    [ObservableProperty] private string _ipAddress;
    [ObservableProperty] private string _opcUaEndpointUrl;
    [ObservableProperty] private string _statusText = "未连接";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _connectBtnText = "TCP 连接";
    [ObservableProperty] private string _logIntervalText;
    [ObservableProperty] private string _logCount = "0";
    [ObservableProperty] private string _alarmCount = "0";

    // 串口配置
    [ObservableProperty] private string _comPort;
    [ObservableProperty] private int _baudRate = 9600;
    [ObservableProperty] private string _serialStatus = "串口未打开";

    // 线程状态
    [ObservableProperty] private string _pipelineStatus = "未启动";
    [ObservableProperty] private int _queueSize;

    // 连接模式: 0=TCP, 1=RTU, 2=OPC UA, 3=S7
    [ObservableProperty] private int _selectedMode;

    public ObservableCollection<TagViewModel> Tags { get; } = new();
    public ObservableCollection<AlarmRecord> Alarms { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();
    public ObservableCollection<HistoryRecord> HistoryRecords { get; } = new();
    public List<string> AvailablePorts => SerialPortService.GetAvailablePorts().ToList();

    // 数据库状态
    [ObservableProperty] private string _dbStatus = "数据库未连接";
    [ObservableProperty] private long _dbRecordCount;
    [ObservableProperty] private long _dbFileSizeKb;
    [ObservableProperty] private string _selectedHistoryTag = "全部";
    [ObservableProperty] private int _historyRecordCount;

    // 图表
    public PlotModel PlotModel { get; }
    private readonly Dictionary<string, LineSeries> _seriesMap = new();

    private readonly List<TagConfig> _tagConfigs = TagConfigLoader.Load();

    public MainViewModel(ModbusService modbusDriver, OpcUaDriver opcUaDriver, S7Driver s7Driver)
    {
        _modbusDriver = modbusDriver;
        _opcUaDriver = opcUaDriver;
        _s7Driver = s7Driver;

        // 加载用户配置
        _settings = SettingsService.Load();
        IpAddress = _settings.IpAddress;
        ComPort = _settings.ComPort;
        BaudRate = _settings.BaudRate;
        SelectedMode = _settings.SelectedMode;
        LogIntervalText = _settings.LogIntervalMs.ToString();
        OpcUaEndpointUrl = _settings.OpcUaEndpointUrl;

        // 保存配置（当属性变化时）
        PropertyChanged += (_, _) => SaveSettings();

        // 图表
        PlotModel = new PlotModel { Title = "实时数据趋势" };
        PlotModel.Axes.Add(new DateTimeAxis
        {
            Position = AxisPosition.Bottom, StringFormat = "HH:mm:ss",
            MajorGridlineStyle = LineStyle.Solid, MinorGridlineStyle = LineStyle.Dot
        });
        PlotModel.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            MajorGridlineStyle = LineStyle.Solid, MinorGridlineStyle = LineStyle.Dot
        });

        var colors = new[] { OxyColors.Red, OxyColors.Blue, OxyColors.Green,
                             OxyColors.Orange, OxyColors.Purple, OxyColors.Brown, OxyColors.Teal };
        for (int i = 0; i < _tagConfigs.Count; i++)
        {
            Tags.Add(new TagViewModel(_tagConfigs[i]));
            var series = new LineSeries
            {
                Title = _tagConfigs[i].Name, Color = colors[i % colors.Length],
                StrokeThickness = 1.5, MarkerType = MarkerType.None
            };
            _seriesMap[_tagConfigs[i].Name] = series;
            PlotModel.Series.Add(series);
        }

        // 报警回调
        _alarm.OnAlarm += record => Application.Current.Dispatcher.Invoke(() =>
        {
            Alarms.Insert(0, record);
            if (Alarms.Count > 200) Alarms.RemoveAt(Alarms.Count - 1);
            AlarmCount = Alarms.Count.ToString();
        });

        _serialPort.OnStatusChanged += msg =>
            Application.Current.Dispatcher.Invoke(() => SerialStatus = msg);

        UpdateConnectBtnText();
    }

    // ==================== 配置持久化 ====================

    private void SaveSettings()
    {
        _settings.IpAddress = IpAddress;
        _settings.ComPort = ComPort;
        _settings.BaudRate = BaudRate;
        _settings.SelectedMode = SelectedMode;
        _settings.OpcUaEndpointUrl = OpcUaEndpointUrl;
        if (int.TryParse(LogIntervalText, out var v)) _settings.LogIntervalMs = v;
        _settings.Save();
    }

    partial void OnSelectedModeChanged(int value)
    {
        UpdateConnectBtnText();
        SaveSettings();
    }

    private void UpdateConnectBtnText()
    {
        ConnectBtnText = SelectedMode switch
        {
            0 => "TCP 连接",
            1 => "RTU 连接",
            2 => "OPC UA 连接",
            3 => "S7 连接",
            _ => "连接"
        };
    }

    // ==================== 命令 ====================

    [RelayCommand]
    private async Task Connect()
    {
        if (IsConnected) { await DisconnectInternal(); return; }

        var driver = SelectedMode switch
        {
            0 or 1 => (IDeviceDriver)_modbusDriver,
            2 => _opcUaDriver,
            3 => _s7Driver,
            _ => throw new InvalidOperationException("未知连接模式")
        };

        var parameters = SelectedMode switch
        {
            0 => new Dictionary<string, object>
            {
                { "Mode", "TCP" },
                { "IpAddress", IpAddress }
            },
            1 => new Dictionary<string, object>
            {
                { "Mode", "RTU" },
                { "ComPort", ComPort },
                { "BaudRate", BaudRate },
                { "Parity", Parity.None },
                { "DataBits", 8 },
                { "StopBits", StopBits.One }
            },
            2 => new Dictionary<string, object>
            {
                { "EndpointUrl", OpcUaEndpointUrl },
                { "Username", _settings.OpcUaUsername },
                { "Password", _settings.OpcUaPassword }
            },
            3 => new Dictionary<string, object>
            {
                { "IpAddress", IpAddress },
                { "Rack", 0 },
                { "Slot", 0 }
            },
            _ => new Dictionary<string, object>()
        };

        bool ok = await driver.ConnectAsync(parameters);

        if (!ok)
        {
            MessageBox.Show("无法连接设备，请检查连接参数后重试。", "连接失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _currentDriver = driver;
        OnConnected();
    }

    [RelayCommand]
    private void ClearAlarms()
    {
        Alarms.Clear();
        AlarmCount = "0";
    }

    [RelayCommand]
    private void QueryHistory()
    {
        if (_logger == null) return;
        var tag = SelectedHistoryTag == "全部" ? null : SelectedHistoryTag;
        var records = _logger.QueryHistory(tag, limit: 500);
        HistoryRecords.Clear();
        foreach (var r in records) HistoryRecords.Add(r);
        HistoryRecordCount = HistoryRecords.Count;
        RefreshDbStatus();
    }

    /// <summary>导出历史数据为 CSV</summary>
    [RelayCommand]
    private void ExportCsv()
    {
        if (_logger == null || HistoryRecords.Count == 0)
        {
            MessageBox.Show("请先查询历史数据再导出。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DataExporter.ExportToCsv(HistoryRecords.ToList());
    }

    private void RefreshDbStatus()
    {
        if (_logger == null) return;
        DbRecordCount = _logger.GetRecordCount();
        DbFileSizeKb = _logger.GetFileSizeKb();
        DbStatus = $"SQLite | {DbRecordCount:N0} 条 | {DbFileSizeKb:N0} KB";
    }

    [RelayCommand]
    private void RefreshPorts()
    {
        OnPropertyChanged(nameof(AvailablePorts));
    }

    [RelayCommand]
    private void OpenSerial()
    {
        if (_serialPort.IsOpen) { _serialPort.Close(); return; }
        _serialPort.Open(ComPort, BaudRate);
    }

    [RelayCommand]
    private void SendSerial()
    {
        _serialPort.SendString($"TEST:{DateTime.Now:HH:mm:ss}");
    }

    // ==================== 连接 / 断开 ====================

    private void OnConnected()
    {
        IsConnected = true;
        StatusText = $"已连接 ({_currentDriver!.ConnectionType})";
        ConnectBtnText = "断开";
        _logger = new DataLogger("modbus_data.db");
        RefreshDbStatus();
        StartPipeline();
    }

    /// <summary>公开的断开方法，供窗口关闭时调用</summary>
    public async Task ShutdownAsync()
    {
        await DisconnectInternal();
        _serialPort.Dispose();
        _modbusDriver.Dispose();
        _opcUaDriver.Dispose();
        _s7Driver.Dispose();
        _logger?.Dispose();
    }

    private async Task DisconnectInternal()
    {
        _timerCts?.Cancel();
        _timerCts?.Dispose();
        _timerCts = null;

        if (_pipeline != null)
        {
            await _pipeline.StopAsync();
            _pipeline.Dispose();
            _pipeline = null;
        }

        _currentDriver?.Disconnect();
        _currentDriver = null;
        _logger?.Dispose();
        _logger = null;

        IsConnected = false;
        StatusText = "未连接";
        UpdateConnectBtnText();
        PipelineStatus = "未启动";
        QueueSize = 0;
    }

    // ==================== 多线程管道 ====================

    private void StartPipeline()
    {
        _pipeline?.Dispose();
        _pipeline = new DataPipeline<Dictionary<string, double>>();
        var driver = _currentDriver!;

        _pipeline.OnLog += msg => Application.Current.Dispatcher.Invoke(() =>
        {
            Logs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {msg}");
            if (Logs.Count > 100) Logs.RemoveAt(Logs.Count - 1);
        });

        _pipeline.OnError += ex => Application.Current.Dispatcher.Invoke(() =>
            Logs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] 错误: {ex.Message}"));

        int interval = int.TryParse(LogIntervalText, out var v) ? Math.Max(v, 200) : 1000;

        _pipeline.Start(
            producerFunc: async (ct) => await driver.ReadAllTagsAsync(_tagConfigs),
            consumerAction: (values) =>
            {
                _alarm.CheckLimits(_tagConfigs, values);
                _logger?.InsertBatch(values);
            },
            produceIntervalMs: interval,
            boundedCapacity: 100
        );

        PipelineStatus = "运行中（生产者-消费者模式）";
        _timerCts?.Cancel();
        _timerCts = new CancellationTokenSource();
        _ = UiUpdateLoop(_timerCts.Token);
    }

    private async Task UiUpdateLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(200, ct);

                if (_pipeline != null)
                {
                    QueueSize = _pipeline.QueueCount;
                    PipelineStatus = $"运行中 | 队列: {QueueSize}/100 | Producer → Consumer → UI";
                }

                while (_pipeline?.ResultQueue.TryDequeue(out var values) == true)
                {
                    var now = DateTime.Now;
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var tagVm in Tags)
                            if (values.TryGetValue(tagVm.Name, out var val))
                                tagVm.UpdateValue(val);

                        foreach (var kv in values)
                        {
                            if (_seriesMap.TryGetValue(kv.Key, out var series))
                            {
                                series.Points.Add(new DataPoint(DateTimeAxis.ToDouble(now), kv.Value));
                                if (series.Points.Count > 300) series.Points.RemoveAt(0);
                            }
                        }
                        PlotModel.InvalidatePlot(true);
                    });

                    if (_logger != null)
                    {
                        LogCount = (int.TryParse(LogCount, out var c) ? c : 0) + 1 + "";
                        RefreshDbStatus();
                    }
                }
            }
            catch (OperationCanceledException) { break; }
        }
    }
}

public partial class TagViewModel : ObservableObject
{
    private readonly TagConfig _config;
    public string Name { get; set; }
    public string Unit { get; set; }

    [ObservableProperty] private double _value;
    [ObservableProperty] private string _displayValue = "--";
    [ObservableProperty] private bool _isAlarm;
    [ObservableProperty] private string _alarmType = "";

    public TagViewModel(TagConfig config)
    {
        _config = config;
        Name = config.Name;
        Unit = config.Unit;
    }

    public void UpdateValue(double val)
    {
        Value = val;
        DisplayValue = $"{val:F2} {_config.Unit}";
        IsAlarm = val > _config.HighLimit || val < _config.LowLimit;
        AlarmType = val > _config.HighLimit ? "▲ 高" : val < _config.LowLimit ? "▼ 低" : "";
    }
}
