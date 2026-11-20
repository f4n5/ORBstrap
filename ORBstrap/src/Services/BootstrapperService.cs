using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Crypt3d.Services
{
    /// <summary>
    /// Core responsibility: locate Roblox, apply FastFlags / mods, launch with multi-instance support.
    /// This is the heart of any Bloxstrap/Fishstrap-style bootstrapper.
    /// </summary>
    public class BootstrapperService
    {
        // Mutex que Roblox usa para impedir una segunda instancia. Mientras ORBstrap lo tenga
        // tomado, se pueden abrir varias instancias. Debe vivir mientras ORBstrap esté abierto.
        private const string RobloxSingletonMutexName = "ROBLOX_singletonMutex";
        private static Mutex? _singletonMutex;

        private static readonly JsonSerializerOptions FlagsJsonOptions = new() { WriteIndented = true };

        /// <summary>Último proceso de Roblox lanzado por ORBstrap (para saber cuándo se cierra).</summary>
        public Process? LastProcess { get; private set; }

        public event Action<string>? StatusChanged;
        public event Action<string>? ErrorOccurred;

        private void SetStatus(string msg)
        {
            StatusChanged?.Invoke(msg);
            App.Logger?.WriteLine("Bootstrapper", msg);
        }

        /// <summary>
        /// Attempt to find the installed Roblox Player executable.
        /// </summary>
        public string? FindRobloxPlayer()
        {
            // 1) Ruta personalizada del usuario (si existe, manda)
            var custom = App.Settings.Prop.CustomRobloxPath;
            if (!string.IsNullOrEmpty(custom) && File.Exists(custom))
                return custom;

            // 2) Instalaciones habituales: por usuario y para todos los usuarios
            var roots = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Roblox", "Versions"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Roblox", "Versions"),
            };

            // La versión más reciente de todas las carpetas que contengan RobloxPlayerBeta.exe
            var candidates = roots
                .Where(Directory.Exists)
                .SelectMany(root => new DirectoryInfo(root).GetDirectories())
                .OrderByDescending(d => d.LastWriteTime)
                .Select(d => Path.Combine(d.FullName, "RobloxPlayerBeta.exe"))
                .Where(File.Exists);

            return candidates.FirstOrDefault();
        }

        public async Task<bool> LaunchAsync(string? placeId = null, string? jobId = null)
        {
            try
            {
                SetStatus("Locating Roblox...");
                var playerPath = FindRobloxPlayer();
                if (playerPath == null)
                {
                    ErrorOccurred?.Invoke("Could not find RobloxPlayerBeta.exe. Is Roblox installed?");
                    return false;
                }

                var versionFolder = Path.GetDirectoryName(playerPath)!;
                SetStatus($"Found: {Path.GetFileName(versionFolder)}");

                // Multi-instance (se hace antes de cualquier await para quedar en el hilo de la UI)
                UpdateMultiInstance(App.Settings.Prop.MultiInstance);

                // Apply FastFlags (write ClientAppSettings.json in the version folder)
                if (App.Settings.Prop.UseFastFlagEditor && App.Settings.Prop.FastFlags.Count > 0)
                {
                    SetStatus("Applying FastFlags...");
                    await ApplyFastFlagsAsync(versionFolder);
                }

                // Custom textures: aplicar (o devolver los originales si se desactivaron)
                if (App.Settings.Prop.EnableMods)
                {
                    SetStatus("Applying custom textures...");
                    var textures = await Task.Run(() => App.Textures.Apply(versionFolder));
                    if (textures > 0) SetStatus($"Applied {textures} custom texture file(s).");
                }
                else
                {
                    await Task.Run(() => App.Textures.RestoreAll());
                }

                SetStatus("Launching Roblox...");

                var args = BuildLaunchArguments(placeId, jobId);

                var psi = new ProcessStartInfo
                {
                    FileName = playerPath,
                    Arguments = args,
                    UseShellExecute = false,
                    WorkingDirectory = versionFolder
                };

                LastProcess = Process.Start(psi);
                SetStatus("Roblox launched successfully.");
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.WriteException("Bootstrapper", ex);
                ErrorOccurred?.Invoke(ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Toma (o suelta) el mutex singleton de Roblox para permitir varias instancias.
        /// Solo funciona para instancias lanzadas mientras ORBstrap sigue abierto, y no si
        /// ya hay un Roblox abierto que haya tomado el mutex antes.
        /// </summary>
        private void UpdateMultiInstance(bool enabled)
        {
            try
            {
                if (enabled)
                {
                    if (_singletonMutex != null) return;

                    var mutex = new Mutex(true, RobloxSingletonMutexName, out bool createdNew);
                    if (createdNew)
                    {
                        _singletonMutex = mutex;
                        SetStatus("Multi-instance enabled.");
                    }
                    else
                    {
                        mutex.Dispose();
                        SetStatus("Roblox is already running and owns the instance lock; " +
                                  "close it and launch from ORBstrap to use multi-instance.");
                    }
                }
                else if (_singletonMutex != null)
                {
                    _singletonMutex.ReleaseMutex();
                    _singletonMutex.Dispose();
                    _singletonMutex = null;
                }
            }
            catch (Exception ex)
            {
                // Nunca debe impedir el lanzamiento
                App.Logger?.WriteException("MultiInstance", ex);
            }
        }

        private string BuildLaunchArguments(string? placeId, string? jobId)
        {
            // Minimal launch; real bootstrapper also handles protocol (roblox-player:1+...) 
            // and channel selection.
            if (!string.IsNullOrEmpty(placeId) && !string.IsNullOrEmpty(jobId))
                return $"-placeId {placeId} -gameInfo {jobId}"; // simplified

            if (!string.IsNullOrEmpty(placeId))
                return $"-placeId {placeId}";

            return string.Empty;
        }

        private async Task ApplyFastFlagsAsync(string versionFolder)
        {
            // ClientAppSettings.json is the way Bloxstrap/Fishstrap inject FastFlags.
            var flagsPath = Path.Combine(versionFolder, "ClientSettings", "ClientAppSettings.json");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(flagsPath)!);
                var json = JsonSerializer.Serialize(App.Settings.Prop.FastFlags, FlagsJsonOptions);
                await File.WriteAllTextAsync(flagsPath, json);
                App.Logger?.WriteLine("Bootstrapper", $"Wrote {App.Settings.Prop.FastFlags.Count} FastFlag(s) to {flagsPath}");
            }
            catch (Exception ex)
            {
                // No bloquea el lanzamiento, pero avisa
                App.Logger?.WriteException("FastFlags", ex);
                SetStatus("Could not write FastFlags (see logs). Launching anyway...");
            }
        }
    }
}
