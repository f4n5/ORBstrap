using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Crypt3d.Models;
using Crypt3d.Services;
using Crypt3d.Utilities;

namespace Crypt3d.UI.Views
{
    public partial class MainWindow : Window
    {
        private readonly BootstrapperService _bootstrapper = new();

        // Lectura tolerante (comentarios y comas finales) y escritura legible para los FastFlags
        private static readonly JsonSerializerOptions FlagsReadOptions = new()
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        private static readonly JsonSerializerOptions FlagsWriteOptions = new() { WriteIndented = true };

        private bool _showActive; // "Show in Discord" publicado actualmente

        // Juego actual (detectado por el log de Roblox)
        private ActivityWatcher? _watcher;
        private long _currentPlaceId;
        private string? _currentGameName;

        public MainWindow()
        {
            InitializeComponent();

            _bootstrapper.StatusChanged += msg =>
                Dispatcher.Invoke(() => StatusText.Text = msg);

            _bootstrapper.ErrorOccurred += msg =>
                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = "Error: " + msg;
                    MessageBox.Show(msg, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                });

            LoadSettingsIntoUi();
            EnsureModsFolder();
            ApplySession();
            RefreshRecentGamesUi();
            _ = RefreshProfileAsync();
        }

        /// <summary>Muestra «¡Bienvenido, usuario!» y los datos de la cuenta verificada por el bot.</summary>
        private void ApplySession()
        {
            var user = App.Session?.User;
            var name = string.IsNullOrWhiteSpace(user?.Name) ? "usuario" : user!.Name;
            TxtWelcome.Text = $"¡Bienvenido, {name}!";
            TxtUserName.Text = name;
            TxtProfileName.Text = name;
            TxtProfileId.Text = string.IsNullOrEmpty(user?.Id) ? "" : "Discord ID: " + user!.Id;

            SetAvatar(AvatarEllipse, user?.AvatarUrl);
            SetAvatar(ProfileAvatar, user?.AvatarUrl);
            UpdateDiscordPreview();

            BtnUninstall.Visibility = InstallerService.IsRunningFromInstall() ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void SetAvatar(System.Windows.Shapes.Ellipse target, string? url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try
            {
                var bitmap = new BitmapImage(new Uri(url));
                target.Fill = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
            }
            catch
            {
                // Sin avatar: se queda el círculo de color
            }
        }

        /// <summary>Vuelve a pedir el perfil al bot para tener nombre y avatar al día. Si falla, se queda lo guardado.</summary>
        private async Task RefreshProfileAsync()
        {
            if (App.Session == null || string.IsNullOrEmpty(App.Session.Token)) return;
            try
            {
                var reply = await App.Orb.GetMeAsync(App.Session.Token);
                if (!reply.Ok || reply.User == null) return;

                App.Session.User = reply.User;
                SessionStore.Save(App.Session);
                ApplySession();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("Profile", ex);
            }
        }

        /// <summary>Muestra cómo aparecerás en Discord con las opciones actuales.</summary>
        private void UpdateDiscordPreview()
        {
            if (TxtDiscordPreview == null) return;
            if (ChkDiscordRpc.IsChecked != true)
            {
                TxtDiscordPreview.Text = "Show in Discord is off, so nobody sees your activity.";
                return;
            }

            var who = ChkDiscordAccount.IsChecked == true
                ? (string.IsNullOrWhiteSpace(App.Session?.User?.Name) ? "usuario" : App.Session!.User.Name)
                : "A player";
            var what = ChkDiscordGame.IsChecked == true && !string.IsNullOrEmpty(_currentGameName)
                ? _currentGameName
                : "Roblox";
            TxtDiscordPreview.Text = $"{who} is playing {what}";
        }

        private void DiscordOptions_Click(object sender, RoutedEventArgs e)
        {
            SaveUiToSettings(); // las opciones se leen de los ajustes
            UpdateDiscordPreview();
            _ = UpdateDiscordGameAsync(); // si ya hay partida, el bot se entera del cambio
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            DarkTitleBar.Apply(this);
        }

        private void LoadSettingsIntoUi()
        {
            var s = App.Settings.Prop;
            ChkMultiInstance.IsChecked = s.MultiInstance;
            ChkActivityQuick.IsChecked = s.ActivityTracking;
            ChkRoValraQuick.IsChecked = s.UseRoValraServerInfo;
            ChkUseFastFlags.IsChecked = s.UseFastFlagEditor;
            ChkEnableMods.IsChecked = s.EnableMods;
            ChkActivityTracking.IsChecked = s.ActivityTracking;
            ChkShowLocation.IsChecked = s.ShowServerLocation;
            ChkShowUptime.IsChecked = s.ShowServerUptime;
            ChkSubmitRoValra.IsChecked = s.SubmitServersToRoValra;
            ChkDiscordRpc.IsChecked = s.DiscordRichPresence;
            ChkDiscordAccount.IsChecked = s.DiscordShowAccount;
            ChkDiscordGame.IsChecked = s.DiscordShowGame;
            ChkShowGames.IsChecked = s.ShowGames;
            ChkEnableCleaner.IsChecked = s.EnableCleaner;
            ChkCleanCache.IsChecked = s.CleanRobloxCache;
            ChkCleanLogs.IsChecked = s.CleanRobloxLogs;
            ChkCleanCryptLogs.IsChecked = s.CleanCryptLogs;
            ChkConfirmMulti.IsChecked = s.ConfirmCloseOnMultiInstance;
            ChkBgUpdates.IsChecked = s.BackgroundUpdates;
            ChkDontExitDesktop.IsChecked = s.DontExitToDesktopApp;

            // Restaurar lo que el usuario eligió la última vez
            SelectComboItem(CmbChannel, s.RobloxChannel, byTag: false);
            SelectComboItem(CmbCleanerDays, s.CleanerDaysThreshold.ToString(), byTag: true);
            SelectComboItem(CmbJoinMode, s.BestServerMode, byTag: true);

            if (!string.IsNullOrEmpty(s.CustomRobloxDomain))
                TxtCustomDomain.Text = s.CustomRobloxDomain;

            TxtFastFlags.Text = s.FastFlags.Count > 0
                ? JsonSerializer.Serialize(s.FastFlags, FlagsWriteOptions)
                : "{\n}";
        }

        private static void SelectComboItem(ComboBox box, string? value, bool byTag)
        {
            if (value == null) return;
            foreach (var item in box.Items)
            {
                if (item is not ComboBoxItem cbi) continue;
                var key = byTag ? cbi.Tag?.ToString() : cbi.Content?.ToString();
                if (string.Equals(key, value, StringComparison.OrdinalIgnoreCase))
                {
                    box.SelectedItem = cbi;
                    return;
                }
            }
        }

        private bool TryParseFastFlags(out Dictionary<string, object> flags, out string? error)
        {
            flags = new Dictionary<string, object>();
            error = null;

            var text = TxtFastFlags.Text;
            if (string.IsNullOrWhiteSpace(text)) return true;

            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(text, FlagsReadOptions);
                if (parsed != null) flags = parsed;
                return true;
            }
            catch (JsonException ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Copia la UI a los ajustes y los guarda. Devuelve false si el JSON de FastFlags es inválido
        /// (en ese caso los FastFlags anteriores se conservan).
        /// </summary>
        private bool SaveUiToSettings(bool reportFlagErrors = false)
        {
            var s = App.Settings.Prop;
            s.MultiInstance = ChkMultiInstance.IsChecked == true;
            s.ActivityTracking = ChkActivityTracking.IsChecked == true;
            s.UseRoValraServerInfo = ChkRoValraQuick.IsChecked == true;
            s.UseFastFlagEditor = ChkUseFastFlags.IsChecked == true;
            s.EnableMods = ChkEnableMods.IsChecked == true;
            s.ShowServerLocation = ChkShowLocation.IsChecked == true;
            s.ShowServerUptime = ChkShowUptime.IsChecked == true;
            s.SubmitServersToRoValra = ChkSubmitRoValra.IsChecked == true;
            s.DiscordRichPresence = ChkDiscordRpc.IsChecked == true;
            s.DiscordShowAccount = ChkDiscordAccount.IsChecked == true;
            s.DiscordShowGame = ChkDiscordGame.IsChecked == true;
            s.ShowGames = ChkShowGames.IsChecked == true;
            s.EnableCleaner = ChkEnableCleaner.IsChecked == true;
            s.CleanRobloxCache = ChkCleanCache.IsChecked == true;
            s.CleanRobloxLogs = ChkCleanLogs.IsChecked == true;
            s.CleanCryptLogs = ChkCleanCryptLogs.IsChecked == true;
            s.ConfirmCloseOnMultiInstance = ChkConfirmMulti.IsChecked == true;
            s.BackgroundUpdates = ChkBgUpdates.IsChecked == true;
            s.DontExitToDesktopApp = ChkDontExitDesktop.IsChecked == true;
            s.CustomRobloxDomain = string.IsNullOrWhiteSpace(TxtCustomDomain.Text) ? null : TxtCustomDomain.Text.Trim();

            if (CmbChannel.SelectedItem is ComboBoxItem ch)
                s.RobloxChannel = ch.Content?.ToString() ?? "LIVE";

            if (CmbJoinMode.SelectedItem is ComboBoxItem modeItem && modeItem.Tag is string modeTag)
                s.BestServerMode = modeTag;

            if (CmbCleanerDays.SelectedItem is ComboBoxItem daysItem &&
                int.TryParse(daysItem.Tag?.ToString(), out var days))
                s.CleanerDaysThreshold = days;

            var flagsOk = TryParseFastFlags(out var flags, out var error);
            if (flagsOk)
                s.FastFlags = flags;
            else if (reportFlagErrors)
                MessageBox.Show("The FastFlags JSON is not valid, so it was not saved:\n\n" + error,
                    AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);

            App.Settings.Save();
            return flagsOk;
        }

        private void EnsureModsFolder()
        {
            var path = App.Textures.ModsRoot;
            Directory.CreateDirectory(path);
            TxtModsPath.Text = path;
            UpdateTexturesStatus();
        }

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton rb || rb.Tag is not string tag) return;

            // Este evento puede dispararse mientras se construye la ventana, antes de que existan los paneles
            if (PanelHome == null || PanelAbout == null) return;

            PanelHome.Visibility = tag == "Home" ? Visibility.Visible : Visibility.Collapsed;
            PanelFastFlags.Visibility = tag == "FastFlags" ? Visibility.Visible : Visibility.Collapsed;
            PanelMods.Visibility = tag == "Mods" ? Visibility.Visible : Visibility.Collapsed;
            PanelActivity.Visibility = tag == "Activity" ? Visibility.Visible : Visibility.Collapsed;
            PanelGames.Visibility = tag == "Games" ? Visibility.Visible : Visibility.Collapsed;
            PanelDiscord.Visibility = tag == "Discord" ? Visibility.Visible : Visibility.Collapsed;
            PanelCleaner.Visibility = tag == "Cleaner" ? Visibility.Visible : Visibility.Collapsed;
            PanelBehaviour.Visibility = tag == "Behaviour" ? Visibility.Visible : Visibility.Collapsed;
            PanelAppearance.Visibility = tag == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
            PanelAbout.Visibility = tag == "About" ? Visibility.Visible : Visibility.Collapsed;
        }

        // "Activity tracking" aparece en Home y en Activity: mantener ambos casillas iguales
        private void ActivityToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb) return;
            var value = cb.IsChecked == true;
            ChkActivityQuick.IsChecked = value;
            ChkActivityTracking.IsChecked = value;
        }

        private async void LaunchButton_Click(object sender, RoutedEventArgs e)
        {
            BtnLaunch.IsEnabled = false; // evita lanzar dos veces con doble clic
            try
            {
                if (!SaveUiToSettings(reportFlagErrors: true)) return;
                var launched = await _bootstrapper.LaunchAsync();
                if (launched) await AfterLaunchAsync(_bootstrapper.LastProcess);
            }
            finally
            {
                BtnLaunch.IsEnabled = true;
            }
        }

        private void SaveFastFlags_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!SaveUiToSettings(reportFlagErrors: true)) return;

                MessageBox.Show("FastFlags saved. They will be applied on the next launch.", AppInfo.Name);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save: " + ex.Message, AppInfo.Name);
            }
        }

        private void PresetFps_Click(object sender, RoutedEventArgs e)
        {
            TxtFastFlags.Text = "{\n  \"DFIntTaskSchedulerTargetFps\": 240\n}";
        }

        private void PresetLowGfx_Click(object sender, RoutedEventArgs e)
        {
            TxtFastFlags.Text = "{\n  \"FFlagDebugGraphicsPreferD3D11FL10\": true,\n  \"DFIntDebugFRMQualityLevelOverride\": 1\n}";
        }

        private void PresetClear_Click(object sender, RoutedEventArgs e)
        {
            TxtFastFlags.Text = "{\n}";
        }

        // ---------------- Show in Discord (vía el bot ORB) ----------------
        private async Task StartShowInDiscordAsync(Process? roblox)
        {
            var s = App.Settings.Prop;
            if (!s.DiscordRichPresence || App.Session == null) return;

            var reply = await App.Orb.ShowAsync(App.Session.Token, true, s.DiscordShowAccount);
            if (!reply.Ok)
            {
                App.Logger.WriteLine("Discord", $"Show in Discord failed: {reply.Reason} {reply.Error}");
                StatusText.Text = "Roblox launched. Show in Discord isn't available right now: " + (reply.Error ?? reply.Reason);
                return;
            }

            _showActive = true;
            if (_currentPlaceId != 0) await UpdateDiscordGameAsync(); // el juego se detectó mientras se activaba
            if (roblox == null) return;
            try
            {
                // Cuando Roblox se cierra, el bot quita el mensaje
                roblox.EnableRaisingEvents = true;
                roblox.Exited += (_, _) => { _ = StopShowInDiscordAsync(); };
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("Discord", ex);
            }
        }

        private async Task StopShowInDiscordAsync()
        {
            if (!_showActive || App.Session == null) return;
            _showActive = false;
            try
            {
                await App.Orb.ShowAsync(App.Session.Token, false, false);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("Discord", ex);
            }
        }

        // ---------------- Games: juego actual, recientes y "Join best server" ----------------
        /// <summary>Tras abrir Roblox: empieza a detectar el juego y a avisar al bot.</summary>
        private async Task AfterLaunchAsync(Process? roblox)
        {
            StartActivityWatcher(roblox);
            await StartShowInDiscordAsync(roblox);
        }

        private void StartActivityWatcher(Process? roblox)
        {
            var s = App.Settings.Prop;
            if (!s.ActivityTracking || !s.ShowGames) return;

            _watcher?.Stop();
            _watcher = new ActivityWatcher();
            _watcher.GameJoined += (placeId, _) => Dispatcher.InvokeAsync(() => OnGameJoinedAsync(placeId));
            _watcher.GameLeft += () => Dispatcher.InvokeAsync(() => OnGameLeftAsync());

            var since = DateTime.UtcNow.AddSeconds(-30);
            try
            {
                if (roblox != null) since = roblox.StartTime.ToUniversalTime().AddSeconds(-5);
            }
            catch { /* sin StartTime: se usa el margen de 30 s */ }

            _watcher.Start(since);

            try
            {
                if (roblox != null)
                {
                    roblox.EnableRaisingEvents = true;
                    roblox.Exited += (_, _) => Dispatcher.InvokeAsync(() => OnGameLeftAsync());
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("Activity", ex);
            }
        }

        private async Task OnGameJoinedAsync(long placeId)
        {
            _currentPlaceId = placeId;
            TxtGameName.Text = "Loading game…";
            TxtGameMeta.Text = $"Place {placeId}";
            BtnUseCurrent.Visibility = Visibility.Visible;

            var info = await App.RobloxApi.GetGameInfoAsync(placeId);
            if (_currentPlaceId != placeId) return; // mientras tanto cambió de juego

            _currentGameName = info?.Name;
            TxtGameName.Text = info?.Name ?? $"Game {placeId}";
            TxtGameMeta.Text = info != null
                ? $"{info.Playing:N0} playing now · Place {placeId}"
                : $"Place {placeId} · couldn't load the game details";
            SetGameIcon(info?.IconUrl);

            if (info != null) AddRecentGame(placeId, info.Name);
            UpdateDiscordPreview();
            await UpdateDiscordGameAsync();
        }

        private async Task OnGameLeftAsync()
        {
            if (_currentPlaceId == 0) return;
            _currentPlaceId = 0;
            _currentGameName = null;

            TxtGameName.Text = "Not in a game";
            TxtGameMeta.Text = "Launch Roblox and join a game to see it here.";
            GameIcon.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x35));
            BtnUseCurrent.Visibility = Visibility.Collapsed;

            UpdateDiscordPreview();
            await UpdateDiscordGameAsync();
        }

        private void SetGameIcon(string? url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try
            {
                GameIcon.Background = new ImageBrush(new BitmapImage(new Uri(url))) { Stretch = Stretch.UniformToFill };
            }
            catch
            {
                // Sin icono: se queda el cuadro gris
            }
        }

        /// <summary>Si "Show in Discord" está activo y se permitió, el bot recibe el juego actual (o ninguno).</summary>
        private async Task UpdateDiscordGameAsync()
        {
            var s = App.Settings.Prop;
            if (!_showActive || !s.DiscordRichPresence || App.Session == null) return;

            try
            {
                var game = s.DiscordShowGame && _currentPlaceId != 0 ? _currentGameName : null;
                var place = game != null ? _currentPlaceId : (long?)null;
                var reply = await App.Orb.ShowGameAsync(App.Session.Token, s.DiscordShowAccount, game, place);
                if (!reply.Ok)
                    App.Logger.WriteLine("Discord", $"Game update not accepted: {reply.Reason} {reply.Error}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("Discord", ex);
            }
        }

        private void AddRecentGame(long placeId, string name)
        {
            var list = App.Settings.Prop.RecentGames;
            list.RemoveAll(g => g.PlaceId == placeId);
            list.Insert(0, new RecentGame { PlaceId = placeId, Name = name, LastPlayedUtc = DateTime.UtcNow });
            if (list.Count > 10) list.RemoveRange(10, list.Count - 10);

            App.Settings.Save();
            RefreshRecentGamesUi();
        }

        private void RefreshRecentGamesUi()
        {
            var recent = App.Settings.Prop.RecentGames.OrderByDescending(g => g.LastPlayedUtc).ToList();
            LstRecentGames.ItemsSource = recent;
            TxtNoRecent.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RecentGame_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { Tag: long placeId })
                TxtJoinPlace.Text = placeId.ToString();
        }

        private void UseCurrentGame_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPlaceId != 0) TxtJoinPlace.Text = _currentPlaceId.ToString();
        }

        /// <summary>Acepta un número o un enlace (roblox.com/games/123/… o …?placeId=123).</summary>
        private static bool TryParsePlaceId(string? text, out long placeId)
        {
            placeId = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var m = Regex.Match(text.Trim(), @"/games/(\d+)|placeId=(\d+)|^(\d+)$");
            if (!m.Success) return false;

            var digits = m.Groups[1].Success ? m.Groups[1].Value
                       : m.Groups[2].Success ? m.Groups[2].Value
                       : m.Groups[3].Value;
            return long.TryParse(digits, out placeId) && placeId > 0;
        }

        private async void JoinBest_Click(object sender, RoutedEventArgs e)
        {
            if (!TryParsePlaceId(TxtJoinPlace.Text, out var placeId))
            {
                TxtJoinStatus.Text = "Enter a place ID or paste a roblox.com/games/… link.";
                return;
            }

            BtnJoinBest.IsEnabled = false;
            try
            {
                var mode = (CmbJoinMode.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch
                {
                    "players" => ServerPickMode.Players,
                    "balanced" => ServerPickMode.Balanced,
                    _ => ServerPickMode.Ping
                };

                TxtJoinStatus.Text = "Looking for servers…";
                var servers = await App.RobloxApi.GetPublicServersAsync(placeId);
                if (servers == null)
                {
                    TxtJoinStatus.Text = "Couldn't load the servers. Roblox may be limiting requests: try again in a minute.";
                    return;
                }

                var best = RobloxApiService.PickBest(servers, mode);
                if (best == null)
                {
                    TxtJoinStatus.Text = "No public servers with free slots were found for that game.";
                    return;
                }

                var ping = best.Ping.HasValue ? $", ~{best.Ping} ms" : "";
                TxtJoinStatus.Text = $"Joining a server with {best.Playing}/{best.MaxPlayers} players{ping}…";

                if (!SaveUiToSettings(reportFlagErrors: true)) return;
                var launched = await _bootstrapper.LaunchAsync(placeId.ToString(), best.Id);
                if (launched) await AfterLaunchAsync(_bootstrapper.LastProcess);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("JoinBest", ex);
                TxtJoinStatus.Text = "Something went wrong: " + ex.Message;
            }
            finally
            {
                BtnJoinBest.IsEnabled = true;
            }
        }

        // ---------------- Custom textures ----------------
        private void UpdateTexturesStatus()
        {
            var count = App.Textures.CountFiles();
            TxtTexturesStatus.Text = count == 0
                ? "No texture pack imported yet."
                : $"{count} texture file(s) ready. They are applied the next time you launch Roblox.";
        }

        private async void ImportTextures_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Choose a texture pack",
                Filter = "Texture pack (*.zip)|*.zip",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != true) return;

            TxtTexturesStatus.Text = "Importing…";
            try
            {
                var zipPath = dialog.FileName;
                var result = await Task.Run(() => App.Textures.ImportZip(zipPath));

                UpdateTexturesStatus();
                var message = $"Imported {result.Imported} file(s).";
                if (result.Skipped > 0) message += $" Skipped {result.Skipped} file(s) that are not inside a “content” folder or are not allowed.";
                if (result.Imported == 0) message += "\nA pack must contain a “content” folder (for example content\\textures\\…).";
                MessageBox.Show(message, AppInfo.Name);
                ChkEnableMods.IsChecked = result.Imported > 0 || ChkEnableMods.IsChecked == true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("Textures", ex);
                UpdateTexturesStatus();
                MessageBox.Show("Could not import the pack:\n" + ex.Message, AppInfo.Name);
            }
        }

        private async void RestoreTextures_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "Put Roblox's original files back and remove your imported texture pack?",
                AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                var restored = await Task.Run(() =>
                {
                    var count = App.Textures.RestoreAll();
                    App.Textures.ClearImported();
                    return count;
                });
                UpdateTexturesStatus();
                MessageBox.Show($"Done. {restored} original file(s) restored.", AppInfo.Name);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("Textures", ex);
                MessageBox.Show("Could not restore the originals:\n" + ex.Message, AppInfo.Name);
            }
        }

        // ---------------- Cuenta / app ----------------
        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show("Log out? You'll need to sign in with Discord again.",
                AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            try { Task.Run(StopShowInDiscordAsync).Wait(2500); } catch { /* ignorar */ }
            SessionStore.Clear();

            try
            {
                // Reabre ORBstrap: pedirá iniciar sesión
                Process.Start(new ProcessStartInfo { FileName = Environment.ProcessPath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("UI", ex);
            }
            Application.Current.Shutdown();
        }

        private void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            if (App.RunUninstallFlow())
                Application.Current.Shutdown();
        }

        private void OpenModsFolder_Click(object sender, RoutedEventArgs e)
        {
            EnsureModsFolder();
            OpenInExplorer(TxtModsPath.Text);
        }

        private void RefreshServer_Click(object sender, RoutedEventArgs e)
        {
            // Placeholder – real ActivityWatcher will fill these
            TxtServerRegion.Text = "Region: (join a game first)";
            TxtServerUptime.Text = "Uptime: —";
            TxtServerJobId.Text = "JobId: —";
            MessageBox.Show(
                "Activity tracking reads Roblox log files while you play.\n" +
                "Once implemented, this panel will show live region & uptime from RoValra.",
                AppInfo.Name);
        }

        private void RunCleaner_Click(object sender, RoutedEventArgs e)
        {
            SaveUiToSettings();
            MessageBox.Show(
                "Your cleaner options were saved, but the cleaner itself is not implemented yet.\n" +
                "No files have been deleted.",
                AppInfo.Name);
        }

        private void LogsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenInExplorer(Path.Combine(AppDataDir(), "Logs"));
        }

        private void SettingsFolder_Click(object sender, RoutedEventArgs e)
        {
            OpenInExplorer(AppDataDir());
        }

        private static string AppDataDir() => AppInfo.DataDir;

        private static void OpenInExplorer(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                App.Logger?.WriteException("UI", ex);
                MessageBox.Show("Could not open the folder:\n" + ex.Message, AppInfo.Name);
            }
        }

        private void RoValraLink_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www.rovalra.com",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                App.Logger?.WriteException("UI", ex);
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            SaveUiToSettings();
            _watcher?.Stop();
            if (_showActive)
            {
                try { Task.Run(StopShowInDiscordAsync).Wait(2500); } catch { /* ignorar */ }
            }
            base.OnClosing(e);
        }
    }
}
