using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Crypt3d.Models;

namespace Crypt3d.Services
{
    /// <summary>
    /// Cliente de las APIs públicas de Roblox (no necesitan cookie ni sesión de Roblox):
    /// información del juego y lista de servidores públicos.
    /// </summary>
    public class RobloxApiService : IDisposable
    {
        private readonly HttpClient _http;
        private readonly Dictionary<string, (DateTime Expiry, GameInfo? Data)> _gameCache = new();
        private readonly object _cacheLock = new();

        public RobloxApiService()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppInfo.Name}/{AppInfo.Version}");
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        /// <summary>Nombre, jugadores activos e icono de un juego a partir de su placeId.</summary>
        public async Task<GameInfo?> GetGameInfoAsync(long placeId, CancellationToken ct = default)
        {
            var key = "game:" + placeId;
            lock (_cacheLock)
            {
                if (_gameCache.TryGetValue(key, out var hit) && hit.Expiry > DateTime.UtcNow)
                    return hit.Data;
            }

            try
            {
                // placeId → universeId
                var uJson = await _http.GetStringAsync($"https://apis.roblox.com/universes/v1/places/{placeId}/universe", ct);
                using var uDoc = JsonDocument.Parse(uJson);
                if (!uDoc.RootElement.TryGetProperty("universeId", out var uid) || uid.ValueKind != JsonValueKind.Number)
                    return null;
                var universeId = uid.GetInt64();

                // universeId → datos del juego
                var gJson = await _http.GetStringAsync($"https://games.roblox.com/v1/games?universeIds={universeId}", ct);
                using var gDoc = JsonDocument.Parse(gJson);
                if (!gDoc.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
                    return null;
                var g = data[0];

                var info = new GameInfo
                {
                    PlaceId = placeId,
                    UniverseId = universeId,
                    Name = g.TryGetProperty("name", out var n) ? n.GetString() ?? "Unknown game" : "Unknown game",
                    Playing = g.TryGetProperty("playing", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt64() : 0,
                    MaxPlayers = g.TryGetProperty("maxPlayers", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetInt32() : 0
                };

                // El icono es opcional: si falla, el juego se muestra igual
                try
                {
                    var iJson = await _http.GetStringAsync(
                        $"https://thumbnails.roblox.com/v1/games/icons?universeIds={universeId}&returnPolicy=PlaceHolder&size=150x150&format=Png&isCircular=false", ct);
                    using var iDoc = JsonDocument.Parse(iJson);
                    if (iDoc.RootElement.TryGetProperty("data", out var iData) && iData.GetArrayLength() > 0 &&
                        iData[0].TryGetProperty("imageUrl", out var url))
                        info.IconUrl = url.GetString();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    App.Logger?.WriteLine("RobloxApi", "Icon lookup failed: " + ex.Message);
                }

                lock (_cacheLock) _gameCache[key] = (DateTime.UtcNow.AddSeconds(60), info);
                return info;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                App.Logger?.WriteLine("RobloxApi", $"Game info for {placeId} failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Servidores públicos con hueco libre (hasta <paramref name="maxPages"/> páginas de 100).
        /// Devuelve null si Roblox no respondió (por ejemplo, límite de peticiones).
        /// </summary>
        public async Task<List<ServerInfo>?> GetPublicServersAsync(long placeId, int maxPages = 3, CancellationToken ct = default)
        {
            var result = new List<ServerInfo>();
            string? cursor = null;

            try
            {
                for (var page = 0; page < maxPages; page++)
                {
                    var url = $"https://games.roblox.com/v1/games/{placeId}/servers/Public?sortOrder=Desc&excludeFullGames=true&limit=100";
                    if (!string.IsNullOrEmpty(cursor)) url += "&cursor=" + Uri.EscapeDataString(cursor);

                    using var res = await _http.GetAsync(url, ct);
                    if (!res.IsSuccessStatusCode)
                    {
                        App.Logger?.WriteLine("RobloxApi",
                            $"Servers for {placeId}: HTTP {(int)res.StatusCode}{(res.StatusCode == HttpStatusCode.TooManyRequests ? " (rate limited)" : "")}");
                        return result.Count > 0 ? result : null; // con una página ya cargada, se puede elegir igualmente
                    }

                    using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
                    if (doc.RootElement.TryGetProperty("data", out var data))
                    {
                        foreach (var s in data.EnumerateArray())
                        {
                            var id = s.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                            if (string.IsNullOrEmpty(id)) continue;

                            result.Add(new ServerInfo
                            {
                                Id = id,
                                Playing = s.TryGetProperty("playing", out var pl) && pl.ValueKind == JsonValueKind.Number ? pl.GetInt32() : 0,
                                MaxPlayers = s.TryGetProperty("maxPlayers", out var mx) && mx.ValueKind == JsonValueKind.Number ? mx.GetInt32() : 0,
                                Ping = s.TryGetProperty("ping", out var pg) && pg.ValueKind == JsonValueKind.Number
                                    ? (int)Math.Round(pg.GetDouble()) : null
                            });
                        }
                    }

                    cursor = doc.RootElement.TryGetProperty("nextPageCursor", out var next) && next.ValueKind == JsonValueKind.String
                        ? next.GetString() : null;
                    if (string.IsNullOrEmpty(cursor)) break;

                    await Task.Delay(400, ct); // sin machacar la API
                }
                return result;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                App.Logger?.WriteLine("RobloxApi", $"Servers for {placeId} failed: {ex.Message}");
                return result.Count > 0 ? result : null;
            }
        }

        /// <summary>Elige el mejor servidor según el criterio. null si no hay ninguno con hueco.</summary>
        public static ServerInfo? PickBest(IEnumerable<ServerInfo> servers, ServerPickMode mode)
        {
            // Nunca servidores llenos; y si se puede, con 2+ huecos libres para que no se llene mientras se entra
            var open = servers.Where(s => s.MaxPlayers > 0 && s.Playing < s.MaxPlayers).ToList();
            if (open.Count == 0) return null;
            var roomy = open.Where(s => s.MaxPlayers - s.Playing >= 2).ToList();
            if (roomy.Count > 0) open = roomy;

            switch (mode)
            {
                case ServerPickMode.Ping:
                    // Menor ping; a igualdad, el que tenga más gente. Sin datos de ping, el más lleno.
                    return open.Where(s => s.Ping.HasValue)
                               .OrderBy(s => s.Ping!.Value).ThenByDescending(s => s.Playing)
                               .FirstOrDefault()
                           ?? open.OrderByDescending(s => s.Playing).First();

                case ServerPickMode.Players:
                    return open.OrderByDescending(s => s.Playing)
                               .ThenBy(s => s.Ping ?? int.MaxValue)
                               .First();

                default: // Balanced: penaliza el ping alto y también los servidores vacíos
                    return open.OrderBy(s => (s.Ping ?? 250) + (1.0 - (double)s.Playing / s.MaxPlayers) * 200.0).First();
            }
        }

        public void Dispose() => _http.Dispose();
    }
}
