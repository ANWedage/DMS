using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace DMS.Helpers
{
    public class InitialsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var input = value as string;
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var words = input.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
                return string.Empty;

            var initials = words
                .Take(2)
                .Select(word => char.ToUpperInvariant(word[0]))
                .ToArray();

            return new string(initials);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
