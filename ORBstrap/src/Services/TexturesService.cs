using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace Crypt3d.Services
{
    /// <summary>
    /// Texturas personalizadas: copia los archivos de %LocalAppData%\ORBstrap\Mods\content\...
    /// sobre la carpeta de la versión de Roblox (guardando una copia de los originales para poder restaurarlos).
    /// Solo se tocan archivos dentro de "content" y nunca ejecutables.
    /// </summary>
    public class TexturesService
    {
        public class Entry
        {
            public string Rel { get; set; } = "";
            public bool HadOriginal { get; set; }
        }

        private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".jar", ".scr", ".msi",
            ".lnk", ".com", ".sys", ".cpl", ".hta", ".reg", ".pif"
        };

        private const long MaxPackBytes = 2L * 1024 * 1024 * 1024; // 2 GB descomprimido
        private const int MaxPackFiles = 20000;

        public string ModsRoot => Path.Combine(AppInfo.DataDir, "Mods");
        private static string BackupRoot => Path.Combine(AppInfo.DataDir, "ModBackups");
        private static string ManifestPath => Path.Combine(AppInfo.DataDir, "mods-applied.json");

        // ---------------- manifiesto ----------------
        private static Dictionary<string, List<Entry>> LoadManifest()
        {
            try
            {
                if (File.Exists(ManifestPath))
                {
                    var m = JsonSerializer.Deserialize<Dictionary<string, List<Entry>>>(File.ReadAllText(ManifestPath));
                    if (m != null) return m;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.WriteException("Textures", ex);
            }
            return new Dictionary<string, List<Entry>>();
        }

        private static void SaveManifest(Dictionary<string, List<Entry>> manifest)
        {
            try
            {
                Directory.CreateDirectory(AppInfo.DataDir);
                File.WriteAllText(ManifestPath, JsonSerializer.Serialize(manifest));
            }
            catch (Exception ex)
            {
                App.Logger?.WriteException("Textures", ex);
            }
        }

        // ---------------- reglas ----------------
        private static bool IsAllowedRel(string rel)
        {
            var parts = rel.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return false;
            if (!parts[0].Equals("content", StringComparison.OrdinalIgnoreCase)) return false;
            if (parts.Any(p => p == "..")) return false;
            return !BlockedExtensions.Contains(Path.GetExtension(rel));
        }

        private static string BackupPath(string versionKey, string rel)
        {
            var versionName = Path.GetFileName(versionKey.TrimEnd(Path.DirectorySeparatorChar));
            return Path.Combine(BackupRoot, versionName, rel);
        }

        private static bool SameFile(string source, string target)
        {
            var a = new FileInfo(source);
            var b = new FileInfo(target);
            return b.Exists && a.Length == b.Length && a.LastWriteTimeUtc == b.LastWriteTimeUtc;
        }

        /// <summary>Cantidad de archivos válidos en la carpeta de texturas.</summary>
        public int CountFiles()
        {
            try
            {
                if (!Directory.Exists(ModsRoot)) return 0;
                return Directory.EnumerateFiles(ModsRoot, "*", SearchOption.AllDirectories)
                    .Count(f => IsAllowedRel(Path.GetRelativePath(ModsRoot, f)));
            }
            catch
            {
                return 0;
            }
        }

        // ---------------- aplicar / restaurar ----------------
        /// <summary>Aplica las texturas a la carpeta de versión de Roblox. Devuelve cuántos archivos están aplicados.</summary>
        public int Apply(string versionFolder)
        {
            var manifest = LoadManifest();
            var key = Path.GetFullPath(versionFolder).TrimEnd(Path.DirectorySeparatorChar);
            var baseDir = key + Path.DirectorySeparatorChar;

            if (!manifest.TryGetValue(key, out var applied) || applied == null)
                applied = new List<Entry>();

            // Qué archivos hay ahora en la carpeta de texturas
            var wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(ModsRoot))
            {
                foreach (var file in Directory.EnumerateFiles(ModsRoot, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(ModsRoot, file);
                    if (IsAllowedRel(rel)) wanted[rel] = file;
                }
            }

            // 1) Restaurar lo que ya no está en la carpeta de texturas
            foreach (var old in applied.Where(a => !wanted.ContainsKey(a.Rel)).ToList())
            {
                RestoreOne(key, old);
                applied.Remove(old);
            }

            // 2) Aplicar lo que toca
            int count = 0;
            foreach (var pair in wanted)
            {
                var rel = pair.Key;
                var source = pair.Value;
                try
                {
                    var target = Path.GetFullPath(Path.Combine(key, rel));
                    if (!target.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase)) continue; // seguridad: solo dentro de la versión

                    var existing = applied.FirstOrDefault(a => string.Equals(a.Rel, rel, StringComparison.OrdinalIgnoreCase));
                    if (existing == null)
                    {
                        var hadOriginal = File.Exists(target);
                        if (hadOriginal)
                        {
                            var backup = BackupPath(key, rel);
                            if (!File.Exists(backup))
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                                File.Copy(target, backup, false);
                            }
                        }
                        applied.Add(new Entry { Rel = rel, HadOriginal = hadOriginal });
                    }

                    if (!SameFile(source, target))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(source, target, true);
                    }
                    count++;
                }
                catch (Exception ex)
                {
                    App.Logger?.WriteLine("Textures", $"Could not apply {rel}: {ex.Message}");
                }
            }

            manifest[key] = applied;
            SaveManifest(manifest);
            return count;
        }

        private static bool RestoreOne(string versionKey, Entry entry)
        {
            try
            {
                var target = Path.GetFullPath(Path.Combine(versionKey, entry.Rel));
                if (!target.StartsWith(versionKey + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return false;

                var backup = BackupPath(versionKey, entry.Rel);
                if (entry.HadOriginal && File.Exists(backup))
                {
                    if (Directory.Exists(Path.GetDirectoryName(target)!)) File.Copy(backup, target, true);
                }
                else if (!entry.HadOriginal && File.Exists(target))
                {
                    File.Delete(target);
                }
                if (File.Exists(backup)) File.Delete(backup);
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.WriteLine("Textures", $"Could not restore {entry.Rel}: {ex.Message}");
                return false;
            }
        }

        /// <summary>Devuelve todos los archivos de Roblox a su estado original.</summary>
        public int RestoreAll()
        {
            var manifest = LoadManifest();
            int restored = 0;
            foreach (var kv in manifest)
            {
                if (!Directory.Exists(kv.Key)) continue; // esa versión de Roblox ya no existe
                foreach (var entry in kv.Value)
                    if (RestoreOne(kv.Key, entry)) restored++;
            }

            SaveManifest(new Dictionary<string, List<Entry>>());
            try { if (Directory.Exists(BackupRoot)) Directory.Delete(BackupRoot, true); }
            catch { /* ignorar */ }
            return restored;
        }

        /// <summary>Borra los packs importados (llamar después de RestoreAll).</summary>
        public void ClearImported()
        {
            try { if (Directory.Exists(ModsRoot)) Directory.Delete(ModsRoot, true); }
            catch (Exception ex) { App.Logger?.WriteException("Textures", ex); }
            Directory.CreateDirectory(ModsRoot);
        }

        // ---------------- importar un pack .zip ----------------
        /// <summary>
        /// Extrae un .zip a la carpeta de texturas. Acepta packs con o sin carpeta contenedora:
        /// busca la carpeta "content" dentro de cada ruta. Ignora todo lo demás.
        /// </summary>
        public (int Imported, int Skipped) ImportZip(string zipPath)
        {
            Directory.CreateDirectory(ModsRoot);
            var rootFull = Path.GetFullPath(ModsRoot).TrimEnd(Path.DirectorySeparatorChar);
            var rootPrefix = rootFull + Path.DirectorySeparatorChar;

            int imported = 0, skipped = 0;
            long total = 0;

            using var zip = ZipFile.OpenRead(zipPath);
            if (zip.Entries.Count > MaxPackFiles)
                throw new InvalidDataException("The pack has too many files.");

            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue; // es una carpeta

                var parts = entry.FullName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                var idx = Array.FindIndex(parts, p => p.Equals("content", StringComparison.OrdinalIgnoreCase));
                if (idx < 0 || idx == parts.Length - 1) { skipped++; continue; }

                var relParts = parts.Skip(idx).ToArray();
                if (relParts.Any(p => p == "..")) { skipped++; continue; }

                var rel = Path.Combine(relParts);
                if (!IsAllowedRel(rel)) { skipped++; continue; }

                total += entry.Length;
                if (total > MaxPackBytes)
                    throw new InvalidDataException("The pack is too large.");

                var dest = Path.GetFullPath(Path.Combine(rootFull, rel));
                if (!dest.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }

                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, true);
                imported++;
            }
            return (imported, skipped);
        }
    }
}
