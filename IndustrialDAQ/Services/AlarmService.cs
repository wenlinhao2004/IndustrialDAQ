using ModbusDAQ.Models;

namespace ModbusDAQ.Services;

/// <summary>
/// 报警服务 —— 检测数值越限并记录报警
/// </summary>
public class AlarmService
{
    private readonly HashSet<string> _activeAlarms = new(); // 防止重复报警

    public event Action<AlarmRecord>? OnAlarm;
    public event Action<string>? OnAlarmClear;

    /// <summary>检查所有点位是否越限</summary>
    public List<AlarmRecord> CheckLimits(List<TagConfig> tags, Dictionary<string, double> values)
    {
        var alarms = new List<AlarmRecord>();

        foreach (var tag in tags)
        {
            if (!values.TryGetValue(tag.Name, out var val)) continue;

            bool isHigh = val > tag.HighLimit;
            bool isLow = val < tag.LowLimit;

            if (isHigh || isLow)
            {
                if (_activeAlarms.Add(tag.Name)) // 新的报警，防止重复
                {
                    var record = new AlarmRecord
                    {
                        TagName = tag.Name,
                        Value = val,
                        Limit = isHigh ? tag.HighLimit : tag.LowLimit,
                        Type = isHigh ? "高报警" : "低报警",
                        Time = DateTime.Now.ToString("HH:mm:ss")
                    };
                    alarms.Add(record);
                    OnAlarm?.Invoke(record);
                }
            }
            else
            {
                if (_activeAlarms.Remove(tag.Name))
                    OnAlarmClear?.Invoke(tag.Name);
            }
        }

        return alarms;
    }
}

/// <summary>
/// 报警记录
/// </summary>
public class AlarmRecord
{
    public string TagName { get; set; } = string.Empty;
    public double Value { get; set; }
    public double Limit { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
}
