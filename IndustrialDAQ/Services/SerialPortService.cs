using System.Collections.Concurrent;
using System.IO.Ports;

namespace IndustrialDAQ.Services;

/// <summary>
/// 串口通信服务 —— RS232 / RS485 原始数据收发
/// 演示: SerialPort 配置、DataReceived 事件、线程安全接收队列
/// </summary>
public class SerialPortService : IDisposable
{
    private SerialPort? _serial;
    private CancellationTokenSource? _receiveCts;

    /// <summary>线程安全的接收缓冲区 —— 采集线程写入，UI 线程读取</summary>
    public ConcurrentQueue<byte[]> ReceiveQueue { get; } = new();

    public bool IsOpen => _serial?.IsOpen ?? false;
    public string PortName => _serial?.PortName ?? "--";

    public event Action<string>? OnStatusChanged;
    public event Action<byte[]>? OnDataReceived;

    /// <summary>获取可用串口列表</summary>
    public static string[] GetAvailablePorts() => SerialPort.GetPortNames();

    /// <summary>打开串口</summary>
    public bool Open(string portName, int baudRate = 9600, Parity parity = Parity.None,
                     int dataBits = 8, StopBits stopBits = StopBits.One)
    {
        try
        {
            _serial = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
            {
                ReadTimeout = 500,
                WriteTimeout = 500,
                ReadBufferSize = 4096
            };
            _serial.Open();
            OnStatusChanged?.Invoke($"串口 {portName} 已打开 {baudRate},{dataBits},{parity},{stopBits}");

            // 启动后台接收线程
            _receiveCts = new CancellationTokenSource();
            _ = ReceiveLoopAsync(_receiveCts.Token);

            return true;
        }
        catch (Exception ex)
        {
            OnStatusChanged?.Invoke($"串口打开失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>后台接收线程 —— 持续读取串口数据</summary>
    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[1024];
        while (!ct.IsCancellationRequested && _serial?.IsOpen == true)
        {
            try
            {
                int count = await _serial.BaseStream.ReadAsync(buffer, 0, buffer.Length, ct);
                if (count > 0)
                {
                    var data = buffer[..count].ToArray();
                    ReceiveQueue.Enqueue(data);           // 线程安全入队
                    OnDataReceived?.Invoke(data);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (TimeoutException) { /* 超时，继续等待 */ }
        }
    }

    /// <summary>发送数据</summary>
    public void Send(byte[] data)
    {
        if (_serial?.IsOpen == true)
            _serial.Write(data, 0, data.Length);
    }

    /// <summary>发送字符串（ASCII）</summary>
    public void SendString(string text)
    {
        if (_serial?.IsOpen == true)
            _serial.WriteLine(text);
    }

    /// <summary>关闭串口</summary>
    public void Close()
    {
        _receiveCts?.Cancel();
        _receiveCts?.Dispose();
        _receiveCts = null;
        _serial?.Close();
        _serial?.Dispose();
        _serial = null;
        OnStatusChanged?.Invoke("串口已关闭");
    }

    public void Dispose() => Close();
}
