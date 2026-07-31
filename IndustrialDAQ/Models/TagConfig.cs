namespace IndustrialDAQ.Models;

/// <summary>
/// 数据点位配置 —— 同时支持 Modbus 寄存器、OPC UA 节点 和 S7 DB 块
/// </summary>
public class TagConfig
{
    public string Name { get; set; } = string.Empty;       // 点位名称，如 "主电机温度"
    public ushort Address { get; set; }                     // Modbus 区内地址 (从 0 开始)
    public string RegisterType { get; set; } = string.Empty; // Modbus 数据区: HR / IR / Coil / DI
    public string NodeId { get; set; } = string.Empty;      // OPC UA NodeId (如 "ns=2;s=Temperature")
    public int DbNumber { get; set; } = 1;                 // S7: DB 块编号
    public int ByteOffset { get; set; } = 0;               // S7: DB 块内字节偏移
    public string S7DataType { get; set; } = "REAL";       // S7: 数据类型 (BOOL/BYTE/INT/DINT/REAL)
    public int BitOffset { get; set; } = 0;                // S7: BOOL 类型时的位偏移 (0-7)
    public string Unit { get; set; } = string.Empty;        // 单位，如 "℃"、"MPa"
    public double HighLimit { get; set; }                   // 高报警限值
    public double LowLimit { get; set; }                    // 低报警限值
    public double Scale { get; set; } = 1.0;               // 缩放系数 (寄存器原始值 × Scale = 实际值)
    public double Offset { get; set; } = 0.0;              // 偏移量
}
