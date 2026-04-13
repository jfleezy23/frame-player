using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace FramePlayer.Controls
{
    public sealed class LoopRangeOverlay : FrameworkElement
    {
        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(
                nameof(Maximum),
                typeof(double),
                typeof(LoopRangeOverlay),
                new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty InPositionProperty =
            DependencyProperty.Register(
                nameof(InPosition),
                typeof(double),
                typeof(LoopRangeOverlay),
                new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty OutPositionProperty =
            DependencyProperty.Register(
                nameof(OutPosition),
                typeof(double),
                typeof(LoopRangeOverlay),
                new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty IsInPendingProperty =
            DependencyProperty.Register(
                nameof(IsInPending),
                typeof(bool),
                typeof(LoopRangeOverlay),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty IsOutPendingProperty =
            DependencyProperty.Register(
                nameof(IsOutPending),
                typeof(bool),
                typeof(LoopRangeOverlay),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty IsInvalidProperty =
            DependencyProperty.Register(
                nameof(IsInvalid),
                typeof(bool),
                typeof(LoopRangeOverlay),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Maximum
        {
            get { return (double)GetValue(MaximumProperty); }
            set { SetValue(MaximumProperty, value); }
        }

        public double InPosition
        {
            get { return (double)GetValue(InPositionProperty); }
            set { SetValue(InPositionProperty, value); }
        }

        public double OutPosition
        {
            get { return (double)GetValue(OutPositionProperty); }
            set { SetValue(OutPositionProperty, value); }
        }

        public bool IsInPending
        {
            get { return (bool)GetValue(IsInPendingProperty); }
            set { SetValue(IsInPendingProperty, value); }
        }

        public bool IsOutPending
        {
            get { return (bool)GetValue(IsOutPendingProperty); }
            set { SetValue(IsOutPendingProperty, value); }
        }

        public bool IsInvalid
        {
            get { return (bool)GetValue(IsInvalidProperty); }
            set { SetValue(IsInvalidProperty, value); }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            var width = ActualWidth;
            var height = ActualHeight;
            if (drawingContext == null || width <= 1d || height <= 1d)
            {
                return;
            }

            var maximum = Maximum > 0d ? Maximum : 1d;
            var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var hasIn = !double.IsNaN(InPosition);
            var hasOut = !double.IsNaN(OutPosition);
            if (!hasIn && !hasOut)
            {
                return;
            }

            var effectiveStart = hasIn ? Clamp(InPosition, maximum) : 0d;
            var effectiveEnd = hasOut ? Clamp(OutPosition, maximum) : maximum;
            var effectiveStartX = ValueToX(effectiveStart, width, maximum);
            var effectiveEndX = ValueToX(effectiveEnd, width, maximum);

            var outerShadeBrush = new SolidColorBrush(Color.FromArgb(136, 86, 94, 108));
            outerShadeBrush.Freeze();
            var invalidShadeBrush = new SolidColorBrush(Color.FromArgb(112, 180, 83, 83));
            invalidShadeBrush.Freeze();
            var readyPen = new Pen(new SolidColorBrush(Color.FromArgb(220, 90, 169, 230)), 1.5);
            readyPen.Freeze();
            var pendingPen = new Pen(new SolidColorBrush(Color.FromArgb(220, 245, 158, 11)), 1.5)
            {
                DashStyle = DashStyles.Dash
            };
            pendingPen.Freeze();
            var invalidPen = new Pen(new SolidColorBrush(Color.FromArgb(220, 248, 113, 113)), 1.5);
            invalidPen.Freeze();

            if (IsInvalid)
            {
                drawingContext.DrawRectangle(invalidShadeBrush, null, new Rect(0d, 0d, width, height));
            }
            else
            {
                if (effectiveStartX > 0d)
                {
                    drawingContext.DrawRectangle(outerShadeBrush, null, new Rect(0d, 0d, effectiveStartX, height));
                }

                if (effectiveEndX < width)
                {
                    drawingContext.DrawRectangle(outerShadeBrush, null, new Rect(effectiveEndX, 0d, width - effectiveEndX, height));
                }
            }

            if (hasIn)
            {
                DrawMarker(drawingContext, effectiveStartX, width, height, "[", IsInvalid ? invalidPen : (IsInPending ? pendingPen : readyPen), pixelsPerDip);
            }

            if (hasOut)
            {
                DrawMarker(drawingContext, effectiveEndX, width, height, "]", IsInvalid ? invalidPen : (IsOutPending ? pendingPen : readyPen), pixelsPerDip);
            }
        }

        private static void DrawMarker(DrawingContext drawingContext, double x, double width, double height, string text, Pen pen, double pixelsPerDip)
        {
            var markerX = Math.Max(0d, Math.Min(x, Math.Max(0d, width)));
            drawingContext.DrawLine(pen, new Point(markerX, 0d), new Point(markerX, height));

            var brush = pen != null ? pen.Brush : Brushes.White;
            var typeface = new Typeface(new FontFamily("Segoe UI Semibold"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            var formattedText = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                11d,
                brush,
                pixelsPerDip);
            var textOrigin = new Point(
                Math.Max(0d, Math.Min(markerX - (formattedText.Width / 2d), Math.Max(0d, width - formattedText.Width))),
                0d);
            drawingContext.DrawText(formattedText, textOrigin);
        }

        private static double Clamp(double value, double maximum)
        {
            if (value < 0d)
            {
                return 0d;
            }

            if (value > maximum)
            {
                return maximum;
            }

            return value;
        }

        private static double ValueToX(double value, double width, double maximum)
        {
            if (maximum <= 0d || width <= 0d)
            {
                return 0d;
            }

            return Clamp(value, maximum) / maximum * width;
        }
    }
}
