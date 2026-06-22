using System.Windows;
using ModbusDAQ.Services;

namespace ModbusDAQ;

public partial class LoginWindow : Window
{
    public string Username => UsernameBox.Text;
    public string Password => PasswordBox.Password;

    private readonly SettingsService _settings;

    public LoginWindow()
    {
        InitializeComponent();
        _settings = SettingsService.Load();
    }

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            MessageBox.Show("请输入用户名", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            UsernameBox.Focus();
            return;
        }

        if (Username != _settings.AdminUsername || Password != _settings.AdminPassword)
        {
            MessageBox.Show("用户名或密码错误", "登录失败", MessageBoxButton.OK, MessageBoxImage.Error);
            PasswordBox.Clear();
            PasswordBox.Focus();
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
