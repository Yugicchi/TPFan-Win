using System;
using System.Globalization;
using System.Windows.Data;

namespace TPFan.GUI;

/// <summary>
/// Converts bool to its inverse. Used for Auto RadioButton (IsManual=false → Auto checked).
/// </summary>
public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return !(value is bool b && b);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return !(value is bool b && b);
    }
}

/// <summary>
/// Converts speed percentage (0-100) + track width to horizontal Canvas.Left position for curve markers.
/// values[0] = speedPercent (int/double)
/// values[1] = trackWidth (double)
/// </summary>
public class MarkerPositionMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return 0d;

        var speed = values[0] is double sd ? sd : (values[0] is int si ? si : 0.0);
        var width = values[1] is double wd ? wd : (values[1] is int wi ? wi : 0.0);

        if (width <= 0) return 0d;

        return width * (speed / 100.0);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}