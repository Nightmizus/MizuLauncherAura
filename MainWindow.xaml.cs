using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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

    public class VersionItemInfo
    {
        public string? Name { get; set; }
        public string? IconPath { get; set; }
        public string? TypeDisplay { get; set; } // 原版/Forge/Fabric
        public string? VersionDisplay { get; set; } // 1.XX.X
        public string? LoaderDisplay { get; set; } // 加载器版本
        public string Details
        {
            get
            {
                if (TypeDisplay == "原版") return VersionDisplay ?? "";
                string loaderInfo = $"{TypeDisplay} {LoaderDisplay}".Trim();
                return string.IsNullOrEmpty(VersionDisplay) ? loaderInfo : $"{VersionDisplay}, {loaderInfo}";
            }
        }
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
        private AIOutputWindow? _aiOutputWindow;
        private const string ConfigFileName = "launcher_config.json";

        public ObservableCollection<PlayerInfo> OnlinePlayers { get; set; } = new();
        public ObservableCollection<PlayerInfo> OfflinePlayers { get; set; } = new();
        public ObservableCollection<string> DownloadableVanillaVersions { get; set; } = new();
        public ObservableCollection<ModInfo> ModSearchResults { get; set; } = new();
        public ObservableCollection<ModVersionInfo> ModVersions { get; set; } = new();
        public ObservableCollection<VersionItemInfo> FilteredVersions { get; set; } = new();
        public ObservableCollection<DownloadTask> DownloadTasks { get; set; } = new();
        private List<string> _allLocalVersions = new();

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

                OnlinePlayers = new ObservableCollection<PlayerInfo>();
                ListOnlinePlayers.ItemsSource = OnlinePlayers;

                OfflinePlayers = new ObservableCollection<PlayerInfo>();
                ListOfflinePlayers.ItemsSource = OfflinePlayers;

                ListVanillaVersions.ItemsSource = DownloadableVanillaVersions;
                ListModResults.ItemsSource = ModSearchResults;
                ListModVersions.ItemsSource = ModVersions;
                ListDownloadTasks.ItemsSource = DownloadTasks;
                ListVersionsCenter.ItemsSource = FilteredVersions;

                string mcDirPath = @"C:\Users\Mizusumi\Personal\play\mc\.minecraft";
                _baseMcPath = new MinecraftPath(mcDirPath);
                _launcher = new MinecraftLauncher(_baseMcPath);

                this.Loaded += MainWindow_Loaded;
                this.Activated += (s, e) => UpdateBackgroundUIFromState();
                this.Deactivated += (s, e) => UpdateBackgroundUIFromState();

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
                
                // 监听系统主题颜色变化
                HwndSource source = HwndSource.FromHwnd(hwnd);
                source.AddHook(WndProc);
            }
        }

        private const int WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_DWMCOLORIZATIONCOLORCHANGED)
            {
                UpdateSystemAccentColor();
            }
            return IntPtr.Zero;
        }

        #region Navigation and Background Settings

        private void BtnHome_Click(object sender, RoutedEventArgs e)
        {
            HomeContent.Visibility = Visibility.Visible;
            DownloadContent.Visibility = Visibility.Collapsed;
            MoreContent.Visibility = Visibility.Collapsed;
        }

        private void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            HomeContent.Visibility = Visibility.Collapsed;
            DownloadContent.Visibility = Visibility.Visible;
            MoreContent.Visibility = Visibility.Collapsed;
        }

        private void BtnMore_Click(object sender, RoutedEventArgs e)
        {
            HomeContent.Visibility = Visibility.Collapsed;
            DownloadContent.Visibility = Visibility.Collapsed;
            MoreContent.Visibility = Visibility.Visible;
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

        private async Task<string> FlattenVersionJsonAsync(MinecraftLauncher launcher, string parentId, string moddedJson, string newId)
        {
            try
            {
                var moddedNode = JsonNode.Parse(moddedJson);
                if (moddedNode == null) return moddedJson;

                string? inheritsFrom = moddedNode["inheritsFrom"]?.ToString();
                if (string.IsNullOrEmpty(inheritsFrom))
                {
                    moddedNode["id"] = newId;
                    return moddedNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                }

                var parentVersion = await launcher.GetVersionAsync(inheritsFrom);
                if (parentVersion == null) return moddedJson;

                string parentJsonPath = Path.Combine(_baseMcPath?.Versions ?? "", inheritsFrom, $"{inheritsFrom}.json");
                string parentJson;
                if (File.Exists(parentJsonPath))
                {
                    parentJson = await File.ReadAllTextAsync(parentJsonPath);
                }
                else
                {
                    await launcher.InstallAsync(inheritsFrom);
                    parentJson = await File.ReadAllTextAsync(parentJsonPath);
                }

                var parentNode = JsonNode.Parse(parentJson);
                if (parentNode == null) return moddedJson;

                var parentLibs = parentNode["libraries"]?.AsArray();
                var moddedLibs = moddedNode["libraries"]?.AsArray();
                if (parentLibs != null && moddedLibs != null)
                {
                    foreach (var lib in moddedLibs)
                        parentLibs.Add(lib?.DeepClone());
                }

                var parentArgs = parentNode["arguments"];
                var moddedArgs = moddedNode["arguments"];
                if (parentArgs != null && moddedArgs != null)
                {
                    var parentGameArgs = parentArgs["game"]?.AsArray();
                    var moddedGameArgs = moddedArgs["game"]?.AsArray();
                    if (parentGameArgs != null && moddedGameArgs != null)
                    {
                        foreach (var arg in moddedGameArgs)
                            parentGameArgs.Add(arg?.DeepClone());
                    }

                    var parentJvmArgs = parentArgs["jvm"]?.AsArray();
                    var moddedJvmArgs = moddedArgs["jvm"]?.AsArray();
                    if (parentJvmArgs != null && moddedJvmArgs != null)
                    {
                        foreach (var arg in moddedJvmArgs)
                            parentJvmArgs.Add(arg?.DeepClone());
                    }
                }

                parentNode["id"] = newId;
                parentNode["mainClass"] = moddedNode["mainClass"]?.DeepClone() ?? parentNode["mainClass"]?.DeepClone();
                if (parentNode["inheritsFrom"] != null)
                    parentNode.AsObject().Remove("inheritsFrom");

                return parentNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            }
            catch
            {
                return moddedJson;
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

                        // 尝试打平 Forge 的 JSON，以避免产生冗余文件夹
                        string versionName = $"{mcVersion}-forge-{loaderVersion}";
                        string versionDir = Path.Combine(_baseMcPath?.Versions ?? "", versionName);
                        string jsonPath = Path.Combine(versionDir, $"{versionName}.json");
                        if (File.Exists(jsonPath))
                        {
                            string jsonContent = await File.ReadAllTextAsync(jsonPath);
                            jsonContent = await FlattenVersionJsonAsync(_launcher, mcVersion, jsonContent, versionName);
                            await File.WriteAllTextAsync(jsonPath, jsonContent);
                        }
                    }
                    else if (RadioFabric.IsChecked == true && !string.IsNullOrEmpty(loaderVersion))
                    {
                        task.Status = "正在从 Fabric Meta 获取配置...";
                        using var client = new System.Net.Http.HttpClient();
                        string url = $"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}/{loaderVersion}/profile/json";
                        string jsonContent = await client.GetStringAsync(url);

                        string versionName = $"{mcVersion}-fabric-{loaderVersion}";
                         
                         // 获取打平后的 JSON，避免产生冗余文件夹
                         jsonContent = await FlattenVersionJsonAsync(_launcher, mcVersion, jsonContent, versionName);
 
                         string versionDir = Path.Combine(_baseMcPath?.Versions ?? "", versionName);
                        if (!Directory.Exists(versionDir)) Directory.CreateDirectory(versionDir);

                        string jsonPath = Path.Combine(versionDir, $"{versionName}.json");
                        await File.WriteAllTextAsync(jsonPath, jsonContent);

                        task.Status = "正在安装 Fabric 依赖资源...";
                        await _launcher.InstallAsync(versionName);
                    }
                    else if (RadioQuilt.IsChecked == true && !string.IsNullOrEmpty(loaderVersion))
                    {
                        task.Status = "正在从 Quilt Meta 获取配置...";
                        using var client = new System.Net.Http.HttpClient();
                        string url = $"https://meta.quiltmc.org/v2/versions/loader/{mcVersion}/{loaderVersion}/profile/json";
                        string jsonContent = await client.GetStringAsync(url);

                        string versionName = $"{mcVersion}-quilt-{loaderVersion}";
                         
                         // 获取打平后的 JSON，避免产生冗余文件夹
                         jsonContent = await FlattenVersionJsonAsync(_launcher, mcVersion, jsonContent, versionName);
 
                         string versionDir = Path.Combine(_baseMcPath?.Versions ?? "", versionName);
                        if (!Directory.Exists(versionDir)) Directory.CreateDirectory(versionDir);

                        string jsonPath = Path.Combine(versionDir, $"{versionName}.json");
                        await File.WriteAllTextAsync(jsonPath, jsonContent);

                        task.Status = "正在安装 Quilt 依赖资源...";
                        await _launcher.InstallAsync(versionName);
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
                    client.DefaultRequestHeaders.Add("User-Agent", "MizuLauncher Aura/1.0");
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
                    client.DefaultRequestHeaders.Add("User-Agent", "MizuLauncher Aura/1.0");
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
                if (ListVersionsCenter.SelectedItem == null)
                {
                    MessageBox.Show("请先在首页选定一个 Minecraft 版本，模组将下载到该版本的隔离文件夹中。");
                    return;
                }

                string selectedVersion = ListVersionsCenter.SelectedItem.ToString()!;
                var task = new DownloadTask { Name = $"下载 Mod: {version.FileName}", Progress = 0, Status = "准备下载..." };
                DownloadTasks.Add(task);
                TabDownloadTasks.IsChecked = true;
                DownloadTab_Click(TabDownloadTasks, new RoutedEventArgs());

                try
                {
                    using var client = new System.Net.Http.HttpClient();
                    client.DefaultRequestHeaders.Add("User-Agent", "MizuLauncher Aura/1.0");

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
            if (ListVersionsCenter.SelectedItem != null)
            {
                string selectedVersion = ListVersionsCenter.SelectedItem.ToString()!;
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

        private void ComboAiProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem item)
            {
                _aiProvider = item.Content.ToString() ?? "DeepSeek";
                UpdateAiSettingsUI();
                SaveConfig();
            }
        }

        private void ChkFakeMicrosoft_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chk)
            {
                _fakeMicrosoftAccount = chk.IsChecked ?? false;
                _ = UpdatePlayerUIFromState();
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
                    if (BorderProgress.Visibility == Visibility.Collapsed)
                    {
                        BorderProgress.Visibility = Visibility.Visible;
                        BorderAIChat.Visibility = Visibility.Collapsed;
                        _isTaskCompleted = false;
                    }

                    TxtProgressStep.Text = status;
                    if (progress >= 0)
                        RectProgress.Width = Math.Clamp(progress, 0, 1) * BorderProgress.ActualWidth;

                    if (progress >= 1.0)
                    {
                        _isTaskCompleted = true;
                        TxtProgressStep.Text += " (点击关闭)";
                    }
                }
                catch { }
            });
        }

        private void BorderProgress_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_isTaskCompleted)
            {
                BorderProgress.Visibility = Visibility.Collapsed;
                BorderAIChat.Visibility = Visibility.Visible;
                _isTaskCompleted = false;
            }
        }

        private async void BtnAIChatSend_Click(object sender, RoutedEventArgs e)
        {
            string userInput = TxtAIChatInput.Text.Trim();
            if (string.IsNullOrEmpty(userInput)) return;

            if (!_aiConfigs.TryGetValue(_aiProvider, out var config) || string.IsNullOrEmpty(config.ApiKey))
            {
                MessageBox.Show($"请先在“更多”页面设置 {_aiProvider} 的 API Key。");
                return;
            }

            string currentModelId = config.Model;
            string currentApiKey = config.ApiKey;
            Uri? currentEndpoint = !string.IsNullOrEmpty(config.BaseUrl) ? new Uri(config.BaseUrl) : null;

            EnsureAIOutputWindow();
            // 移除了这里的 ShowAtPosition，改为在完成后显示

            TxtAIChatInput.Clear();
            UpdateMainProgress("AI 操作中...", 0.1);

            try
            {
                string selectedVersion = ListVersionsCenter.SelectedItem?.ToString() ?? "未选择";
                string systemPrompt = $@"你是一个专业的 Minecraft 启动器助手。
当前用户选择的游戏版本是：{selectedVersion}。
你的任务是帮助用户管理模组、解决问题，以及安装新的游戏版本、核心、光影包和材质包。

**重要原则：在执行任何安装、下载或添加操作之前，必须先检查该资源是否已经安装或存在。**

当用户要求下载或安装模组 (Mod) 时：
1. 先使用 ListLocalModsAsync 查看当前版本已有的模组，避免重复安装。
2. 使用 SearchModAsync 搜索模组。
3. 使用 GetResourceVersionsAsync 获取对应版本和加载器的版本列表。
4. 调用 InstallModWithDependenciesAsync 安装。

当用户要求安装光影包 (Shader Pack) 时：
1. 先调用 ListLocalShaderPacksAsync 检查是否已安装。
2. **必须**先调用 EnsureShaderLoaderAsync 确保环境有光影加载器（如 Iris/Oculus）。
3. 使用 SearchShaderPackAsync 搜索光影。
4. 使用 GetResourceVersionsAsync 获取版本。
5. 调用 DownloadResourceAsync (type: shader) 下载。

当用户要求安装材质包 (Resource Pack) 时：
1. 先调用 ListLocalResourcePacksAsync 检查是否已安装。
2. 使用 SearchResourcePackAsync 搜索。
3. 使用 GetResourceVersionsAsync 获取版本。
4. 调用 DownloadResourceAsync (type: resourcepack) 下载。

当用户要求删除资源时：
1. 使用 ListLocalModsAsync / ListLocalShaderPacksAsync / ListLocalResourcePacksAsync 确认文件名。
2. 使用 DeleteResourceAsync (type: mod/shader/resourcepack) 进行删除。

当用户要求安装新版本或核心时：
1. 如果是安装纯净版，直接调用 InstallVanillaAsync。
2. 如果是安装 Forge，先调用 GetForgeVersionsAsync 获取可用版本，然后调用 InstallForgeAsync。
3. 如果是安装 NeoForge，先调用 GetNeoForgeVersionsAsync 获取可用版本，然后调用 InstallNeoForgeAsync。
4. 如果是安装 Fabric，先调用 GetFabricVersionsAsync 获取可用版本，然后调用 InstallFabricAsync。
5. 如果是安装 Quilt，先调用 GetQuiltVersionsAsync 获取可用版本，然后调用 InstallQuiltAsync。

请始终确保下载的资源版本与用户当前选择的游戏版本（{selectedVersion}）兼容。";

                var builder = Kernel.CreateBuilder();
                if (currentEndpoint != null)
                {
                    builder.AddOpenAIChatCompletion(modelId: currentModelId, apiKey: currentApiKey, endpoint: currentEndpoint);
                }
                else
                {
                    builder.AddOpenAIChatCompletion(modelId: currentModelId, apiKey: currentApiKey);
                }
                var kernel = builder.Build();

                kernel.Plugins.AddFromObject(new MinecraftResourcePlugin(this));

                var executionSettings = new OpenAIPromptExecutionSettings { ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions };
                var chatHistory = new ChatHistory(systemPrompt);
                chatHistory.AddUserMessage(userInput);

                var chatService = kernel.GetRequiredService<IChatCompletionService>();
                var result = await chatService.GetChatMessageContentAsync(chatHistory, executionSettings, kernel);

                if (_aiOutputWindow != null)
                {
                    _aiOutputWindow.SetResponse(result.Content ?? "无响应内容");
                    
                    // 计算弹出位置并显示
                    double left = this.Left + (this.Width - _aiOutputWindow.Width) / 2;
                    double top = this.Top + this.Height - 100;
                    _aiOutputWindow.ShowAtPosition(left, top);
                }

                UpdateMainProgress("AI 操作完成", 1.0);
            }
            catch (Exception ex)
            {
                UpdateMainProgress($"错误: {ex.Message}", 1.0);
                if (_aiOutputWindow != null)
                {
                    _aiOutputWindow.SetResponse($"错误: {ex.Message}");
                    // 发生错误也弹出，否则用户不知道失败了
                    double left = this.Left + (this.Width - _aiOutputWindow.Width) / 2;
                    double top = this.Top + this.Height - 100;
                    _aiOutputWindow.ShowAtPosition(left, top);
                }
            }
        }

        private void EnsureAIOutputWindow()
        {
            if (_aiOutputWindow == null)
            {
                _aiOutputWindow = new AIOutputWindow();
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

        private async void RefreshVersionList()
        {
            try
            {
                if (_launcher == null) return;
                
                var versions = await _launcher.GetAllVersionsAsync();
                
                _allLocalVersions.Clear();
                foreach (var v in versions)
                {
                    // CmlLib.Core 4.0 中，判断本地版本通常通过检查其 ID 对应的文件夹是否存在
                    string versionPath = Path.Combine(_baseMcPath!.Versions, v.Name, $"{v.Name}.json");
                    if (File.Exists(versionPath))
                    {
                        _allLocalVersions.Add(v.Name);
                    }
                }

                FilterVersionList();
                
                if (FilteredVersions.Count > 0)
                {
                    ListVersionsCenter.SelectedIndex = 0;
                    TxtCurrentVersion.Text = FilteredVersions[0].Name ?? "未选择版本";
                    BtnLaunch.IsEnabled = true;
                }
                else
                {
                    TxtCurrentVersion.Text = "未找到版本";
                    BtnLaunch.IsEnabled = false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("扫描本地版本失败: " + ex.Message);
            }
        }

        private async void FilterVersionList()
        {
            if (_launcher == null) return;
            string filter = TxtVersionSearch?.Text.Trim().ToLower() ?? "";
            FilteredVersions.Clear();

            // 为了获取详细信息，我们需要加载 MVersion 对象
            foreach (var vName in _allLocalVersions)
            {
                if (string.IsNullOrEmpty(filter) || vName.ToLower().Contains(filter))
                {
                    try
                    {
                        var v = await _launcher.GetVersionAsync(vName);
                        
                        string icon = "vanilla.png";
                        string type = "原版";
                        
                        // 提取纯粹的 MC 版本号 (例如从 1.21-fabric-xxx 中提取 1.21)
                        string mcVersion = v.InheritsFrom ?? v.Id ?? "";
                        var mcMatch = System.Text.RegularExpressions.Regex.Match(mcVersion, @"^\d+\.\d+(\.\d+)?");
                        if (mcMatch.Success) mcVersion = mcMatch.Value;

                        string loaderVersion = "";

                        // 根据 mainClass 或其它属性判断加载器
                        if (v.MainClass != null)
                        {
                            if (v.MainClass.Contains("fabric", StringComparison.OrdinalIgnoreCase))
                            {
                                type = "Fabric";
                                icon = "fabric.png";
                                // 尝试从 ID 获取加载器版本
                                var match = System.Text.RegularExpressions.Regex.Match(v.Id ?? "", @"fabric-?(?:loader-)?(\d+\.\d+\.\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                if (match.Success) loaderVersion = match.Groups[1].Value;
                            }
                            else if (v.MainClass.Contains("cpw.mods.bootstraplauncher.Main") || v.MainClass.Contains("net.minecraftforge.bootstrap.ForgeBootstrap"))
                            {
                                type = "Forge";
                                icon = "forge.png";
                                var match = System.Text.RegularExpressions.Regex.Match(v.Id ?? "", @"forge-?(\d+\.\d+\.\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                if (match.Success) loaderVersion = match.Groups[1].Value;
                            }
                        }

                        // 使用 URI 格式加载本地文件，确保 WPF 正确识别
                        string iconFullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "clienticon", icon);
                        string iconUri = new Uri(iconFullPath).AbsoluteUri;

                        FilteredVersions.Add(new VersionItemInfo 
                        { 
                            Name = vName, 
                            IconPath = iconUri,
                            TypeDisplay = type,
                            VersionDisplay = mcVersion,
                            LoaderDisplay = loaderVersion
                        });
                    }
                    catch { /* 忽略无法解析的版本 */ }
                }
            }

            // 更新搜索占位符
            if (TxtVersionSearch != null)
            {
                TxtVersionSearch.Tag = $"在 {_allLocalVersions.Count} 个游戏中搜索";
            }
        }

        private void TxtVersionSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterVersionList();
        }

        private void BtnRefreshVersionList_Click(object sender, RoutedEventArgs e)
        {
            RefreshVersionList();
        }

        private void ListVersionsCenter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ListVersionsCenter.SelectedItem is VersionItemInfo item)
            {
                TxtCurrentVersion.Text = item.Name;
                HideRow1VersionPanel();
                BtnLaunch.IsEnabled = true;
            }
        }

        private void BtnVersionSelect_Click(object sender, RoutedEventArgs e)
        {
            ShowRow1VersionPanel();
        }

        private void BtnCloseVersionCenter_Click(object sender, RoutedEventArgs e)
        {
            HideRow1VersionPanel();
        }

        private void ShowRow1VersionPanel()
        {
            if (Row1VersionPanel.Visibility == Visibility.Visible) return;

            Row1VersionPanel.Visibility = Visibility.Visible;
            BtnCloseVersionCenter.Visibility = Visibility.Collapsed; // 隐藏旧位置按钮
            
            Storyboard? slideUp = this.Resources["SlideUpAnimation"] as Storyboard;
            slideUp?.Begin(Row1VersionPanel);

            Storyboard? openMenu = this.Resources["OpenMenuAnimation"] as Storyboard;
            openMenu?.Begin(this);
        }

        private void HideRow1VersionPanel()
        {
            if (Row1VersionPanel.Visibility == Visibility.Collapsed) return;

            Storyboard? slideDown = this.Resources["SlideDownAnimation"] as Storyboard;
            if (slideDown != null)
            {
                slideDown = slideDown.Clone();
                slideDown.Completed += (s, e) => Row1VersionPanel.Visibility = Visibility.Collapsed;
                slideDown.Begin(Row1VersionPanel);
            }
            else
            {
                Row1VersionPanel.Visibility = Visibility.Collapsed;
            }

            Storyboard? closeMenu = this.Resources["CloseMenuAnimation"] as Storyboard;
            if (closeMenu != null)
            {
                closeMenu = closeMenu.Clone();
                closeMenu.Completed += (s, e) => BtnCloseVersionCenter.Visibility = Visibility.Collapsed;
                closeMenu.Begin(this);
            }
            else
            {
                BtnCloseVersionCenter.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnPlayerCard_MouseEnter(object sender, MouseEventArgs e)
        {
            ShowPlayerOverlay();
        }

        private void BtnPlayerCard_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!PlayerListOverlay.IsMouseOver)
            {
                HidePlayerOverlay();
            }
        }

        private void PopupPlayers_MouseEnter(object sender, MouseEventArgs e)
        {
            ShowPlayerOverlay();
        }

        private void PopupPlayers_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!BtnPlayerCard.IsMouseOver)
            {
                HidePlayerOverlay();
            }
        }

        private void ShowPlayerOverlay()
        {
            if (PlayerListOverlay.Visibility == Visibility.Visible && PlayerListOverlay.Opacity > 0.5) return;

            PlayerListOverlay.Visibility = Visibility.Visible;

            DoubleAnimation fadeAnim = new DoubleAnimation(1, TimeSpan.FromMilliseconds(250));
            DoubleAnimation slideAnim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            PlayerListOverlay.BeginAnimation(OpacityProperty, fadeAnim);
            PlayerOverlayTransform.BeginAnimation(TranslateTransform.YProperty, slideAnim);
        }

        private void HidePlayerOverlay()
        {
            if (PlayerListOverlay.Visibility == Visibility.Collapsed) return;

            DoubleAnimation fadeAnim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
            DoubleAnimation slideAnim = new DoubleAnimation(20, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            fadeAnim.Completed += (s, e) => PlayerListOverlay.Visibility = Visibility.Collapsed;

            PlayerListOverlay.BeginAnimation(OpacityProperty, fadeAnim);
            PlayerOverlayTransform.BeginAnimation(TranslateTransform.YProperty, slideAnim);
        }

        #region Configuration Storage (Safe)

        private int _currentBgType = 3; // Default Acrylic
        private string _currentCustomColor = "#FF1E1E1E";
        private string _currentPlayerName = "添加玩家";
        private bool _isTaskCompleted = false;
        private bool _fakeMicrosoftAccount = false;

        private Dictionary<string, AiConfig> _aiConfigs = new()
        {
            { "OpenAI", new AiConfig { Provider = "OpenAI", BaseUrl = "https://api.openai.com/v1", Model = "gpt-4o" } },
            { "DeepSeek", new AiConfig { Provider = "DeepSeek", BaseUrl = "https://api.deepseek.com/v1", Model = "deepseek-chat" } },
            { "GLM (ZhipuAI)", new AiConfig { Provider = "GLM (ZhipuAI)", BaseUrl = "https://open.bigmodel.cn/api/paas/v4/", Model = "glm-4.7-flash" } },
            { "Claude (Anthropic)", new AiConfig { Provider = "Claude (Anthropic)", BaseUrl = "https://api.anthropic.com/v1", Model = "claude-3-5-sonnet-20240620" } },
            { "Gemini (Google)", new AiConfig { Provider = "Gemini (Google)", BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai/", Model = "gemini-1.5-pro" } },
            { "Moonshot (Kimi)", new AiConfig { Provider = "Moonshot (Kimi)", BaseUrl = "https://api.moonshot.cn/v1", Model = "moonshot-v1-8k" } },
            { "Custom", new AiConfig { Provider = "Custom", BaseUrl = "", Model = "" } }
        };
        private string _aiProvider = "DeepSeek";

        private string ConfigExportDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "MizuLauncherAura_Configs");
        private string AiConfigsPath => Path.Combine(ConfigExportDir, "ai_configs.json");

        private void SaveConfig()
        {
            try
            {
                var config = new LauncherConfig
                {
                    BackgroundType = _currentBgType,
                    CustomColor = _currentCustomColor,
                    PlayerName = _currentPlayerName,
                    Players = OnlinePlayers.Concat(OfflinePlayers).ToList(),
                    AiConfigs = _aiConfigs.Values.ToList(),
                    AiProvider = _aiProvider,
                    FakeMicrosoftAccount = _fakeMicrosoftAccount
                };
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFileName, json);

                // 实时同步 AI 配置到配置文件夹
                Directory.CreateDirectory(ConfigExportDir);
                File.WriteAllText(AiConfigsPath, JsonSerializer.Serialize(_aiConfigs.Values.ToList(), new JsonSerializerOptions { WriteIndented = true }));
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
                        _currentBgType = config.BackgroundType;
                        _currentCustomColor = config.CustomColor ?? "#FF1E1E1E";
                        _currentPlayerName = config.PlayerName ?? "添加玩家";
                        _aiProvider = config.AiProvider ?? "DeepSeek";
                        _fakeMicrosoftAccount = config.FakeMicrosoftAccount;

                        if (config.AiConfigs != null && config.AiConfigs.Count > 0)
                        {
                            foreach (var ai in config.AiConfigs)
                            {
                                if (!string.IsNullOrEmpty(ai.Provider))
                                {
                                    _aiConfigs[ai.Provider] = ai;
                                }
                            }
                        }

                        ChkFakeMicrosoft.IsChecked = _fakeMicrosoftAccount;

                        // 尝试从同步文件恢复
                        if (File.Exists(AiConfigsPath))
                        {
                            var syncedConfigs = JsonSerializer.Deserialize<List<AiConfig>>(File.ReadAllText(AiConfigsPath));
                            if (syncedConfigs != null)
                            {
                                foreach (var ai in syncedConfigs)
                                {
                                    if (!string.IsNullOrEmpty(ai.Provider))
                                    {
                                        _aiConfigs[ai.Provider] = ai;
                                    }
                                }
                            }
                        }

                        // 更新 UI
                        UpdateAiSettingsUI();

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
                    UpdateBackgroundUIFromState();
                    _ = UpdatePlayerUIFromState();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Load config error: {ex.Message}");
            }
        }

        private void UpdateAiSettingsUI()
        {
            if (ComboAiProvider == null || TxtAiApiKey == null || TxtAiModel == null || TxtAiBaseUrl == null) return;

            // 设置 ComboBox 选中项
            foreach (ComboBoxItem item in ComboAiProvider.Items)
            {
                if (item.Content.ToString() == _aiProvider)
                {
                    ComboAiProvider.SelectedItem = item;
                    break;
                }
            }

            // 加载当前提供商的配置
            if (_aiConfigs.TryGetValue(_aiProvider, out var config))
            {
                _isUpdatingAiUI = true;
                TxtAiApiKey.Password = config.ApiKey;
                TxtAiModel.Text = config.Model;
                TxtAiBaseUrl.Text = config.BaseUrl;
                _isUpdatingAiUI = false;
            }
        }

        private bool _isUpdatingAiUI = false;

        private void TxtAiApiKey_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingAiUI) return;
            if (sender is PasswordBox pb && _aiConfigs.TryGetValue(_aiProvider, out var config))
            {
                config.ApiKey = pb.Password;
                SaveConfig();
            }
        }

        private void TxtAiModel_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingAiUI) return;
            if (sender is TextBox tb && _aiConfigs.TryGetValue(_aiProvider, out var config))
            {
                config.Model = tb.Text;
                SaveConfig();
            }
        }

        private void TxtAiBaseUrl_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingAiUI) return;
            if (sender is TextBox tb && _aiConfigs.TryGetValue(_aiProvider, out var config))
            {
                config.BaseUrl = tb.Text;
                SaveConfig();
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
                    targetColor = System.Windows.Media.Color.FromArgb(0x30, 0x00, 0x00, 0x00);
                }
                else // Mica (无论聚焦与否都保持云母遮罩色)
                {
                    targetColor = System.Windows.Media.Color.FromArgb(0x05, 0x00, 0x00, 0x00);
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

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetColorizationColor(out uint pcbColorization, out bool pfOpaqueBlend);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
            public MEMORYSTATUSEX()
            {
                this.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        private int GetRecommendedMemoryMb()
        {
            try
            {
                var memStatus = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(memStatus))
                {
                    long availMb = (long)(memStatus.ullAvailPhys / (1024 * 1024));
                    long totalMb = (long)(memStatus.ullTotalPhys / (1024 * 1024));

                    // 自动分配逻辑：
                    // 1. 优先保证系统留有足够运行空间
                    // 2. 游戏内存分配原则：
                    int recommended;
                    if (availMb > 16384) recommended = 8192; // 剩余 16G 以上，分 8G
                    else if (availMb > 8192) recommended = 4096; // 剩余 8G 以上，分 4G
                    else if (availMb > 4096) recommended = 3072; // 剩余 4G 以上，分 3G
                    else if (availMb > 2048) recommended = 2048; // 剩余 2G 以上，分 2G
                    else recommended = (int)Math.Max(1024, availMb - 512); // 极低内存，留 512MB 给系统

                    // 保护：不分配超过总内存 70% 的内存
                    int maxSafe = (int)(totalMb * 0.7);
                    recommended = Math.Min(recommended, maxSafe);
                    
                    return recommended;
                }
            }
            catch { }
            return 4096; // 默认回退 4GB
        }

        private void UpdateSystemAccentColor()
        {
            try
            {
                if (DwmGetColorizationColor(out uint color, out bool opaque) == 0)
                {
                    // color 是 ARGB 格式
                    byte r = (byte)((color >> 16) & 0xFF);
                    byte g = (byte)((color >> 8) & 0xFF);
                    byte b = (byte)(color & 0xFF);

                    // 转换为 HSL 进行调整
                    RGBtoHSL(r, g, b, out double h, out double s, out double l);

                    // 核心调整：提高明度，降低饱和度以达到 "Aura" 粉嫩效果
                    // 1. 提高明度 (Lightness): 确保至少在 0.7 以上，如果太暗则大幅提升
                    if (l < 0.7) l = 0.75;
                    else l = Math.Min(0.9, l + 0.1);

                    // 2. 降低饱和度 (Saturation): 限制在 0.4 - 0.6 之间，防止颜色太“浓”
                    s = Math.Clamp(s * 0.7, 0.4, 0.6);

                    // 转回 RGB
                    var systemColor = HSLtoRGB(h, s, l);
                    this.Resources["SystemAccentBrush"] = new System.Windows.Media.SolidColorBrush(systemColor);
                }
            }
            catch { }
        }

        // HSL 转换辅助函数
        private void RGBtoHSL(byte r, byte g, byte b, out double h, out double s, out double l)
        {
            double rd = r / 255.0;
            double gd = g / 255.0;
            double bd = b / 255.0;
            double max = Math.Max(rd, Math.Max(gd, bd));
            double min = Math.Min(rd, Math.Min(gd, bd));
            double delta = max - min;

            l = (max + min) / 2.0;

            if (delta == 0)
            {
                h = s = 0;
            }
            else
            {
                s = l <= 0.5 ? delta / (max + min) : delta / (2.0 - max - min);

                if (rd == max) h = (gd - bd) / delta + (gd < bd ? 6 : 0);
                else if (gd == max) h = (bd - rd) / delta + 2;
                else h = (rd - gd) / delta + 4;
                h /= 6.0;
            }
        }

        private System.Windows.Media.Color HSLtoRGB(double h, double s, double l)
        {
            double r, g, b;

            if (s == 0)
            {
                r = g = b = l;
            }
            else
            {
                double q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
                double p = 2.0 * l - q;
                r = HueToRGB(p, q, h + 1.0 / 3.0);
                g = HueToRGB(p, q, h);
                b = HueToRGB(p, q, h - 1.0 / 3.0);
            }

            return System.Windows.Media.Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        private double HueToRGB(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
            return p;
        }

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

                // 4. 初始化一次主题颜色
                UpdateSystemAccentColor();
            }
            catch (Exception ex)
            {
                WriteLog($"DWM设置失败: {ex.Message}");
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

                if (_fakeMicrosoftAccount)
                {
                    TxtPlayerType.Text = "正版账号";
                }
                else if (currentPlayer != null)
                {
                    TxtPlayerType.Text = currentPlayer.IsOnline ? "正版账号" : "离线模式";
                }
                else
                {
                    TxtPlayerType.Text = "离线模式";
                }

                // 更新玩家列表中的 Header
                if (_fakeMicrosoftAccount)
                {
                    TxtOfflineHeader.Text = "正版账号";
                    // 如果开启了伪装，隐藏分隔线以让列表看起来更像一个整体（可选）
                    // PlayerListSeparator.Visibility = Visibility.Collapsed;
                }
                else
                {
                    TxtOfflineHeader.Text = "离线账号";
                    // PlayerListSeparator.Visibility = Visibility.Visible;
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
            // 关闭 Overlay
            HidePlayerOverlay();

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
                HidePlayerOverlay(); // 关闭 Overlay
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

        public class AiConfig
        {
            public string Provider { get; set; } = "";
            public string ApiKey { get; set; } = "";
            public string Model { get; set; } = "";
            public string? BaseUrl { get; set; }
        }

        public class LauncherConfig
        {
            public int BackgroundType { get; set; } = 3; // Default Acrylic
            public string? CustomColor { get; set; } = "#FF1E1E1E";
            public string PlayerName { get; set; } = "添加玩家";
            public System.Collections.Generic.List<PlayerInfo>? Players { get; set; }
            public bool ShowDragHint { get; set; } = true;
            public System.Collections.Generic.List<AiConfig>? AiConfigs { get; set; }
            public string? AiProvider { get; set; } = "DeepSeek";
            public bool FakeMicrosoftAccount { get; set; } = false;
        }

        #endregion

        #region Window Controls

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (_aiOutputWindow != null)
            {
                _aiOutputWindow.Close();
            }
            this.Close();
        }

        #endregion

        #region Launch Logic (Protected)

        private async void LaunchGame(string versionName)
        {
            if (string.IsNullOrEmpty(versionName) || _baseMcPath == null) return;

            try
            {
                BtnLaunch.IsEnabled = false;
                _isTaskCompleted = false;
                BorderProgress.Visibility = Visibility.Visible;
                BorderAIChat.Visibility = Visibility.Collapsed;
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
                    MaximumRamMb = GetRecommendedMemoryMb(),
                    Session = session
                };

                WriteLog($"游戏启动内存分配: {launchOption.MaximumRamMb} MB");
                TxtProgressStep.Text = $"正在校验资源 (内存分配: {launchOption.MaximumRamMb}MB)...";
                var process = await currentLauncher.InstallAndBuildProcessAsync(versionName, launchOption);

                _isTaskCompleted = true;
                TxtProgressStep.Text = "游戏已启动！(点击关闭)";
                RectProgress.Width = BorderProgress.ActualWidth;
                process.Start();
                // 不再自动延迟并关闭，等待用户点击
            }
            catch (Exception ex)
            {
                TxtProgressStep.Text = "启动失败 (点击关闭)";
                _isTaskCompleted = true;
                MessageBox.Show("启动失败: " + ex.Message, "错误");
            }
            finally
            {
                BtnLaunch.IsEnabled = true;
                // 不再在这里设置 Visibility，交由用户点击或下次启动时处理
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

                if (ListVersionsCenter.SelectedItem is VersionItemInfo item)
                {
                    LaunchGame(item.Name ?? "");
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

        private void BtnVersionSettings_Click(object sender, RoutedEventArgs e)
        {
            if (VersionSettingsPanel.Visibility == Visibility.Collapsed)
            {
                VersionSettingsPanel.Visibility = Visibility.Visible;
                RefreshSavedConfigs();
            }
            else
            {
                VersionSettingsPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnListLaunch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is VersionItemInfo item)
            {
                LaunchGame(item.Name ?? "");
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
            if (ListVersionsCenter.SelectedItem == null)
            {
                MessageBox.Show("请先在左侧选择一个版本。");
                return;
            }

            string versionName = ListVersionsCenter.SelectedItem.ToString()!;
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

                // 2. 导出 AI 配置备份到该子文件夹
                File.WriteAllText(Path.Combine(exportSubDir, "ai_configs.json"), JsonSerializer.Serialize(_aiConfigs.Values.ToList(), new JsonSerializerOptions { WriteIndented = true }));

                MessageBox.Show($"配置已导出！\n键位和 AI 配置备份已保存至 Keybinds\\{versionName}_{timestamp}");
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
            if (ListVersionsCenter.SelectedItem == null)
            {
                MessageBox.Show("请先在左侧选择一个版本。");
                return;
            }

            if (ComboSavedConfigs.SelectedItem == null)
            {
                MessageBox.Show("请选择要导入的键位备份。");
                return;
            }

            string versionName = ListVersionsCenter.SelectedItem.ToString()!;
            string configName = ComboSavedConfigs.SelectedItem.ToString()!;
            string optionsPath = Path.Combine(_baseMcPath!.BasePath, "versions", versionName, "options.txt");
            string importDir = Path.Combine(ConfigExportDir, "Keybinds", configName);

            try
            {
                // 1. 导入选中备份文件夹下的 AI 配置 (如果存在备份)
                string aiConfigsFile = Path.Combine(importDir, "ai_configs.json");
                if (File.Exists(aiConfigsFile))
                {
                    var importedConfigs = JsonSerializer.Deserialize<List<AiConfig>>(File.ReadAllText(aiConfigsFile));
                    if (importedConfigs != null)
                    {
                        foreach (var ai in importedConfigs)
                        {
                            if (!string.IsNullOrEmpty(ai.Provider))
                            {
                                _aiConfigs[ai.Provider] = ai;
                            }
                        }
                    }
                    UpdateAiSettingsUI();
                    SaveConfig();
                }
                
                // 兼容旧版本
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
                            if (key == "DeepSeekApiKey" && _aiConfigs.ContainsKey("DeepSeek")) 
                            {
                                _aiConfigs["DeepSeek"].ApiKey = value;
                            }
                            else if (key == "GlmApiKey" && _aiConfigs.ContainsKey("GLM (ZhipuAI)")) 
                            {
                                _aiConfigs["GLM (ZhipuAI)"].ApiKey = value;
                            }
                        }
                    }
                    UpdateAiSettingsUI();
                    SaveConfig();
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