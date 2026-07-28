using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Text.Json;
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
    private readonly AlarmService _alarm = new();
    private readonly SerialPortService _serialPort = new();
    private readonly SettingsService _settings;
    private DataLogger? _logger;

    private DataLogger Logger => _logger ??= new DataLogger("modbus_data.db");

    // ==================== 设备管理 ====================

    public ObservableCollection<DeviceViewModel> Devices { get; } = new();

    [ObservableProperty] private DeviceViewModel? _selectedDevice;
    [ObservableProperty] private string _selectedProtocol = "ModbusTCP";

    // ==================== UI 绑定属性 ====================

    [ObservableProperty] private string _ipAddress = "127.0.0.1";
    [ObservableProperty] private string _opcUaEndpointUrl = "opc.tcp://127.0.0.1:4840";
    [ObservableProperty] private string _logIntervalText = "1000";
    [ObservableProperty] private string _logCount = "0";
    [ObservableProperty] private string _alarmCount = "0";

    // 串口
    [ObservableProperty] private string _comPort = "COM1";
    [ObservableProperty] private int _baudRate = 9600;
    [ObservableProperty] private string _serialStatus = "串口未打开";

    // 写入
    [ObservableProperty] private TagConfig? _selectedWriteTag;
    [ObservableProperty] private string _writeValueText = "0";
    [ObservableProperty] private string _writeStatus = "";
    public List<TagConfig> WritableTags { get; } = new();

    // 聚合
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

    // 图表（动态管理）
    public PlotModel PlotModel { get; }
    private readonly Dictionary<string, LineSeries> _seriesMap = new();
    private readonly OxyColor[] _colors = {
        OxyColors.Red, OxyColors.Blue, OxyColors.Green,
        OxyColors.Orange, OxyColors.Purple, OxyColors.Brown,
        OxyColors.Teal, OxyColors.HotPink, OxyColors.Olive, OxyColors.Cyan
    };
    private int _colorIndex;

    public MainViewModel()
    {
        // 加载设置
        _settings = SettingsService.Load();
        IpAddress = _settings.IpAddress;
        ComPort = _settings.ComPort;
        BaudRate = _settings.BaudRate;

        // 加载设备配置
        LoadDevices();

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

        // 报警回调
        _alarm.OnAlarm += record => Application.Current.Dispatcher.Invoke(() =>
        {
            Alarms.Insert(0, record);
            if (Alarms.Count > 200) Alarms.RemoveAt(Alarms.Count - 1);
            AlarmCount = Alarms.Count.ToString();
        });

        _serialPort.OnStatusChanged += msg =>
            Application.Current.Dispatcher.Invoke(() => SerialStatus = msg);

        PropertyChanged += (_, _) => SaveSettings();
    }

    partial void OnSelectedDeviceChanged(DeviceViewModel? value)
    {
        if (value != null)
        {
            SelectedProtocol = value.Protocol;
            RefreshWritableTags();
        }
    }

    // ==================== 设备加载 ====================

    private void LoadDevices()
    {
        var configs = DeviceConfigLoader.Load();
        foreach (var cfg in configs)
        {
            DeviceViewModel device;
            try
            {
                device = new DeviceViewModel(cfg);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载设备 '{cfg.DeviceName}' 失败:\n{ex.Message}\n\n" +
                    $"堆栈: {ex.StackTrace}",
                    "设备加载错误", MessageBoxButton.OK, MessageBoxImage.Error);
                continue;
            }

            // 转发数据事件
            device.OnDataReceived += values =>
                Application.Current.Dispatcher.Invoke(() => OnDeviceData(device, values));

            device.OnLog += msg =>
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Logs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {msg}");
                    if (Logs.Count > 200) Logs.RemoveAt(Logs.Count - 1);
                });

            device.OnError += ex =>
                Application.Current.Dispatcher.Invoke(() =>
                    Logs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] 错误: {ex.Message}"));

            Devices.Add(device);
        }
        SelectedDevice = Devices.FirstOrDefault();
    }

    private void OnDeviceData(DeviceViewModel device, Dictionary<string, double> values)
    {
        var now = DateTime.Now;

        // 报警 + 入库
        _alarm.CheckLimits(device.TagConfigs, values);
        Logger.InsertBatch(values);

        // 更新 UI
        foreach (var tagVm in Tags)
            if (values.TryGetValue(tagVm.TagName, out var val))
                tagVm.UpdateValue(val);

        // 更新图表
        foreach (var kv in values)
        {
            var seriesKey = $"[{device.DeviceName}]{kv.Key}";
            if (!_seriesMap.TryGetValue(seriesKey, out var series))
            {
                series = new LineSeries
                {
                    Title = seriesKey,
                    Color = _colors[_colorIndex++ % _colors.Length],
                    StrokeThickness = 1.5,
                    MarkerType = MarkerType.None
                };
                _seriesMap[seriesKey] = series;
                PlotModel.Series.Add(series);
            }
            series.Points.Add(new DataPoint(DateTimeAxis.ToDouble(now), kv.Value));
            if (series.Points.Count > 300) series.Points.RemoveAt(0);
        }
        PlotModel.InvalidatePlot(true);

        LogCount = (int.TryParse(LogCount, out var c) ? c : 0) + 1 + "";
        RefreshDbStatus();
    }

    private void RefreshWritableTags()
    {
        WritableTags.Clear();
        if (SelectedDevice != null)
            foreach (var tag in SelectedDevice.TagConfigs)
                if (tag.Name is "电机转速" or "阀门开度")
                    WritableTags.Add(tag);
        SelectedWriteTag = WritableTags.FirstOrDefault();
    }

    // ==================== 配置持久化 ====================

    private void SaveSettings()
    {
        _settings.IpAddress = IpAddress;
        _settings.ComPort = ComPort;
        _settings.BaudRate = BaudRate;
        _settings.Save();
    }

    // ==================== 命令 ====================

    [RelayCommand]
    private async Task Connect()
    {
        if (SelectedDevice == null) return;

        if (SelectedDevice.IsConnected)
        {
            await SelectedDevice.DisconnectAsync();
            RemoveDeviceTags(SelectedDevice);
            return;
        }

        // 同步协议：UI 选的协议可能与设备配置的默认协议不同
        SelectedDevice.SwitchProtocol(SelectedProtocol);

        var parameters = SelectedProtocol switch
        {
            "ModbusTCP" => new Dictionary<string, object>
            {
                { "Mode", "TCP" }, { "IpAddress", IpAddress }
            },
            "ModbusRTU" => new Dictionary<string, object>
            {
                { "Mode", "RTU" }, { "ComPort", ComPort }, { "BaudRate", BaudRate },
                { "Parity", Parity.None }, { "DataBits", 8 }, { "StopBits", StopBits.One }
            },
            "OpcUa" => new Dictionary<string, object>
            {
                { "EndpointUrl", OpcUaEndpointUrl },
                { "Username", _settings.OpcUaUsername },
                { "Password", _settings.OpcUaPassword }
            },
            "S7" => new Dictionary<string, object>
            {
                { "IpAddress", IpAddress }, { "Rack", 0 }, { "Slot", 0 }
            },
            _ => new Dictionary<string, object>()
        };

        bool ok;
        try
        {
            ok = await SelectedDevice.ConnectAsync(parameters);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"连接异常: {ex.Message}", "连接失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!ok)
        {
            MessageBox.Show("无法连接设备，请检查连接参数后重试。", "连接失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        AddDeviceTags(SelectedDevice);
        RefreshDbStatus();
    }

    private void AddDeviceTags(DeviceViewModel device)
    {
        foreach (var tagVm in device.Tags)
        {
            tagVm.DisplayName = $"[{device.DeviceName}] {tagVm.TagName}";
            Tags.Add(tagVm);
        }
    }

    private void RemoveDeviceTags(DeviceViewModel device)
    {
        var toRemove = Tags.Where(t => t.DisplayName.StartsWith($"[{device.DeviceName}]")).ToList();
        foreach (var t in toRemove) Tags.Remove(t);
    }

    [RelayCommand]
    private void ClearAlarms()
    {
        Alarms.Clear();
        AlarmCount = "0";
    }

    [RelayCommand]
    private async Task WriteTag()
    {
        if (SelectedWriteTag == null) { WriteStatus = "请选择要写入的点位"; return; }
        if (!double.TryParse(WriteValueText, out var value)) { WriteStatus = "请输入有效的数值"; return; }
        if (SelectedDevice == null || !SelectedDevice.IsConnected) { WriteStatus = "设备未连接"; return; }

        bool ok = await SelectedDevice.WriteTagAsync(SelectedWriteTag, value);
        WriteStatus = ok
            ? $"✓ 已写入 {SelectedWriteTag.Name} = {value} {SelectedWriteTag.Unit}"
            : "✗ 写入失败";
    }

    [RelayCommand]
    private void QueryHistory()
    {
        var tag = SelectedHistoryTag == "全部" ? null : SelectedHistoryTag;
        var records = Logger.QueryHistory(tag, limit: 500);
        HistoryRecords.Clear();
        foreach (var r in records) HistoryRecords.Add(r);
        HistoryRecordCount = HistoryRecords.Count;
        RefreshDbStatus();
    }

    [RelayCommand]
    private void ExportCsv()
    {
        if (HistoryRecords.Count == 0)
        {
            MessageBox.Show("请先查询历史数据再导出。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DataExporter.ExportToCsv(HistoryRecords.ToList());
    }

    private void RefreshDbStatus()
    {
        DbRecordCount = Logger.GetRecordCount();
        DbFileSizeKb = Logger.GetFileSizeKb();
        DbStatus = $"SQLite | {DbRecordCount:N0} 条 | {DbFileSizeKb:N0} KB";
    }

    [RelayCommand] private void RefreshPorts()
        => OnPropertyChanged(nameof(AvailablePorts));

    [RelayCommand]
    private void OpenSerial()
    {
        if (_serialPort.IsOpen) { _serialPort.Close(); return; }
        _serialPort.Open(ComPort, BaudRate);
    }

    [RelayCommand]
    private void SendSerial()
        => _serialPort.SendString($"TEST:{DateTime.Now:HH:mm:ss}");

    // ==================== 生命周期 ====================

    public async Task ShutdownAsync()
    {
        foreach (var dev in Devices)
            await dev.DisconnectAsync();
        _serialPort.Dispose();
        foreach (var dev in Devices)
            dev.Dispose();
        _logger?.Dispose();
    }
}

// ==================== DeviceConfig 加载器 ====================

public static class DeviceConfigLoader
{
    private const string DefaultPath = "devices.json";

    public static List<DeviceConfig> Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
            return new List<DeviceConfig>();

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<DeviceConfig>>(json) ?? new();
    }
}

// ==================== TagViewModel ====================

public partial class TagViewModel : ObservableObject
{
    private readonly TagConfig _config;
    public string TagName { get; set; }
    public string Unit { get; set; }
    public string DisplayName { get; set; } = "";  // UI 显示用（含设备名前缀）

    [ObservableProperty] private double _value;
    [ObservableProperty] private string _displayValue = "--";
    [ObservableProperty] private bool _isAlarm;
    [ObservableProperty] private string _alarmType = "";

    public TagViewModel(TagConfig config)
    {
        _config = config;
        TagName = config.Name;
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
