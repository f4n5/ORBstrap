using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Crypt3d.Services
{
    public class OrbUser
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("avatarUrl")] public string? AvatarUrl { get; set; }
    }

    /// <summary>Sesión iniciada con Discord (la verifica el bot ORB).</summary>
    public class OrbSession
    {
        public string Token { get; set; } = "";
        public OrbUser User { get; set; } = new();
    }

    /// <summary>Respuesta genérica del backend (los campos que no aplican quedan vacíos).</summary>
    public class OrbReply
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("reason")] public string? Reason { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("token")] public string? Token { get; set; }
        [JsonPropertyName("user")] public OrbUser? User { get; set; }
        [JsonPropertyName("code")] public string? Code { get; set; }
        [JsonPropertyName("secret")] public string? Secret { get; set; }
        [JsonPropertyName("loginUrl")] public string? LoginUrl { get; set; }
        [JsonPropertyName("inviteUrl")] public string? InviteUrl { get; set; }
        [JsonPropertyName("showEnabled")] public bool ShowEnabled { get; set; }
        [JsonPropertyName("expiresIn")] public int ExpiresIn { get; set; }
    }

    /// <summary>
    /// Cliente del backend https://orb-official-bot.onrender.com
    /// Reason == "network" significa que no se pudo contactar con el servidor.
    /// </summary>
    public class OrbApiService : IDisposable
    {
        private readonly HttpClient _http;
        private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

        public OrbApiService()
        {
            // Render (plan gratis) duerme el servicio: la primera petición puede tardar ~1 min
            _http = new HttpClient
            {
                BaseAddress = new Uri(AppInfo.BackendUrl),
                Timeout = TimeSpan.FromSeconds(75)
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppInfo.Name}/{AppInfo.Version}");
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        private async Task<OrbReply> SendAsync(HttpMethod method, string path, string? token, object? body, CancellationToken ct)
        {
            try
            {
                using var req = new HttpRequestMessage(method, path);
                if (!string.IsNullOrEmpty(token))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                if (body != null)
                    req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

                using var res = await _http.SendAsync(req, ct);
                var text = await res.Content.ReadAsStringAsync(ct);

                OrbReply? reply = null;
                try { reply = JsonSerializer.Deserialize<OrbReply>(text, JsonOpts); }
                catch (JsonException) { /* respuesta que no es JSON (p. ej. página de error de Render) */ }
                reply ??= new OrbReply();

                if (!res.IsSuccessStatusCode)
                {
                    reply.Ok = false;
                    if (string.IsNullOrEmpty(reply.Reason))
                        reply.Reason = res.StatusCode == HttpStatusCode.TooManyRequests ? "rate_limited" : "http_" + (int)res.StatusCode;
                    if (string.IsNullOrEmpty(reply.Error))
                        reply.Error = "The ORB server answered with an error (" + (int)res.StatusCode + "). Try again in a minute.";
                }
                return reply;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // cancelado por la app (no es un fallo de red)
            }
            catch (Exception ex)
            {
                App.Logger?.WriteLine("OrbApi", $"{method} {path} failed: {ex.Message}");
                return new OrbReply
                {
                    Ok = false,
                    Reason = "network",
                    Error = "Cannot reach the ORB server. Check your internet connection and try again."
                };
            }
        }

        public Task<OrbReply> GetConfigAsync(CancellationToken ct = default)
            => SendAsync(HttpMethod.Get, "/api/app/config", null, null, ct);

        public Task<OrbReply> StartLoginAsync(CancellationToken ct = default)
            => SendAsync(HttpMethod.Post, "/api/app/login/start", null, new { }, ct);

        public Task<OrbReply> PollLoginAsync(string code, string secret, CancellationToken ct = default)
            => SendAsync(HttpMethod.Get,
                $"/api/app/login/poll?code={Uri.EscapeDataString(code)}&secret={Uri.EscapeDataString(secret)}",
                null, null, ct);

        public Task<OrbReply> GetMeAsync(string token, CancellationToken ct = default)
            => SendAsync(HttpMethod.Get, "/api/app/me", token, null, ct);

        public Task<OrbReply> ShowAsync(string token, bool start, bool showName, CancellationToken ct = default)
            => SendAsync(HttpMethod.Post, "/api/app/show", token,
                new { action = start ? "start" : "stop", showName }, ct);

        /// <summary>
        /// Actualiza el mensaje de "Show in Discord" con el juego actual (gameName/placeId a null = sin juego).
        /// Requiere que el bot acepte action "update" en /api/app/show.
        /// </summary>
        public Task<OrbReply> ShowGameAsync(string token, bool showName, string? gameName, long? placeId, CancellationToken ct = default)
            => SendAsync(HttpMethod.Post, "/api/app/show", token,
                new { action = "update", showName, gameName, placeId }, ct);

        public void Dispose() => _http.Dispose();
    }

    /// <summary>Guarda la sesión en %LocalAppData%\ORBstrap\session.json.</summary>
    public static class SessionStore
    {
        private static string FilePath => Path.Combine(AppInfo.DataDir, "session.json");

        public static OrbSession? Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var s = JsonSerializer.Deserialize<OrbSession>(File.ReadAllText(FilePath));
                return s == null || string.IsNullOrEmpty(s.Token) ? null : s;
            }
            catch
            {
                return null;
            }
        }

        public static void Save(OrbSession session)
        {
            try
            {
                Directory.CreateDirectory(AppInfo.DataDir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(session));
            }
            catch (Exception ex)
            {
                App.Logger?.WriteException("Session", ex);
            }
        }

        public static void Clear()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch { /* ignorar */ }
        }
    }
}
