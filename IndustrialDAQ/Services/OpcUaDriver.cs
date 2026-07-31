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
///   - 订阅(Subscription) vs 轮询(Polling): 本驱动实现 Read / Write / Browse / Subscribe 四大基础操作
///   - 安全策略: None / Basic256Sha256 / Sign & Encrypt
///   - 会话(Session)管理: 断线重连、KeepAlive
/// </summary>
public class OpcUaDriver : IDeviceDriver
{
    private Session? _session;
    private ApplicationConfiguration? _appConfig;
    private Dictionary<string, object> _lastParams = new();

    // ==================== 订阅管理 ====================
    private Subscription? _subscription;
    /// <summary>
    /// ClientHandle → TagConfig 映射表
    /// 每个 MonitoredItem 建好时 SDK 分配一个 Handle 编号，
    /// 回调时用 Handle 反查是哪个点位，才能做 Scale/Offset 换算
    /// </summary>
    private readonly Dictionary<uint, TagConfig> _monitoredTags = new();

    public bool IsConnected { get; private set; }
    public string ConnectionType { get; private set; } = "无";

    /// <summary>
    /// 订阅数据推送事件 — 服务器主动推送变化值时触发
    /// UI 层订阅此事件即可拿到已换算的工程值，无需自己轮询
    /// </summary>
    public event Action<SubscribedValueUpdate>? OnSubscribedDataReceived;

    /// <summary>
    /// 连接到 OPC UA 服务器
    /// parameters 需包含: "EndpointUrl" (string)
    /// parameters 可选:   "Username" (string), "Password" (string) —— 提供则走用户名密码认证，否则匿名
    /// </summary>
    public async Task<bool> ConnectAsync(Dictionary<string, object> parameters)
    {
        _lastParams = new Dictionary<string, object>(parameters);
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

            await _appConfig.Validate(ApplicationType.Client);// 验证配置

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
                // OPC UA 返回的直接就是工程值，不需要换算
                var value = Convert.ToDouble(response.Results[i].Value);
                var tag = tagList[i];
                result[tag.Name] = value;
            }
        }

        return result;
    }

    // ==================== 数据写入 ====================

    /// <summary>
    /// 写入单个 OPC UA 节点
    /// OPC UA 直接写工程值，不需要 Scale/Offset 反算
    /// </summary>
    public async Task<bool> WriteTagAsync(TagConfig tag, double value)
    {
        if (_session == null || !_session.Connected)
            throw new InvalidOperationException("OPC UA 会话未连接");

        if (string.IsNullOrEmpty(tag.NodeId)) return false;

        try
        {
            // OPC UA 写工程值，不需要 (value - Offset) / Scale 反算
            var nodeId = NodeId.Parse(tag.NodeId);

            var writeValue = new WriteValue
            {
                NodeId = nodeId,
                AttributeId = Attributes.Value,
                Value = new DataValue(new Variant(value))
            };

            var response = await _session.WriteAsync(
                requestHeader: null,
                nodesToWrite: new WriteValueCollection { writeValue },
                ct: CancellationToken.None);

            return StatusCode.IsGood(response.Results[0]);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OPC UA] 写入失败 '{tag.Name}': {ex.Message}");
            return false;
        }
    }

    // ==================== Browse（浏览地址空间） ====================

    /// <summary>
    /// 浏览 OPC UA 地址空间，递归发现所有变量节点
    /// </summary>
    /// <param name="startNode">浏览起点，null 则从 Objects/ 开始</param>
    /// <param name="maxDepth">最大递归深度，防止无限遍历</param>
    /// <param name="ct">取消令牌</param>
    public async Task<List<Models.BrowseNode>> BrowseAsync(
        NodeId? startNode = null,
        int maxDepth = 5,
        CancellationToken ct = default)
    {
        if (_session == null || !_session.Connected)
            throw new InvalidOperationException("OPC UA 会话未连接");

        var result = new List<Models.BrowseNode>();
        var visited = new HashSet<NodeId>();
        var root = startNode ?? ObjectIds.ObjectsFolder;

        await BrowseRecursiveAsync(_session, root, string.Empty, maxDepth, 0, visited, result, ct);
        return result;
    }

    private static async Task BrowseRecursiveAsync(
        Session session,
        NodeId currentNode,
        string currentPath,
        int maxDepth,
        int depth,
        HashSet<NodeId> visited,
        List<Models.BrowseNode> result,
        CancellationToken ct)
    {
        if (depth > maxDepth || !visited.Add(currentNode))
            return;

        ct.ThrowIfCancellationRequested();

        var (_, _, referencesList, _) = await session.BrowseAsync(
            null,                                    // requestHeader
            null,                                    // view
            new[] { currentNode },                   // nodesToBrowse (IList<NodeId>)
            0u,                                      // maxResultsToReturn
            BrowseDirection.Forward,                 // browseDirection
            ReferenceTypeIds.HierarchicalReferences, // referenceTypeId
            true,                                    // includeSubtypes
            0u,                                      // nodeClassMask
            ct);                                     // cancellationToken

        foreach (var refDesc in referencesList[0])
        {
            var childNodeId = ExpandedNodeId.ToNodeId(refDesc.NodeId, session.NamespaceUris);
            if (childNodeId == null) continue;

            var childPath = string.IsNullOrEmpty(currentPath)
                ? refDesc.DisplayName.Text
                : $"{currentPath}/{refDesc.DisplayName.Text}";

            var browseNode = new Models.BrowseNode
            {
                NodeId = childNodeId.ToString(),
                DisplayName = refDesc.DisplayName.Text,
                BrowsePath = childPath,
                NodeClass = refDesc.NodeClass.ToString()
            };
            result.Add(browseNode);

            // 只有 Object 类型的节点才继续往下翻
            if (refDesc.NodeClass == NodeClass.Object)
            {
                await BrowseRecursiveAsync(
                    session, childNodeId, childPath,
                    maxDepth, depth + 1, visited, result, ct);
            }
        }
    }

    // ==================== Subscription（订阅） ====================

    /// <summary>
    /// 创建订阅 — 服务器主动推送数据变化，无需轮询
    ///
    /// 三步流程：
    ///   ① new Subscription → AddSubscription（告诉服务器「我要开个订阅」）
    ///   ② 为每个点位 new MonitoredItem（告诉服务器「帮我盯着这些点」）
    ///   ③ 设 FastDataChangeCallback + ApplyChanges（告诉服务器「推过来时调这个方法」）
    ///
    /// 之后服务器会在后台持续采样，有变化时回调 OnSubscribedDataReceived 事件
    /// </summary>
    /// <param name="tags">要监控的点位列表（至少需要 NodeId 和 Name）</param>
    /// <param name="publishingIntervalMs">推送间隔 (ms)，即服务器最多每隔多久打包推送一次变化</param>
    /// <param name="samplingIntervalMs">采样间隔 (ms)，即服务器每隔多久去看一次值</param>
    public async Task SubscribeAsync(
        List<TagConfig> tags,
        int publishingIntervalMs = 1000,
        int samplingIntervalMs = 500)
    {
        if (_session == null || !_session.Connected)
            throw new InvalidOperationException("OPC UA 会话未连接");
        if (tags.Count == 0)
            throw new ArgumentException("点位列表不能为空");

        // 如果已有订阅，先清理
        Unsubscribe();

        // ── ① 建订阅容器 ──
        _subscription = new Subscription
        {
            PublishingInterval = publishingIntervalMs,    // 最多隔多久推送一次
            MaxNotificationsPerPublish = 0,               // 0 = 不限每次推送的条目数
            Priority = 0,                                 // 优先级（仅多订阅时有意义）
        };
        _session.AddSubscription(_subscription);

        // ── ② 为每个点位建 MonitoredItem ──
        _monitoredTags.Clear();

        foreach (var tag in tags)
        {
            if (string.IsNullOrEmpty(tag.NodeId)) continue;

            var monitoredItem = new MonitoredItem
            {
                DisplayName = tag.Name,
                StartNodeId = NodeId.Parse(tag.NodeId),
                AttributeId = Attributes.Value,          // 盯着 Value 属性
                SamplingInterval = samplingIntervalMs,   // 服务器采样频率
                QueueSize = 1,                           // 只保留最新值，旧值丢弃
                // ClientHandle 由 SDK 在 AddItem 后分配，不能手动设
            };
            _subscription.AddItem(monitoredItem);

            // AddItem 之后 SDK 才分配 ClientHandle → 此时才能建立映射
            _monitoredTags[monitoredItem.ClientHandle] = tag;
        }

        if (_monitoredTags.Count == 0)
        {
            Unsubscribe();
            throw new ArgumentException("没有有效的 NodeId 可订阅");
        }

        // ── ③ 设回调 — 服务器推送数据时 SDK 的后台线程会调用这段代码 ──
        _subscription.FastDataChangeCallback = (sub, notification, _) =>
        {
            // notification.MonitoredItems 只包含这次有变化的点，不是全部
            foreach (var item in notification.MonitoredItems)
            {
                // 用 ClientHandle 反查是哪个 TagConfig
                if (!_monitoredTags.TryGetValue(item.ClientHandle, out var tag))
                    continue;

                var dataValue = item.Value;
                if (!StatusCode.IsGood(dataValue.StatusCode))
                    continue;  // 数据质量不佳时不推送，避免上层拿到脏数据

                // OPC UA 服务器返回的直接就是工程值，不需要 Scale/Offset 换算
                var value = Convert.ToDouble(dataValue.Value);

                // 通过事件通知 UI 层
                OnSubscribedDataReceived?.Invoke(new SubscribedValueUpdate(
                    TagName: tag.Name,
                    Value: value,
                    SourceTimestamp: dataValue.SourceTimestamp,
                    StatusCode: dataValue.StatusCode.ToString()
                ));
            }
        };

        // 提交草稿 — 服务器从此刻开始真正盯着这些点
        _subscription.ApplyChanges();
    }

    /// <summary>
    /// 取消订阅 — 告诉服务器停止监控，释放本地资源
    /// </summary>
    public void Unsubscribe()
    {
        if (_subscription != null)
        {
            try
            {
                _session?.RemoveSubscription(_subscription);
            }
            catch
            {
                // 会话可能已断开，忽略清理时的异常
            }
            _subscription = null;
        }
        _monitoredTags.Clear();
    }

    public void Disconnect()
    {
        // 先取消订阅再断会话：订阅依赖于会话
        Unsubscribe();

        _session?.Close();
        _session?.Dispose();
        _session = null;
        IsConnected = false;
        ConnectionType = "无";
    }

    /// <summary>断线重连，使用上次连接参数</summary>
    public async Task<bool> ReconnectAsync()
    {
        Disconnect();
        if (_lastParams.Count == 0) return false;

        return await ConnectAsync(_lastParams);
    }

    public void Dispose() => Disconnect();
}
