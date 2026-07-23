using System.IO.Ports;
using Modbus.IO;

namespace IndustrialDAQ.Services;

/// <summary>
/// 串口适配器 —— 将 System.IO.Ports.SerialPort 包装为 NModbus 的 IStreamResource
/// 设计模式: Adapter 适配器模式
/// </summary>
public class SerialPortAdapter : IStreamResource, IDisposable
{
    private readonly SerialPort _serialPort;

    public SerialPortAdapter(SerialPort serialPort)
    {
        _serialPort = serialPort;
    }

    public int InfiniteTimeout => SerialPort.InfiniteTimeout;
    public int ReadTimeout { get => _serialPort.ReadTimeout; set => _serialPort.ReadTimeout = value; }
    public int WriteTimeout { get => _serialPort.WriteTimeout; set => _serialPort.WriteTimeout = value; }

    public void DiscardInBuffer() => _serialPort.DiscardInBuffer();

    public int Read(byte[] buffer, int offset, int count)
        => _serialPort.Read(buffer, offset, count);

    public void Write(byte[] buffer, int offset, int count)
        => _serialPort.Write(buffer, offset, count);

    public void Dispose() => _serialPort?.Dispose();
}
