using System.Windows;
using System.Windows.Media;

namespace D7SystemIntelligence;

internal static class D7KtBrand
{
    public static ImageSource CreateIcon()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(Color.FromRgb(8, 8, 10)),
            new Pen(new SolidColorBrush(Color.FromRgb(95, 15, 18)), 5),
            new RectangleGeometry(new Rect(3, 3, 58, 58), 12, 12)));

        var d = new FormattedText(
            "D",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Black"), FontStyles.Normal, FontWeights.Black, FontStretches.Normal),
            37,
            new SolidColorBrush(Color.FromRgb(225, 225, 225)),
            1.0);
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(225, 225, 225)), null, d.BuildGeometry(new Point(9, 12))));

        var seven = new FormattedText(
            "7",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Black"), FontStyles.Normal, FontWeights.Black, FontStretches.Normal),
            40,
            new SolidColorBrush(Color.FromRgb(225, 24, 32)),
            1.0);
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(225, 24, 32)), null, seven.BuildGeometry(new Point(28, 10))));

        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    public static Brush HeroBrush()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(
            new LinearGradientBrush(
                Color.FromRgb(5, 5, 7),
                Color.FromRgb(18, 5, 7),
                0),
            null,
            new RectangleGeometry(new Rect(0, 0, 1200, 360), 26, 26)));

        var glow = new RadialGradientBrush
        {
            Center = new Point(.72, .45),
            GradientOrigin = new Point(.72, .45),
            RadiusX = .45,
            RadiusY = .85
        };
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(125, 205, 10, 20), 0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 205, 10, 20), 1));
        group.Children.Add(new GeometryDrawing(glow, null, new EllipseGeometry(new Point(850, 160), 400, 250)));

        var lines = new Pen(new SolidColorBrush(Color.FromArgb(75, 220, 20, 30)), 2);
        for (var i = 0; i < 9; i++)
        {
            var y = 60 + i * 31;
            group.Children.Add(new GeometryDrawing(null, lines, new LineGeometry(new Point(550, y), new Point(1180, y - 38))));
        }

        return new DrawingBrush(group) { Stretch = Stretch.Fill };
    }
}
