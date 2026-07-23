using System.Collections.Concurrent;

namespace IndustrialDAQ.Services;

/// <summary>
/// 多线程数据管道 —— 经典生产者-消费者模式
///
/// 架构:
///   [设备读取线程] ──BlockingCollection──> [数据处理线程] ──ConcurrentQueue──> [UI 线程]
///       Producer             有界缓冲              Consumer               Dispatcher
///
/// 面试要点:
///   - BlockingCollection 有界容量 → 背压控制，防止内存溢出
///   - CancellationToken → 优雅关闭
///   - ConcurrentQueue → 无锁线程安全
///   - Task + async/await → 托管线程
/// </summary>
public class DataPipeline<T> : IDisposable
{
    /// <summary>生产者-消费者之间的有界缓冲队列</summary>
    private BlockingCollection<T>? _workQueue;

    /// <summary>处理后的结果，供 UI 线程安全消费</summary>
    public ConcurrentQueue<T> ResultQueue { get; } = new();

    private Task? _producerTask;
    private Task? _consumerTask;
    private CancellationTokenSource? _cts;

    public bool IsRunning { get; private set; }
    public int QueueCount => _workQueue?.Count ?? 0;

    public event Action<string>? OnLog;
    public event Action<Exception>? OnError;

    /// <summary>
    /// 启动管道
    /// </summary>
    /// <param name="producerFunc">生产者逻辑 —— 从设备读取数据</param>
    /// <param name="consumerAction">消费者逻辑 —— 数据处理(日志/报警/入库)</param>
    /// <param name="produceIntervalMs">生产间隔(ms)</param>
    /// <param name="boundedCapacity">缓冲队列容量(背压控制)</param>
    public void Start(
        Func<CancellationToken, Task<T>> producerFunc,
        Action<T> consumerAction,
        int produceIntervalMs = 1000,
        int boundedCapacity = 100)
    {
        if (IsRunning) return;

        _workQueue = new BlockingCollection<T>(boundedCapacity);
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        IsRunning = true;

        // ---- 生产者线程 ----
        _producerTask = Task.Run(async () =>
        {
            OnLog?.Invoke($"[Producer-{Environment.CurrentManagedThreadId}] 启动");
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var data = await producerFunc(token);
                    _workQueue.Add(data, token); // 队列满时自动阻塞 → 背压
                    await Task.Delay(produceIntervalMs, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { OnError?.Invoke(ex); }
            finally { _workQueue.CompleteAdding(); }
            OnLog?.Invoke($"[Producer-{Environment.CurrentManagedThreadId}] 退出");
        }, token);

        // ---- 消费者线程 ----
        _consumerTask = Task.Run(() =>
        {
            OnLog?.Invoke($"[Consumer-{Environment.CurrentManagedThreadId}] 启动");
            try
            {
                // GetConsumingEnumerable 会阻塞等待，直到 CompleteAdding 被调用
                foreach (var item in _workQueue.GetConsumingEnumerable(token))
                {
                    consumerAction(item);
                    ResultQueue.Enqueue(item);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { OnError?.Invoke(ex); }
            OnLog?.Invoke($"[Consumer-{Environment.CurrentManagedThreadId}] 退出");
        }, token);
    }

    /// <summary>优雅关闭管道</summary>
    public async Task StopAsync()
    {
        if (!IsRunning) return;

        _cts?.Cancel();
        _workQueue?.CompleteAdding();

        var tasks = new List<Task>();
        if (_producerTask != null) tasks.Add(_producerTask);
        if (_consumerTask != null) tasks.Add(_consumerTask);

        await Task.WhenAll(tasks); // 等待两个线程安全退出

        _cts?.Dispose();
        _cts = null;
        _workQueue?.Dispose();
        _workQueue = null;
        IsRunning = false;
        OnLog?.Invoke("管道已停止");
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _workQueue?.Dispose();
    }
}
