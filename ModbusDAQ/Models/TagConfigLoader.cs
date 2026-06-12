using System.IO;
using System.Text.Json;

namespace ModbusDAQ.Models;

/// <summary>
/// 点位配置加载器 —— 从 JSON 文件翻译为 TagConfig 实例列表
/// </summary>
public static class TagConfigLoader
{
    private const string DefaultPath = "tagconfigs.json";

    public static List<TagConfig> Load(string? path = null)
    {
        path ??= DefaultPath;

        if (!File.Exists(path))
            throw new FileNotFoundException($"点位配置文件未找到: {path}");

        var json = File.ReadAllText(path);
        var wrapper = JsonSerializer.Deserialize<TagConfigWrapper>(json);
        return wrapper?.Tags ?? new List<TagConfig>();
    }

    /// <summary>辅助类，匹配 JSON 的最外层结构 { "Tags": [...] }</summary>
    private class TagConfigWrapper
    {
        public List<TagConfig> Tags { get; set; } = new();
    }
}
