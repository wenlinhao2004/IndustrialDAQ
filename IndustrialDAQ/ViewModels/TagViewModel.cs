using CommunityToolkit.Mvvm.ComponentModel;
using IndustrialDAQ.Models;

namespace IndustrialDAQ.ViewModels;

/// <summary>
/// 点位视图模型 —— TagConfig 配置 + 运行时值，绑定到 UI
/// </summary>
public partial class TagViewModel : ObservableObject
{
    private readonly TagConfig _config;
    public string TagName { get; set; }
    public string Unit { get; set; }
    public string DisplayName { get; set; } = "";

    [ObservableProperty] private double _value;
    [ObservableProperty] private string _displayValue = "--";
    [ObservableProperty] private bool _isAlarm;
    [ObservableProperty] private string _alarmType = "";

    public TagViewModel(TagConfig config)
    {
        _config = config;
        TagName = config.Name;
        Unit = config.Unit;
    }

    public void UpdateValue(double val)
    {
        Value = val;
        DisplayValue = $"{val:F2} {_config.Unit}";
        IsAlarm = val > _config.HighLimit || val < _config.LowLimit;
        AlarmType = val > _config.HighLimit ? "▲ 高" : val < _config.LowLimit ? "▼ 低" : "";
    }
}
