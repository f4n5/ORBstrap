using System;
using System.Text.Json.Serialization;

namespace Crypt3d.Models
{
    /// <summary>Datos públicos de un juego de Roblox.</summary>
    public class GameInfo
    {
        public long PlaceId { get; set; }
        public long UniverseId { get; set; }
        public string Name { get; set; } = "Unknown game";
        public long Playing { get; set; }
        public int MaxPlayers { get; set; }
        public string? IconUrl { get; set; }
    }

    /// <summary>Un servidor público de un juego (según la API de Roblox).</summary>
    public class ServerInfo
    {
        public string Id { get; set; } = "";
        public int Playing { get; set; }
        public int MaxPlayers { get; set; }
        /// <summary>Ping estimado por Roblox. Puede faltar.</summary>
        public int? Ping { get; set; }
    }

    /// <summary>Criterio de "Join best server".</summary>
    public enum ServerPickMode
    {
        Ping,
        Players,
        Balanced
    }

    /// <summary>Juego del historial reciente (se guarda en Settings.json).</summary>
    public class RecentGame
    {
        public long PlaceId { get; set; }
        public string Name { get; set; } = "";
        public DateTime LastPlayedUtc { get; set; }

        [JsonIgnore]
        public string Subtitle => $"Place {PlaceId} · {LastPlayedUtc.ToLocalTime():g}";
    }
}
