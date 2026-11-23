using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Crypt3d.Services
{
    /// <summary>
    /// Lee el log de Roblox mientras se juega para saber en qué juego estás.
    /// Usa los mismos marcadores que Bloxstrap/Fishstrap; si Roblox cambia el formato del log,
    /// solo hay que ajustar las constantes de abajo.
    /// </summary>
    public sealed class ActivityWatcher
    {
        // [FLog::Output] ! Joining game '<jobId>' place <placeId> at <ip>
        private static readonly Regex JoiningRx = new(
            @"! Joining game '(?<job>[0-9a-fA-F\-]{36})' place (?<place>\d+) at ",
            RegexOptions.Compiled);

        private const string DisconnectMarker = "Time to disconnect replication data";
        private const string LeaveMarker = "leaveUGCGameInternal";

        /// <summary>(placeId, jobId) al entrar a un servidor.</summary>
        public event Action<long, string>? GameJoined;
        public event Action? GameLeft;

        private CancellationTokenSource? _cts;
        private string? _lastJobId;

        public static string LogsDir { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "logs");

        /// <summary>Empieza a vigilar el log de Player creado a partir de <paramref name="launchedAtUtc"/>.</summary>
        public void Start(DateTime launchedAtUtc)
        {
            Stop();
            _lastJobId = null;
            var cts = new CancellationTokenSource();
            _cts = cts;
            _ = Task.Run(() => RunAsync(launchedAtUtc, cts.Token));
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { /* ya cancelado */ }
            _cts = null;
        }

        private static string? FindLog(DateTime launchedAtUtc)
        {
            try
            {
                if (!Directory.Exists(LogsDir)) return null;
                return new DirectoryInfo(LogsDir).EnumerateFiles("*.log")
                    .Where(f => f.Name.Contains("Player", StringComparison.OrdinalIgnoreCase)
                                && f.LastWriteTimeUtc >= launchedAtUtc)
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Select(f => f.FullName)
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private async Task RunAsync(DateTime launchedAtUtc, CancellationToken ct)
        {
            try
            {
                // El log aparece unos segundos después de abrir Roblox
                string? path = null;
                var deadline = DateTime.UtcNow.AddSeconds(90);
                while (path == null && DateTime.UtcNow < deadline)
                {
                    path = FindLog(launchedAtUtc);
                    if (path == null) await Task.Delay(1000, ct);
                }
                if (path == null)
                {
                    App.Logger?.WriteLine("Activity", "No Roblox log found; game detection is off for this session.");
                    return;
                }

                App.Logger?.WriteLine("Activity", "Watching " + Path.GetFileName(path));
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs, Encoding.UTF8);

                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line == null)
                    {
                        await Task.Delay(500, ct);
                        continue;
                    }
                    HandleLine(line);
                }
            }
            catch (OperationCanceledException)
            {
                // parado a propósito
            }
            catch (Exception ex)
            {
                App.Logger?.WriteException("Activity", ex);
            }
        }

        private void HandleLine(string line)
        {
            var m = JoiningRx.Match(line);
            if (m.Success)
            {
                var job = m.Groups["job"].Value;
                if (job == _lastJobId) return; // el log puede repetir la línea
                if (!long.TryParse(m.Groups["place"].Value, out var place)) return;

                _lastJobId = job;
                GameJoined?.Invoke(place, job);
                return;
            }

            if (line.Contains(DisconnectMarker) || line.Contains(LeaveMarker))
            {
                if (_lastJobId == null) return;
                _lastJobId = null;
                GameLeft?.Invoke();
            }
        }
    }
}
