using System.IO;
using System.Text;

namespace ModbusDAQ.Services;

/// <summary>
/// 数据导出服务 —— 将历史数据导出为 CSV 文件
/// 工控常见需求：日报表、月报表导出
/// </summary>
public static class DataExporter
{
    /// <summary>导出为 CSV（GB2312 编码，兼容国产 PLC 上位机软件）</summary>
    public static void ExportToCsv(IEnumerable<HistoryRecord> records, string? filePath = null)
    {
        filePath ??= $"ModbusData_{DateTime.Now:yyyyMMdd_HHmmss}.csv";

        // GB2312 编码 —— 解决 Excel 打开中文乱码
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding("GB2312");

        using var writer = new StreamWriter(filePath, false, encoding);
        writer.WriteLine("序号,点位名称,数值,时间");

        int index = 1;
        foreach (var r in records)
        {
            writer.WriteLine($"{index++},{r.TagName},{r.Value:F2},{r.Timestamp}");
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{Path.GetFullPath(filePath)}\""
        });
    }
}
