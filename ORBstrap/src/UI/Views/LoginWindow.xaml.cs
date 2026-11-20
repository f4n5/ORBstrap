using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Crypt3d.Services;
using Crypt3d.Utilities;

namespace Crypt3d.UI.Views
{
    /// <summary>
    /// Puerta de acceso: la persona debe estar en el servidor de Discord y verificada por el bot ORB.
    /// Cuando el bot confirma, se muestra "¡Bienvenido, usuario!" y se abre la app.
    /// </summary>
    public partial class LoginWindow : Window
    {
        private enum State { Checking, Login, Offline, Waiting, Welcome }

        private CancellationTokenSource? _cts;
        private string? _inviteUrl;
        private bool _closing;

        public OrbSession? Session { get; private set; }

        public LoginWindow()
        {
            InitializeComponent();
            Loaded += async (_, _) => await StartAsync();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            DarkTitleBar.Apply(this);
        }

        protected override void OnClosed(EventArgs e)
        {
            _closing = true;
            _cts?.Cancel();
            base.OnClosed(e);
        }

        private void SetState(State state)
        {
            PanelChecking.Visibility = state == State.Checking ? Visibility.Visible : Visibility.Collapsed;
            PanelLogin.Visibility = state == State.Login ? Visibility.Visible : Visibility.Collapsed;
            PanelOffline.Visibility = state == State.Offline ? Visibility.Visible : Visibility.Collapsed;
            PanelWaiting.Visibility = state == State.Waiting ? Visibility.Visible : Visibility.Collapsed;
            PanelWelcome.Visibility = state == State.Welcome ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowLogin(string? error)
        {
            TxtLoginError.Text = error ?? "";
            TxtLoginError.Visibility = string.IsNullOrEmpty(error) ? Visibility.Collapsed : Visibility.Visible;
            BtnSignIn.IsEnabled = true;
            SetState(State.Login);
        }

        private void ShowOffline(string? message)
        {
            TxtOfflineMessage.Text = message ?? "Cannot reach the ORB server.";
            SetState(State.Offline);
        }

        /// <summary>Al abrir: datos del servidor y, si ya hay sesión guardada, se revalida en el bot.</summary>
        private async Task StartAsync()
        {
            SetState(State.Checking);

            var cfg = await App.Orb.GetConfigAsync();
            if (_closing) return;
            if (cfg.Reason == "network")
            {
                ShowOffline(cfg.Error);
                return;
            }
            if (cfg.Ok && !string.IsNullOrEmpty(cfg.InviteUrl))
            {
                _inviteUrl = cfg.InviteUrl;
                BtnJoin.Visibility = Visibility.Visible;
            }

            var saved = SessionStore.Load();
            if (saved == null)
            {
                ShowLogin(null);
                return;
            }

            TxtCheckingTitle.Text = "Checking your account…";
            var me = await App.Orb.GetMeAsync(saved.Token);
            if (_closing) return;

            if (me.Ok && me.User != null)
            {
                await CompleteAsync(new OrbSession { Token = me.Token ?? saved.Token, User = me.User });
                return;
            }

            // Fallo de conexión o del servidor: se conserva la sesión y se permite reintentar
            if (me.Reason == "network" || me.Reason == "server_error" || (me.Reason?.StartsWith("http_") ?? false))
            {
                ShowOffline(me.Error);
                return;
            }

            // Sesión caducada, o la persona ya no está verificada / salió del servidor
            SessionStore.Clear();
            ShowLogin(me.Reason == "expired" || me.Reason == "invalid" ? null : me.Error);
        }

        private async void Retry_Click(object sender, RoutedEventArgs e)
        {
            TxtCheckingTitle.Text = "Connecting to the ORB server…";
            await StartAsync();
        }

        private void Join_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_inviteUrl)) return;
            OpenUrl(_inviteUrl);
        }

        private async void SignIn_Click(object sender, RoutedEventArgs e)
        {
            BtnSignIn.IsEnabled = false;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            try
            {
                TxtCheckingTitle.Text = "Starting sign-in…";
                SetState(State.Checking);

                var start = await App.Orb.StartLoginAsync(ct);
                if (!start.Ok || string.IsNullOrEmpty(start.LoginUrl) || string.IsNullOrEmpty(start.Code) || string.IsNullOrEmpty(start.Secret))
                {
                    if (start.Reason == "network") ShowOffline(start.Error);
                    else ShowLogin(start.Error ?? "Could not start the sign-in. Try again.");
                    return;
                }

                if (!OpenUrl(start.LoginUrl))
                {
                    try { Clipboard.SetText(start.LoginUrl); } catch { /* ignorar */ }
                    ShowLogin("I couldn't open your browser. The sign-in link was copied to your clipboard: paste it in your browser.");
                    return;
                }

                SetState(State.Waiting);

                var deadline = DateTime.UtcNow.AddSeconds(Math.Max(60, start.ExpiresIn));
                while (DateTime.UtcNow < deadline)
                {
                    await Task.Delay(2000, ct);

                    var poll = await App.Orb.PollLoginAsync(start.Code, start.Secret, ct);

                    if (poll.Status == "approved" && !string.IsNullOrEmpty(poll.Token) && poll.User != null)
                    {
                        await CompleteAsync(new OrbSession { Token = poll.Token, User = poll.User });
                        return;
                    }
                    if (poll.Status == "denied")
                    {
                        ShowLogin(poll.Error ?? "Your account could not be verified.");
                        return;
                    }
                    if (poll.Status == "expired" || poll.Status == "unknown")
                    {
                        ShowLogin("The sign-in expired. Press “Sign in with Discord” again.");
                        return;
                    }
                    // "pending" o un fallo de red puntual → seguir esperando
                }

                ShowLogin("The sign-in took too long. Press “Sign in with Discord” again.");
            }
            catch (OperationCanceledException)
            {
                // El usuario canceló o cerró la ventana
                if (!_closing) ShowLogin(null);
            }
        }

        private void CancelWait_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
        }

        /// <summary>Cuenta verificada: guarda la sesión, muestra «¡Bienvenido, usuario!» y abre la app.</summary>
        private async Task CompleteAsync(OrbSession session)
        {
            SessionStore.Save(session);
            TxtWelcome.Text = $"¡Bienvenido, {session.User.Name}!";
            SetState(State.Welcome);

            await Task.Delay(1500);
            if (_closing) return;

            Session = session;
            DialogResult = true;
        }

        private void Quit_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private static bool OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.WriteException("Login", ex);
                return false;
            }
        }
    }
}
