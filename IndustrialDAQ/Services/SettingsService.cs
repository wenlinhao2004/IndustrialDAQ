using System.IO;
using System.Text.Json;

namespace ModbusDAQ.Services;

/// <summary>
/// 配置持久化服务 —— 保存/加载用户设置到 JSON 文件
/// 避免每次启动重新配置 IP、串口号等
/// </summary>
public class SettingsService
{
    private static readonly string FilePath = "appsettings.json";

    public string IpAddress { get; set; } = "127.0.0.1";
    public string ComPort { get; set; } = "COM1";
    public int BaudRate { get; set; } = 9600;
    public int SelectedMode { get; set; } // 0=TCP, 1=RTU, 2=OPC UA
    public int LogIntervalMs { get; set; } = 1000;
    public string OpcUaEndpointUrl { get; set; } = "opc.tcp://localhost:4840";
    public string OpcUaUsername { get; set; } = "";
    public string OpcUaPassword { get; set; } = "";
    public string AdminUsername { get; set; } = "admin";
    public string AdminPassword { get; set; } = "admin";

    public static SettingsService Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<SettingsService>(json) ?? new();
            }
        }
        catch { /* 文件损坏则使用默认 */ }
        return new SettingsService();
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
