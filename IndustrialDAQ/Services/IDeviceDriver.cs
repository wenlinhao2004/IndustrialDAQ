using IndustrialDAQ.Models;

namespace IndustrialDAQ.Services;

/// <summary>
/// 设备驱动抽象 —— 统一 Modbus TCP/RTU 和 OPC UA 的读取接口
/// 面试要点: 策略模式 + 依赖注入, 新增协议无需修改上层代码
/// </summary>
public interface IDeviceDriver : IDisposable
{
    bool IsConnected { get; }
    string ConnectionType { get; }

    /// <summary>连接设备，parameters 根据协议不同传递不同参数</summary>
    Task<bool> ConnectAsync(Dictionary<string, object> parameters);

    /// <summary>断开连接</summary>
    void Disconnect();

    /// <summary>批量读取所有配置点位</summary>
    Task<Dictionary<string, double>> ReadAllTagsAsync(List<TagConfig> tags);
}
