using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentFTP;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class FtpClientViewModel : ObservableObject
{
    [ObservableProperty] private string _host = "ftp.example.com";
    [ObservableProperty] private int _port = 21;
    [ObservableProperty] private string _user = "anonymous";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _remotePath = "/";
    [ObservableProperty] private string _status = "Enter credentials and List.";
    [ObservableProperty] private bool _isBusy;
    public ObservableCollection<FtpRow> Items { get; } = new();
    [ObservableProperty] private FtpRow? _selected;

    [RelayCommand]
    private async Task ListAsync()
    {
        IsBusy = true; Status = "Connecting…"; Items.Clear();
        try
        {
            using var c = new AsyncFtpClient(Host, User, Password, Port);
            await c.AutoConnect();
            foreach (var it in await c.GetListing(RemotePath))
                Items.Add(new FtpRow { Name = it.Name, Type = it.Type.ToString(), Size = it.Size, Modified = it.Modified, FullName = it.FullName });
            Status = $"{Items.Count} entries in {RemotePath}";
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; }
        IsBusy = false;
    }

    public async Task DownloadAsync(string localDir)
    {
        if (Selected is null) return;
        IsBusy = true; Status = "Downloading…";
        try
        {
            using var c = new AsyncFtpClient(Host, User, Password, Port);
            await c.AutoConnect();
            var local = Path.Combine(localDir, Selected.Name);
            await c.DownloadFile(local, Selected.FullName);
            Status = $"Saved {local}";
        }
        catch (Exception ex) { Status = "Download failed: " + ex.Message; }
        IsBusy = false;
    }
}

public sealed class FtpRow
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public long Size { get; set; }
    public DateTime Modified { get; set; }
    public string FullName { get; set; } = "";
}
