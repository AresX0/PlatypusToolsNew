using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class FileDiffViewModel : ObservableObject
{
    [ObservableProperty] private string _leftPath = "";
    [ObservableProperty] private string _rightPath = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _identical;

    public ObservableCollection<DiffRow> Rows { get; } = new();

    [RelayCommand]
    public async Task CompareAsync()
    {
        Rows.Clear();
        if (!File.Exists(LeftPath) || !File.Exists(RightPath))
        { Status = "Both files must exist."; return; }

        Status = "Comparing…";
        var l = await File.ReadAllLinesAsync(LeftPath);
        var r = await File.ReadAllLinesAsync(RightPath);
        Identical = l.SequenceEqual(r);
        if (Identical) { Status = "Files are identical."; return; }

        // LCS-based line diff (simple Myers-ish)
        var diff = MyersDiff(l, r);
        foreach (var d in diff) Rows.Add(d);
        Status = $"{Rows.Count(d => d.Kind != DiffKind.Same)} differences.";
    }

    private static List<DiffRow> MyersDiff(string[] a, string[] b)
    {
        // Simple O(n*m) LCS table – fine for files up to a few thousand lines.
        int n = a.Length, m = b.Length;
        var lcs = new int[n + 1, m + 1];
        for (int i = 1; i <= n; i++)
            for (int j = 1; j <= m; j++)
                lcs[i, j] = a[i - 1] == b[j - 1]
                    ? lcs[i - 1, j - 1] + 1
                    : Math.Max(lcs[i - 1, j], lcs[i, j - 1]);
        var result = new List<DiffRow>();
        int x = n, y = m;
        while (x > 0 && y > 0)
        {
            if (a[x - 1] == b[y - 1]) { result.Add(new DiffRow(DiffKind.Same, x, y, a[x - 1])); x--; y--; }
            else if (lcs[x - 1, y] >= lcs[x, y - 1]) { result.Add(new DiffRow(DiffKind.Removed, x, null, a[x - 1])); x--; }
            else { result.Add(new DiffRow(DiffKind.Added, null, y, b[y - 1])); y--; }
        }
        while (x > 0) { result.Add(new DiffRow(DiffKind.Removed, x, null, a[x - 1])); x--; }
        while (y > 0) { result.Add(new DiffRow(DiffKind.Added, null, y, b[y - 1])); y--; }
        result.Reverse();
        return result;
    }
}

public enum DiffKind { Same, Added, Removed }

public sealed class DiffRow
{
    public DiffKind Kind { get; }
    public int? LeftLine { get; }
    public int? RightLine { get; }
    public string Text { get; }
    public DiffRow(DiffKind k, int? l, int? r, string t) { Kind = k; LeftLine = l; RightLine = r; Text = t; }
    public string Marker => Kind == DiffKind.Added ? "+" : Kind == DiffKind.Removed ? "-" : " ";
    public string LeftDisplay => LeftLine?.ToString() ?? "";
    public string RightDisplay => RightLine?.ToString() ?? "";
    public string ColorTag => Kind switch { DiffKind.Added => "#1B5E20", DiffKind.Removed => "#7F1D1D", _ => "Transparent" };
}
