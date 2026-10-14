using System.Globalization;

namespace ReportScene.Designer;

/// <summary>A byte-exact sRGB/alpha value for report color editors; report files continue to store color strings.</summary>
public readonly record struct DesignerColorValue(byte A, byte R, byte G, byte B)
{
    public const int MaximumInputCharacters = 64;
    public static DesignerColorValue Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return TryParse(text, out var color) ? color : throw new FormatException("Use #RRGGBB or #AARRGGBB for a color.");
    }
    public static bool TryParse(string? text, out DesignerColorValue color)
    {
        color = default;
        if (text is null || text.Length > MaximumInputCharacters) return false;
        var value = text.AsSpan().Trim();
        if (!value.IsEmpty && value[0] == '#') value = value[1..];
        if (value.Length is not (6 or 8)) return false;
        uint bits = 0;
        foreach (char digit in value)
        {
            int nibble = digit is >= '0' and <= '9' ? digit - '0' : digit is >= 'A' and <= 'F' ? digit - 'A' + 10 : digit is >= 'a' and <= 'f' ? digit - 'a' + 10 : -1;
            if (nibble < 0) return false;
            bits = (bits << 4) | (uint)nibble;
        }
        color = new(value.Length == 6 ? byte.MaxValue : (byte)(bits >> 24), (byte)(bits >> 16), (byte)(bits >> 8), (byte)bits);
        return true;
    }
    public string ToHexString(bool includeOpaqueAlpha = false) => A == 255 && !includeOpaqueAlpha
        ? string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}")
        : string.Create(CultureInfo.InvariantCulture, $"#{A:X2}{R:X2}{G:X2}{B:X2}");
    public override string ToString() => ToHexString();
    public static bool TryReadChannel(string? text, out byte value)
    {
        value = 0;
        if (text is null || text.Length > 16) return false;
        var input = text.AsSpan().Trim(); if (input.Length is < 1 or > 3) return false;
        int number = 0;
        foreach (char digit in input) { if (digit is < '0' or > '9') return false; number = number * 10 + digit - '0'; }
        if (number > 255) return false; value = (byte)number; return true;
    }
    public static DesignerColorValue FromChannels(string? alpha, string? red, string? green, string? blue)
    {
        if (!TryReadChannel(alpha, out byte a) || !TryReadChannel(red, out byte r)
            || !TryReadChannel(green, out byte g) || !TryReadChannel(blue, out byte b))
            throw new FormatException("Red, green, blue and alpha must be whole numbers from 0 to 255.");
        return new(a, r, g, b);
    }
}
