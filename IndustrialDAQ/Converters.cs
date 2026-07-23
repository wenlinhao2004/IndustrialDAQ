using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace IndustrialDAQ.Converters;

public class BoolToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (bool)value ? new SolidColorBrush(Color.FromRgb(0x28, 0xA7, 0x45))  // 绿色-已连接
                        : new SolidColorBrush(Color.FromRgb(0xCC, 0x33, 0x33)); // 红色-未连接

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BoolToAlarmBgConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (bool)value ? new SolidColorBrush(Color.FromRgb(0xFF, 0xEE, 0xEE))  // 报警-浅红
                        : new SolidColorBrush(Colors.Transparent);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
