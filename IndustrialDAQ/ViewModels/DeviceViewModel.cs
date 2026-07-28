using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using IndustrialDAQ.Models;
using IndustrialDAQ.Services;

namespace IndustrialDAQ.ViewModels;

/// <summary>
/// 单设备视图模型 —— 封装一个设备的完整生命周期：驱动 + 管道 + 点位
/// </summary>
public partial class DeviceViewModel : ObservableObject, IDisposable
{
    private IDeviceDriver _driver;
    private DataPipeline<Dictionary<string, double>>? _pipeline;
    private CancellationTokenSource? _timerCts;

    public string DeviceId { get; set; }
    public string DeviceName { get; set; } = "";
    public string Protocol { get; set; } = "";

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _statusText = "未连接";
    [ObservableProperty] private string _pipelineStatus = "未启动";
    [ObservableProperty] private int _queueSize;

    public ObservableCollection<TagViewModel> Tags { get; } = new();
    public List<TagConfig> TagConfigs { get; }

    /// <summary>每次采集完成时触发，主 ViewModel 订阅来统一处理报警和入库</summary>
    public event Action<Dictionary<string, double>>? OnDataReceived;

    /// <summary>管道日志</summary>
    public event Action<string>? OnLog;
    public event Action<Exception>? OnError;

    /// <summary>
    /// 工厂方法 —— 根据协议类型创建驱动
    /// </summary>
    private static IDeviceDriver CreateDriver(string protocol)
    {
        return protocol switch
        {
            "ModbusTCP" or "ModbusRTU" => new ModbusService(),
            "OpcUa" => new OpcUaDriver(),
            "S7" => new S7Driver(),
            _ => throw new ArgumentException($"不支持的协议: {protocol}")
        };
    }

    public DeviceViewModel(DeviceConfig config)
    {
        DeviceId = config.DeviceId;
        DeviceName = config.DeviceName;
        Protocol = config.Protocol;

        _driver = CreateDriver(config.Protocol);

        TagConfigs = config.Tags;
        foreach (var tag in TagConfigs)
            Tags.Add(new TagViewModel(tag));
    }

    /// <summary>切换协议（重建驱动）</summary>
    public void SwitchProtocol(string newProtocol)
    {
        if (Protocol == newProtocol) return;
        if (IsConnected) return; // 已连接时不允许切换

        _driver.Dispose();
        _driver = CreateDriver(newProtocol);
        Protocol = newProtocol;
    }

    // ==================== 连接 ====================

    public IDeviceDriver? Driver => IsConnected ? _driver : null;

    public async Task<bool> ConnectAsync(Dictionary<string, object> parameters)
    {
        if (IsConnected) await DisconnectAsync();

        try
        {
            bool ok = await _driver.ConnectAsync(parameters);
            if (!ok)
            {
                StatusText = $"[{DeviceName}] 连接失败";
                return false;
            }
        }
        catch (Exception ex)
        {
            StatusText = $"[{DeviceName}] 连接异常: {ex.Message}";
            return false;
        }

        IsConnected = true;
        StatusText = $"[{DeviceName}] 已连接 ({_driver.ConnectionType})";
        StartPipeline();
        return true;
    }

    public async Task DisconnectAsync()
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

        _driver.Disconnect();
        IsConnected = false;
        StatusText = $"[{DeviceName}] 未连接";
        PipelineStatus = "未启动";
        QueueSize = 0;
    }

    // ==================== 管道 ====================

    private void StartPipeline()
    {
        _pipeline?.Dispose();
        _pipeline = new DataPipeline<Dictionary<string, double>>();

        _pipeline.OnLog += msg => OnLog?.Invoke($"[{DeviceName}] {msg}");
        _pipeline.OnError += ex => OnError?.Invoke(ex);

        var driver = _driver;

        _pipeline.Start(
            producerFunc: async (ct) =>
            {
                try
                {
                    return await driver.ReadAllTagsAsync(TagConfigs);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    OnLog?.Invoke($"[{DeviceName}] 连接断开: {ex.Message}");

                    for (int attempt = 1; attempt <= 5; attempt++)
                    {
                        if (ct.IsCancellationRequested) break;

                        await Task.Delay(attempt * 2000, ct);
                        bool reconnected = await driver.ReconnectAsync();
                        if (reconnected)
                        {
                            IsConnected = true;
                            StatusText = $"[{DeviceName}] 已连接 ({driver.ConnectionType})";
                            OnLog?.Invoke($"[{DeviceName}] 重连成功 (第 {attempt} 次)");
                            return new Dictionary<string, double>();
                        }
                    }
                    throw;
                }
            },
            consumerAction: (values) =>
            {
                OnDataReceived?.Invoke(values);
            },
            produceIntervalMs: 1000,
            boundedCapacity: 100
        );

        PipelineStatus = "运行中";
        _timerCts?.Cancel();
        _timerCts = new CancellationTokenSource();
        _ = UiRefreshLoop(_timerCts.Token);
    }

    private async Task UiRefreshLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(200, ct);

                if (_pipeline != null)
                {
                    QueueSize = _pipeline.QueueCount;
                    PipelineStatus = $"运行中 | 队列: {QueueSize}/100";
                }

                while (_pipeline?.ResultQueue.TryDequeue(out var values) == true)
                {
                    foreach (var tagVm in Tags)
                        if (values.TryGetValue(tagVm.TagName, out var val))
                            tagVm.UpdateValue(val);
                }
            }
            catch (OperationCanceledException) { break; }
        }
    }

    // ==================== 写入 ====================

    public async Task<bool> WriteTagAsync(TagConfig tag, double value)
    {
        if (!IsConnected)
            throw new InvalidOperationException("设备未连接");

        return await _driver.WriteTagAsync(tag, value);
    }

    public void Dispose() => _driver.Dispose();
}
