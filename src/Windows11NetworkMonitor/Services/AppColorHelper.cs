using Windows.UI;

namespace Windows11NetworkMonitor.Services;

public static class AppColorHelper
{
    public static Color ParseColor(string? hexOrName, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hexOrName)) return fallback;
        var s = hexOrName.Trim();

        if (s.StartsWith('#')) s = s[1..];

        try
        {
            if (s.Length == 6 && uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            {
                return Color.FromArgb(255, (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
            }
            if (s.Length == 8 && uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var argb))
            {
                return Color.FromArgb((byte)((argb >> 24) & 0xFF), (byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));
            }
            if (s.Length == 3 && uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var srgb))
            {
                byte r = (byte)(((srgb >> 8) & 0xF) * 17);
                byte g = (byte)(((srgb >> 4) & 0xF) * 17);
                byte b = (byte)((srgb & 0xF) * 17);
                return Color.FromArgb(255, r, g, b);
            }

            return s.ToLowerInvariant() switch
            {
                "white" => Microsoft.UI.Colors.White,
                "black" => Microsoft.UI.Colors.Black,
                "cyan" => Color.FromArgb(255, 0, 229, 255),
                "green" => Color.FromArgb(255, 0, 255, 102),
                "amber" => Color.FromArgb(255, 255, 179, 0),
                "orange" => Color.FromArgb(255, 255, 152, 0),
                "yellow" => Microsoft.UI.Colors.Yellow,
                "red" => Microsoft.UI.Colors.Red,
                "blue" => Color.FromArgb(255, 56, 189, 248),
                "purple" => Color.FromArgb(255, 168, 85, 247),
                "violet" => Color.FromArgb(255, 192, 132, 252),
                "slate" => Color.FromArgb(255, 30, 41, 59),
                "gray" or "grey" => Microsoft.UI.Colors.Gray,
                "transparent" => Microsoft.UI.Colors.Transparent,
                _ => fallback
            };
        }
        catch
        {
            return fallback;
        }
    }

    public static string ToHex(Color c, bool includeAlpha = false)
    {
        return includeAlpha ? $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }
}
