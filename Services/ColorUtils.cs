using System.Windows;
using System.Windows.Media;

namespace HorizonRadioOverlay.Services;

public static class ColorUtils
{
    public static (double H, double S, double V) ColorToHsv(Color color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double h = 0.0;
        if (delta > 0.00001)
        {
            if (Math.Abs(max - r) < 0.00001)
            {
                h = 60.0 * (((g - b) / delta) % 6.0);
            }
            else if (Math.Abs(max - g) < 0.00001)
            {
                h = 60.0 * (((b - r) / delta) + 2.0);
            }
            else
            {
                h = 60.0 * (((r - g) / delta) + 4.0);
            }

            if (h < 0)
            {
                h += 360.0;
            }
        }

        double s = max < 0.00001 ? 0.0 : delta / max;
        double v = max;

        return (h, Math.Clamp(s, 0.0, 1.0), Math.Clamp(v, 0.0, 1.0));
    }

    public static Color HsvToColor(double h, double s, double v)
    {
        h = ((h % 360.0) + 360.0) % 360.0;
        s = Math.Clamp(s, 0.0, 1.0);
        v = Math.Clamp(v, 0.0, 1.0);

        double c = v * s;
        double x = c * (1.0 - Math.Abs((h / 60.0 % 2.0) - 1.0));
        double m = v - c;

        double r = 0, g = 0, b = 0;

        if (h < 60)
        {
            r = c; g = x; b = 0;
        }
        else if (h < 120)
        {
            r = x; g = c; b = 0;
        }
        else if (h < 180)
        {
            r = 0; g = c; b = x;
        }
        else if (h < 240)
        {
            r = 0; g = x; b = c;
        }
        else if (h < 300)
        {
            r = x; g = 0; b = c;
        }
        else
        {
            r = c; g = 0; b = x;
        }

        byte red = (byte)Math.Round((r + m) * 255.0);
        byte green = (byte)Math.Round((g + m) * 255.0);
        byte blue = (byte)Math.Round((b + m) * 255.0);

        return Color.FromRgb(red, green, blue);
    }

    public static string ColorToHex(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    public static bool TryParseHex(string? hex, out Color color)
    {
        color = Colors.White;
        if (string.IsNullOrWhiteSpace(hex)) return false;

        string trimmed = hex.Trim().TrimStart('#');
        if (trimmed.Length != 6 && trimmed.Length != 8) return false;

        try
        {
            if (trimmed.Length == 6)
            {
                byte r = Convert.ToByte(trimmed.Substring(0, 2), 16);
                byte g = Convert.ToByte(trimmed.Substring(2, 2), 16);
                byte b = Convert.ToByte(trimmed.Substring(4, 2), 16);
                color = Color.FromRgb(r, g, b);
                return true;
            }
            else
            {
                byte a = Convert.ToByte(trimmed.Substring(0, 2), 16);
                byte r = Convert.ToByte(trimmed.Substring(2, 2), 16);
                byte g = Convert.ToByte(trimmed.Substring(4, 2), 16);
                byte b = Convert.ToByte(trimmed.Substring(6, 2), 16);
                color = Color.FromArgb(a, r, g, b);
                return true;
            }
        }
        catch
        {
            return false;
        }
    }
}
