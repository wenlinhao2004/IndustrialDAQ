using System.IO;
using Microsoft.Data.Sqlite;

namespace IndustrialDAQ.Services;

/// <summary>
/// 数据记录服务 —— SQLite 数据库读写
/// </summary>
public class DataLogger : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly string _dbPath;

    public DataLogger(string dbPath = "modbus_data.db")
    {
        _dbPath = dbPath;
        _db = new SqliteConnection($"Data Source={dbPath}");
        _db.Open();
        InitTable();
    }

    private void InitTable()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS TagRecords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TagName TEXT NOT NULL,
                Value REAL NOT NULL,
                Timestamp TEXT NOT NULL DEFAULT (datetime('now', 'localtime'))
            );
            CREATE INDEX IF NOT EXISTS idx_timestamp ON TagRecords(Timestamp);
            CREATE INDEX IF NOT EXISTS idx_tagname ON TagRecords(TagName);
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>批量写入 (事务)</summary>
    public void InsertBatch(Dictionary<string, double> values)
    {
        using var tx = _db.BeginTransaction();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "INSERT INTO TagRecords (TagName, Value) VALUES (@name, @val)";
        var p1 = cmd.CreateParameter(); p1.ParameterName = "@name"; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter(); p2.ParameterName = "@val"; cmd.Parameters.Add(p2);

        foreach (var kv in values)
        {
            p1.Value = kv.Key;
            p2.Value = kv.Value;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>总记录数</summary>
    public long GetRecordCount()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM TagRecords";
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>数据库文件大小 (KB)</summary>
    public long GetFileSizeKb()
    {
        try { return new FileInfo(_dbPath).Length / 1024; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DataLogger] 获取文件大小失败: {ex.Message}");
            return 0;
        }
    }

    /// <summary>历史数据查询</summary>
    public List<HistoryRecord> QueryHistory(string? tagName = null, int limit = 500,
                                             string? fromTime = null, string? toTime = null)
    {
        var result = new List<HistoryRecord>();
        using var cmd = _db.CreateCommand();

        var conditions = new List<string>();
        if (!string.IsNullOrEmpty(tagName))
        {
            conditions.Add("TagName = @tag");
            cmd.Parameters.AddWithValue("@tag", tagName);
        }
        if (!string.IsNullOrEmpty(fromTime))
        {
            conditions.Add("Timestamp >= @from");
            cmd.Parameters.AddWithValue("@from", fromTime);
        }
        if (!string.IsNullOrEmpty(toTime))
        {
            conditions.Add("Timestamp <= @to");
            cmd.Parameters.AddWithValue("@to", toTime);
        }

        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
        cmd.CommandText = $"SELECT TagName, Value, Timestamp FROM TagRecords {where} ORDER BY Id DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(new HistoryRecord
            {
                TagName = reader.GetString(0),
                Value = reader.GetDouble(1),
                Timestamp = reader.GetString(2)
            });
        result.Reverse();
        return result;
    }

    public void Dispose() => _db.Dispose();
}

/// <summary>
/// 历史数据记录（用于 UI 绑定）
/// </summary>
public class HistoryRecord
{
    public string TagName { get; set; } = "";
    public double Value { get; set; }
    public string Timestamp { get; set; } = "";
    // 格式化后的显示值
    public string DisplayValue => $"{Value:F2}";
}
