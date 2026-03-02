using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Speakly.ViewModels;

namespace Speakly
{
    public sealed class ModelFavoriteVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2) return Visibility.Collapsed;
            if (values[0] is not string modelId) return Visibility.Collapsed;
            if (values[1] is not MainViewModel viewModel) return Visibility.Collapsed;

            var scope = parameter as string;
            bool isFavorite = string.Equals(scope, "stt", StringComparison.OrdinalIgnoreCase)
                ? viewModel.IsSttModelFavorite(modelId)
                : viewModel.IsRefinementModelFavorite(modelId);

            return isFavorite ? Visibility.Visible : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
