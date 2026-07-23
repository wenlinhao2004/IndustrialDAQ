using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using IndustrialDAQ.Services;
using IndustrialDAQ.ViewModels;

namespace IndustrialDAQ;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 防止登录窗关闭时自动退出
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // 1. 登录验证
        var login = new LoginWindow();
        if (login.ShowDialog() != true)
        {
            Shutdown();
            return;
        }

        // 2. 持久化凭证，供 MainViewModel 读取
        var settings = SettingsService.Load();
        settings.OpcUaUsername = login.Username;
        settings.OpcUaPassword = login.Password;
        settings.Save();

        try
        {
            // 3. 构建 DI 并启动主界面
            var services = new ServiceCollection();

            // 注册驱动 —— 单例，整个应用生命周期共享
            services.AddSingleton<ModbusService>();
            services.AddSingleton<OpcUaDriver>();
            services.AddSingleton<S7Driver>();

            // 注册 ViewModel 和 Window
            services.AddTransient<MainViewModel>();
            services.AddTransient<MainWindow>();

            Services = services.BuildServiceProvider();

            var window = Services.GetRequiredService<MainWindow>();
            window.Show();

            // 主界面已启动，恢复正常的关闭行为
            ShutdownMode = ShutdownMode.OnLastWindowClose;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动失败: {ex.Message}\n\n{ex.InnerException?.Message}",
                "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }
}

