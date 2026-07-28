namespace IndustrialDAQ.Models;

/// <summary>
/// 设备配置 —— 描述一个物理设备：用什么协议、怎么连、点位在哪
/// </summary>
public class DeviceConfig
{
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string Protocol { get; set; } = "ModbusTCP";
    public Dictionary<string, object> ConnectionParams { get; set; } = new();
    public List<TagConfig> Tags { get; set; } = new();
}
