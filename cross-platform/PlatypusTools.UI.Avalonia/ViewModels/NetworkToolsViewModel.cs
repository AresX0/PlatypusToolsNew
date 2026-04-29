using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class NetworkToolsViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;
    [ObservableProperty] private string _host = "8.8.8.8";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private bool _isRunning;

    [RelayCommand] private Task PingAsync() => RunAsync("ping",
        ShellHelper.IsWindows ? $"-n 4 {Host}" : $"-c 4 {Host}");

    [RelayCommand] private Task TraceAsync() => RunAsync(
        ShellHelper.IsWindows ? "tracert" : "traceroute", Host);

    [RelayCommand] private Task DnsAsync() => RunAsync(
        ShellHelper.IsWindows ? "nslookup" : "dig", Host);

    [RelayCommand] private Task PortsAsync() => RunAsync(
        ShellHelper.IsWindows ? "netstat" : "ss",
        ShellHelper.IsWindows ? "-an" : "-tuln");

    [RelayCommand] private Task IpAsync() => RunAsync(
        ShellHelper.IsWindows ? "ipconfig" : ShellHelper.IsMac ? "ifconfig" : "ip", "addr");

    [RelayCommand] private void Cancel() => _cts?.Cancel();

    private async Task RunAsync(string file, string args)
    {
        if (IsRunning) return;
        IsRunning = true;
        Output = $"$ {file} {args}\n\n";
        _cts = new CancellationTokenSource();
        try
        {
            var r = await ShellHelper.RunAsync(file, args, _cts.Token,
                line => global::Avalonia.Threading.Dispatcher.UIThread.Post(() => Output += line + "\n"));
            Output += $"\n[exit {r.ExitCode}] {r.StdErr}";
        }
        finally { IsRunning = false; _cts?.Dispose(); _cts = null; }
    }
}
