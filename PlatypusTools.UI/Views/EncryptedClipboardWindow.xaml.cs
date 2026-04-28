using System;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using PlatypusTools.UI.Services.ClipboardSync;

namespace PlatypusTools.UI.Views
{
    public partial class EncryptedClipboardWindow : Window
    {
        public EncryptedClipboardWindow()
        {
            InitializeComponent();
            var key = EncryptedClipboardSyncService.Instance.LoadOrCreateKey();
            KeyBox.Text = Convert.ToBase64String(key);
            // Try to autoload local API token
            var tokenPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PlatypusTools", "api-token.txt");
            try { if (File.Exists(tokenPath)) TokenBox.Text = File.ReadAllText(tokenPath).Trim(); } catch { }
        }

        private byte[] CurrentKey()
        {
            try { return Convert.FromBase64String(KeyBox.Text.Trim()); }
            catch { throw new InvalidOperationException("Key must be base64-encoded 32 bytes."); }
        }

        private void NewKey_Click(object sender, RoutedEventArgs e)
        {
            var k = RandomNumberGenerator.GetBytes(32);
            KeyBox.Text = Convert.ToBase64String(k);
            try { File.WriteAllBytes(EncryptedClipboardSyncService.Instance.KeyFilePath, k); } catch { }
            StatusText.Text = "New key generated and saved.";
        }

        private void CopyKey_Click(object sender, RoutedEventArgs e)
        {
            try { System.Windows.Clipboard.SetText(KeyBox.Text); StatusText.Text = "Key copied."; } catch { }
        }

        private async void Push_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StatusText.Text = "Encrypting & pushing...";
                await EncryptedClipboardSyncService.Instance.PushAsync(PeerBox.Text.Trim(), TokenBox.Text.Trim(), CurrentKey(), TextArea.Text);
                StatusText.Text = "Pushed.";
            }
            catch (Exception ex) { StatusText.Text = "Push failed: " + ex.Message; }
        }

        private async void Pull_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StatusText.Text = "Pulling...";
                var text = await EncryptedClipboardSyncService.Instance.PullAsync(PeerBox.Text.Trim(), TokenBox.Text.Trim(), CurrentKey());
                if (text == null) { StatusText.Text = "Nothing to pull or decryption failed."; return; }
                TextArea.Text = text;
                StatusText.Text = "Pulled & decrypted.";
            }
            catch (Exception ex) { StatusText.Text = "Pull failed: " + ex.Message; }
        }

        private void CopyOut_Click(object sender, RoutedEventArgs e)
        {
            try { System.Windows.Clipboard.SetText(TextArea.Text); StatusText.Text = "Copied to system clipboard."; } catch { }
        }
    }
}
