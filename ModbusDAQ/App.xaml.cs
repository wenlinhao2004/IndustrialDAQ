using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ModbusDAQ.Services;
using ModbusDAQ.ViewModels;

namespace ModbusDAQ;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();

        // 注册驱动 —— 单例，整个应用生命周期共享
        services.AddSingleton<ModbusService>();
        services.AddSingleton<OpcUaDriver>();

        // 注册 ViewModel 和 Window
        services.AddTransient<MainViewModel>();
        services.AddTransient<MainWindow>();

        Services = services.BuildServiceProvider();

        var window = Services.GetRequiredService<MainWindow>();
        window.Show();
    }
}

