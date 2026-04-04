using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;

namespace MizuLauncher
{
    public partial class AddAccountWindow : Window
    {
        public MainWindow.PlayerInfo? ResultPlayer { get; private set; }

        public AddAccountWindow()
        {
            InitializeComponent();
            this.SourceInitialized += AddAccountWindow_SourceInitialized;
        }

        private void AddAccountWindow_SourceInitialized(object? sender, EventArgs e)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            EnableMicaBackdrop(hwnd);
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        private void EnableMicaBackdrop(IntPtr hwnd)
        {
            try
            {
                int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
                int useDarkMode = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, Marshal.SizeOf(typeof(int)));

                int DWMWA_SYSTEMBACKDROP_TYPE = 38;
                int type = 3; // Acrylic
                DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, Marshal.SizeOf(typeof(int)));

                int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
                int DWMWCP_ROUND = 2;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref DWMWCP_ROUND, Marshal.SizeOf(typeof(int)));
            }
            catch { }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void BtnConfirmOffline_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtOfflineName.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("请输入玩家 ID");
                return;
            }

            ResultPlayer = new MainWindow.PlayerInfo
            {
                Name = name,
                IsOnline = false
            };
            this.DialogResult = true;
            this.Close();
        }

        private void BtnLittleSkinLogin_Click(object sender, RoutedEventArgs e)
        {
            var win = new LittleSkinLoginWindow { Owner = this };
            if (win.ShowDialog() == true && win.ResultPlayer != null)
            {
                ResultPlayer = win.ResultPlayer;
                this.DialogResult = true;
                this.Close();
            }
        }

        private async void BtnMicrosoftLogin_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BtnMicrosoftLogin.IsEnabled = false;
                BtnMicrosoftLogin.Content = "正在登录...";

                var loginHandler = new JELoginHandlerBuilder()
                    .Build();

                var session = await loginHandler.Authenticate();

                ResultPlayer = new MainWindow.PlayerInfo
                {
                    Name = session.Username ?? "Unknown",
                    IsOnline = true,
                    UUID = session.UUID,
                    AccessToken = session.AccessToken
                };

                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Microsoft 登录失败: " + ex.Message);
            }
            finally
            {
                BtnMicrosoftLogin.IsEnabled = true;
                BtnMicrosoftLogin.Content = "使用 Microsoft 账号登录";
            }
        }
    }
}
