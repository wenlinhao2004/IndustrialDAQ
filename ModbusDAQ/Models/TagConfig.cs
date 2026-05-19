namespace ModbusDAQ.Models;

/// <summary>
/// 数据点位配置 —— 同时支持 Modbus 寄存器 和 OPC UA 节点
/// </summary>
public class TagConfig
{
    public string Name { get; set; } = string.Empty;       // 点位名称，如 "主电机温度"
    public ushort Address { get; set; }                     // Modbus 保持寄存器地址
    public string NodeId { get; set; } = string.Empty;      // OPC UA NodeId (如 "ns=2;s=Temperature")
    public string Unit { get; set; } = string.Empty;        // 单位，如 "℃"、"MPa"
    public double HighLimit { get; set; }                   // 高报警限值
    public double LowLimit { get; set; }                    // 低报警限值
    public double Scale { get; set; } = 1.0;               // 缩放系数 (寄存器原始值 × Scale = 实际值)
    public double Offset { get; set; } = 0.0;              // 偏移量
}
