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
    private bool _simulationMode;
    private byte _slaveId = 1;               // RTU 从站地址
    private readonly Random _random = new();

    public bool IsConnected { get; private set; }
    public bool IsSimulation => _simulationMode;
    public string ConnectionType { get; private set; } = "无"; // "TCP" / "RTU" / "Simulation"

    // ==================== 统一连接入口 (IDeviceDriver) ====================

    /// <summary>
    /// 统一连接入口
    /// TCP:  parameters = { "Mode": "TCP", "IpAddress": "...", "Port": 502 }
    /// RTU:  parameters = { "Mode": "RTU", "ComPort": "COM1", "BaudRate": 9600, ... }
    /// </summary>
    public async Task<bool> ConnectAsync(Dictionary<string, object> parameters)
    {
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
            _simulationMode = false;
            IsConnected = true;
            ConnectionType = $"TCP ({ip}:{port})";
            return true;
        }
        catch
        {
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
            _simulationMode = false;
            IsConnected = true;
            ConnectionType = $"RTU ({portName},{baudRate},ID={slaveId})";
            return true;
        }
        catch
        {
            IsConnected = false;
            return false;
        }
    }

    // ==================== 模拟模式 ====================

    public void EnableSimulation()
    {
        Disconnect();
        _simulationMode = true;
        IsConnected = true;
        ConnectionType = "Modbus (Simulation)";
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
        _simulationMode = false;
        IsConnected = false;
        ConnectionType = "无";
    }

    // ==================== 数据读取 ====================

    /// <summary>读取保持寄存器</summary>
    private async Task<ushort[]> ReadHoldingRegistersAsync(ushort startAddress, ushort count, byte slaveId)
    {
        if (_simulationMode)
            return Enumerable.Range(0, count).Select(_ => (ushort)_random.Next(0, 65535)).ToArray();

        if (_master == null)
            throw new InvalidOperationException("未连接到设备");

        return await _master.ReadHoldingRegistersAsync(slaveId, startAddress, count);
    }

    /// <summary>批量读取所有配置点位</summary>
    public async Task<Dictionary<string, double>> ReadAllTagsAsync(List<TagConfig> tags)
    {
        if (tags.Count == 0) return new Dictionary<string, double>();

        var minAddr = tags.Min(t => t.Address);
        var maxAddr = tags.Max(t => t.Address);
        ushort count = (ushort)(maxAddr - minAddr + 1);

        var rawValues = await ReadHoldingRegistersAsync(minAddr, count, _slaveId);
        var result = new Dictionary<string, double>();

        foreach (var tag in tags)
        {
            int index = tag.Address - minAddr;
            if (index < rawValues.Length)
                result[tag.Name] = rawValues[index] * tag.Scale + tag.Offset;
        }

        return result;
    }

    public void Dispose() => Disconnect();
}
