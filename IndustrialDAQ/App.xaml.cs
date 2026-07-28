using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using IndustrialDAQ.ViewModels;

namespace IndustrialDAQ;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var services = new ServiceCollection();

            services.AddSingleton<MainViewModel>();
            services.AddTransient<MainWindow>();

            Services = services.BuildServiceProvider();

            var window = Services.GetRequiredService<MainWindow>();
            window.Show();

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
