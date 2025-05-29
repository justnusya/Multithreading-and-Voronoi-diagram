using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Voronoi_diagram
{
    public partial class MainWindow : Window
    {
        private List<Point> sites = new List<Point>();
        private bool useMultiThreading = false;
        private DistanceMetric currentMetric = DistanceMetric.Euclidean;

        private enum DistanceMetric
        {
            Euclidean,
            Manhattan
        }

        public MainWindow()
        {
            InitializeComponent();
        }

        private void UpdatePerformanceInfo(TimeSpan cpuTime, TimeSpan realTime)
        {
            PerformanceInfo.Text = $"CPU Time: {cpuTime.TotalMilliseconds:F2} ms | " +
                                 $"Real Time: {realTime.TotalMilliseconds:F2} ms";
        }

        private double CalculateDistance(Point a, Point b)
        {
            switch (currentMetric)
            {
                case DistanceMetric.Euclidean:
                    double dx = a.X - b.X;
                    double dy = a.Y - b.Y;
                    return Math.Sqrt(dx * dx + dy * dy);
                case DistanceMetric.Manhattan:
                    return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void MetricComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MetricComboBox.SelectedIndex == 0)
                currentMetric = DistanceMetric.Euclidean;
            else
                currentMetric = DistanceMetric.Manhattan;

            UpdateVoronoiDiagram();
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Point p = e.GetPosition(canvas);
            sites.Add(p);
            UpdateVoronoiDiagram();
        }

        private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            Point click = e.GetPosition(canvas);
            if (sites.Count == 0) return;

            var nearest = sites.OrderBy(p => CalculateDistance(p, click)).First();
            sites.Remove(nearest);
            UpdateVoronoiDiagram();
        }

        private void DrawSites(WriteableBitmap wb)
        {
            if (wb == null) return;

            foreach (var site in sites)
            {
                DrawCircle(wb, (int)site.X, (int)site.Y, 3, Colors.Black);
            }
        }

        private void DrawCircle(WriteableBitmap wb, int x, int y, int radius, Color color)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy <= radius * radius)
                    {
                        int px = x + dx;
                        int py = y + dy;
                        if (px >= 0 && px < wb.PixelWidth && py >= 0 && py < wb.PixelHeight)
                        {
                            wb.SetPixel(px, py, color);
                        }
                    }
                }
            }
        }

        private void GenerateRandomPoints_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(DotAmount.Text, out int count) || count <= 0)
            {
                MessageBox.Show("Please enter a positive integer");
                return;
            }

            sites.Clear();
            var rand = new Random();
            for (int i = 0; i < count; i++)
            {
                sites.Add(new Point(rand.Next((int)canvas.ActualWidth), rand.Next((int)canvas.ActualHeight)));
            }
            UpdateVoronoiDiagram();
        }

        private void SingleThreaded_Click(object sender, RoutedEventArgs e)
        {
            useMultiThreading = false;
            UpdateVoronoiDiagram();
        }

        private void MultiThreaded_Click(object sender, RoutedEventArgs e)
        {
            useMultiThreading = true;
            UpdateVoronoiDiagram();
        }

        private void UpdateVoronoiDiagram()
        {
            if (canvas == null || canvas.ActualWidth == 0 || canvas.ActualHeight == 0)
                return;

            if (useMultiThreading)
                ComputeVoronoiMultiThreaded();
            else
                ComputeVoronoiSingleThreaded();
        }

        private void ComputeVoronoiSingleThreaded()
        {
            var stopwatch = Stopwatch.StartNew();
            var process = Process.GetCurrentProcess();
            long startMemory = process.WorkingSet64;
            var startCpuTime = process.TotalProcessorTime;

            int width = (int)canvas.ActualWidth;
            int height = (int)canvas.ActualHeight;
            var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);

            if (sites.Count == 0)
            {
                wb.Clear(Colors.White);
                canvas.Children.Clear();
                canvas.Children.Add(new Image { Source = wb });
                return;
            }

            wb.Lock();
            try
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int nearest = GetNearestSiteIndex(new Point(x, y));
                        Color color = GetColorForSite(nearest);
                        wb.SetPixel(x, y, color);
                    }
                }
            }
            finally
            {
                wb.Unlock();
            }

            DrawSites(wb);
            canvas.Children.Clear();
            canvas.Children.Add(new Image { Source = wb });

            stopwatch.Stop();
            var endCpuTime = process.TotalProcessorTime;

            UpdatePerformanceInfo(
                endCpuTime - startCpuTime,
                stopwatch.Elapsed);
        }

        private void ComputeVoronoiMultiThreaded()
        {
            var stopwatch = Stopwatch.StartNew();
            var process = Process.GetCurrentProcess();
            long startMemory = process.WorkingSet64;
            var startCpuTime = process.TotalProcessorTime;

            int width = (int)canvas.ActualWidth;
            int height = (int)canvas.ActualHeight;
            byte[] pixels = new byte[width * height * 4];

            if (sites.Count == 0)
            {
                var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
                wb.Clear(Colors.White);
                UpdateCanvasWithBitmap(wb);
                return;
            }

            // Pre-calculate colors for each site
            Color[] siteColors = new Color[sites.Count];
            for (int i = 0; i < sites.Count; i++)
            {
                siteColors[i] = GetColorForSite(i);
            }

            int numThreads = Environment.ProcessorCount;
            int regionHeight = height / numThreads;

            Parallel.For(0, numThreads, t =>
            {
                int yStart = t * regionHeight;
                int yEnd = (t == numThreads - 1) ? height : yStart + regionHeight;

                for (int y = yStart; y < yEnd; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int nearest = GetNearestSiteIndex(new Point(x, y));
                        Color color = nearest == -1 ? Colors.White : siteColors[nearest];

                        int index = (y * width + x) * 4;
                        pixels[index] = color.B;
                        pixels[index + 1] = color.G;
                        pixels[index + 2] = color.R;
                        pixels[index + 3] = color.A;
                    }
                }
            });

            Dispatcher.Invoke(() =>
            {
                var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
                wb.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
                DrawSites(wb);
                UpdateCanvasWithBitmap(wb);

                stopwatch.Stop();
                var endCpuTime = process.TotalProcessorTime;

                UpdatePerformanceInfo(
                    endCpuTime - startCpuTime,
                    stopwatch.Elapsed);
            });
        }

        private void UpdateCanvasWithBitmap(WriteableBitmap wb)
        {
            canvas.Children.Clear();
            canvas.Children.Add(new Image { Source = wb });
        }

        private int GetNearestSiteIndex(Point p)
        {
            int index = -1;
            double minDist = double.MaxValue;
            for (int i = 0; i < sites.Count; i++)
            {
                double d = CalculateDistance(p, sites[i]);
                if (d < minDist)
                {
                    minDist = d;
                    index = i;
                }
            }
            return index;
        }

        private Color GetColorForSite(int index)
        {
            if (index == -1) return Colors.White;

            var localRand = new Random(index * 1000);
            return Color.FromRgb(
                (byte)localRand.Next(100, 255),
                (byte)localRand.Next(100, 255),
                (byte)localRand.Next(100, 255));
        }
    }

    public static class WriteableBitmapExtensions
    {
        public static void SetPixel(this WriteableBitmap wb, int x, int y, Color color)
        {
            if (x < 0 || x >= wb.PixelWidth || y < 0 || y >= wb.PixelHeight)
                return;

            Int32Rect rect = new Int32Rect(x, y, 1, 1);
            byte[] colorData = { color.B, color.G, color.R, color.A };
            wb.WritePixels(rect, colorData, 4, 0);
        }

        public static void Clear(this WriteableBitmap wb, Color color)
        {
            Int32Rect rect = new Int32Rect(0, 0, wb.PixelWidth, wb.PixelHeight);
            byte[] colorData = new byte[wb.PixelWidth * wb.PixelHeight * 4];
            for (int i = 0; i < colorData.Length; i += 4)
            {
                colorData[i] = color.B;
                colorData[i + 1] = color.G;
                colorData[i + 2] = color.R;
                colorData[i + 3] = color.A;
            }
            wb.WritePixels(rect, colorData, wb.PixelWidth * 4, 0);
        }
    }
}