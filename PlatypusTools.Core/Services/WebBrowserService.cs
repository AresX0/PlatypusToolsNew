using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for managing web browser favorites/bookmarks.
    /// Memory efficient - loads favorites on demand.
    /// </summary>
    public class WebBrowserService
    {
        private readonly string _favoritesPath;
        private List<BrowserFavorite>? _favorites;

        public WebBrowserService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _favoritesPath = Path.Combine(appData, "PlatypusTools", "BrowserFavorites.json");
        }

        /// <summary>
        /// Gets all favorites, loading from disk if needed.
        /// </summary>
        public List<BrowserFavorite> GetFavorites()
        {
            if (_favorites == null)
            {
                LoadFavorites();
            }
            return _favorites ?? new List<BrowserFavorite>();
        }

        /// <summary>
        /// Adds a new favorite.
        /// </summary>
        public void AddFavorite(string title, string url, string? folder = null)
        {
            var favorites = GetFavorites();
            
            // Check for duplicates
            if (favorites.Any(f => f.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            favorites.Add(new BrowserFavorite
            {
                Id = Guid.NewGuid().ToString(),
                Title = title,
                Url = url,
                Folder = folder ?? "Favorites",
                DateAdded = DateTime.Now
            });

            SaveFavorites();
        }

        /// <summary>
        /// Removes a favorite by ID.
        /// </summary>
        public void RemoveFavorite(string id)
        {
            var favorites = GetFavorites();
            favorites.RemoveAll(f => f.Id == id);
            SaveFavorites();
        }

        /// <summary>
        /// Updates an existing favorite.
        /// </summary>
        public void UpdateFavorite(string id, string? title = null, string? url = null, string? folder = null)
        {
            var favorite = GetFavorites().FirstOrDefault(f => f.Id == id);
            if (favorite != null)
            {
                if (title != null) favorite.Title = title;
                if (url != null) favorite.Url = url;
                if (folder != null) favorite.Folder = folder;
                SaveFavorites();
            }
        }

        /// <summary>
        /// Gets all folder names.
        /// </summary>
        public List<string> GetFolders()
        {
            return GetFavorites()
                .Select(f => f.Folder)
                .Distinct()
                .OrderBy(f => f)
                .ToList();
        }

        /// <summary>
        /// Gets favorites in a specific folder.
        /// </summary>
        public List<BrowserFavorite> GetFavoritesByFolder(string folder)
        {
            return GetFavorites()
                .Where(f => f.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.Title)
                .ToList();
        }

        /// <summary>
        /// Imports favorites from Edge browser.
        /// </summary>
        public int ImportFromEdge()
        {
            try
            {
                var edgePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "Edge", "User Data", "Default", "Bookmarks");

                if (!File.Exists(edgePath))
                {
                    return 0;
                }

                var json = File.ReadAllText(edgePath);
                var doc = JsonDocument.Parse(json);
                var imported = 0;

                if (doc.RootElement.TryGetProperty("roots", out var roots))
                {
                    imported += ImportBookmarkNode(roots, "bookmark_bar", "Edge - Bookmarks Bar");
                    imported += ImportBookmarkNode(roots, "other", "Edge - Other Bookmarks");
                }

                return imported;
            }
            catch
            {
                return 0;
            }
        }

        private int ImportBookmarkNode(JsonElement roots, string nodeName, string targetFolder)
        {
            int imported = 0;

            try
            {
                if (roots.TryGetProperty(nodeName, out var node))
                {
                    imported += ProcessBookmarkChildren(node, targetFolder);
                }
            }
            catch { }

            return imported;
        }

        private int ProcessBookmarkChildren(JsonElement node, string folder)
        {
            int imported = 0;

            if (node.TryGetProperty("children", out var children))
            {
                foreach (var child in children.EnumerateArray())
                {
                    var type = child.GetProperty("type").GetString();

                    if (type == "url")
                    {
                        var name = child.GetProperty("name").GetString() ?? "Untitled";
                        var url = child.GetProperty("url").GetString();

                        if (!string.IsNullOrEmpty(url))
                        {
                            AddFavorite(name, url, folder);
                            imported++;
                        }
                    }
                    else if (type == "folder")
                    {
                        var folderName = child.GetProperty("name").GetString() ?? "Folder";
                        imported += ProcessBookmarkChildren(child, $"{folder}/{folderName}");
                    }
                }
            }

            return imported;
        }

        /// <summary>
        /// Exports favorites to HTML format (bookmark format).
        /// </summary>
        public void ExportToHtml(string filePath)
        {
            var favorites = GetFavorites();
            var folders = GetFolders();

            using var writer = new StreamWriter(filePath);
            writer.WriteLine("<!DOCTYPE NETSCAPE-Bookmark-file-1>");
            writer.WriteLine("<META HTTP-EQUIV=\"Content-Type\" CONTENT=\"text/html; charset=UTF-8\">");
            writer.WriteLine("<TITLE>Bookmarks</TITLE>");
            writer.WriteLine("<H1>Bookmarks</H1>");
            writer.WriteLine("<DL><p>");

            foreach (var folder in folders)
            {
                writer.WriteLine($"    <DT><H3>{System.Web.HttpUtility.HtmlEncode(folder)}</H3>");
                writer.WriteLine("    <DL><p>");

                foreach (var fav in GetFavoritesByFolder(folder))
                {
                    var timestamp = ((DateTimeOffset)fav.DateAdded).ToUnixTimeSeconds();
                    writer.WriteLine($"        <DT><A HREF=\"{System.Web.HttpUtility.HtmlEncode(fav.Url)}\" ADD_DATE=\"{timestamp}\">{System.Web.HttpUtility.HtmlEncode(fav.Title)}</A>");
                }

                writer.WriteLine("    </DL><p>");
            }

            writer.WriteLine("</DL><p>");
        }

        private void LoadFavorites()
        {
            _favorites = new List<BrowserFavorite>();

            try
            {
                if (File.Exists(_favoritesPath))
                {
                    var json = File.ReadAllText(_favoritesPath);
                    _favorites = JsonSerializer.Deserialize<List<BrowserFavorite>>(json) ?? new List<BrowserFavorite>();
                }
            }
            catch
            {
                _favorites = new List<BrowserFavorite>();
            }

            // Add default favorites if empty
            if (_favorites.Count == 0)
            {
                _favorites.Add(new BrowserFavorite
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "Google",
                    Url = "https://www.google.com",
                    Folder = "Search Engines",
                    DateAdded = DateTime.Now
                });
                _favorites.Add(new BrowserFavorite
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "Bing",
                    Url = "https://www.bing.com",
                    Folder = "Search Engines",
                    DateAdded = DateTime.Now
                });
                _favorites.Add(new BrowserFavorite
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "DuckDuckGo",
                    Url = "https://duckduckgo.com",
                    Folder = "Search Engines",
                    DateAdded = DateTime.Now
                });
                _favorites.Add(new BrowserFavorite
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "GitHub",
                    Url = "https://github.com",
                    Folder = "Development",
                    DateAdded = DateTime.Now
                });
                _favorites.Add(new BrowserFavorite
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "Stack Overflow",
                    Url = "https://stackoverflow.com",
                    Folder = "Development",
                    DateAdded = DateTime.Now
                });
                SaveFavorites();
            }
        }

        private void SaveFavorites()
        {
            try
            {
                var directory = Path.GetDirectoryName(_favoritesPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(_favorites, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_favoritesPath, json);
            }
            catch { }
        }
    }

    /// <summary>
    /// Represents a browser bookmark/favorite.
    /// </summary>
    public class BrowserFavorite
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Folder { get; set; } = "Favorites";
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public string? FaviconUrl { get; set; }
    }
}
