using System.IO;
using System.Text.Json;

namespace IndustrialDAQ.Models;

/// <summary>
/// 设备配置加载器 —— 从 devices.json 读取设备列表
/// </summary>
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
