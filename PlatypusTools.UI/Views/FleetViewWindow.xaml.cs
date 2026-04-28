using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PlatypusTools.UI.Views
{
    public partial class FleetViewWindow : Window
    {
        public class FleetMember
        {
            public string Address { get; set; } = "";
            public string Status { get; set; } = "Probing";
            public long LatencyMs { get; set; }
            public string Banner { get; set; } = "";
        }

        private readonly ObservableCollection<FleetMember> _members = new();
        private CancellationTokenSource? _cts;
        private static readonly HttpClient _http = CreateClient();

        private static HttpClient CreateClient()
        {
#pragma warning disable CA2000 // handler ownership transferred to HttpClient
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };
#pragma warning restore CA2000
            return new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromMilliseconds(800) };
        }

        public FleetViewWindow()
        {
            InitializeComponent();
            FleetGrid.ItemsSource = _members;
        }

        private async void Scan_Click(object sender, RoutedEventArgs e)
        {
            Stop_Click(sender, e);
            _members.Clear();

            var subnet = (SubnetBox.Text ?? "").Trim().TrimEnd('.');
            if (subnet.Split('.').Length != 3)
            {
                StatusText.Text = "Subnet must be three octets, e.g. 192.168.1";
                return;
            }
            var port = int.TryParse(PortBox.Text, out var p) ? p : 47392;

            _cts = new CancellationTokenSource();
            StatusText.Text = "Scanning…";
            var ct = _cts.Token;

            var tasks = new System.Collections.Generic.List<Task>();
            for (int i = 1; i <= 254; i++)
            {
                var host = $"{subnet}.{i}";
                var url = $"https://{host}:{port}/health";
                tasks.Add(ProbeAsync(host, url, ct));
            }
            try
            {
                await Task.WhenAll(tasks);
                StatusText.Text = $"Done — {_members.Count} responder(s)";
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Cancelled";
            }
        }

        private async Task ProbeAsync(string host, string url, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
                sw.Stop();
                if (!resp.IsSuccessStatusCode) return;
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var m = new FleetMember
                {
                    Address = host,
                    Status = "OK",
                    LatencyMs = sw.ElapsedMilliseconds,
                    Banner = body.Length > 200 ? body.Substring(0, 200) : body
                };
                await Dispatcher.InvokeAsync(() => _members.Add(m));
            }
            catch
            {
                // unreachable / not a Platypus host — silently skip
            }
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            try { _cts?.Cancel(); } catch { }
            _cts = null;
        }
    }
}
