using System.Globalization;
using ReportScene.Designer;

namespace ReportScene.Validation;

public static class DesignerColorTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("designer color: " + name, true); } catch (Exception e) { return new("designer color: " + name, false, e.ToString()); } }
        yield return Test("RGB strings default to opaque alpha and canonical uppercase", () =>
        { var c = DesignerColorValue.Parse("#123aBC"); Conformance.Equal(new DesignerColorValue(255, 18, 58, 188), c); Conformance.Equal("#123ABC", c.ToHexString()); Conformance.Equal("#FF123ABC", c.ToHexString(true)); });
        yield return Test("ARGB order and transparent hidden RGB channels are preserved", () =>
        {
            Conformance.Equal(new DesignerColorValue(128, 17, 34, 51), DesignerColorValue.Parse("#80112233"));
            var c = DesignerColorValue.Parse("00ffffff"); Conformance.Equal(new DesignerColorValue(0, 255, 255, 255), c); Conformance.Equal("#00FFFFFF", c.ToHexString());
        });
        yield return Test("optional prefix and surrounding whitespace are accepted", () =>
        { Conformance.Equal(new DesignerColorValue(255, 16, 136, 64), DesignerColorValue.Parse(" \t108840\r\n")); Conformance.Equal("#000000", DesignerColorValue.Parse("000000").ToString()); });
        yield return Test("malformed partial and non-ASCII hex input is rejected", () =>
        {
            foreach (string? text in new string?[] { null, "", "#", "#123", "#12345", "#1234567", "#123456789", "#GG0000", "##123456", "red", "１２３４５６", "+12345", "123 56" })
                Conformance.Equal(false, DesignerColorValue.TryParse(text, out _));
            Conformance.Throws<FormatException>(() => DesignerColorValue.Parse("#123"));
            Conformance.Throws<ArgumentNullException>(() => DesignerColorValue.Parse(null!));
        });
        yield return Test("input storage is bounded before trimming", () =>
        { Conformance.Equal(false, DesignerColorValue.TryParse(new string(' ', 10000) + "112233", out _)); Conformance.Equal(false, DesignerColorValue.TryReadChannel(new string('0', 10000), out _)); });
        yield return Test("byte channels accept only trimmed decimal integers", () =>
        {
            Conformance.Equal(new DesignerColorValue(0, 255, 17, 2), DesignerColorValue.FromChannels("000", "255", " 17 ", "002"));
            foreach (var text in new[] { "", "256", "-1", "+1", "1.0", "1,0", "1e2", "1000", "٢" })
                Conformance.Equal(false, DesignerColorValue.TryReadChannel(text, out _));
            Conformance.Throws<FormatException>(() => DesignerColorValue.FromChannels("255", "256", "0", "0"));
        });
        yield return Test("every alpha and selected channel extremes round trip exactly", () =>
        {
            for (int a = 0; a < 256; a++) foreach (byte r in new byte[] { 0, 1, 127, 128, 254, 255 })
            {
                var c = new DesignerColorValue((byte)a, r, (byte)(255 - r), (byte)(a ^ r));
                Conformance.Equal(c, DesignerColorValue.Parse(c.ToHexString())); Conformance.Equal(c, DesignerColorValue.Parse(c.ToHexString(true)));
            }
        });
        yield return Test("culture cannot change channel parsing or stored hexadecimal", () =>
        {
            var before = CultureInfo.CurrentCulture;
            try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL"); var c = DesignerColorValue.FromChannels("128", "16", "136", "64"); Conformance.Equal("#80108840", c.ToHexString()); }
            finally { CultureInfo.CurrentCulture = before; }
        });
    }
}
