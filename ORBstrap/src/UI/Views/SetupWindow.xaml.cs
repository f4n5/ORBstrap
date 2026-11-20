using System;
using System.Threading.Tasks;
using System.Windows;
using Crypt3d.Services;
using Crypt3d.Utilities;

namespace Crypt3d.UI.Views
{
    public enum SetupResult
    {
        Cancelled,
        Installed,
        Portable
    }

    /// <summary>Panel que se abre al ejecutar ORBstrap sin estar instalado: configura e instala.</summary>
    public partial class SetupWindow : Window
    {
        public SetupResult Result { get; private set; } = SetupResult.Cancelled;

        public SetupWindow()
        {
            InitializeComponent();

            var existing = InstallerService.GetInstalledDir();
            if (existing != null)
            {
                TxtDir.Text = existing;
                BtnInstall.Content = "Update";
                TxtSubtitle.Text = "ORBstrap is already installed. You can update it with this version.";
            }
            else
            {
                TxtDir.Text = AppInfo.DefaultInstallDir;
            }

            // Valores iniciales = los ajustes actuales
            var s = App.Settings.Prop;
            ChkMulti.IsChecked = s.MultiInstance;
            ChkTextures.IsChecked = s.EnableMods;
            ChkShowDiscord.IsChecked = s.DiscordRichPresence;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            DarkTitleBar.Apply(this);
        }

        private void SetBusy(bool busy)
        {
            BtnInstall.IsEnabled = !busy;
            BtnPortable.IsEnabled = !busy;
            BtnCancel.IsEnabled = !busy;
            TxtDir.IsEnabled = !busy;
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Choose where to install ORBstrap"
            };
            if (dialog.ShowDialog(this) == true)
                TxtDir.Text = dialog.FolderName;
        }

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            SetBusy(true);
            TxtStatus.Text = "Installing...";
            try
            {
                var dir = InstallerService.NormalizeInstallDir(TxtDir.Text);
                TxtDir.Text = dir;

                // Configuración inicial
                var s = App.Settings.Prop;
                s.MultiInstance = ChkMulti.IsChecked == true;
                s.EnableMods = ChkTextures.IsChecked == true;
                s.DiscordRichPresence = ChkShowDiscord.IsChecked == true;
                App.Settings.Save();

                // Copiar el .exe (puede tardar un par de segundos)
                var exe = await Task.Run(() => InstallerService.CopySelf(dir));

                InstallerService.RegisterInstall(dir, exe);

                try
                {
                    if (ChkDesktop.IsChecked == true) InstallerService.CreateShortcut(InstallerService.DesktopLnk, exe);
                    if (ChkStartMenu.IsChecked == true) InstallerService.CreateShortcut(InstallerService.StartMenuLnk, exe);
                }
                catch (Exception ex)
                {
                    // Los accesos directos son opcionales: no deben fallar la instalación
                    App.Logger.WriteException("Setup", ex);
                }

                App.Logger.WriteLine("Setup", $"Installed to {dir}");
                Result = SetupResult.Installed;

                if (ChkLaunch.IsChecked == true)
                {
                    try
                    {
                        InstallerService.LaunchInstalled(exe);
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteException("Setup", ex);
                        MessageBox.Show(this, "ORBstrap was installed, but it could not be opened automatically.\nOpen it from the Start menu or the desktop shortcut.",
                            AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    MessageBox.Show(this, "ORBstrap was installed. You can open it from the Start menu or the desktop shortcut.",
                        AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);
                }
                Close();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("Setup", ex);
                TxtStatus.Text = "Installation failed: " + ex.Message +
                                 (ex is System.IO.IOException ? "\nIf ORBstrap is already open, close it and try again." : "");
                SetBusy(false);
            }
        }

        private void Portable_Click(object sender, RoutedEventArgs e)
        {
            Result = SetupResult.Portable;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Result = SetupResult.Cancelled;
            Close();
        }
    }
}
