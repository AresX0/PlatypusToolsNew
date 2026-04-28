using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace PlatypusTools.UI.Services.Visualization
{
    // Squarified treemap (Bruls/Huijing/van Wijk 2000). Pure WPF Rect output.
    public class TreeMapItem
    {
        public string Name { get; set; } = string.Empty;
        public double Value { get; set; }
        public object? Tag { get; set; }
    }

    public class TreeMapNode
    {
        public TreeMapItem Item { get; set; } = new();
        public Rect Rect;
    }

    public static class TreeMapLayout
    {
        public static List<TreeMapNode> Layout(IEnumerable<TreeMapItem> items, Rect bounds)
        {
            var sorted = items.Where(i => i.Value > 0).OrderByDescending(i => i.Value).ToList();
            var result = new List<TreeMapNode>();
            if (sorted.Count == 0) return result;
            double total = sorted.Sum(i => i.Value);
            if (total <= 0) return result;
            double scale = bounds.Width * bounds.Height / total;
            var scaled = sorted.Select(i => new { Item = i, Area = i.Value * scale }).ToList();
            Squarify(scaled.Select(x => x.Area).ToList(), bounds, scaled.Select(x => x.Item).ToList(), result, 0);
            return result;
        }

        private static void Squarify(List<double> areas, Rect rect, List<TreeMapItem> items, List<TreeMapNode> result, int start)
        {
            if (start >= areas.Count) return;
            var row = new List<int> { start };
            int i = start + 1;
            double w = Math.Min(rect.Width, rect.Height);
            while (i < areas.Count)
            {
                var test = new List<int>(row) { i };
                if (Worst(areas, row, w) >= Worst(areas, test, w))
                {
                    row = test;
                    i++;
                }
                else break;
            }
            LayoutRow(areas, row, rect, items, result, out var remaining);
            Squarify(areas, remaining, items, result, i);
        }

        private static double Worst(List<double> areas, List<int> row, double w)
        {
            double sum = 0;
            double max = 0, min = double.MaxValue;
            foreach (var idx in row)
            {
                sum += areas[idx];
                if (areas[idx] > max) max = areas[idx];
                if (areas[idx] < min) min = areas[idx];
            }
            if (sum <= 0) return double.MaxValue;
            double w2 = w * w;
            double s2 = sum * sum;
            return Math.Max((w2 * max) / s2, s2 / (w2 * min));
        }

        private static void LayoutRow(List<double> areas, List<int> row, Rect rect, List<TreeMapItem> items, List<TreeMapNode> result, out Rect remaining)
        {
            double sum = row.Sum(idx => areas[idx]);
            bool horizontal = rect.Width >= rect.Height;
            if (horizontal)
            {
                double w = sum / rect.Height;
                double y = rect.Y;
                foreach (var idx in row)
                {
                    double h = areas[idx] / w;
                    result.Add(new TreeMapNode { Item = items[idx], Rect = new Rect(rect.X, y, Math.Max(0, w - 1), Math.Max(0, h - 1)) });
                    y += h;
                }
                remaining = new Rect(rect.X + w, rect.Y, Math.Max(0, rect.Width - w), rect.Height);
            }
            else
            {
                double h = sum / rect.Width;
                double x = rect.X;
                foreach (var idx in row)
                {
                    double w = areas[idx] / h;
                    result.Add(new TreeMapNode { Item = items[idx], Rect = new Rect(x, rect.Y, Math.Max(0, w - 1), Math.Max(0, h - 1)) });
                    x += w;
                }
                remaining = new Rect(rect.X, rect.Y + h, rect.Width, Math.Max(0, rect.Height - h));
            }
        }
    }
}
