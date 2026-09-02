using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DMS.Helpers
{
    public class StatusBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isActive)
                return isActive
                    ? new SolidColorBrush(Color.FromRgb(22, 163, 74))
                    : new SolidColorBrush(Color.FromRgb(148, 163, 184));

            return new SolidColorBrush(Color.FromRgb(148, 163, 184));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
