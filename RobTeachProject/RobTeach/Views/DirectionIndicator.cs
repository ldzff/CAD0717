using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Diagnostics; // For Trace

namespace RobTeach.Views
{
    public class DirectionIndicator : Shape
    {
        // DependencyProperties
        public static readonly DependencyProperty StartPointProperty =
            DependencyProperty.Register("StartPoint", typeof(Point), typeof(DirectionIndicator),
                new FrameworkPropertyMetadata(new Point(0, 0), FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty EndPointProperty =
            DependencyProperty.Register("EndPoint", typeof(Point), typeof(DirectionIndicator),
                new FrameworkPropertyMetadata(new Point(0, 0), FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ArrowheadSizeProperty =
            DependencyProperty.Register("ArrowheadSize", typeof(double), typeof(DirectionIndicator),
                new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ColorProperty =
            DependencyProperty.Register("Color", typeof(Brush), typeof(DirectionIndicator),
                new FrameworkPropertyMetadata(Brushes.Red, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure),
                new ValidateValueCallback(IsValidBrush));

        private static bool IsValidBrush(object value)
        {
            return value is Brush;
        }

        // CLR Properties
        public Point StartPoint
        {
            get { return (Point)GetValue(StartPointProperty); }
            set { SetValue(StartPointProperty, value); }
        }

        public Point EndPoint
        {
            get { return (Point)GetValue(EndPointProperty); }
            set { SetValue(EndPointProperty, value); }
        }

        public double ArrowheadSize
        {
            get { return (double)GetValue(ArrowheadSizeProperty); }
            set { SetValue(ArrowheadSizeProperty, value); }
        }

        public Brush Color
        {
            get { return (Brush)GetValue(ColorProperty); }
            set { SetValue(ColorProperty, value); }
        }

        // Constructor
        public DirectionIndicator()
        {
            // Default values are set by DependencyProperty metadata.
            // StrokeThickness is a property from the base Shape class.
            StrokeThickness = 2;
        }

        // Override DefiningGeometry
        protected override Geometry DefiningGeometry
        {
            get
            {
                Trace.WriteLine($"DirectionIndicator.DefiningGeometry: StartPoint=({StartPoint.X:F3}, {StartPoint.Y:F3}), EndPoint=({EndPoint.X:F3}, {EndPoint.Y:F3}), ArrowheadSize={ArrowheadSize:F3}");

                if (StartPoint == EndPoint || ArrowheadSize <= 0)
                {
                    Trace.WriteLine("  -> Returning Geometry.Empty (StartPoint == EndPoint or ArrowheadSize <= 0)");
                    Trace.Flush();
                    return Geometry.Empty;
                }

                this.Fill = Color;
                this.Stroke = Color;

                PathGeometry pathGeometry = new PathGeometry();

                PathFigure lineFigure = new PathFigure();
                lineFigure.StartPoint = StartPoint;
                lineFigure.Segments.Add(new LineSegment(EndPoint, true /* isStroked */));
                pathGeometry.Figures.Add(lineFigure);

                Vector dir = EndPoint - StartPoint;
                if (dir.Length == 0)
                {
                    Trace.WriteLine("  -> Returning Geometry.Empty (Direction vector length is 0 after initial check)");
                    Trace.Flush();
                    return Geometry.Empty;
                }
                dir.Normalize();

                double angleInDegrees = 30.0;
                double angleInRadians = Math.PI * angleInDegrees / 180.0;

                Vector wingDir1 = new Vector(
                    dir.X * Math.Cos(angleInRadians) - dir.Y * Math.Sin(angleInRadians),
                    dir.X * Math.Sin(angleInRadians) + dir.Y * Math.Cos(angleInRadians)
                );
                Point wingPoint1 = EndPoint - wingDir1 * ArrowheadSize;

                Vector wingDir2 = new Vector(
                    dir.X * Math.Cos(-angleInRadians) - dir.Y * Math.Sin(-angleInRadians),
                    dir.X * Math.Sin(-angleInRadians) + dir.Y * Math.Cos(-angleInRadians)
                );
                Point wingPoint2 = EndPoint - wingDir2 * ArrowheadSize;

                PathFigure arrowheadFigure = new PathFigure();
                arrowheadFigure.StartPoint = EndPoint;
                arrowheadFigure.Segments.Add(new LineSegment(wingPoint1, true /* isStroked */));
                arrowheadFigure.Segments.Add(new LineSegment(wingPoint2, true /* isStroked */));
                arrowheadFigure.IsClosed = true;
                arrowheadFigure.IsFilled = true;
                pathGeometry.Figures.Add(arrowheadFigure);

                Trace.WriteLine($"  -> Returning PathGeometry with {pathGeometry.Figures.Count} figures.");
                Trace.Flush();
                return pathGeometry;
            }
        }
    }
}
