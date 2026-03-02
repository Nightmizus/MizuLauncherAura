using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Microsoft.VisualBasic;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.Installer;

using System.Windows.Media.Animation;

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace MizuLauncher
{
    public class PlayerInfo
    {
        public string? Name { get; set; }
        public string? Avatar { get; set; }
    }

    public class ModInfo
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? IconUrl { get; set; }
        public string? ProjectId { get; set; }
        public string? VersionId { get; set; }
        public string? Author { get; set; }
        public string? Categories { get; set; }
    }

    public class ModVersionInfo
    {
        public string? Id { get; set; }
        public string? VersionNumber { get; set; }
        public string? Name { get; set; }
        public string? Type { get; set; } // release, beta, alpha
        public List<string>? GameVersions { get; set; }
        public List<string>? Loaders { get; set; }
        public string? DownloadUrl { get; set; }
        public string? FileName { get; set; }
        
        public string DisplayGameVersions => GameVersions != null ? string.Join(", ", GameVersions) : "";
        public string DisplayLoaders => Loaders != null ? string.Join(", ", Loaders) : "";
        public string DisplayType => Type?.ToUpper() ?? "";
    }

    public class DownloadTask : System.ComponentModel.INotifyPropertyChanged
    {
        private int _progress;
        private string? _status;

        public string? Name { get; set; }
        public int Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(nameof(Progress)); }
        }
        public string? Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(nameof(Status)); }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }

    public partial class MainWindow : Window
    {
        // 绝对生效的手搓日志方法 
        private static void WriteLog(string message)
        {
            try
            {
                // 日志会直接生成在你程序的运行目录下 
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mizu_debug.txt");
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\r\n");
            }
            catch { }
        }

        private MinecraftLauncher? _launcher;
        private MinecraftPath? _baseMcPath;
        private Point _dragStartPoint;
        private bool _isDragging = false;
        private const string ConfigFileName = "launcher_config.json";
        private const int MaxQuickLaunchItems = 9; // 3x3 grid

        public ObservableCollection<string> QuickLaunchVersions { get; set; } = new();
        public ObservableCollection<PlayerInfo> OnlinePlayers { get; set; } = new();
        public ObservableCollection<PlayerInfo> OfflinePlayers { get; set; } = new();
        public ObservableCollection<string> DownloadableVanillaVersions { get; set; } = new();
        public ObservableCollection<ModInfo> ModSearchResults { get; set; } = new();
        public ObservableCollection<ModVersionInfo> ModVersions { get; set; } = new();
        public ObservableCollection<DownloadTask> DownloadTasks { get; set; } = new();

        public MainWindow()
        {
            // 全局崩溃拦截写入日志 
            Application.Current.DispatcherUnhandledException += (s, e) =>
            {
                WriteLog($"【致命崩溃】 {e.Exception}");
                MessageBox.Show($"软件崩溃了！请查看目录下的 mizu_debug.txt\n{e.Exception.Message}");
                e.Handled = true;
            };

            try
            {
                WriteLog("=== 软件启动 ===");
                InitializeComponent();

                // 挂载系统底层句柄初始化事件，用于开启 Mica/Acrylic
                this.SourceInitialized += MainWindow_SourceInitialized;

                QuickLaunchVersions = new ObservableCollection<string>();
                QuickLaunchItems.ItemsSource = QuickLaunchVersions;

                OnlinePlayers = new ObservableCollection<PlayerInfo>();
                ListOnlinePlayers.ItemsSource = OnlinePlayers;

                OfflinePlayers = new ObservableCollection<PlayerInfo>();
                ListOfflinePlayers.ItemsSource = OfflinePlayers;

                ListVanillaVersions.ItemsSource = DownloadableVanillaVersions;
                ListModResults.ItemsSource = ModSearchResults;
                ListModVersions.ItemsSource = ModVersions;
                ListDownloadTasks.ItemsSource = DownloadTasks;

                string mcDirPath = @"C:\Users\Mizusumi\Personal\play\mc\.minecraft";
                _baseMcPath = new MinecraftPath(mcDirPath);
                _launcher = new MinecraftLauncher(_baseMcPath);

                this.Loaded += MainWindow_Loaded;
                this.Activated += (s, e) => UpdateBackgroundUIFromState();
                this.Deactivated += (s, e) => UpdateBackgroundUIFromState();
                QuickLaunchVersions.CollectionChanged += (s, e) => SaveConfig();

                // 移除构造函数中的直接调用，改到 Loaded 事件中统一处理
                // _ = UpdatePlayerUIFromState();
                // 以前的抓屏更新事件 (LocationChanged, SizeChanged) 已经全部被扬了！
            }
            catch (Exception ex)
            {
                WriteLog($"初始化失败: {ex.Message}");
                MessageBox.Show($"初始化失败: {ex.Message}");
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                LoadConfig();
                RefreshVersionList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Loaded error: {ex.Message}");
            }
        }

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                EnableMicaBackdrop(hwnd, _currentBgType);
            }
        }

        #region Navigation and Background Settings

        private void BtnHome_Click(object sender, RoutedEventArgs e)
        {
            HomeContent.Visibility = Visibility.Visible;
            DownloadContent.Visibility = Visibility.Collapsed;
            AIChatContent.Visibility = Visibility.Collapsed;
            MoreContent.Visibility = Visibility.Collapsed;
            PlayerSeparator.Visibility = Visibility.Visible;
        }

        private void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            HomeContent.Visibility = Visibility.Collapsed;
            DownloadContent.Visibility = Visibility.Visible;
            AIChatContent.Visibility = Visibility.Collapsed;
            MoreContent.Visibility = Visibility.Collapsed;
            PlayerSeparator.Visibility = Visibility.Collapsed;
        }

        private void BtnAIChat_Click(object sender, RoutedEventArgs e)
        {
            HomeContent.Visibility = Visibility.Collapsed;
            DownloadContent.Visibility = Visibility.Collapsed;
            AIChatContent.Visibility = Visibility.Visible;
            MoreContent.Visibility = Visibility.Collapsed;
            PlayerSeparator.Visibility = Visibility.Collapsed;
        }

        private void BtnMore_Click(object sender, RoutedEventArgs e)
        {
            HomeContent.Visibility = Visibility.Collapsed;
            DownloadContent.Visibility = Visibility.Collapsed;
            AIChatContent.Visibility = Visibility.Collapsed;
            MoreContent.Visibility = Visibility.Visible;
            PlayerSeparator.Visibility = Visibility.Collapsed;
        }

        #region Download Logic

        private void DownloadTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb)
            {
                System.Windows.Controls.Panel? targetPanel = null;
                if (rb == TabGameCore) targetPanel = GameCorePanel;
                else if (rb == TabModSearch) targetPanel = ModSearchPanel;
                else if (rb == TabDownloadTasks) targetPanel = DownloadTasksPanel;

                if (targetPanel != null)
                {
                    GameCorePanel.Visibility = Visibility.Collapsed;
                    ModSearchPanel.Visibility = Visibility.Collapsed;
                    DownloadTasksPanel.Visibility = Visibility.Collapsed;
                    targetPanel.Visibility = Visibility.Visible;
                }
            }
        }

        private async void BtnRefreshVanilla_Click(object sender, RoutedEventArgs e)
        {
            if (_launcher == null) return;
            try
            {
                BtnRefreshVanilla.IsEnabled = false;
                BtnRefreshVanilla.Content = "刷新中...";
                DownloadableVanillaVersions.Clear();
                
                var versions = await _launcher.GetAllVersionsAsync();
                foreach (var v in versions.Where(x => x.Type == "release").Take(20))
                {
                    DownloadableVanillaVersions.Add(v.Name);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"刷新失败: {ex.Message}");
            }
            finally
            {
                BtnRefreshVanilla.IsEnabled = true;
                BtnRefreshVanilla.Content = "刷新版本列表";
            }
        }

        private void ListVanillaVersions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ListVanillaVersions.SelectedItem is string version)
            {
                VersionInstallPanel.Visibility = Visibility.Visible;
                TxtSelectedVersion.Text = version;
                // 默认选择 Vanilla
                RadioVanilla.IsChecked = true;
                LoaderRadio_Checked(RadioVanilla, new RoutedEventArgs());
            }
        }

        private async void LoaderRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton rb || TxtSelectedVersion == null || ComboLoaderVersions == null) return;

            string mcVersion = TxtSelectedVersion.Text;
            if (string.IsNullOrEmpty(mcVersion) || mcVersion == "选择一个版本") return;

            if (rb == RadioVanilla)
            {
                ComboLoaderVersions.Visibility = Visibility.Collapsed;
                TxtLoaderVersionLabel.Visibility = Visibility.Collapsed;
            }
            else
            {
                ComboLoaderVersions.Visibility = Visibility.Visible;
                TxtLoaderVersionLabel.Visibility = Visibility.Visible;
                TxtLoaderVersionLabel.Text = $"{rb.Content} 版本";
                
                try
                {
                    ComboLoaderVersions.Items.Clear();
                    ComboLoaderVersions.Items.Add("正在加载...");
                    ComboLoaderVersions.SelectedIndex = 0;

                    if (rb == RadioForge)
                    {
                        var forgeInstaller = new CmlLib.Core.Installer.Forge.ForgeInstaller(_launcher!);
                        var forgeVersions = await forgeInstaller.GetForgeVersions(mcVersion);
                        ComboLoaderVersions.Items.Clear();
                        foreach (var f in forgeVersions.Take(10))
                            ComboLoaderVersions.Items.Add(f.ForgeVersionName);
                    }
                    else if (rb == RadioFabric)
                    {
                        using var client = new System.Net.Http.HttpClient();
                        var response = await client.GetStringAsync($"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}");
                        var doc = JsonDocument.Parse(response);
                        ComboLoaderVersions.Items.Clear();
                        foreach (var item in doc.RootElement.EnumerateArray().Take(10))
                        {
                            var loader = item.GetProperty("loader");
                            ComboLoaderVersions.Items.Add(loader.GetProperty("version").GetString());
                        }
                    }
                    else if (rb == RadioQuilt)
                    {
                        using var client = new System.Net.Http.HttpClient();
                        var response = await client.GetStringAsync($"https://meta.quiltmc.org/v2/versions/loader/{mcVersion}");
                        var doc = JsonDocument.Parse(response);
                        ComboLoaderVersions.Items.Clear();
                        foreach (var item in doc.RootElement.EnumerateArray().Take(10))
                        {
                            var loader = item.GetProperty("loader");
                            ComboLoaderVersions.Items.Add(loader.GetProperty("version").GetString());
                        }
                    }

                    if (ComboLoaderVersions.Items.Count > 0)
                        ComboLoaderVersions.SelectedIndex = 0;
                    else
                        ComboLoaderVersions.Items.Add("无可用版本");
                }
                catch (Exception ex)
                {
                    ComboLoaderVersions.Items.Clear();
                    ComboLoaderVersions.Items.Add("加载失败");
                    WriteLog($"加载加载器版本失败: {ex.Message}");
                }
            }
        }

        private async void BtnInstallVersion_Click(object sender, RoutedEventArgs e)
        {
            if (ListVanillaVersions.SelectedItem is not string mcVersion || _launcher == null) return;

            string? loaderVersion = ComboLoaderVersions.SelectedItem as string;
            string loaderName = RadioForge.IsChecked == true ? "Forge" : RadioFabric.IsChecked == true ? "Fabric" : RadioQuilt.IsChecked == true ? "Quilt" : "";
            string taskName = RadioVanilla.IsChecked == true ? $"安装 {mcVersion}" : $"安装 {mcVersion} ({loaderName} {loaderVersion})";
            
            var task = new DownloadTask { Name = taskName, Progress = 0, Status = "准备中..." };
            DownloadTasks.Add(task);
            TabDownloadTasks.IsChecked = true;
            DownloadTab_Click(TabDownloadTasks, new RoutedEventArgs());

            try
            {
                _launcher.FileProgressChanged += (s, args) =>
                {
                    if (args.TotalTasks > 0)
                        task.Progress = (int)((double)args.ProgressedTasks / args.TotalTasks * 100);
                    task.Status = $"正在下载: {args.Name}";
                };

                try
                {
                    if (RadioVanilla.IsChecked == true)
                    {
                        await _launcher.InstallAsync(mcVersion);
                    }
                    else if (RadioForge.IsChecked == true && !string.IsNullOrEmpty(loaderVersion))
                    {
                        task.Status = "正在安装 Forge...";
                        var forgeInstaller = new CmlLib.Core.Installer.Forge.ForgeInstaller(_launcher);
                        await forgeInstaller.Install(mcVersion, loaderVersion);
                    }
                    else if (RadioFabric.IsChecked == true && !string.IsNullOrEmpty(loaderVersion))
                    {
                        task.Status = "正在安装 Fabric...";
                        MessageBox.Show("Fabric 自动安装正在开发中，请使用 Vanilla 核心后手动放入加载器。");
                        DownloadTasks.Remove(task);
                        return;
                    }
                    else
                    {
                        MessageBox.Show("请选择有效的加载器版本。");
                        DownloadTasks.Remove(task);
                        return;
                    }
                }
                finally
                {
                    // _launcher.FileProgressChanged -= progressHandler;
                }

                task.Progress = 100;
                task.Status = "安装完成";
                RefreshVersionList();
            }
            catch (Exception ex)
            {
                task.Status = $"错误: {ex.Message}";
                WriteLog($"安装失败: {ex}");
                MessageBox.Show($"安装失败: {ex.Message}");
            }
        }

        private async void BtnModSearch_Click(object sender, RoutedEventArgs e)
        {
            string query = TxtModSearchQuery.Text.Trim();
            if (string.IsNullOrEmpty(query)) return;

            try
            {
                ModSearchResults.Clear();
                if (ComboModSource.SelectedIndex == 0) // Modrinth
                {
                    using var client = new System.Net.Http.HttpClient();
                    client.DefaultRequestHeaders.Add("User-Agent", "MizuLauncher/1.0");
                    var response = await client.GetStringAsync($"https://api.modrinth.com/v2/search?query={query}&limit=10");
                    var doc = JsonDocument.Parse(response);
                    foreach (var item in doc.RootElement.GetProperty("hits").EnumerateArray())
                    {
                        ModSearchResults.Add(new ModInfo
                        { 
                            Name = item.GetProperty("title").GetString(),
                            Description = item.GetProperty("description").GetString(),
                            IconUrl = item.GetProperty("icon_url").GetString(),
                            ProjectId = item.GetProperty("project_id").GetString(),
                            Author = item.TryGetProperty("author", out var author) ? author.GetString() : "Unknown",
                            Categories = item.TryGetProperty("categories", out var cats) ? string.Join(", ", cats.EnumerateArray().Select(c => c.GetString())) : ""
                        });
                    }
                }
                else // CurseForge
                {
                    MessageBox.Show("CurseForge 搜索暂不可用（需要 API Key）。请使用 Modrinth。");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"搜索失败: {ex.Message}");
            }
        }

        private async void BtnShowModVersions_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ModInfo mod)
            {
                try
                {
                    ModVersions.Clear();
                    ListModResults.Visibility = Visibility.Collapsed;
                    ModVersionsPanel.Visibility = Visibility.Visible;
                    
                    using var client = new System.Net.Http.HttpClient();
                    client.DefaultRequestHeaders.Add("User-Agent", "MizuLauncher/1.0");
                    var response = await client.GetStringAsync($"https://api.modrinth.com/v2/project/{mod.ProjectId}/version");
                    var doc = JsonDocument.Parse(response);
                    
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        var file = item.GetProperty("files").EnumerateArray().First();
                        ModVersions.Add(new ModVersionInfo
                        {
                            Id = item.GetProperty("id").GetString(),
                            VersionNumber = item.GetProperty("version_number").GetString(),
                            Name = item.GetProperty("name").GetString(),
                            Type = item.GetProperty("version_type").GetString(),
                            GameVersions = item.GetProperty("game_versions").EnumerateArray().Select(v => v.GetString() ?? "").ToList(),
                            Loaders = item.GetProperty("loaders").EnumerateArray().Select(l => l.GetString() ?? "").ToList(),
                            DownloadUrl = file.GetProperty("url").GetString(),
                            FileName = file.GetProperty("filename").GetString()
                        });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"获取版本列表失败: {ex.Message}");
                    ListModResults.Visibility = Visibility.Visible;
                    ModVersionsPanel.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void BtnBackToModSearch_Click(object sender, RoutedEventArgs e)
        {
            ListModResults.Visibility = Visibility.Visible;
            ModVersionsPanel.Visibility = Visibility.Collapsed;
        }

        private async void BtnDownloadModVersion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ModVersionInfo version)
            {
                if (ListVersions.SelectedItem == null)
                {
                    MessageBox.Show("请先在首页选定一个 Minecraft 版本，模组将下载到该版本的隔离文件夹中。");
                    return;
                }

                string selectedVersion = ListVersions.SelectedItem.ToString()!;
                var task = new DownloadTask { Name = $"下载 Mod: {version.FileName}", Progress = 0, Status = "准备下载..." };
                DownloadTasks.Add(task);
                TabDownloadTasks.IsChecked = true;
                DownloadTab_Click(TabDownloadTasks, new RoutedEventArgs());

                try
                {
                    using var client = new System.Net.Http.HttpClient();
                    client.DefaultRequestHeaders.Add("User-Agent", "MizuLauncher/1.0");

                    task.Status = "开始下载...";
                    // 下载到选定版本的隔离文件夹中
                    string modsPath = Path.Combine(_baseMcPath?.BasePath ?? "", "versions", selectedVersion, "mods");
                    if (!Directory.Exists(modsPath)) Directory.CreateDirectory(modsPath);
                    
                    using var response = await client.GetAsync(version.DownloadUrl!, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                    using var fileStream = new FileStream(Path.Combine(modsPath, version.FileName!), FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    var buffer = new byte[8192];
                    var totalRead = 0L;
                    using var stream = await response.Content.ReadAsStreamAsync();
                    
                    int read;
                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read);
                        totalRead += read;
                        if (totalBytes != -1)
                            task.Progress = (int)((double)totalRead / totalBytes * 100);
                    }

                    task.Progress = 100;
                    task.Status = "下载完成";
                }
                catch (Exception ex)
                {
                    task.Status = $"错误: {ex.Message}";
                    MessageBox.Show($"下载失败: {ex.Message}");
                }
            }
        }

        private void BtnClearCompleted_Click(object sender, RoutedEventArgs e)
        {
            var completed = DownloadTasks.Where(x => x.Progress == 100).ToList();
            foreach (var t in completed)
                DownloadTasks.Remove(t);
        }

        private void BtnOpenModsFolder_Click(object sender, RoutedEventArgs e)
        {
            string modsPath;
            if (ListVersions.SelectedItem != null)
            {
                string selectedVersion = ListVersions.SelectedItem.ToString()!;
                modsPath = Path.Combine(_baseMcPath?.BasePath ?? "", "versions", selectedVersion, "mods");
            }
            else
            {
                modsPath = Path.Combine(_baseMcPath?.BasePath ?? "", "mods");
            }

            if (!Directory.Exists(modsPath)) Directory.CreateDirectory(modsPath);
            System.Diagnostics.Process.Start("explorer.exe", modsPath);
        }

        #endregion

        private void TxtDeepSeekApiKey_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox pb)
            {
                _deepSeekApiKey = pb.Password;
                SaveConfig();
            }
        }

        private void ComboDeepSeekModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem item)
            {
                _deepSeekModel = item.Content.ToString() ?? "deepseek-chat";
                SaveConfig();
            }
        }

        private void TxtGlmApiKey_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox pb)
            {
                _glmApiKey = pb.Password;
                SaveConfig();
            }
        }

        private void ComboGlmModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem item)
            {
                _glmModel = item.Content.ToString() ?? "glm-4.7-flash";
                SaveConfig();
            }
        }

        private void ComboAiProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem item)
            {
                _aiProvider = item.Content.ToString() ?? "DeepSeek";
                SaveConfig();
            }
        }

        public CmlLib.Core.MinecraftPath? GetBaseMcPath() => _baseMcPath;
        public CmlLib.Core.MinecraftLauncher? GetLauncher() => _launcher;
        public void CallRefreshVersionList() => RefreshVersionList();

        public void UpdateMainProgress(string status, double progress)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    TxtProgressStep.Text = status;
                    if (progress >= 0)
                        RectProgress.Width = Math.Clamp(progress, 0, 1) * BorderProgress.ActualWidth;
                }
                catch { }
            });
        }

        private async void BtnAIChatSend_Click(object sender, RoutedEventArgs e)
        {
            string userInput = TxtAIChatInput.Text.Trim();
            if (string.IsNullOrEmpty(userInput)) return;

            string currentModelId;
            string currentApiKey;
            Uri currentEndpoint;

            if (_aiProvider.Contains("GLM"))
            {
                if (string.IsNullOrEmpty(_glmApiKey))
                {
                    MessageBox.Show("请先在“更多”页面设置 GLM API Key。");
                    return;
                }
                currentModelId = _glmModel;
                currentApiKey = _glmApiKey;
                currentEndpoint = new Uri("https://open.bigmodel.cn/api/paas/v4/");
            }
            else
            {
                if (string.IsNullOrEmpty(_deepSeekApiKey))
                {
                    MessageBox.Show("请先在“更多”页面设置 DeepSeek API Key。");
                    return;
                }
                currentModelId = _deepSeekModel;
                currentApiKey = _deepSeekApiKey;
                currentEndpoint = new Uri("https://api.deepseek.com/v1");
            }

            TxtAIResponse.Text = "思考中...";
            TxtAIChatInput.Clear();

            try
            {
                // 获取当前选择的版本信息
                string selectedVersion = ListVersions.SelectedItem?.ToString() ?? "未选择";
                
                // 构造系统提示词，包含当前环境信息
                string systemPrompt = $@"你是一个专业的 Minecraft 启动器助手。
当前用户选择的游戏版本是：{selectedVersion}。
你的任务是帮助用户管理模组、解决问题，以及安装新的游戏版本或核心。

当用户要求下载模组时：
1. 必须先使用 SearchModAsync 搜索模组。
2. 然后使用 GetModVersionsAsync 获取对应版本和加载器的版本列表。
3. 最后选择正确的下载链接调用 DownloadModAsync。

当用户要求安装新版本或核心时：
1. 如果是安装纯净版，直接调用 InstallVanillaAsync。
2. 如果是安装 Forge，先调用 GetForgeVersionsAsync 获取可用版本，然后调用 InstallForgeAsync。
3. 如果用户没指定具体版本（如“安装 1.20.1”），默认安装纯净版；如果指定了核心（如“安装 1.20.1 的 forge”），则按需操作。

请始终确保下载的模组版本与用户当前选择的游戏版本（{selectedVersion}）和加载器兼容。";

                var builder = Kernel.CreateBuilder();
                
                // 使用 OpenAI 接口兼容 DeepSeek 或 GLM
                builder.AddOpenAIChatCompletion(
                    modelId: currentModelId,
                    apiKey: currentApiKey,
                    endpoint: currentEndpoint
                );

                builder.Plugins.AddFromObject(new MinecraftResourcePlugin(this));
                var kernel = builder.Build();

                // 开启自动函数调用
                var executionSettings = new OpenAIPromptExecutionSettings 
                { 
                    ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions 
                };

                var chatHistory = new ChatHistory(systemPrompt);
                chatHistory.AddUserMessage(userInput);

                var chatService = kernel.GetRequiredService<IChatCompletionService>();
                var result = await chatService.GetChatMessageContentAsync(chatHistory, executionSettings, kernel);

                TxtAIResponse.Text = result.Content ?? "无响应内容";
            }
            catch (Exception ex)
            {
                TxtAIResponse.Text = $"错误: {ex.Message}";
            }
        }

        private void TxtAIChatInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnAIChatSend_Click(sender, e);
            }
        }


        private void RadioBg_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb)
            {
                if (rb == RadioMica) _currentBgType = 2;
                else if (rb == RadioAcrylic) _currentBgType = 3;
                else if (rb == RadioButtonSolid) _currentBgType = 1;

                UpdateBackgroundUIFromState();
                SaveConfig();
            }
        }

        private void ColorBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string colorStr)
            {
                try
                {
                    var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorStr);
                    MainRoot.Background = new System.Windows.Media.SolidColorBrush(color);
                    _currentCustomColor = colorStr;
                    TxtCustomColor.Text = colorStr;
                    SaveConfig();
                }
                catch { }
            }
        }

        private void ApplyCustomColor_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string input = TxtCustomColor.Text;
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(input);
                MainRoot.Background = new System.Windows.Media.SolidColorBrush(color);
                _currentCustomColor = input;
                SaveConfig();
            }
            catch
            {
                MessageBox.Show("无效的颜色代码");
            }
        }

        #endregion

        private void RefreshVersionList()
        {
            try
            {
                if (_baseMcPath == null) return;
                ListVersions.Items.Clear();
                string versionsDir = _baseMcPath.Versions;
                if (Directory.Exists(versionsDir))
                {
                    string[] localVersionDirs = Directory.GetDirectories(versionsDir);
                    foreach (string dir in localVersionDirs)
                    {
                        string versionName = Path.GetFileName(dir);
                        ListVersions.Items.Add(versionName);
                    }
                }

                if (ListVersions.Items.Count > 0)
                {
                    ListVersions.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("扫描本地版本失败: " + ex.Message);
            }
        }

        #region Configuration Storage (Safe)

        private int _currentBgType = 3; // Default Acrylic
        private string _currentCustomColor = "#FF1E1E1E";
        private string _currentPlayerName = "添加玩家";
        private bool _showDragHint = true;
        private string _deepSeekApiKey = "";
        private string _deepSeekModel = "deepseek-chat";
        private string _glmApiKey = "";
        private string _glmModel = "glm-4.7-flash";
        private string _aiProvider = "DeepSeek";

        private string ConfigExportDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "MizuLauncher_Configs");
        private string GlmKeyPath => Path.Combine(ConfigExportDir, "glm_api_key.txt");
        private string DeepSeekKeyPath => Path.Combine(ConfigExportDir, "deepseek_api_key.txt");

        private void SaveConfig()
        {
            try
            {
                var config = new LauncherConfig
                {
                    QuickLaunchVersions = QuickLaunchVersions.ToList(),
                    BackgroundType = _currentBgType,
                    CustomColor = _currentCustomColor,
                    PlayerName = _currentPlayerName,
                    Players = OnlinePlayers.Concat(OfflinePlayers).ToList(),
                    ShowDragHint = _showDragHint,
                    DeepSeekApiKey = _deepSeekApiKey,
                    DeepSeekModel = _deepSeekModel,
                    GlmApiKey = _glmApiKey,
                    GlmModel = _glmModel,
                    AiProvider = _aiProvider
                };
                string json = JsonSerializer.Serialize(config);
                File.WriteAllText(ConfigFileName, json);

                // 实时同步 API Key 到配置文件夹根目录
                Directory.CreateDirectory(ConfigExportDir);
                if (!string.IsNullOrEmpty(_glmApiKey)) File.WriteAllText(GlmKeyPath, _glmApiKey);
                if (!string.IsNullOrEmpty(_deepSeekApiKey)) File.WriteAllText(DeepSeekKeyPath, _deepSeekApiKey);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Save config error: {ex.Message}");
            }
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigFileName))
                {
                    string json = File.ReadAllText(ConfigFileName);
                    var config = JsonSerializer.Deserialize<LauncherConfig>(json);
                    if (config != null)
                    {
                        if (config.QuickLaunchVersions != null)
                        {
                            QuickLaunchVersions.Clear();
                            foreach (var v in config.QuickLaunchVersions)
                            {
                                if (QuickLaunchVersions.Count < MaxQuickLaunchItems)
                                    QuickLaunchVersions.Add(v);
                            }
                        }
                        _currentBgType = config.BackgroundType;
                        _currentCustomColor = config.CustomColor ?? "#FF1E1E1E";
                        _currentPlayerName = config.PlayerName ?? "添加玩家";
                        _showDragHint = config.ShowDragHint;
                        _deepSeekApiKey = config.DeepSeekApiKey ?? "";
                        _deepSeekModel = config.DeepSeekModel ?? "deepseek-chat";
                        _glmApiKey = config.GlmApiKey ?? "";
                        _glmModel = config.GlmModel ?? "glm-4.7-flash";
                        _aiProvider = config.AiProvider ?? "DeepSeek";

                        // 从桌面模拟 AppData 文件夹恢复 (优先级更高，模拟实时同步)
                        if (File.Exists(GlmKeyPath)) _glmApiKey = File.ReadAllText(GlmKeyPath).Trim();
                        if (File.Exists(DeepSeekKeyPath)) _deepSeekApiKey = File.ReadAllText(DeepSeekKeyPath).Trim();

                        TxtDeepSeekApiKey.Password = _deepSeekApiKey;
                        foreach (ComboBoxItem item in ComboDeepSeekModel.Items)
                        {
                            if (item.Content.ToString() == _deepSeekModel)
                            {
                                ComboDeepSeekModel.SelectedItem = item;
                                break;
                            }
                        }

                        TxtGlmApiKey.Password = _glmApiKey;
                        foreach (ComboBoxItem item in ComboGlmModel.Items)
                        {
                            if (item.Content.ToString() == _glmModel)
                            {
                                ComboGlmModel.SelectedItem = item;
                                break;
                            }
                        }

                        foreach (ComboBoxItem item in ComboAiProvider.Items)
                        {
                            if (item.Content.ToString() == _aiProvider)
                            {
                                ComboAiProvider.SelectedItem = item;
                                break;
                            }
                        }

                        if (config.Players != null)
                        {
                            OnlinePlayers.Clear();
                            OfflinePlayers.Clear();
                            foreach (var p in config.Players)
                            {
                                if (p.IsOnline) OnlinePlayers.Add(p);
                                else OfflinePlayers.Add(p);
                                _ = LoadPlayerAvatarAsync(p);
                            }
                        }
                        
                        // Apply to UI state
                        UpdateBackgroundUIFromState();
                        _ = UpdatePlayerUIFromState();
                    }
                }
                else
                {
                    // No config file, ensure defaults are applied
                    _showDragHint = true;
                    UpdateBackgroundUIFromState();
                    _ = UpdatePlayerUIFromState();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Load config error: {ex.Message}");
            }
        }

        private async Task LoadPlayerAvatarAsync(PlayerInfo player)
        {
            player.Avatar = await LittleSkinFetcher.GetAvatarAsync(player.Name);
        }

        private void UpdateBackgroundUIFromState()
        {
            if (RadioMica == null || RadioAcrylic == null || RadioButtonSolid == null) return;

            if (_currentBgType == 2) RadioMica.IsChecked = true;
            else if (_currentBgType == 3) RadioAcrylic.IsChecked = true;
            else if (_currentBgType == 1) RadioButtonSolid.IsChecked = true;
            
            TxtCustomColor.Text = _currentCustomColor;
            CheckShowDragHint.IsChecked = _showDragHint;
            UpdateDragHintVisibility();

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            // 核心逻辑：
            // 1. 如果用户选择亚克力 (3)，则根据是否聚焦切换：聚焦用亚克力 (3)，不聚焦用云母 (2)
            // 2. 如果用户选择云母 (2)，则始终用云母 (2)
            // 3. 纯色 (1) 正常切换
            int effectiveType = _currentBgType;
            if (_currentBgType == 3 && !this.IsActive)
            {
                effectiveType = 2; // 亚克力失去焦点自动切为云母
            }
            
            EnableMicaBackdrop(hwnd, effectiveType);
            
            if (_currentBgType == 1)
            {
                SolidColorSettings.Visibility = Visibility.Visible;
                BgTint.Visibility = Visibility.Visible; // 纯色模式也启用蒙层用于过渡
                MainRoot.Background = System.Windows.Media.Brushes.Transparent; // 保持 Root 透明

                try
                {
                    var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_currentCustomColor);
                    AnimateBgTint(color);
                }
                catch { }
            }
            else
            {
                MainRoot.Background = System.Windows.Media.Brushes.Transparent;
                BgTint.Visibility = Visibility.Visible;

                System.Windows.Media.Color targetColor;
                // 仅亚克力模式在失去焦点时执行背景色切换 (切为深灰蓝并辅以云母底层)
                if (_currentBgType == 3 && !this.IsActive)
                {
                    // 失去焦点时的亚克力回退色：稍微浅一点的深灰蓝 (#252A2E)
                    targetColor = System.Windows.Media.Color.FromRgb(0x25, 0x2A, 0x2E);
                }
                else if (_currentBgType == 3) // Acrylic Active
                {
                    targetColor = System.Windows.Media.Color.FromArgb(0x60, 0x00, 0x00, 0x00);
                }
                else // Mica (无论聚焦与否都保持云母遮罩色)
                {
                    targetColor = System.Windows.Media.Color.FromArgb(0x15, 0x00, 0x00, 0x00);
                }

                AnimateBgTint(targetColor);
            }
        }

        private void AnimateBgTint(System.Windows.Media.Color targetColor)
        {
            if (BgTint.Background is not System.Windows.Media.SolidColorBrush currentBrush)
            {
                BgTint.Background = new System.Windows.Media.SolidColorBrush(targetColor);
                return;
            }

            // 如果当前是冻结的，我们需要创建一个新的
            if (currentBrush.IsFrozen)
            {
                currentBrush = new System.Windows.Media.SolidColorBrush(currentBrush.Color);
                BgTint.Background = currentBrush;
            }

            ColorAnimation animation = new ColorAnimation
            {
                To = targetColor,
                Duration = TimeSpan.FromMilliseconds(400),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            currentBrush.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, animation);
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        private void EnableMicaBackdrop(IntPtr hwnd, int type = 2)
        {
            try
            {
                // 1. 强制窗口使用深色模式
                int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
                int useDarkMode = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, Marshal.SizeOf(typeof(int)));

                // 2. 启用材质
                int DWMWA_SYSTEMBACKDROP_TYPE = 38;
                DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, Marshal.SizeOf(typeof(int)));

                // 3. 启用系统原生圆角
                int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
                int DWMWCP_ROUND = 2;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref DWMWCP_ROUND, Marshal.SizeOf(typeof(int)));
            }
            catch (Exception ex)
            {
                WriteLog($"DWM设置失败: {ex.Message}");
            }
        }

        private void CheckShowDragHint_Click(object sender, RoutedEventArgs e)
        {
            _showDragHint = CheckShowDragHint.IsChecked ?? true;
            UpdateDragHintVisibility();
            SaveConfig();
        }

        private void UpdateDragHintVisibility()
        {
            if (TxtDragHint == null) return;
            
            if (!_showDragHint)
            {
                TxtDragHint.Visibility = Visibility.Collapsed;
            }
            else
            {
                // 如果启用了提示，则由 Style 中的 DataTrigger 决定是否显示（当磁贴数为0时显示）
                // 这里我们手动刷新一下，或者由于 Style 已经绑定了，我们可以通过设置一个本地值来覆盖或清除
                TxtDragHint.ClearValue(TextBlock.VisibilityProperty);
            }
        }

        private async Task UpdatePlayerUIFromState()
        {
            try
            {
                TxtPlayerName.Text = _currentPlayerName;
                
                // 立即显示默认状态/清除旧头像，防止切换时残留上一个玩家的头像
                ImgPlayerAvatar.Source = null;

                var currentPlayer = OnlinePlayers.FirstOrDefault(p => p.Name == _currentPlayerName)
                                  ?? OfflinePlayers.FirstOrDefault(p => p.Name == _currentPlayerName);

                if (currentPlayer != null)
                {
                    TxtPlayerType.Text = currentPlayer.IsOnline ? "正版账号" : "离线模式";
                }
                else
                {
                    TxtPlayerType.Text = "离线模式";
                }

                var avatar = await LittleSkinFetcher.GetAvatarAsync(_currentPlayerName);
                if (avatar != null)
                {
                    ImgPlayerAvatar.Source = avatar;
                }
                else
                {
                    ImgPlayerAvatar.Source = null; // 清空头像或显示默认占位
                }
                
                // 如果是“添加玩家”，禁用启动按钮
                BtnLaunch.IsEnabled = _currentPlayerName != "添加玩家";
            }
            catch (Exception ex)
            {
                WriteLog($"更新玩家UI异常: {ex.Message}");
            }
        }

        private void DeletePlayer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is MenuItem menuItem && menuItem.Parent is ContextMenu contextMenu)
                {
                    var border = contextMenu.PlacementTarget as Border;
                    if (border != null && border.DataContext is PlayerInfo player)
                    {
                        if (player.IsOnline) OnlinePlayers.Remove(player);
                        else OfflinePlayers.Remove(player);
                        
                        // 如果删掉的是当前玩家，重置为默认
                        if (_currentPlayerName == player.Name)
                        {
                            _currentPlayerName = "添加玩家";
                            _ = UpdatePlayerUIFromState();
                        }
                        SaveConfig();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Delete player error: " + ex.Message);
            }
        }

        private void BtnAddPlayer_Click(object sender, RoutedEventArgs e)
        {
            // 关闭 Popup
            BtnPlayerCard.IsChecked = false;

            // 弹出自定义添加账号窗口
            var addWindow = new AddAccountWindow();
            addWindow.Owner = this;
            if (addWindow.ShowDialog() == true && addWindow.ResultPlayer != null)
            {
                AddPlayer(addWindow.ResultPlayer);
            }
        }

        private async void AddPlayer(PlayerInfo player)
        {
            // 检查是否已存在
            var existing = OnlinePlayers.FirstOrDefault(p => p.Name == player.Name && p.IsOnline == player.IsOnline)
                        ?? OfflinePlayers.FirstOrDefault(p => p.Name == player.Name && p.IsOnline == player.IsOnline);

            if (existing == null)
            {
                if (player.IsOnline) OnlinePlayers.Add(player);
                else OfflinePlayers.Add(player);
                
                _currentPlayerName = player.Name;
                await UpdatePlayerUIFromState();
                SaveConfig();
                
                // 异步加载头像
                player.Avatar = await LittleSkinFetcher.GetAvatarAsync(player.Name);
            }
            else
            {
                _currentPlayerName = existing.Name;
                await UpdatePlayerUIFromState();
                SaveConfig();
            }
        }

        private async void ListPlayers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox lb && lb.SelectedItem is PlayerInfo selected)
            {
                _currentPlayerName = selected.Name;
                await UpdatePlayerUIFromState();
                SaveConfig();
                BtnPlayerCard.IsChecked = false; // 关闭 Popup
                lb.SelectedItem = null; // 重置选择
            }
        }

        public class PlayerInfo : System.ComponentModel.INotifyPropertyChanged
        {
            private string _name = "";
            private System.Windows.Media.Imaging.BitmapImage? _avatar;
            private bool _isOnline = false;
            private string? _uuid;
            private string? _accessToken;

            public string Name 
            { 
                get => _name; 
                set { _name = value; OnPropertyChanged(nameof(Name)); }
            }

            public bool IsOnline
            {
                get => _isOnline;
                set { _isOnline = value; OnPropertyChanged(nameof(IsOnline)); }
            }

            public string? UUID
            {
                get => _uuid;
                set { _uuid = value; OnPropertyChanged(nameof(UUID)); }
            }

            public string? AccessToken
            {
                get => _accessToken;
                set { _accessToken = value; OnPropertyChanged(nameof(AccessToken)); }
            }

            [System.Text.Json.Serialization.JsonIgnore]
            public System.Windows.Media.Imaging.BitmapImage? Avatar 
            { 
                get => _avatar; 
                set { _avatar = value; OnPropertyChanged(nameof(Avatar)); }
            }

            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }

        public class LauncherConfig
        {
            public System.Collections.Generic.List<string>? QuickLaunchVersions { get; set; }
            public int BackgroundType { get; set; } = 3; // Default Acrylic
            public string? CustomColor { get; set; } = "#FF1E1E1E";
            public string PlayerName { get; set; } = "添加玩家";
            public System.Collections.Generic.List<PlayerInfo>? Players { get; set; }
            public bool ShowDragHint { get; set; } = true;
            public string? DeepSeekApiKey { get; set; }
            public string? DeepSeekModel { get; set; }
            public string? GlmApiKey { get; set; }
            public string? GlmModel { get; set; }
            public string? AiProvider { get; set; } = "DeepSeek";
        }

        #endregion

        #region Window Controls

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        #endregion

        #region Drag and Drop Logic (Protected)

        private void ListVersions_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement fe &&
               (fe.GetType().Name.Contains("Scroll") ||
               (fe.TemplatedParent?.GetType().Name.Contains("Scroll") == true)))
            {
                return;
            }
            _dragStartPoint = e.GetPosition(null);
            WriteLog("鼠标左键按下，记录坐标。");
        }

        private void ListVersions_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                Point mousePos = e.GetPosition(null);
                Vector diff = _dragStartPoint - mousePos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (sender is ListBox listBox && listBox.SelectedItem != null)
                    {
                        string selectedVersion = listBox.SelectedItem.ToString() ?? "";
                        _isDragging = true;

                        WriteLog($"检测到移动，准备拖拽版本: {selectedVersion}");

                        if (e.OriginalSource is UIElement uiElement)
                        {
                            uiElement.ReleaseMouseCapture();
                        }
                        Mouse.Capture(null);

                        // 将系统级拖拽推迟到 UI 线程执行
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                WriteLog("--> 开始执行 DoDragDrop 系统方法");
                                DragDrop.DoDragDrop(listBox, selectedVersion, DragDropEffects.Copy);
                                WriteLog("--> DoDragDrop 执行完毕，用户松开了鼠标");
                            }
                            catch (Exception ex)
                            {
                                WriteLog($"拖拽内部异常: {ex.Message}");
                            }
                            finally
                            {
                                _isDragging = false;
                            }
                        }), System.Windows.Threading.DispatcherPriority.Background);
                    }
                }
            }
        }

        private void QuickLaunch_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.StringFormat))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void QuickLaunch_Drop(object sender, DragEventArgs e)
        {
            WriteLog("触发 Drop (放下) 事件！");
            try
            {
                if (QuickLaunchVersions.Count < MaxQuickLaunchItems && e.Data.GetDataPresent(DataFormats.StringFormat))
                {
                    string? version = e.Data.GetData(DataFormats.StringFormat) as string;
                    WriteLog($"正在尝试将 [{version}] 加入快速启动栏");

                    if (!string.IsNullOrEmpty(version) && !QuickLaunchVersions.Contains(version))
                    {
                        QuickLaunchVersions.Add(version);
                        WriteLog("加入成功！");
                    }
                    else
                    {
                        WriteLog("加入失败：版本为空，或该卡片已存在。");
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"Drop 崩溃: {ex.Message}");
            }
        }

        // 暂时注销单向拖拽事件
        private void QuickLaunchCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) { }
        private void QuickLaunchCard_MouseMove(object sender, MouseEventArgs e) { }
        private void ListVersions_DragOver(object sender, DragEventArgs e) { }
        private void ListVersions_Drop(object sender, DragEventArgs e) { }

        #endregion

        #region Drag and Drop Logic (Protected)

        public void DeleteQuickLaunchCard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is MenuItem menuItem && menuItem.Parent is ContextMenu contextMenu)
                {
                    FrameworkElement? target = contextMenu.PlacementTarget as FrameworkElement;
                    Button? button = target as Button;

                    if (button == null && target != null)
                    {
                        button = target.TemplatedParent as Button;
                    }

                    if (button != null && button.Content != null)
                    {
                        string version = button.Content.ToString() ?? "";
                        QuickLaunchVersions.Remove(version);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Delete card error: " + ex.Message);
            }
        }

        #endregion

        #region Launch Logic (Protected)

        private async void LaunchGame(string versionName)
        {
            if (string.IsNullOrEmpty(versionName) || _baseMcPath == null) return;

            try
            {
                BtnLaunch.IsEnabled = false;
                TxtProgressStep.Text = "正在准备...";
                RectProgress.Width = 0;

                var isolatedPath = new MinecraftPath(Path.Combine(_baseMcPath.BasePath, "versions", versionName));
                isolatedPath.Assets = _baseMcPath.Assets;
                isolatedPath.Library = _baseMcPath.Library;
                isolatedPath.Runtime = _baseMcPath.Runtime;
                isolatedPath.Versions = _baseMcPath.Versions;

                var currentLauncher = new MinecraftLauncher(isolatedPath);

                currentLauncher.FileProgressChanged += (s, e) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            TxtProgressStep.Text = $"{e.Name} ({e.ProgressedTasks}/{e.TotalTasks})";
                            if (e.TotalTasks > 0)
                                RectProgress.Width = (double)e.ProgressedTasks / e.TotalTasks * BorderProgress.ActualWidth;
                        }
                        catch { }
                    });
                };

                currentLauncher.ByteProgressChanged += (s, e) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            if (e.TotalBytes > 0)
                                RectProgress.Width = (double)e.ProgressedBytes / e.TotalBytes * BorderProgress.ActualWidth;
                        }
                        catch { }
                    });
                };

                var currentPlayer = OnlinePlayers.FirstOrDefault(p => p.Name == _currentPlayerName)
                                  ?? OfflinePlayers.FirstOrDefault(p => p.Name == _currentPlayerName);

                MSession session;
                if (currentPlayer != null && currentPlayer.IsOnline && !string.IsNullOrEmpty(currentPlayer.AccessToken))
                {
                    session = new MSession
                    {
                        Username = currentPlayer.Name,
                        UUID = currentPlayer.UUID,
                        AccessToken = currentPlayer.AccessToken,
                        UserType = "msa"
                    };
                }
                else
                {
                    session = MSession.CreateOfflineSession(_currentPlayerName);
                }

                var launchOption = new MLaunchOption
                {
                    Path = isolatedPath,
                    MaximumRamMb = 4096,
                    Session = session
                };

                TxtProgressStep.Text = "正在校验资源...";
                var process = await currentLauncher.InstallAndBuildProcessAsync(versionName, launchOption);

                TxtProgressStep.Text = "游戏已启动！";
                RectProgress.Width = BorderProgress.ActualWidth;
                process.Start();
            }
            catch (Exception ex)
            {
                TxtProgressStep.Text = "启动失败";
                MessageBox.Show("启动失败: " + ex.Message, "错误");
            }
            finally
            {
                BtnLaunch.IsEnabled = true;
            }
        }

        private void BtnLaunch_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentPlayerName == "添加玩家")
                {
                    MessageBox.Show("请先点击左下角添加玩家账号。");
                    return;
                }

                if (ListVersions.SelectedItem != null)
                {
                    LaunchGame(ListVersions.SelectedItem.ToString() ?? "");
                }
                else
                {
                    MessageBox.Show("请先从列表中选择一个版本。");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Launch click error: " + ex.Message);
            }
        }

        private void QuickLaunchCard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.Content != null)
                {
                    LaunchGame(btn.Content.ToString() ?? "");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Quick launch error: " + ex.Message);
            }
        }

        private void BtnVersionSettings_Click(object sender, RoutedEventArgs e)
        {
            if (VersionSettingsPanel.Visibility == Visibility.Collapsed)
            {
                QuickLaunchPanel.Visibility = Visibility.Collapsed;
                VersionSettingsPanel.Visibility = Visibility.Visible;
                RefreshSavedConfigs();
            }
            else
            {
                QuickLaunchPanel.Visibility = Visibility.Visible;
                VersionSettingsPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void RefreshSavedConfigs()
        {
            try
            {
                ComboSavedConfigs.Items.Clear();
                string keybindsDir = Path.Combine(ConfigExportDir, "Keybinds");
                if (Directory.Exists(keybindsDir))
                {
                    var dirs = Directory.GetDirectories(keybindsDir);
                    foreach (var dir in dirs)
                    {
                        ComboSavedConfigs.Items.Add(Path.GetFileName(dir));
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"刷新保存配置失败: {ex.Message}");
            }
        }

        private void BtnExportConfig_Click(object sender, RoutedEventArgs e)
        {
            if (ListVersions.SelectedItem == null)
            {
                MessageBox.Show("请先在左侧选择一个版本。");
                return;
            }

            string versionName = ListVersions.SelectedItem.ToString()!;
            string optionsPath = Path.Combine(_baseMcPath!.BasePath, "versions", versionName, "options.txt");

            if (!File.Exists(optionsPath))
            {
                MessageBox.Show($"在版本 {versionName} 中找不到 options.txt。");
                return;
            }

            try
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string exportSubDir = Path.Combine(ConfigExportDir, "Keybinds", $"{versionName}_{timestamp}");
                Directory.CreateDirectory(exportSubDir);

                // 1. 导出 options.txt 中的键位
                var lines = File.ReadAllLines(optionsPath);
                var keybindLines = lines.Where(l => l.StartsWith("key_") || l.StartsWith("soundCategory_") || l.StartsWith("modelPart_")).ToList();
                File.WriteAllLines(Path.Combine(exportSubDir, "keybinds.txt"), keybindLines);

                // 2. 导出 API Keys 备份到该子文件夹
                StringBuilder sb = new StringBuilder();
                if (!string.IsNullOrEmpty(_deepSeekApiKey)) sb.AppendLine($"DeepSeekApiKey:{_deepSeekApiKey}");
                if (!string.IsNullOrEmpty(_glmApiKey)) sb.AppendLine($"GlmApiKey:{_glmApiKey}");
                File.WriteAllText(Path.Combine(exportSubDir, "apikeys.txt"), sb.ToString());

                MessageBox.Show($"配置已导出！\n键位和 API Key 备份已保存至 Keybinds\\{versionName}_{timestamp}");
                RefreshSavedConfigs();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败: {ex.Message}");
                WriteLog($"导出配置失败: {ex}");
            }
        }

        private void BtnImportConfig_Click(object sender, RoutedEventArgs e)
        {
            if (ListVersions.SelectedItem == null)
            {
                MessageBox.Show("请先在左侧选择一个版本。");
                return;
            }

            if (ComboSavedConfigs.SelectedItem == null)
            {
                MessageBox.Show("请选择要导入的键位备份。");
                return;
            }

            string versionName = ListVersions.SelectedItem.ToString()!;
            string configName = ComboSavedConfigs.SelectedItem.ToString()!;
            string optionsPath = Path.Combine(_baseMcPath!.BasePath, "versions", versionName, "options.txt");
            string importDir = Path.Combine(ConfigExportDir, "Keybinds", configName);

            try
            {
                // 1. 导入选中备份文件夹下的 API Keys (如果存在备份)
                string apiKeysFile = Path.Combine(importDir, "apikeys.txt");
                if (File.Exists(apiKeysFile))
                {
                    var apiLines = File.ReadAllLines(apiKeysFile);
                    foreach (var line in apiLines)
                    {
                        var parts = line.Split(':', 2);
                        if (parts.Length == 2)
                        {
                            string key = parts[0].Trim();
                            string value = parts[1].Trim();
                            if (key == "DeepSeekApiKey") 
                            {
                                _deepSeekApiKey = value;
                                TxtDeepSeekApiKey.Password = value;
                            }
                            else if (key == "GlmApiKey") 
                            {
                                _glmApiKey = value;
                                TxtGlmApiKey.Password = value;
                            }
                        }
                    }
                    SaveConfig(); // 这也会自动更新根目录下的 api_key.txt
                }

                // 2. 导入选中备份文件夹下的键位配置
                string keybindsFile = Path.Combine(importDir, "keybinds.txt");
                if (File.Exists(keybindsFile))
                {
                    var importKeybinds = File.ReadAllLines(keybindsFile)
                        .Select(l => l.Split(':', 2))
                        .Where(parts => parts.Length == 2)
                        .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim());

                    List<string> currentLines = File.Exists(optionsPath) 
                        ? File.ReadAllLines(optionsPath).ToList() 
                        : new List<string>();

                    var currentSettings = currentLines
                        .Select(l => l.Split(':', 2))
                        .Where(parts => parts.Length == 2)
                        .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim());

                    foreach (var kvp in importKeybinds)
                    {
                        currentSettings[kvp.Key] = kvp.Value;
                    }

                    var newLines = currentSettings.Select(kvp => $"{kvp.Key}:{kvp.Value}");
                    File.WriteAllLines(optionsPath, newLines);
                }

                MessageBox.Show("配置导入完成！键位已合并。");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导入失败: {ex.Message}");
                WriteLog($"导入配置失败: {ex}");
            }
        }

        #endregion
    }
}