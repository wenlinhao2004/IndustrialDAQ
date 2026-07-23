# IndustrialDAQ

基于 WPF 的工业数据采集与监控（SCADA/DAQ）桌面应用，支持 **Modbus TCP**、**Modbus RTU** 和 **OPC UA** 三种工业协议，具备实时监控、报警检测、历史记录与数据导出功能。

## 功能特性

- **多协议支持** — Modbus TCP（以太网）、Modbus RTU（RS-232/RS-485 串口）、OPC UA，通过统一驱动接口切换
- **实时数据采集** — 可配置轮询间隔（默认 1 秒），从 PLC 或传感器读取保持寄存器 / OPC UA 节点数据
- **数据可视化** — 左侧实时数据表格 + 中央 OxyPlot 实时趋势图（滚动 300 数据点）
- **限值报警** — 支持高限 / 低限报警，超限标签红色高亮，报警记录带时间戳
- **历史记录** — SQLite 持久化存储，支持按标签名和时间范围查询
- **数据导出** — 历史数据导出为 CSV 文件（GB2312 编码，Excel 可直接打开）
- **模拟模式** — 无需硬件即可运行，随机数据模拟，方便测试和调试
- **串口调试** — 内置串口调试面板，支持打开 / 关闭串口、发送测试数据、监控接收
- **生产者-消费者架构** — 采集与处理分离，有界队列（容量 100）提供背压控制
- **配置持久化** — 连接参数自动保存到 `appsettings.json`，下次启动恢复

## 技术栈

| 层 | 技术 |
|---|---|
| 运行时 | .NET 10.0 (net10.0-windows) |
| UI | WPF |
| MVVM | CommunityToolkit.Mvvm 8.4.2 |
| DI | Microsoft.Extensions.DependencyInjection |
| Modbus | NModbus4 3.0.0-alpha2 |
| OPC UA | OPCFoundation.NetStandard.Opc.Ua 1.5.374 |
| 数据库 | Microsoft.Data.Sqlite |
| 图表 | OxyPlot.Wpf 2.2.0 |
| 串口 | System.IO.Ports |

## 项目结构

```
IndustrialDAQ/
├── IndustrialDAQ.slnx
└── IndustrialDAQ/
    ├── IndustrialDAQ.csproj
    ├── App.xaml / App.xaml.cs          # 应用入口 + DI 容器
    ├── MainWindow.xaml / .cs           # 主窗口 UI
    ├── Converters.cs                   # XAML 值转换器
    ├── Models/
    │   └── TagConfig.cs                # 采集标签模型
    ├── ViewModels/
    │   └── MainViewModel.cs            # 主视图模型（核心业务逻辑）
    └── Services/
        ├── IDeviceDriver.cs            # 驱动统一接口
        ├── ModbusService.cs            # Modbus TCP/RTU 驱动
        ├── OpcUaDriver.cs              # OPC UA 客户端
        ├── SerialPortAdapter.cs        # SerialPort → NModbus 适配器
        ├── SerialPortService.cs        # 串口调试服务
        ├── DataPipeline.cs             # 生产者-消费者多线程管道
        ├── DataLogger.cs               # SQLite 数据持久化
        ├── DataExporter.cs             # CSV 导出
        ├── AlarmService.cs             # 报警检测引擎
        └── SettingsService.cs          # 配置文件读写
```

## 快速开始

### 环境要求

- Windows 10 / 11
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2022（推荐）

### 构建运行

```bash
git clone https://github.com/wenlinhao/IndustrialDAQ.git
cd IndustrialDAQ
dotnet restore
dotnet build
dotnet run --project IndustrialDAQ/IndustrialDAQ.csproj
```

### 使用说明

1. 启动后，从顶部下拉菜单选择连接模式（TCP / RTU / OPC UA）
2. 填写连接参数（IP:端口、串口号 / 波特率、或 OPC UA 端点 URL）
3. 点击 **连接** 建立通信，或点击 **模拟模式** 用随机数据体验全部功能
4. 左侧面板查看实时数据，中间面板查看趋势曲线
5. 右侧选项卡查看报警记录，或查询 / 导出历史数据

## 采集标签

内置 7 个工业采集点，均为保持寄存器（Modbus）或 OPC UA 节点：

| 标签 | Modbus 地址 | OPC UA 节点 ID | 单位 | 高限 | 低限 |
|---|---|---|---|---|---|
| 主电机温度 | 0 | ns=2;s=Temperature1 | °C | 80 | 0 |
| 冷却水温度 | 1 | ns=2;s=Temperature2 | °C | 35 | 5 |
| 管道压力 | 2 | ns=2;s=Pressure1 | MPa | 1.6 | 0.1 |
| 电机转速 | 3 | ns=2;s=Speed1 | RPM | 1500 | 10 |
| 流量 | 4 | ns=2;s=Flow1 | m³/h | 50 | 2 |
| 液位 | 5 | ns=2;s=Level1 | m | 4.5 | 0.5 |
| 阀门开度 | 6 | ns=2;s=Valve1 | % | 100 | 0 |

## 架构说明

采集管道采用经典生产者-消费者模式：

```
[Modbus / OPC 驱动] → BlockingCollection (容量 100) → [数据处理线程]
       ↑                                                     ↓
   定时轮询采集                                    报警检测 + SQLite 批量写入
                                                             ↓
                                                 ConcurrentQueue → UI Dispatcher
                                                             ↓
                                                 表格刷新 + 图表更新 + 日志
```

- 生产者（采集）和消费者（处理）各自在独立线程上运行
- 有界队列容量 100，生产者过快时自动阻塞，防止内存溢出
- UI 更新通过 WPF Dispatcher 回到主线程，线程安全
