using System.Collections.Generic;

namespace Crypt3d.Models
{
    public class AppSettings
    {
        // Bootstrapper behaviour
        public bool MultiInstance { get; set; } = true;
        public bool ConfirmCloseOnMultiInstance { get; set; } = true;
        public bool BackgroundUpdates { get; set; } = true;
        public bool DontExitToDesktopApp { get; set; } = false;

        // FastFlags
        public Dictionary<string, object> FastFlags { get; set; } = new();
        public bool UseFastFlagEditor { get; set; } = true;

        // Mods
        public bool EnableMods { get; set; } = true;
        public string ModsFolder { get; set; } = "Mods";

        // Activity / Integrations
        public bool ActivityTracking { get; set; } = true;
        // "Show in Discord" (vía el bot ORB). Desactivado por defecto: es opt-in.
        public bool DiscordRichPresence { get; set; } = false;
        public bool ShowServerLocation { get; set; } = true;
        public bool ShowServerUptime { get; set; } = true;
        public bool DiscordShowAccount { get; set; } = false;
        public bool DiscordAllowJoin { get; set; } = false;
        // Además del nombre, enviar al bot el juego en el que estás. Opt-in (privacidad).
        public bool DiscordShowGame { get; set; } = false;

        // Games: mostrar el juego actual / recientes y "Join best server"
        public bool ShowGames { get; set; } = true;
        public string BestServerMode { get; set; } = "ping"; // ping | players | balanced
        public List<RecentGame> RecentGames { get; set; } = new();

        // RoValra
        public bool UseRoValraServerInfo { get; set; } = true;
        public bool SubmitServersToRoValra { get; set; } = true; // helps improve the API

        // Appearance
        public string Theme { get; set; } = "Dark";
        public string BootstrapperStyle { get; set; } = "Fluent";

        // Cleaner
        public bool EnableCleaner { get; set; } = true;
        public int CleanerDaysThreshold { get; set; } = 7;
        public bool CleanRobloxCache { get; set; } = true;
        public bool CleanRobloxLogs { get; set; } = true;
        public bool CleanCryptLogs { get; set; } = false;

        // Channel / Domain
        public string RobloxChannel { get; set; } = "LIVE";
        public string? CustomRobloxDomain { get; set; }

        // Paths (resolved at runtime)
        public string? CustomRobloxPath { get; set; }
    }
}
