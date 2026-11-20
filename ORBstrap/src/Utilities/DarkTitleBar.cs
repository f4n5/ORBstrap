using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Crypt3d.Utilities
{
    /// <summary>Barra de título oscura (Windows 10 1809+ / 11) para que combine con la interfaz.</summary>
    public static class DarkTitleBar
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>Llamar desde OnSourceInitialized de la ventana.</summary>
        public static void Apply(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                int useDark = 1;
                // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (Win10 20H1+/Win11); 19 en versiones anteriores
                if (DwmSetWindowAttribute(hwnd, 20, ref useDark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref useDark, sizeof(int));
            }
            catch
            {
                // Solo estético
            }
        }
    }
}
