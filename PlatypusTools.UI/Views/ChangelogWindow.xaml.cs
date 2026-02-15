using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.Views
{
    public partial class ChangelogWindow : Window
    {
        public ChangelogWindow()
        {
            InitializeComponent();
            Loaded += ChangelogWindow_Loaded;
        }

        private async void ChangelogWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadReleasesAsync();
        }

        private async Task LoadReleasesAsync()
        {
            try
            {
                var client = HttpClientFactory.Api;
                var url = "https://api.github.com/repos/AresX0/PlatypusToolsNew/releases?per_page=50";
                var json = await client.GetStringAsync(url);

                using var doc = JsonDocument.Parse(json);
                var releases = new List<ReleaseEntry>();

                foreach (var elem in doc.RootElement.EnumerateArray())
                {
                    releases.Add(new ReleaseEntry
                    {
                        TagName = elem.GetProperty("tag_name").GetString() ?? "",
                        Name = elem.GetProperty("name").GetString() ?? "",
                        Body = elem.GetProperty("body").GetString() ?? "(no release notes)",
                        PublishedAt = elem.GetProperty("published_at").GetDateTime(),
                        IsPrerelease = elem.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()
                    });
                }

                LoadingText.Visibility = Visibility.Collapsed;
                ReleasesListBox.ItemsSource = releases;
            }
            catch (Exception ex)
            {
                LoadingText.Text = $"Failed to load releases: {ex.Message}";
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }

    public class ReleaseEntry
    {
        public string TagName { get; set; } = "";
        public string Name { get; set; } = "";
        public string Body { get; set; } = "";
        public DateTime PublishedAt { get; set; }
        public bool IsPrerelease { get; set; }
        public string DisplayDate => PublishedAt.ToString("MMMM d, yyyy");
        public string DisplayTitle => string.IsNullOrWhiteSpace(Name) ? TagName : $"{TagName} — {Name}";
    }
}
