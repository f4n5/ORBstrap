using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Crypt3d.Services
{
    public class InstallOptions
    {
        public string InstallDir { get; set; } = AppInfo.DefaultInstallDir;
        public bool DesktopShortcut { get; set; } = true;
        public bool StartMenuShortcut { get; set; } = true;
    }

    /// <summary>
    /// Instalación por usuario (sin administrador): copia el .exe, crea accesos directos y registra
    /// ORBstrap en "Aplicaciones instaladas" de Windows. La desinstalación se hace desde la propia app.
    /// </summary>
    public static class InstallerService
    {
        public static string CurrentExe => Environment.ProcessPath ?? "";

        public static string DesktopLnk =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppInfo.Name + ".lnk");

        public static string StartMenuLnk =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppInfo.Name + ".lnk");

        /// <summary>Carpeta donde está instalado ORBstrap (según el registro), o null si no está instalado.</summary>
        public static string? GetInstalledDir()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(AppInfo.UninstallKeyPath);
                var dir = key?.GetValue("InstallLocation") as string;
                return string.IsNullOrWhiteSpace(dir) ? null : dir;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>¿Este .exe es el instalado (y no una copia descargada)?</summary>
        public static bool IsRunningFromInstall()
        {
            var installed = GetInstalledDir();
            var here = Path.GetDirectoryName(CurrentExe);
            if (installed == null || here == null) return false;
            return string.Equals(
                Path.GetFullPath(here).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(installed).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Siempre instala dentro de una carpeta llamada "ORBstrap": así la desinstalación
        /// solo borra esa carpeta y nunca archivos ajenos (p. ej. si eligen "Documentos").
        /// </summary>
        public static string NormalizeInstallDir(string chosen)
        {
            var full = Path.GetFullPath(chosen.Trim().Trim('"'));
            var name = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar));
            return string.Equals(name, AppInfo.Name, StringComparison.OrdinalIgnoreCase)
                ? full
                : Path.Combine(full, AppInfo.Name);
        }

        /// <summary>Copia este .exe a la carpeta de instalación. Devuelve la ruta del .exe instalado.</summary>
        public static string CopySelf(string installDir)
        {
            Directory.CreateDirectory(installDir);
            var target = Path.Combine(installDir, AppInfo.ExeName);

            if (!string.Equals(Path.GetFullPath(CurrentExe), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                File.Copy(CurrentExe, target, true); // falla si ORBstrap instalado está abierto

            return target;
        }

        /// <summary>Lo registra en Windows (Configuración → Aplicaciones) con su desinstalador.</summary>
        public static void RegisterInstall(string installDir, string exePath)
        {
            using var key = Registry.CurrentUser.CreateSubKey(AppInfo.UninstallKeyPath);
            if (key == null) return;
            key.SetValue("DisplayName", AppInfo.Name);
            key.SetValue("DisplayVersion", AppInfo.Version);
            key.SetValue("Publisher", AppInfo.Publisher);
            key.SetValue("InstallLocation", installDir);
            key.SetValue("DisplayIcon", exePath);
            key.SetValue("UninstallString", "\"" + exePath + "\" --uninstall");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }

        public static void CreateShortcut(string lnkPath, string targetExe)
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return;

            var shell = Activator.CreateInstance(type);
            if (shell == null) return;
            try
            {
                dynamic sh = shell;
                dynamic shortcut = sh.CreateShortcut(lnkPath);
                shortcut.TargetPath = targetExe;
                shortcut.WorkingDirectory = Path.GetDirectoryName(targetExe);
                shortcut.Description = AppInfo.Name;
                shortcut.IconLocation = targetExe + ",0";
                shortcut.Save();
            }
            finally
            {
                try { Marshal.ReleaseComObject(shell); } catch { /* ignorar */ }
            }
        }

        public static void LaunchInstalled(string exePath)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = Path.GetDirectoryName(exePath),
                UseShellExecute = true
            });
        }

        // ---------------- desinstalar ----------------
        public static void Uninstall(bool deleteUserData)
        {
            var installDir = GetInstalledDir() ?? Path.GetDirectoryName(CurrentExe);

            // 1) Devolver los archivos de Roblox a su estado original (texturas)
            try { App.Textures?.RestoreAll(); }
            catch (Exception ex) { App.Logger?.WriteException("Uninstall", ex); }

            // 2) Accesos directos
            TryDeleteFile(DesktopLnk);
            TryDeleteFile(StartMenuLnk);

            // 3) Registro de Windows
            try { Registry.CurrentUser.DeleteSubKeyTree(AppInfo.UninstallKeyPath, false); }
            catch (Exception ex) { App.Logger?.WriteException("Uninstall", ex); }

            // 4) Borrar archivos cuando la app se cierre (el .exe en uso no se puede borrar ahora)
            var toDelete = new List<string>();
            if (installDir != null) toDelete.Add(installDir);
            if (deleteUserData) toDelete.Add(AppInfo.DataDir);
            ScheduleDelete(toDelete);
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* ignorar */ }
        }

        /// <summary>Solo se borran carpetas llamadas exactamente "ORBstrap".</summary>
        private static bool SafeToDelete(string dir)
        {
            try
            {
                var full = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar);
                return full.Length > 10
                    && Directory.Exists(full)
                    && string.Equals(Path.GetFileName(full), AppInfo.Name, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void ScheduleDelete(List<string> dirs)
        {
            var commands = new List<string>();
            foreach (var dir in dirs)
            {
                if (SafeToDelete(dir))
                    commands.Add("rmdir /s /q \"" + Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + "\"");
            }
            if (commands.Count == 0) return;

            // Espera unos segundos a que ORBstrap se cierre y entonces borra
            var line = "ping 127.0.0.1 -n 4 >nul & " + string.Join(" & ", commands);
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c " + line,
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
    }
}
