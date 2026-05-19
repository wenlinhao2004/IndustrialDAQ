using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ModbusDAQ.ViewModels;

namespace ModbusDAQ;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public MainWindow() : this(App.Services.GetRequiredService<MainViewModel>()) { }

    private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            await vm.ShutdownAsync();
        }
    }
}
