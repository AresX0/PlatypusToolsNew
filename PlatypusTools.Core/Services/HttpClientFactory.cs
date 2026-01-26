using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Net.Http.Headers;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Factory for creating and managing shared HttpClient instances.
    /// Resolves socket exhaustion issues from creating HttpClient per-request.
    /// </summary>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "HttpClient instances are singleton and intentionally kept alive for app lifetime")]
    public static class HttpClientFactory
    {
        private static readonly Lazy<HttpClient> _defaultClient = new(CreateDefaultClient);
        private static readonly Lazy<HttpClient> _noTimeoutClient = new(CreateNoTimeoutClient);
        private static readonly Lazy<HttpClient> _downloadClient = new(CreateDownloadClient);
        private static readonly Lazy<HttpClient> _apiClient = new(CreateApiClient);

        /// <summary>
        /// Default HttpClient with 30 second timeout.
        /// </summary>
        public static HttpClient Default => _defaultClient.Value;

        /// <summary>
        /// HttpClient with no timeout for long-running operations.
        /// </summary>
        public static HttpClient NoTimeout => _noTimeoutClient.Value;

        /// <summary>
        /// HttpClient configured for file downloads.
        /// </summary>
        public static HttpClient Download => _downloadClient.Value;

        /// <summary>
        /// HttpClient configured for API calls.
        /// </summary>
        public static HttpClient Api => _apiClient.Value;

        private static HttpClient CreateDefaultClient()
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 10
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            ConfigureDefaultHeaders(client);
            return client;
        }

        private static HttpClient CreateNoTimeoutClient()
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                MaxConnectionsPerServer = 5
            };

            var client = new HttpClient(handler)
            {
                Timeout = System.Threading.Timeout.InfiniteTimeSpan
            };

            ConfigureDefaultHeaders(client);
            return client;
        }

        private static HttpClient CreateDownloadClient()
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(30),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(10),
                MaxConnectionsPerServer = 3
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(30)
            };

            ConfigureDefaultHeaders(client);
            return client;
        }

        private static HttpClient CreateApiClient()
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 20
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15)
            };

            ConfigureDefaultHeaders(client);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        private static void ConfigureDefaultHeaders(HttpClient client)
        {
            client.DefaultRequestHeaders.Add("User-Agent", $"PlatypusTools/{GetVersion()} (https://github.com/platypustools)");
        }

        private static string GetVersion()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "3.2.0";
            }
            catch
            {
                return "3.2.0";
            }
        }

        /// <summary>
        /// Creates a new HttpClient with custom configuration.
        /// Use this only when Default/NoTimeout/Download/Api don't fit your needs.
        /// </summary>
        public static HttpClient CreateCustomClient(TimeSpan timeout, int maxConnections = 10)
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = maxConnections
            };

            var client = new HttpClient(handler)
            {
                Timeout = timeout
            };

            ConfigureDefaultHeaders(client);
            return client;
        }
    }
}
