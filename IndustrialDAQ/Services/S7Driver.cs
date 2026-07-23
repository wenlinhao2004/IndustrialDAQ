using IndustrialDAQ.Models;
using S7.Net;

namespace IndustrialDAQ.Services;

/// <summary>
/// Siemens S7 协议驱动 —— 基于 S7NetPlus
/// 支持 S7-200/300/400/1200/1500 系列 PLC
///
/// 面试要点:
///   - S7comm 是西门子私有协议，基于 ISO-on-TCP (RFC 1006)，默认端口 102
///   - 寻址模型: DB 块 → 字节偏移 → 数据类型（与 Modbus 寄存器平址模型不同）
///   - 大端序 (Big-Endian): 高字节在前，与 PC 的小端序相反，读取后须翻转
///   - S7-1200/1500 仅允许 PUT/GET 通信需在 TIA Portal 中启用
/// </summary>
public class S7Driver : IDeviceDriver
{
    private Plc? _plc;
    private bool _simulationMode;
    private readonly Random _random = new();

    public bool IsConnected { get; private set; }
    public bool IsSimulation => _simulationMode;
    public string ConnectionType { get; private set; } = "无";

    // ==================== 连接 (IDeviceDriver) ====================

    /// <summary>
    /// 连接到 S7 PLC
    /// parameters:
    ///   "IpAddress" (string)  — PLC IP 地址
    ///   "Rack"      (int)     — 机架号，S7-300/400 通常=0，S7-1200/1500=0
    ///   "Slot"      (int)     — 槽位号，S7-300/400 通常=2，S7-1200/1500=1
    ///   "CpuType"   (string)  — CPU 类型，默认 S71200
    /// </summary>
    public async Task<bool> ConnectAsync(Dictionary<string, object> parameters)
    {
        if (!parameters.TryGetValue("IpAddress", out var ipObj) || ipObj is not string ip)
            return false;

        var rack = parameters.TryGetValue("Rack", out var r) ? Convert.ToInt16(r) : (short)0;
        var slot = parameters.TryGetValue("Slot", out var s) ? Convert.ToInt16(s) : (short)0;

        var cpuTypeStr = parameters.TryGetValue("CpuType", out var ct) ? ct.ToString() : "S71200";
        var cpuType = cpuTypeStr switch
        {
            "S7200" => CpuType.S7200,
            "S7300" => CpuType.S7300,
            "S7400" => CpuType.S7400,
            "S71200" => CpuType.S71200,
            "S71500" => CpuType.S71500,
            _ => CpuType.S71200
        };

        try
        {
            _plc = new Plc(cpuType, ip, rack, slot);
            await Task.Run(() => _plc.Open());

            if (!_plc.IsConnected)
            {
                _plc = null;
                IsConnected = false;
                return false;
            }

            _simulationMode = false;
            IsConnected = true;
            ConnectionType = $"S7 ({ip}, Rack={rack}, Slot={slot})";
            return true;
        }
        catch
        {
            _plc?.Close();
            _plc = null;
            IsConnected = false;
            return false;
        }
    }

    // ==================== 断开 ====================

    public void Disconnect()
    {
        _plc?.Close();
        _plc = null;
        _simulationMode = false;
        IsConnected = false;
        ConnectionType = "无";
    }

    // ==================== 模拟模式 ====================

    public void EnableSimulation()
    {
        Disconnect();
        _simulationMode = true;
        IsConnected = true;
        ConnectionType = "S7 (Simulation)";
    }

    // ==================== 数据读取 ====================

    /// <summary>批量读取 S7 DB 块中的点位</summary>
    public async Task<Dictionary<string, double>> ReadAllTagsAsync(List<TagConfig> tags)
    {
        if (_simulationMode)
            return SimulateRead(tags);

        if (_plc == null || !_plc.IsConnected)
            throw new InvalidOperationException("S7 PLC 未连接");

        return await Task.Run(() =>
        {
            var result = new Dictionary<string, double>();
            foreach (var tag in tags)
            {
                try
                {
                    var byteCount = GetByteCount(tag.S7DataType);
                    var rawBytes = _plc.ReadBytes(
                        S7.Net.DataType.DataBlock,
                        tag.DbNumber,
                        tag.ByteOffset,
                        byteCount);

                    var value = ConvertFromBytes(rawBytes, tag.S7DataType, tag.BitOffset);
                    result[tag.Name] = value * tag.Scale + tag.Offset;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[S7] 读取失败 '{tag.Name}' (DB{tag.DbNumber}.{tag.S7DataType}{tag.ByteOffset}): {ex.Message}");
                }
            }
            return result;
        });
    }

    // ==================== 模拟数据生成 ====================

    private Dictionary<string, double> SimulateRead(List<TagConfig> tags)
    {
        var result = new Dictionary<string, double>();
        foreach (var tag in tags)
        {
            double raw = tag.S7DataType switch
            {
                "BOOL" => _random.Next(0, 2),                         // 0 或 1
                "BYTE" => _random.Next(0, 256),                        // 0 ~ 255
                "INT" => _random.Next(-32768, 32768),                  // -32768 ~ 32767
                "DINT" => _random.Next(-100000, 100000),               // 模拟范围
                "REAL" => Math.Round(_random.NextDouble() * 100, 2),  // 0.00 ~ 100.00
                _ => Math.Round(_random.NextDouble() * 100, 2)
            };
            result[tag.Name] = raw * tag.Scale + tag.Offset;
        }
        return result;
    }

    // ==================== 字节操作 ====================

    /// <summary>根据 S7 数据类型返回所需字节数</summary>
    private static int GetByteCount(string dataType) => dataType switch
    {
        "BOOL" => 1,
        "BYTE" => 1,
        "INT" => 2,
        "DINT" => 4,
        "REAL" => 4,
        _ => 4
    };

    /// <summary>
    /// 将 S7 大端序字节转换为 double 值
    /// S7 (大端序) → PC (小端序): 读取后翻转字节再转换
    /// </summary>
    private static double ConvertFromBytes(byte[] bytes, string dataType, int bitOffset)
    {
        return dataType switch
        {
            "BOOL" => ((bytes[0] >> bitOffset) & 1) == 1 ? 1.0 : 0.0,
            "BYTE" => bytes[0],
            "INT" => Convert.ToDouble(BitConverter.ToInt16(SwapEndian(bytes, 2), 0)),
            "DINT" => Convert.ToDouble(BitConverter.ToInt32(SwapEndian(bytes, 4), 0)),
            "REAL" => Convert.ToDouble(BitConverter.ToSingle(SwapEndian(bytes, 4), 0)),
            _ => 0.0
        };
    }

    /// <summary>大端序 → 小端序字节翻转</summary>
    private static byte[] SwapEndian(byte[] bytes, int count)
    {
        var result = new byte[count];
        Array.Copy(bytes, result, count);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(result);
        return result;
    }

    // ==================== 资源清理 ====================

    public void Dispose() => Disconnect();
}
