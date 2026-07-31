using System.IO.Ports;
using System.Net.Sockets;
using Modbus.Device;
using IndustrialDAQ.Models;

namespace IndustrialDAQ.Services;

/// <summary>
/// Modbus 通信服务 —— 同时支持 Modbus TCP 和 Modbus RTU (串口)
/// 实现 IDeviceDriver 统一驱动接口，策略模式：根据通信类型创建不同的 IModbusMaster
/// </summary>
public class ModbusService : IDeviceDriver
{
    private object? _transport;              // TcpClient 或 SerialPort
    private IModbusMaster? _master;
    private byte _slaveId = 1;               // RTU 从站地址
    private Dictionary<string, object> _lastParams = new();

    public bool IsConnected { get; private set; }
    public string ConnectionType { get; private set; } = "无"; // "TCP" / "RTU"

    // ==================== 统一连接入口 (IDeviceDriver) ====================

    /// <summary>
    /// 统一连接入口
    /// TCP:  parameters = { "Mode": "TCP", "IpAddress": "...", "Port": 502 }
    /// RTU:  parameters = { "Mode": "RTU", "ComPort": "COM1", "BaudRate": 9600, ... }
    /// </summary>
    public async Task<bool> ConnectAsync(Dictionary<string, object> parameters)
    {
        _lastParams = new Dictionary<string, object>(parameters);
        var mode = parameters.TryGetValue("Mode", out var m) ? m.ToString() : "TCP";

        return mode switch
        {
            "TCP" => await ConnectTcpInternalAsync(
                parameters.TryGetValue("IpAddress", out var ip) ? ip.ToString()! : "127.0.0.1",
                parameters.TryGetValue("Port", out var p) ? Convert.ToInt32(p) : 502),
            "RTU" => ConnectRtuInternal(
                parameters.TryGetValue("ComPort", out var com) ? com.ToString()! : "COM1",
                parameters.TryGetValue("BaudRate", out var br) ? Convert.ToInt32(br) : 9600,
                parameters.TryGetValue("Parity", out var par) ? (Parity)par : Parity.None,
                parameters.TryGetValue("DataBits", out var db) ? Convert.ToInt32(db) : 8,
                parameters.TryGetValue("StopBits", out var sb) ? (StopBits)sb : StopBits.One,
                parameters.TryGetValue("SlaveId", out var sid) ? Convert.ToByte(sid) : (byte)1),
            _ => false
        };
    }

    // ==================== Modbus TCP ====================

    private async Task<bool> ConnectTcpInternalAsync(string ip, int port = 502)
    {
        try
        {
            var client = new TcpClient();
            await client.ConnectAsync(ip, port);
            _transport = client;
            _master = ModbusIpMaster.CreateIp(client);
            IsConnected = true;
            ConnectionType = $"TCP ({ip}:{port})";
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Modbus TCP] 连接失败 ({ip}:{port}): {ex.Message}");
            IsConnected = false;
            return false;
        }
    }

    // ==================== Modbus RTU (串口) ====================

    private bool ConnectRtuInternal(string portName, int baudRate = 9600, Parity parity = Parity.None,
                           int dataBits = 8, StopBits stopBits = StopBits.One, byte slaveId = 1)
    {
        try
        {
            var serialPort = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
            {
                ReadTimeout = 1000,
                WriteTimeout = 1000
            };
            serialPort.Open();

            _transport = serialPort;
            _master = ModbusSerialMaster.CreateRtu(new SerialPortAdapter(serialPort));
            _slaveId = slaveId;
            IsConnected = true;
            ConnectionType = $"RTU ({portName},{baudRate},ID={slaveId})";
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Modbus RTU] 连接失败 ({portName},{baudRate}): {ex.Message}");
            IsConnected = false;
            return false;
        }
    }

    // ==================== 断开 ====================

    public void Disconnect()
    {
        _master?.Dispose();
        _master = null;

        switch (_transport)
        {
            case TcpClient tcp:
                tcp.Close();
                break;
            case SerialPort sp:
                sp.Close();
                sp.Dispose();
                break;
        }
        _transport = null;
        IsConnected = false;
        ConnectionType = "无";
    }

    /// <summary>断线重连，使用上次连接参数</summary>
    public async Task<bool> ReconnectAsync()
    {
        Disconnect();
        if (_lastParams.Count == 0) return false;

        return await ConnectAsync(_lastParams);
    }

    // ==================== 单点读取（功能码 + 数据转换 + 异常保护） ====================

    /// <summary>读保持寄存器 (功能码 03)，自动做 Scale/Offset 换算，失败返回 NaN</summary>
    public async Task<double> ReadHoldingRegisterAsync(TagConfig tag)
    {
        try
        {
            var raw = (await _master!.ReadHoldingRegistersAsync(_slaveId, tag.Address, 1))[0];
            return raw * tag.Scale + tag.Offset;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Modbus HR] 读取失败 '{tag.Name}'(地址{tag.Address}): {ex.Message}");
            return double.NaN;
        }
    }

    /// <summary>读输入寄存器 (功能码 04)，自动做 Scale/Offset 换算，失败返回 NaN</summary>
    public async Task<double> ReadInputRegisterAsync(TagConfig tag)
    {
        try
        {
            var raw = (await _master!.ReadInputRegistersAsync(_slaveId, tag.Address, 1))[0];
            return raw * tag.Scale + tag.Offset;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Modbus IR] 读取失败 '{tag.Name}'(地址{tag.Address}): {ex.Message}");
            return double.NaN;
        }
    }

    /// <summary>读线圈 (功能码 01)，bool 转 0.0/1.0，失败返回 NaN</summary>
    public async Task<double> ReadCoilAsync(TagConfig tag)
    {
        try
        {
            var raw = (await _master!.ReadCoilsAsync(_slaveId, tag.Address, 1))[0];
            return raw ? 1.0 : 0.0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Modbus Coil] 读取失败 '{tag.Name}'(地址{tag.Address}): {ex.Message}");
            return double.NaN;
        }
    }

    /// <summary>读离散输入 (功能码 02)，bool 转 0.0/1.0，失败返回 NaN</summary>
    public async Task<double> ReadDiscreteInputAsync(TagConfig tag)
    {
        try
        {
            var raw = (await _master!.ReadInputsAsync(_slaveId, tag.Address, 1))[0];
            return raw ? 1.0 : 0.0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Modbus DI] 读取失败 '{tag.Name}'(地址{tag.Address}): {ex.Message}");
            return double.NaN;
        }
    }

    // ==================== 批量采集 ====================

    /// <summary>批量读取全部点位，按 RegisterType 分发到对应读方法</summary>
    public async Task<Dictionary<string, double>> ReadAllTagsAsync(List<TagConfig> tags)
    {
        var result = new Dictionary<string, double>();
        foreach (var tag in tags)
        {
            result[tag.Name] = tag.RegisterType switch
            {
                "HR"   => await ReadHoldingRegisterAsync(tag),
                "Coil" => await ReadCoilAsync(tag),
                "DI"   => await ReadDiscreteInputAsync(tag),
                "IR"   => await ReadInputRegisterAsync(tag),
                _      => double.NaN
            };
        }
        return result;
    }

    // ==================== 线圈写入 ====================

    /// <summary>
    /// 写单个线圈 (功能码 05)
    /// </summary>
    public async Task<bool> WriteCoilAsync(ushort address, bool value, byte slaveId)
    {
        if (_master == null) throw new InvalidOperationException("未连接到设备");
        try
        {
            await _master.WriteSingleCoilAsync(slaveId, address, value);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Modbus] 写线圈失败 地址={address}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 写多个线圈 (功能码 15)
    /// </summary>
    public async Task<bool> WriteMultipleCoilsAsync(ushort address, bool[] values, byte slaveId)
    {
        if (_master == null) throw new InvalidOperationException("未连接到设备");
        try
        {
            await _master.WriteMultipleCoilsAsync(slaveId, address, values);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Modbus] 写多线圈失败 地址={address}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 写线圈并回读验证 (功能码 05 + 01)
    /// 流程：写命令 → 等 50ms → 读回 → 比对
    /// </summary>
    public async Task<bool> WriteCoilAndVerifyAsync(ushort address, bool value, byte slaveId)
    {
        try
        {
            if (!await WriteCoilAsync(address, value, slaveId)) return false;
            await Task.Delay(50);
            var actual = (await _master!.ReadCoilsAsync(slaveId, address, 1))[0];
            if (actual == value) return true;

            System.Diagnostics.Debug.WriteLine(
                $"[Modbus] 线圈验证失败: 地址={address}, 期望={value}, 实际={actual}");
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Modbus] 线圈操作失败 地址={address}: {ex.Message}");
            return false;
        }
    }

    // ==================== 寄存器写入 ====================

    /// <summary>
    /// 写入单个保持寄存器 (功能码 06)
    /// 自动做 工程值 → 原始值 换算：rawValue = (value - Offset) / Scale
    /// </summary>
    public async Task<bool> WriteTagAsync(TagConfig tag, double value)
    {
        if (_master == null) throw new InvalidOperationException("未连接到设备");
        try
        {
            var rawValue = (ushort)Math.Round((value - tag.Offset) / tag.Scale);
            await _master.WriteSingleRegisterAsync(_slaveId, tag.Address, rawValue);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Modbus] 写入失败 '{tag.Name}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 写多个保持寄存器 (功能码 16)
    /// </summary>
    public async Task<bool> WriteMultipleRegistersAsync(ushort address, ushort[] values, byte slaveId)
    {
        if (_master == null) throw new InvalidOperationException("未连接到设备");
        try
        {
            await _master.WriteMultipleRegistersAsync(slaveId, address, values);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Modbus] 多寄存器写入失败 地址={address}: {ex.Message}");
            return false;
        }
    }

    public void Dispose() => Disconnect();
}
