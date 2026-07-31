namespace IndustrialDAQ.Models;

/// <summary>
/// Browse 结果 —— 浏览 OPC UA 地址空间时发现的节点信息
/// 用于动态发现服务器上的数据点，替代手写配置文件的方式
/// </summary>
public class BrowseNode
{
    /// <summary>OPC UA NodeId 字符串，可直接用于 Read/Write/Subscribe</summary>
    public string NodeId { get; init; } = string.Empty;

    /// <summary>显示名，如 "燃烧室温度"</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>从浏览起点到这里的完整路径，如 "Objects/设备1/温度"</summary>
    public string BrowsePath { get; init; } = string.Empty;

    /// <summary>节点类型 (Object / Variable / Method 等)</summary>
    public string NodeClass { get; init; } = string.Empty;
}
