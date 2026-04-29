using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailKit.Net.Smtp;
using MimeKit;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class MailSenderViewModel : ObservableObject
{
    [ObservableProperty] private string _smtpHost = "smtp.example.com";
    [ObservableProperty] private int _port = 587;
    [ObservableProperty] private bool _useSsl = true;
    [ObservableProperty] private string _user = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _from = "";
    [ObservableProperty] private string _to = "";
    [ObservableProperty] private string _subject = "";
    [ObservableProperty] private string _body = "";
    [ObservableProperty] private string _status = "Configure SMTP and Send.";
    [ObservableProperty] private bool _isBusy;

    [RelayCommand]
    private async Task SendAsync()
    {
        IsBusy = true; Status = "Sending…";
        try
        {
            var msg = new MimeMessage();
            msg.From.Add(MailboxAddress.Parse(string.IsNullOrEmpty(From) ? User : From));
            foreach (var addr in To.Split(',', ';', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries))
                msg.To.Add(MailboxAddress.Parse(addr));
            msg.Subject = Subject;
            msg.Body = new TextPart("plain") { Text = Body };

            using var client = new SmtpClient();
            await client.ConnectAsync(SmtpHost, Port, UseSsl ? MailKit.Security.SecureSocketOptions.StartTlsWhenAvailable : MailKit.Security.SecureSocketOptions.None);
            if (!string.IsNullOrEmpty(User)) await client.AuthenticateAsync(User, Password);
            await client.SendAsync(msg);
            await client.DisconnectAsync(true);
            Status = "Sent";
        }
        catch (System.Exception ex) { Status = "Failed: " + ex.Message; }
        IsBusy = false;
    }
}
