namespace IndustrialDAQ.Models;

/// <summary>
/// 订阅回调推送的数据包 — 收到服务器推送时，回调里组装好再交给 UI 层
/// </summary>
public record SubscribedValueUpdate(
    /// <summary>点位名称 (来自 TagConfig.Name)</summary>
    string TagName,

    /// <summary>工程值 (原始值 × Scale + Offset 已完成)</summary>
    double Value,

    /// <summary>服务器实际采集时间 (不是收到推送的时间)</summary>
    DateTime SourceTimestamp,

    /// <summary>数据质量码 (Good / Bad / Uncertain)</summary>
    string StatusCode
);
