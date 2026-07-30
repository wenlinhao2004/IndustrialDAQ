# IndustrialDAQ

基于 WPF (.NET 10) 的工业数据采集与监控（SCADA/DAQ）桌面应用，支持 **Modbus TCP**、**Modbus RTU**、**OPC UA** 和 **Siemens S7** 四种工业协议，具备多设备并发采集、实时监控、限值报警、历史查询、数据写入与 CSV 导出能力。

## 功能特性

- **四协议支持** — Modbus TCP（以太网）、Modbus RTU（RS-232/RS-485 串口）、OPC UA（工业 4.0 标准）、Siemens S7（S7-200/300/400/1200/1500），通过统一 `IDeviceDriver` 接口切换，策略模式新增协议不改上层代码
- **多设备并发** — 基于 `devices.json` 配置驱动，每个设备独立管道、独立驱动、独立生命周期，支持运行时切换协议
- **实时数据采集** — 生产者-消费者多线程管道，有界队列（容量 100）背压控制，默认 1 秒轮询间隔
- **数据可视化** — 左侧实时数据表格 + 中央 OxyPlot 实时趋势图（滚动 300 数据点，按设备+点位自动分色）
- **数据写入** — 支持向设备写入工程值，内部自动按 Scale/Offset 反算原始值（Modbus 寄存器 / OPC UA 节点 / S7 DB 块）
- **限值报警** — 高限 / 低限检测，HashSet 去重防止重复报警，报警标签红色高亮，带时间戳记录
- **断线重连** — 采集异常自动触发 5 次指数退避重连（2s → 4s → 6s → 8s → 10s），恢复后继续采集
- **历史查询** — SQLite 持久化存储，支持按标签名筛选、按时间范围查询
- **数据导出** — 历史数据导出为 CSV 文件（GB2312 编码，Excel 可直接打开不乱码）
- **串口调试** — 内置串口调试面板，支持打开/关闭串口、发送测试数据、后台异步接收
- **配置持久化** — 连接参数（IP、串口号、波特率等）自动保存到 `appsettings.json`，下次启动恢复

## 技术栈

| 层 | 技术 |
|---|---|
| 运行时 | .NET 10.0 (net10.0-windows) |
| UI | WPF |
| MVVM | CommunityToolkit.Mvvm 8.4.2 |
| DI | Microsoft.Extensions.DependencyInjection |
| Modbus | NModbus4 3.0.0-alpha2 |
| OPC UA | OPCFoundation.NetStandard.Opc.Ua 1.5.374 |
| S7 | S7NetPlus 0.20.0 |
| 数据库 | Microsoft.Data.Sqlite |
| 图表 | OxyPlot.Wpf 2.2.0 |
| 串口 | System.IO.Ports |

## 项目结构

```
IndustrialDAQ/
├── IndustrialDAQ.slnx
└── IndustrialDAQ/
    ├── IndustrialDAQ.csproj
    ├── App.xaml / App.xaml.cs              # 应用入口 + DI 容器
    ├── MainWindow.xaml / .cs               # 主窗口 UI
    ├── Converters.cs                       # XAML 值转换器（连接状态颜色 / 报警背景色）
    ├── Models/
    │   ├── TagConfig.cs                    # 数据点位模型（Modbus / OPC UA / S7 三合一）
    │   ├── DeviceConfig.cs                 # 设备配置模型
    │   ├── DeviceConfigLoader.cs           # 从 devices.json 加载设备列表
    │   └── TagConfigLoader.cs              # 从 tagconfigs.json 加载独立点位配置
    ├── ViewModels/
    │   ├── MainViewModel.cs                # 主视图模型（UI 绑定 + 命令 + 报警/日志/图表聚合）
    │   ├── DeviceViewModel.cs              # 单设备视图模型（驱动 + 管道 + 点位 完整生命周期）
    │   └── TagViewModel.cs                 # 点位视图模型（运行值 + 报警状态）
    └── Services/
        ├── IDeviceDriver.cs                # 设备驱动统一接口（策略模式）
        ├── ModbusService.cs                # Modbus TCP/RTU 驱动（NModbus4）
        ├── OpcUaDriver.cs                  # OPC UA 客户端（OPC Foundation SDK）
        ├── S7Driver.cs                     # Siemens S7 驱动（S7NetPlus，大端序处理）
        ├── SerialPortAdapter.cs            # SerialPort → NModbus IStreamResource 适配器
        ├── SerialPortService.cs            # 独立串口调试服务（后台异步接收）
        ├── DataPipeline.cs                 # 泛型生产者-消费者多线程管道
        ├── DataLogger.cs                   # SQLite 数据持久化（事务批量写入 + 索引查询）
        ├── AlarmService.cs                 # 报警检测引擎（越限判断 + HashSet 去重）
        ├── DataExporter.cs                 # CSV 导出（GB2312 编码）
        └── SettingsService.cs              # 用户配置 JSON 读写
```

## 快速开始

### 环境要求

- Windows 10 / 11
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2022（推荐）或 VS Code

### 构建运行

```bash
git clone https://github.com/wenlinhao/IndustrialDAQ.git
cd IndustrialDAQ
dotnet restore
dotnet build
dotnet run --project IndustrialDAQ/IndustrialDAQ.csproj
```

### 使用说明

1. 启动后，顶部工具栏下拉选择**设备**（设备 A / 设备 B）和**协议**（ModbusTCP / ModbusRTU / OpcUa / S7）
2. 填写对应连接参数（IP:端口 / 串口号+波特率 / OPC UA 端点 URL / S7 Rack+Slot）
3. 点击 **连接** 建立通信，按钮变红显示「断开」
4. 连接成功后自动启动采集管道：
   - **左侧** — 实时数据表格（报警行红色高亮，显示 ▲ 高 / ▼ 低）
   - **中间** — 实时趋势曲线（自动按 `[设备名]标签名` 分色，滚动 300 点）
   - **右上** — 报警记录面板（显示超限标签、数值、限值、时间）
   - **右下** — 历史数据查询面板 + CSV 导出按钮
   - **底部** — 多线程日志（深色终端风格）+ 串口调试工具
   - **状态栏** — 数据库记录数、采集计数
5. 可通过 **写入** 控件选择可写标签（如电机转速、阀门开度），输入数值后写入设备

## 架构

### 数据流

```
devices.json → DeviceConfigLoader → DeviceViewModel (每个设备独立)
                                        ├── IDeviceDriver (Modbus / OPC UA / S7)
                                        ├── DataPipeline<Dictionary<string, double>>
                                        │    ├── Producer: 定时采集设备数据
                                        │    │    └── 异常 → 断线重连 (5次指数退避)
                                        │    └── Consumer: 入队 → ResultQueue → UI
                                        └── [200ms UI 刷新循环]

多个设备并发采集:
  DeviceViewModel.OnDataReceived → MainViewModel.OnDeviceData
    ├── AlarmService.CheckLimits     → 报警记录 + UI 通知
    ├── DataLogger.InsertBatch       → SQLite 事务写入
    ├── TagViewModel.UpdateValue     → 实时数据表格刷新
    └── OxyPlot LineSeries           → 趋势图更新
```

### 生产者-消费者管道

```
[设备驱动读取] ──BlockingCollection (容量 100)──> [数据处理线程]
      ↑                                                   ↓
  定时轮询 (1s)                              报警检测 + SQLite 批量事务写入
                                                           ↓
                                               ConcurrentQueue → 200ms UI Dispatcher
                                                           ↓
                                         表格刷新 + 图表更新 + 日志输出
```

- 生产者（采集）和消费者（处理）各自在独立线程上运行
- 有界队列容量 100，生产者过快时 `Add()` 自动阻塞 → 背压控制，防止内存溢出
- UI 更新通过 WPF Dispatcher 回到主线程，线程安全

### 关键设计模式

| 模式 | 位置 | 说明 |
|---|---|---|
| 策略模式 | `IDeviceDriver` → ModbusService / OpcUaDriver / S7Driver | 协议可互换，新增协议不改上层 |
| 适配器模式 | `SerialPortAdapter` | 将 .NET SerialPort 适配为 NModbus 的 IStreamResource |
| 工厂方法 | `DeviceViewModel.CreateDriver()` | 根据协议字符串创建对应驱动实例 |
| 观察者模式 | `AlarmService.OnAlarm` 事件 | 解耦报警检测与 UI 响应 |
| 生产者-消费者 | `DataPipeline<T>` | 采集线程与处理线程分离 |

### 协议对比

| | Modbus TCP | Modbus RTU | OPC UA | Siemens S7 |
|---|---|---|---|---|
| 物理层 | 以太网 | RS-232/485 | 以太网 | 以太网 / Profinet |
| 寻址模型 | 寄存器地址 (0-65535) | 寄存器地址 | NodeId (命名空间+字符串/数字) | DB 块号 + 字节偏移 |
| 字节序 | 大端 | 大端 | 平台相关 | 大端 (需翻转) |
| 默认端口 | 502 | — | 4840 | 102 |
| 库 | NModbus4 | NModbus4 | OPC Foundation SDK | S7NetPlus |
| 模拟支持 | ✅ 内置模拟 | ✅ 未连接时自动模拟 | ❌ | ❌ |

## 设备配置

设备通过 `devices.json` 配置，支持多设备、每设备不同协议：

```json
[
  {
    "DeviceId": "dev_a",
    "DeviceName": "设备 A",
    "Protocol": "ModbusTCP",
    "ConnectionParams": { "Mode": "TCP", "IpAddress": "127.0.0.1" },
    "Tags": [
      {
        "Name": "主电机温度", "Address": 0, "NodeId": "ns=2;s=Temperature1",
        "DbNumber": 1, "ByteOffset": 0, "S7DataType": "REAL",
        "Unit": "℃", "HighLimit": 80.0, "LowLimit": 0.0, "Scale": 0.1, "Offset": 0.0
      }
    ]
  }
]
```

> **注意**: TagConfig 同时包含 Modbus (`Address`)、OPC UA (`NodeId`) 和 S7 (`DbNumber`/`ByteOffset`/`S7DataType`) 字段，驱动按当前协议取对应字段，其余忽略。这是一种简化设计，生产环境可考虑按协议分文件。

## 内置采集标签

共 7 个工业采集点，分布在一台 ModbusTCP 设备和一台 OPC UA 设备中：

| 设备 | 标签 | Modbus 地址 | OPC UA 节点 | S7 DB/偏移 | 单位 | 高限 | 低限 |
|---|---|---|---|---|---|---|---|
| 设备 A | 主电机温度 | 0 | ns=2;s=Temperature1 | DB1.0 REAL | ℃ | 80 | 0 |
| 设备 A | 冷却水温度 | 1 | ns=2;s=Temperature2 | DB1.4 REAL | ℃ | 35 | 5 |
| 设备 A | 管道压力 | 2 | ns=2;s=Pressure1 | DB1.8 REAL | MPa | 1.6 | 0.1 |
| 设备 A | 电机转速 | 3 | ns=2;s=Speed1 | DB1.12 INT | rpm | 1500 | 10 |
| 设备 B | 流量 | 4 | ns=2;s=Flow1 | DB1.14 REAL | m³/h | 50 | 2 |
| 设备 B | 液位 | 5 | ns=2;s=Level1 | DB1.18 REAL | m | 4.5 | 0.5 |
| 设备 B | 阀门开度 | 6 | ns=2;s=Valve1 | DB1.22 REAL | % | 100 | 0 |

> 可写标签: 电机转速、阀门开度（通过顶部写入控件操作）

## 面试要点

阅读本项目的推荐顺序和每个文件要搞懂的核心问题：

1. **`IDeviceDriver.cs`** — 策略模式的含义？为什么接口要同时包含 Read 和 Write？
2. **`ModbusService.cs`** — TCP 和 RTU 的 `_transport` 字段为什么是 `object` 类型？批量读取时怎么知道读几个寄存器？
3. **`OpcUaDriver.cs`** — OPC UA 的地址空间模型和 Modbus 的平址模型有什么区别？为什么 OPC UA 不需要"寄存器地址"？
4. **`S7Driver.cs`** — 西门子大端序 vs PC 小端序，字节翻转（`Array.Reverse`）发生在读还是写？
5. **`DataPipeline.cs`** — BlockingCollection 怎么实现背压？`GetConsumingEnumerable` 和 `CompleteAdding` 配合的原理？
6. **`AlarmService.cs`** — `HashSet<string>` 怎样做到"同一个标签只报一次警"？报警恢复后怎么重新允许报警？
7. **`DeviceViewModel.cs`** — 工厂方法 `CreateDriver` 为什么放在 ViewModel 而不是 App.xaml.cs 的 DI 容器里？
8. **`MainViewModel.cs`** — 多设备场景下，图表怎么做到"按设备名+标签名自动分色"？设备和图表的生命周期关系？
9. **`DataLogger.cs`** — SQLite 事务批量写入的好处？为什么不每行单独 INSERT？
10. **`SerialPortAdapter.cs`** — 适配器模式在这解决了什么问题？（NModbus 不认识 .NET 的 SerialPort）
