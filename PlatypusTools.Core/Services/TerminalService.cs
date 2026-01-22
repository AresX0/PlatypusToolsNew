using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Lightweight terminal service for Telnet and SSH connections.
    /// Uses built-in .NET sockets for Telnet. SSH requires external implementation.
    /// Memory efficient - streams data instead of buffering.
    /// </summary>
    public class TerminalService : IDisposable
    {
        private TcpClient? _tcpClient;
        private NetworkStream? _networkStream;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private CancellationTokenSource? _readCts;
        private bool _disposed;

        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 23; // Default Telnet port
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string PrivateKeyPath { get; set; } = string.Empty; // For SSH key authentication
        public bool UseSsh { get; set; }
        public int ConnectionTimeout { get; set; } = 10000;

        public bool IsConnected => _tcpClient?.Connected ?? false;

        public event EventHandler<string>? DataReceived;
        public event EventHandler<string>? StatusChanged;
        public event EventHandler? Disconnected;

        private void ReportStatus(string status) => StatusChanged?.Invoke(this, status);

        /// <summary>
        /// Connects to the remote host.
        /// </summary>
        public async Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            if (UseSsh)
            {
                return await ConnectSshViaOpenSshAsync(ct);
            }

            return await ConnectTelnetAsync(ct);
        }

        private async Task<bool> ConnectTelnetAsync(CancellationToken ct)
        {
            try
            {
                ReportStatus($"Connecting to {Host}:{Port} (Telnet)...");

                _tcpClient = new TcpClient();
                
                using var timeoutCts = new CancellationTokenSource(ConnectionTimeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                await _tcpClient.ConnectAsync(Host, Port, linkedCts.Token);

                _networkStream = _tcpClient.GetStream();
                _reader = new StreamReader(_networkStream, Encoding.ASCII);
                _writer = new StreamWriter(_networkStream, Encoding.ASCII) { AutoFlush = true };

                ReportStatus("Connected!");

                // Start reading data in background
                _readCts = new CancellationTokenSource();
                _ = ReadDataLoopAsync(_readCts.Token);

                return true;
            }
            catch (OperationCanceledException)
            {
                ReportStatus("Connection timed out");
                return false;
            }
            catch (Exception ex)
            {
                ReportStatus($"Connection failed: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> ConnectSshViaOpenSshAsync(CancellationToken ct)
        {
            // Use OpenSSH client if available (Windows 10+ has it built-in)
            try
            {
                ReportStatus($"Connecting to {Host}:{Port} (SSH via OpenSSH)...");
                
                // Check if OpenSSH is available
                var sshPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh.exe");
                if (!File.Exists(sshPath))
                {
                    sshPath = "ssh"; // Try PATH
                }

                ReportStatus("SSH connection requires interactive terminal. Use Windows Terminal or PowerShell for SSH.");
                ReportStatus($"Command: ssh {Username}@{Host} -p {Port}");
                
                return false;
            }
            catch (Exception ex)
            {
                ReportStatus($"SSH error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Sends a command/text to the remote host.
        /// </summary>
        public async Task SendAsync(string text, CancellationToken ct = default)
        {
            if (_writer == null || !IsConnected)
            {
                ReportStatus("Not connected");
                return;
            }

            try
            {
                await _writer.WriteLineAsync(text.AsMemory(), ct);
            }
            catch (Exception ex)
            {
                ReportStatus($"Send error: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends a command and waits for response.
        /// </summary>
        public async Task<string> SendCommandAsync(string command, int timeoutMs = 5000, CancellationToken ct = default)
        {
            if (_writer == null || _networkStream == null || !IsConnected)
            {
                return "Not connected";
            }

            try
            {
                // Send command
                await _writer.WriteLineAsync(command.AsMemory(), ct);

                // Wait for response
                var response = new StringBuilder();
                var buffer = new byte[4096];

                using var timeoutCts = new CancellationTokenSource(timeoutMs);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                try
                {
                    // Wait a bit for response to accumulate
                    await Task.Delay(100, linkedCts.Token);

                    while (_networkStream.DataAvailable)
                    {
                        var bytesRead = await _networkStream.ReadAsync(buffer, 0, buffer.Length, linkedCts.Token);
                        if (bytesRead > 0)
                        {
                            response.Append(Encoding.ASCII.GetString(buffer, 0, bytesRead));
                        }
                    }
                }
                catch (OperationCanceledException) { }

                return response.ToString();
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        private async Task ReadDataLoopAsync(CancellationToken ct)
        {
            var buffer = new byte[4096];

            try
            {
                while (!ct.IsCancellationRequested && _networkStream != null && IsConnected)
                {
                    if (_networkStream.DataAvailable)
                    {
                        var bytesRead = await _networkStream.ReadAsync(buffer, 0, buffer.Length, ct);
                        if (bytesRead > 0)
                        {
                            var text = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                            // Process Telnet control sequences (basic)
                            text = ProcessTelnetData(text);
                            DataReceived?.Invoke(this, text);
                        }
                    }
                    else
                    {
                        await Task.Delay(50, ct);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                ReportStatus($"Read error: {ex.Message}");
            }

            Disconnected?.Invoke(this, EventArgs.Empty);
        }

        private string ProcessTelnetData(string data)
        {
            // Basic Telnet IAC (Interpret As Command) handling
            // IAC = 0xFF, followed by command byte and option byte
            var result = new StringBuilder();
            
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] == (char)255 && i + 2 < data.Length)
                {
                    // Skip IAC sequence (3 bytes)
                    i += 2;
                }
                else if (data[i] >= 32 || data[i] == '\r' || data[i] == '\n' || data[i] == '\t')
                {
                    result.Append(data[i]);
                }
            }

            return result.ToString();
        }

        /// <summary>
        /// Disconnects from the remote host.
        /// </summary>
        public void Disconnect()
        {
            try
            {
                _readCts?.Cancel();
                _writer?.Dispose();
                _reader?.Dispose();
                _networkStream?.Dispose();
                _tcpClient?.Dispose();
            }
            catch { }
            finally
            {
                _writer = null;
                _reader = null;
                _networkStream = null;
                _tcpClient = null;
                ReportStatus("Disconnected");
            }
        }

        /// <summary>
        /// Opens SSH connection using Windows Terminal or default terminal.
        /// Supports public key authentication.
        /// </summary>
        public void OpenSshInTerminal()
        {
            try
            {
                // Build SSH command with optional key file
                var sshArgs = $"{Username}@{Host} -p {Port}";
                if (!string.IsNullOrWhiteSpace(PrivateKeyPath) && File.Exists(PrivateKeyPath))
                {
                    sshArgs = $"-i \"{PrivateKeyPath}\" {sshArgs}";
                }

                // PuTTY uses different args format
                var puttyArgs = $"-ssh {Username}@{Host} -P {Port}";
                if (!string.IsNullOrWhiteSpace(PrivateKeyPath) && File.Exists(PrivateKeyPath))
                {
                    // PuTTY uses .ppk format, but can convert on the fly with -i
                    puttyArgs = $"-ssh -i \"{PrivateKeyPath}\" {Username}@{Host} -P {Port}";
                }
                
                // Try Windows Terminal first
                var wtPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "WindowsApps", "wt.exe");

                if (File.Exists(wtPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = wtPath,
                        Arguments = $"ssh {sshArgs}",
                        UseShellExecute = true
                    });
                }
                else
                {
                    // Fall back to PuTTY if available
                    var puttyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PuTTY", "putty.exe");
                    if (File.Exists(puttyPath))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = puttyPath,
                            Arguments = puttyArgs,
                            UseShellExecute = true
                        });
                    }
                    else
                    {
                        // Use cmd with ssh
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/k ssh {sshArgs}",
                            UseShellExecute = true
                        });
                    }
                }

                ReportStatus("Opened SSH session in terminal" + 
                    (string.IsNullOrWhiteSpace(PrivateKeyPath) ? "" : " (with key)"));
            }
            catch (Exception ex)
            {
                ReportStatus($"Failed to open terminal: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Disconnect();
                _readCts?.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Saved terminal session for quick connect.
    /// </summary>
    public class SavedTerminalSession
    {
        public string Name { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 22;
        public string Username { get; set; } = string.Empty;
        public string PrivateKeyPath { get; set; } = string.Empty; // For SSH key authentication
        public bool UseSsh { get; set; } = true;
        public DateTime LastUsed { get; set; }
    }
}
