using System;
using System.Linq;
using System.Windows;
using Crypt3d.Services;
using Crypt3d.UI.Views;
using Crypt3d.Utilities;

namespace Crypt3d
{
    public partial class App : Application
    {
        public static SettingsService Settings { get; private set; } = null!;
        public static Logger Logger { get; private set; } = null!;
        public static RoValraService RoValra { get; private set; } = null!;
        public static OrbApiService Orb { get; private set; } = null!;
        public static TexturesService Textures { get; private set; } = null!;

        /// <summary>Sesión verificada por el bot ORB (se establece tras el login).</summary>
        public static OrbSession Session { get; set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Varias ventanas se abren en secuencia (instalador → login → principal):
            // la app solo se cierra cuando se llama a Shutdown().
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Los errores nunca deben ser silenciosos: se registran y se muestran.
            DispatcherUnhandledException += (_, args) =>
            {
                Logger?.WriteException("Unhandled", args.Exception);
                MessageBox.Show(args.Exception.ToString(), AppInfo.Name + " - Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
                Shutdown(1);
            };

            Logger = new Logger();
            Logger.WriteLine("App", $"{AppInfo.Name} {AppInfo.Version} starting...");

            Settings = new SettingsService();
            Settings.Load();

            RoValra = new RoValraService();
            Orb = new OrbApiService();
            Textures = new TexturesService();

            base.OnStartup(e);

            try
            {
                RunStartupFlow(e.Args);
            }
            catch (Exception ex)
            {
                Logger.WriteException("Startup", ex);
                MessageBox.Show(ex.ToString(), AppInfo.Name + " - Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        /// <summary>
        /// 1) Si no está instalado → panel de instalación (o "ejecutar sin instalar").
        /// 2) Login con Discord (el bot ORB verifica que estés en el servidor y verificado).
        /// 3) Ventana principal.
        /// </summary>
        private void RunStartupFlow(string[] args)
        {
            if (args.Contains("--uninstall"))
            {
                RunUninstallFlow();
                Shutdown();
                return;
            }

            var portable = args.Contains("--portable");
            if (!portable && !InstallerService.IsRunningFromInstall())
            {
                var setup = new SetupWindow();
                setup.ShowDialog();

                if (setup.Result == SetupResult.Installed)
                {
                    Shutdown(); // el instalador ya abrió la copia instalada (si se pidió)
                    return;
                }
                if (setup.Result == SetupResult.Cancelled)
                {
                    Shutdown();
                    return;
                }
                // SetupResult.Portable → continuar sin instalar
            }

            var login = new LoginWindow();
            if (login.ShowDialog() != true || login.Session == null)
            {
                Shutdown();
                return;
            }
            Session = login.Session;

            var main = new Crypt3d.UI.Views.MainWindow();
            MainWindow = main;
            main.Closed += (_, _) => Shutdown();
            main.Show();
        }

        /// <summary>Pide confirmación y desinstala. Devuelve true si se desinstaló.</summary>
        public static bool RunUninstallFlow()
        {
            var confirm = MessageBox.Show(
                $"Uninstall {AppInfo.Name}?\n\nThis removes the app and its shortcuts, and restores any custom textures in Roblox.",
                $"Uninstall {AppInfo.Name}", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return false;

            var deleteData = MessageBox.Show(
                "Also delete your settings, logs, textures and sign-in data?",
                $"Uninstall {AppInfo.Name}", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

            try
            {
                InstallerService.Uninstall(deleteData);
                MessageBox.Show($"{AppInfo.Name} was uninstalled.", AppInfo.Name,
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                Logger?.WriteException("Uninstall", ex);
                MessageBox.Show("Could not uninstall:\n" + ex.Message, AppInfo.Name,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Settings?.Save();
            RoValra?.Dispose();
            Orb?.Dispose();
            Logger?.WriteLine("App", $"{AppInfo.Name} exiting.");
            base.OnExit(e);
        }
    }
}
