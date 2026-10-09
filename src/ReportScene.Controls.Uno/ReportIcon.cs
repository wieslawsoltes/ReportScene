using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace ReportScene.Controls.Uno;

/// <summary>Original vector icons; no proprietary FastReport artwork or font glyph dependency.</summary>
public sealed class ReportIcon : SKCanvasElement
{
    public string Icon { get; set; } = "report";
    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        canvas.Save(); canvas.Scale((float)(area.Width / 24), (float)(area.Height / 24));
        using var line = new SKPaint { Color = new(62, 88, 112), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, IsAntialias = true };
        using var fill = new SKPaint { Color = new(126, 176, 219), IsAntialias = true };
        void L(float x, float y, float x2, float y2) => canvas.DrawLine(x, y, x2, y2, line);
        void R(float x, float y, float w, float h, bool solid = false) => canvas.DrawRect(x, y, w, h, solid ? fill : line);
        void Path(params float[] xy) { using var p = new SKPath(); p.MoveTo(xy[0], xy[1]); for (int n = 2; n < xy.Length; n += 2) p.LineTo(xy[n], xy[n + 1]); canvas.DrawPath(p, line); }
        switch (Icon.ToLowerInvariant())
        {
            case "new": case "report": R(5, 2, 14, 20); L(8, 7, 16, 7); L(8, 11, 16, 11); L(8, 15, 14, 15); break;
            case "open": fill.Color = new(239, 194, 103); R(2, 6, 19, 15, true); Path(2, 10, 2, 5, 9, 5, 11, 8, 21, 8); Path(2, 21, 5, 11, 23, 11, 20, 21, 2, 21); break;
            case "save": R(3, 3, 18, 18, true); R(3, 3, 18, 18); fill.Color = SKColors.White; R(7, 3, 10, 6, true); R(7, 13, 10, 8, true); R(7, 3, 10, 6); R(7, 13, 10, 8); break;
            case "undo": Path(9, 4, 3, 10, 9, 15); Path(4, 10, 14, 10, 19, 13, 20, 17, 18, 20); break;
            case "redo": Path(15, 4, 21, 10, 15, 15); Path(20, 10, 10, 10, 5, 13, 4, 17, 6, 20); break;
            case "copy": R(8, 3, 13, 15); fill.Color = SKColors.White; R(3, 7, 13, 15, true); R(3, 7, 13, 15); L(6, 12, 13, 12); L(6, 16, 13, 16); break;
            case "paste": fill.Color = new(225, 188, 120); R(5, 4, 15, 18, true); R(5, 4, 15, 18); fill.Color = SKColors.White; R(8, 2, 9, 4, true); R(8, 2, 9, 4); L(8, 11, 17, 11); L(8, 15, 17, 15); break;
            case "cut": canvas.DrawCircle(6, 18, 3, line); canvas.DrawCircle(18, 18, 3, line); L(8, 16, 19, 3); L(16, 16, 5, 3); break;
            case "delete": line.Color = new(184, 65, 64); L(5, 5, 19, 19); L(19, 5, 5, 19); break;
            case "text": L(4, 4, 20, 4); L(4, 4, 4, 7); L(20, 4, 20, 7); L(12, 4, 12, 21); L(8, 21, 16, 21); break;
            case "line": L(3, 20, 21, 4); break;
            case "shape": case "rectangle": R(3, 5, 18, 14); break;
            case "ellipse": canvas.DrawOval(12, 12, 9, 7, line); break;
            case "image": R(2, 3, 20, 18); fill.Color = new(238, 191, 79); canvas.DrawCircle(16, 8, 2.5f, fill); Path(3, 19, 9, 10, 14, 16, 18, 12, 22, 18); break;
            case "checkbox": R(3, 3, 18, 18); line.Color = new(63, 132, 77); line.StrokeWidth = 2.5f; Path(6, 12, 10, 17, 19, 6); break;
            case "barcode": line.Color = SKColors.Black; for (int x = 3; x < 22; x += 3) { line.StrokeWidth = x % 2 == 0 ? 2 : 1; L(x, 4, x, 20); } break;
            case "chart": fill.Color = new(106, 151, 196); R(4, 13, 4, 7, true); fill.Color = new(226, 168, 72); R(10, 7, 4, 13, true); fill.Color = new(97, 168, 135); R(16, 3, 4, 17, true); L(2, 21, 22, 21); break;
            case "table": R(2, 3, 20, 18); fill.Color = new(170, 196, 221); R(3, 4, 18, 4, true); L(2, 9, 22, 9); L(2, 15, 22, 15); L(9, 3, 9, 21); L(16, 3, 16, 21); break;
            case "preview": R(3, 2, 13, 18); canvas.DrawCircle(15, 14, 5, line); L(19, 18, 23, 22); break;
            case "print": R(6, 2, 12, 6); R(2, 8, 20, 11); fill.Color = SKColors.White; R(6, 14, 12, 8, true); R(6, 14, 12, 8); L(6, 11, 8, 11); break;
            case "export": R(3, 3, 10, 18); L(9, 12, 22, 12); Path(17, 7, 22, 12, 17, 17); break;
            case "data": canvas.DrawOval(12, 5, 8, 3, line); L(4, 5, 4, 19); L(20, 5, 20, 19); canvas.DrawArc(SKRect.Create(4, 14, 16, 8), 0, 180, false, line); canvas.DrawArc(SKRect.Create(4, 7, 16, 8), 0, 180, false, line); break;
            case "pointer": Path(4, 2, 4, 20, 9, 16, 13, 23, 16, 21, 12, 14, 20, 14, 4, 2); break;
            case "zoom": canvas.DrawCircle(10, 10, 7, line); L(15, 15, 22, 22); L(6, 10, 14, 10); L(10, 6, 10, 14); break;
            case "grid": for (int i = 4; i <= 20; i += 8) { L(i, 2, i, 22); L(2, i, 22, i); } break;
            case "left": L(3, 2, 3, 22); R(6, 4, 14, 5, true); R(6, 13, 10, 5, true); break;
            case "right": L(21, 2, 21, 22); R(4, 4, 14, 5, true); R(8, 13, 10, 5, true); break;
            case "center": L(12, 2, 12, 22); R(3, 4, 18, 5, true); R(7, 13, 10, 5, true); break;
            case "front": R(3, 3, 13, 13); R(9, 9, 13, 13, true); R(9, 9, 13, 13); break;
            case "back": R(9, 9, 13, 13, true); R(9, 9, 13, 13); fill.Color = SKColors.White; R(3, 3, 13, 13, true); R(3, 3, 13, 13); break;
            case "band": R(2, 3, 20, 18); fill.Color = new(240, 194, 139); R(3, 10, 18, 5, true); L(2, 10, 22, 10); L(2, 15, 22, 15); break;
            case "next": Path(8, 4, 16, 12, 8, 20); break;
            case "previous": Path(16, 4, 8, 12, 16, 20); break;
            case "settings": canvas.DrawCircle(12, 12, 6, line); canvas.DrawCircle(12, 12, 2, line); for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; L((float)(12 + 7 * Math.Cos(a)), (float)(12 + 7 * Math.Sin(a)), (float)(12 + 10 * Math.Cos(a)), (float)(12 + 10 * Math.Sin(a))); } break;
            default: R(4, 4, 16, 16); L(8, 9, 16, 9); L(8, 14, 16, 14); break;
        }
        canvas.Restore();
    }
}
