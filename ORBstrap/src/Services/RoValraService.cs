using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Crypt3d.Models.APIs.RoValra;

namespace Crypt3d.Services
{
    /// <summary>
    /// Client for RoValra public APIs.
    /// Must attribute: "Powered by RoValra" → https://www.rovalra.com
    /// Respect rate limits and ToS: https://www.rovalra.com/tou
    /// </summary>
    public class RoValraService : IDisposable
    {
        private readonly HttpClient _http;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        // Simple in-memory cache (60s minimum as recommended by RoValra)
        private readonly Dictionary<string, (DateTime Expiry, object? Data)> _cache = new();
        private readonly object _cacheLock = new();

        public RoValraService()
        {
            _http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(12)
            };

            // Required identification + attribution spirit
            _http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppInfo.Name}/{AppInfo.Version} (Roblox Bootstrapper; +https://github.com)");
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        private T? GetFromCache<T>(string key)
        {
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(key, out var entry) && entry.Expiry > DateTime.UtcNow)
                    return (T?)entry.Data;
            }
            return default;
        }

        private void SetCache(string key, object? data, int seconds = 90)
        {
            lock (_cacheLock)
            {
                _cache[key] = (DateTime.UtcNow.AddSeconds(seconds), data);
            }
        }

        private static bool IsPrivateOrInvalidIp(string? ip)
        {
            if (string.IsNullOrWhiteSpace(ip) || !System.Net.IPAddress.TryParse(ip, out var addr))
                return true;
            if (System.Net.IPAddress.IsLoopback(addr))
                return true;

            var b = addr.GetAddressBytes();
            if (b.Length == 4)
            {
                return b[0] == 10
                    || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                    || (b[0] == 192 && b[1] == 168)
                    || (b[0] == 169 && b[1] == 254);
            }
            return addr.IsIPv6LinkLocal || addr.IsIPv6SiteLocal;
        }

        /// <summary>
        /// Resolve geographic location of a Roblox server IP.
        /// Endpoint used by Fishstrap: /v1/geolocation?ip=
        /// </summary>
        public async Task<string?> QueryServerLocationAsync(string ip)
        {
            if (IsPrivateOrInvalidIp(ip))
                return null;

            var cacheKey = $"geo:{ip}";
            var cached = GetFromCache<string>(cacheKey);
            if (cached != null) return cached;

            try
            {
                var url = $"https://apis.rovalra.com/v1/geolocation?ip={Uri.EscapeDataString(ip)}";
                var json = await _http.GetStringAsync(url);
                var data = JsonSerializer.Deserialize<RoValraGeolocation>(json, JsonOptions);

                var loc = data?.Location;
                if (loc == null) return null;

                string formatted;
                if (!string.IsNullOrEmpty(loc.City) &&
                    !string.IsNullOrEmpty(loc.Region) &&
                    loc.City != loc.Region)
                {
                    formatted = $"{loc.City}, {loc.Region}, {loc.Country}";
                }
                else if (!string.IsNullOrEmpty(loc.Region))
                {
                    formatted = $"{loc.Region}, {loc.Country}";
                }
                else
                {
                    formatted = loc.Country ?? "Unknown";
                }

                SetCache(cacheKey, formatted);
                App.Logger?.WriteLine("RoValra", $"Location for {ip}: {formatted}");
                return formatted;
            }
            catch (Exception ex)
            {
                App.Logger?.WriteException("RoValra.Geo", ex);
                return null;
            }
        }

        /// <summary>
        /// Fire-and-forget submit of server IDs so RoValra can track uptime/total servers.
        /// Helps the community dataset (no personal data).
        /// </summary>
        public void ProcessServers(long placeId, IEnumerable<string> serverIds)
        {
            if (!App.Settings.Prop.SubmitServersToRoValra) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    var body = new RoValraProcessServerBody
                    {
                        PlaceId = placeId,
                        ServerIds = new List<string>(serverIds)
                    };

                    var json = JsonSerializer.Serialize(body);
                    using var content = new StringContent(json, Encoding.UTF8, "application/json");
                    await _http.PostAsync("https://apis.rovalra.com/process_servers", content);
                    App.Logger?.WriteLine("RoValra", $"Submitted {body.ServerIds.Count} server(s) for place {placeId}");
                }
                catch (Exception ex)
                {
                    App.Logger?.WriteException("RoValra.Process", ex);
                }
            });
        }

        /// <summary>
        /// List known datacenters (used for better matchmaking / region preference).
        /// </summary>
        public async Task<List<RoValraDatacenter>?> GetDatacentersAsync()
        {
            const string cacheKey = "datacenters";
            var cached = GetFromCache<List<RoValraDatacenter>>(cacheKey);
            if (cached != null) return cached;

            try
            {
                var json = await _http.GetStringAsync("https://apis.rovalra.com/v1/datacenters/list");
                var list = JsonSerializer.Deserialize<List<RoValraDatacenter>>(json, JsonOptions);
                if (list != null) SetCache(cacheKey, list, 300);
                return list;
            }
            catch (Exception ex)
            {
                App.Logger?.WriteException("RoValra.Datacenters", ex);
                return null;
            }
        }

        /// <summary>
        /// Find servers in a specific region for a place (better matchmaking helper).
        /// </summary>
        public async Task<RoValraServersResponse?> GetServersByRegionAsync(long placeId, string region)
        {
            try
            {
                var url = $"https://apis.rovalra.com/v1/servers/region?place_id={placeId}&region={Uri.EscapeDataString(region)}";
                var json = await _http.GetStringAsync(url);
                return JsonSerializer.Deserialize<RoValraServersResponse>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                App.Logger?.WriteException("RoValra.RegionServers", ex);
                return null;
            }
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
