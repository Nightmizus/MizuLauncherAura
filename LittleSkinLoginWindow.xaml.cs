using System;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Threading.Tasks;

namespace MizuLauncher
{
    public partial class LittleSkinLoginWindow : Window
    {
        public MainWindow.PlayerInfo? ResultPlayer { get; private set; }

        public LittleSkinLoginWindow()
        {
            InitializeComponent();
            this.SourceInitialized += LittleSkinLoginWindow_SourceInitialized;
        }

        public void SetApiUrl(string url)
        {
            TxtApiUrl.Text = url;
        }

        private void LittleSkinLoginWindow_SourceInitialized(object? sender, EventArgs e)
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

        private async void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            string email = TxtEmail.Text.Trim();
            string password = TxtPassword.Password;
            string apiUrl = TxtApiUrl.Text.Trim().TrimEnd('/');

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(apiUrl))
            {
                TxtStatus.Text = "请输入完整的验证信息";
                TxtStatus.Visibility = Visibility.Visible;
                return;
            }

            BtnLogin.IsEnabled = false;
            TxtStatus.Text = "正在登录...";
            TxtStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 200, 200, 200));
            TxtStatus.Visibility = Visibility.Visible;

            try
            {
                using var client = new HttpClient();
                var requestBody = new
                {
                    agent = new { name = "Minecraft", version = 1 },
                    username = email,
                    password = password,
                    clientToken = Guid.NewGuid().ToString("N"),
                    requestUser = true
                };

                string jsonBody = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                var response = await client.PostAsync($"{apiUrl}/authserver/authenticate", content);
                string responseStr = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(responseStr);
                    var root = doc.RootElement;
                    string accessToken = root.GetProperty("accessToken").GetString() ?? "";
                    
                    var selectedProfile = root.TryGetProperty("selectedProfile", out var sp) ? sp : root.GetProperty("availableProfiles")[0];
                    string uuid = selectedProfile.GetProperty("id").GetString() ?? "";
                    string name = selectedProfile.GetProperty("name").GetString() ?? "";

                    ResultPlayer = new MainWindow.PlayerInfo
                    {
                        Name = name,
                        IsOnline = true,
                        UUID = uuid,
                        AccessToken = accessToken,
                        AuthType = "LittleSkin"
                    };

                    this.DialogResult = true;
                    this.Close();
                }
                else
                {
                    TxtStatus.Text = "登录失败，请检查账号和密码";
                    TxtStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 255, 139, 139));
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "错误: " + ex.Message;
                TxtStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 255, 139, 139));
            }
            finally
            {
                BtnLogin.IsEnabled = true;
            }
        }
    }
}