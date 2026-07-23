using IndustrialDAQ.Models;
using Opc.Ua;
using Opc.Ua.Client;

namespace IndustrialDAQ.Services;

/// <summary>
/// OPC UA 设备驱动 —— 基于 OPC Foundation .NET Standard SDK
///
/// 面试要点:
///   - OPC UA 是工业 4.0 核心协议, 替代传统 OPC DA
///   - 地址空间模型 (Address Space): NodeId / BrowsePath / Attributes
///   - 订阅(Subscription) vs 轮询(Polling): 本驱动支持 Read 批量读取
///   - 安全策略: None / Basic256Sha256 / Sign & Encrypt
///   - 会话(Session)管理: 断线重连、KeepAlive
/// </summary>
public class OpcUaDriver : IDeviceDriver
{
    private Session? _session;
    private ApplicationConfiguration? _appConfig;
    private bool _simulationMode;
    private readonly Random _random = new();

    public bool IsConnected { get; private set; }
    public bool IsSimulation => _simulationMode;
    public string ConnectionType { get; private set; } = "无";

    /// <summary>
    /// 连接到 OPC UA 服务器
    /// parameters 需包含: "EndpointUrl" (string)
    /// parameters 可选:   "Username" (string), "Password" (string) —— 提供则走用户名密码认证，否则匿名
    /// </summary>
    public async Task<bool> ConnectAsync(Dictionary<string, object> parameters)
    {
        if (!parameters.TryGetValue("EndpointUrl", out var urlObj) || urlObj is not string endpointUrl)
            return false;

        try
        {
            // 构建客户端配置 (Demo 模式: 自动接受所有证书)
            _appConfig = new ApplicationConfiguration
            {
                ApplicationName = "IndustrialDAQ",
                ApplicationType = ApplicationType.Client,
                ApplicationUri = $"urn:IndustrialDAQ:{Environment.MachineName}",
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier(),
                    AutoAcceptUntrustedCertificates = true // 仅 Demo, 生产环境须校验
                },
                TransportQuotas = new TransportQuotas
                {
                    OperationTimeout = 5000,
                    MaxStringLength = 65536
                },
                ClientConfiguration = new ClientConfiguration
                {
                    DefaultSessionTimeout = 60000
                }
            };

            await _appConfig.Validate(ApplicationType.Client);

            // 使用端点 URL 创建会话
            var endpointDesc = new EndpointDescription(endpointUrl);
            var endpointConfig = EndpointConfiguration.Create(_appConfig);
            var configuredEndpoint = new ConfiguredEndpoint(null, endpointDesc, endpointConfig);

            // 从参数读取凭证，提供则用用户名密码认证，否则匿名
            var username = parameters.TryGetValue("Username", out var u) ? u.ToString() ?? "" : "";
            var password = parameters.TryGetValue("Password", out var p) ? p.ToString() ?? "" : "";
            var identity = !string.IsNullOrEmpty(username)
                ? new UserIdentity(username, password)
                : new UserIdentity();

            _session = await Session.Create(
                configuration: _appConfig,
                endpoint: configuredEndpoint,
                updateBeforeConnect: false,
                checkDomain: false,
                sessionName: "IndustrialDAQ-Session",
                sessionTimeout: 60000U,
                identity: identity,
                preferredLocales: null);

            _simulationMode = false;
            IsConnected = true;
            ConnectionType = $"OPC UA ({endpointUrl})";
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OPC UA] 连接失败: {ex.Message}");
            IsConnected = false;
            return false;
        }
    }

    /// <summary>批量读取 OPC UA 节点</summary>
    public async Task<Dictionary<string, double>> ReadAllTagsAsync(List<TagConfig> tags)
    {
        if (_simulationMode)
            return tags.ToDictionary(t => t.Name, _ => _random.NextDouble() * 100);

        if (_session == null || !_session.Connected)
            throw new InvalidOperationException("OPC UA 会话未连接");

        var nodesToRead = new ReadValueIdCollection();
        var tagList = new List<TagConfig>();

        foreach (var tag in tags)
        {
            if (string.IsNullOrEmpty(tag.NodeId)) continue;

            try
            {
                nodesToRead.Add(new ReadValueId
                {
                    NodeId = NodeId.Parse(tag.NodeId),
                    AttributeId = Attributes.Value
                });
                tagList.Add(tag);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OPC UA] 解析 NodeId 失败 '{tag.NodeId}': {ex.Message}");
            }
        }

        if (nodesToRead.Count == 0) return new Dictionary<string, double>();

        var response = await _session.ReadAsync(
            requestHeader: null,
            maxAge: 0,
            timestampsToReturn: TimestampsToReturn.Neither,
            nodesToRead: nodesToRead,
            ct: CancellationToken.None);

        var result = new Dictionary<string, double>();
        for (int i = 0; i < tagList.Count; i++)
        {
            if (StatusCode.IsGood(response.Results[i].StatusCode))
            {
                var rawValue = response.Results[i].Value;
                var doubleValue = Convert.ToDouble(rawValue);
                var tag = tagList[i];
                result[tag.Name] = doubleValue * tag.Scale + tag.Offset;
            }
        }

        return result;
    }

    public void EnableSimulation()
    {
        Disconnect();
        _simulationMode = true;
        IsConnected = true;
        ConnectionType = "OPC UA (Simulation)";
    }

    public void Disconnect()
    {
        _session?.Close();
        _session?.Dispose();
        _session = null;
        _simulationMode = false;
        IsConnected = false;
        ConnectionType = "无";
    }

    public void Dispose() => Disconnect();
}
