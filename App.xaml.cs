using Microsoft.UI.Xaml;
using System;
using Windows.ApplicationModel;

namespace Clipora
{
    public partial class App : Application
    {
        private Window? m_window;
        private TrayIcon? _tray;

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            m_window = new MainWindow();
            m_window.Activate();

            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(m_window);

            var iconPath = System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "icon.ico");

            _tray = new TrayIcon(
                hWnd,
                tooltip: "Clipora (Ctrl+Shift+V)",
                iconPath: iconPath,
                onOpen: () => m_window?.Activate(),
                onExit: () =>
                {
                    m_window?.Close();
                    Environment.Exit(0);
                },
                onHotkey: () => m_window?.Activate()
            );
        }
    }
}