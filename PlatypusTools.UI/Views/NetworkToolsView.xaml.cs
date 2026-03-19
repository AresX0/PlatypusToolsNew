using System.Windows;
using System.Windows.Controls;

namespace PlatypusTools.UI.Views
{
    public partial class NetworkToolsView : UserControl
    {
        public NetworkToolsView()
        {
            InitializeComponent();
        }
        
        private async void OnPingClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.NetworkToolsViewModel vm)
            {
                vm.TargetHost = TargetHostTextBox.Text;
                if (string.IsNullOrWhiteSpace(vm.TargetHost))
                {
                    MessageBox.Show("Please enter a target host.", "Network Tools", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                await vm.PingAsync();
                PingResultsControl.ItemsSource = null;
                PingResultsControl.ItemsSource = vm.PingResults;
            }
        }

        private async void OnTracerouteClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.NetworkToolsViewModel vm)
            {
                vm.TargetHost = TracerouteHostTextBox.Text;
                if (string.IsNullOrWhiteSpace(vm.TargetHost))
                {
                    MessageBox.Show("Please enter a target host.", "Network Tools", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                await vm.TracerouteAsync();
                TracerouteResultsControl.ItemsSource = null;
                TracerouteResultsControl.ItemsSource = vm.PingResults;
            }
        }

        private async void OnRefreshConnectionsClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.NetworkToolsViewModel vm)
            {
                await vm.RefreshConnectionsAsync();
                ConnectionsDataGrid.ItemsSource = null;
                ConnectionsDataGrid.ItemsSource = vm.Connections;
            }
        }

        private async void OnRefreshAdaptersClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.NetworkToolsViewModel vm)
            {
                await vm.RefreshAdaptersAsync();
                AdaptersDataGrid.ItemsSource = null;
                AdaptersDataGrid.ItemsSource = vm.Adapters;
            }
        }

        private async void OnPortScanClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.NetworkToolsViewModel vm)
            {
                await vm.PortScanAsync();
            }
        }

        private void OnCancelPortScanClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.NetworkToolsViewModel vm)
            {
                vm.CancelPortScanCommand.Execute(null);
            }
        }

        private async void OnDnsLookupClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.NetworkToolsViewModel vm)
            {
                await vm.DnsLookupAsync();
            }
        }

        private async void OnNetworkDiscoveryClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.NetworkToolsViewModel vm)
            {
                await vm.NetworkDiscoveryAsync();
            }
        }

        private void OnCancelDiscoveryClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.NetworkToolsViewModel vm)
            {
                vm.CancelDiscoveryCommand.Execute(null);
            }
        }

        private void OnStartBandwidthClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.NetworkToolsViewModel vm)
            {
                vm.StartBandwidthMonitorCommand.Execute(null);
            }
        }

        private void OnStopBandwidthClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.NetworkToolsViewModel vm)
            {
                vm.StopBandwidthMonitorCommand.Execute(null);
            }
        }

        private async void OnWhoisLookupClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.NetworkToolsViewModel vm)
            {
                await vm.WhoisLookupAsync();
            }
        }
    }
}
