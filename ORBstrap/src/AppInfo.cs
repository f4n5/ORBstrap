using System;
using System.IO;

namespace Crypt3d
{
    /// <summary>Constantes de la aplicación (nombre, backend, carpetas).</summary>
    public static class AppInfo
    {
        public const string Name = "ORBstrap";
        public const string Version = "1.1.0";
        public const string Publisher = "ORB";
        public const string ExeName = "ORBstrap.exe";

        /// <summary>Backend / bot oficial (login con Discord y "Show in Discord").</summary>
        public const string BackendUrl = "https://orb-official-bot.onrender.com";

        public const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ORBstrap";

        /// <summary>Ajustes, logs, sesión y texturas del usuario.</summary>
        public static string DataDir { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name);

        /// <summary>Instalación por usuario (no necesita permisos de administrador).</summary>
        public static string DefaultInstallDir { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Name);
    }
}
