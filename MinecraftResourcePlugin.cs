using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Net.Http;
using System.Text.Json;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CmlLib.Core;
using CmlLib.Core.Installer.Forge;

namespace MizuLauncher
{
    public class MinecraftResourcePlugin
    {
        private readonly MainWindow _mainWindow;
        private readonly HttpClient _httpClient;

        public MinecraftResourcePlugin(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "MizuLauncher Aura/1.0");
        }

        [KernelFunction, Description("搜索 Minecraft 的 Mod。")]
        public async Task<string> SearchModAsync(
            [Description("Mod 的名称关键词，例如 JEI, OptiFine")] string query,
            [Description("Minecraft 版本号，例如 1.20.1")] string mcVersion,
            [Description("模组加载器，例如 forge, fabric, quilt, neoforge")] string loader)
        {
            try
            {
                // Modrinth API 搜索
                string facets = $"[[\"versions:{mcVersion}\"],[\"categories:{loader.ToLower()}\"]]";
                string url = $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(query)}&facets={Uri.EscapeDataString(facets)}&limit=5";
                
                var response = await _httpClient.GetStringAsync(url);
                return response;
            }
            catch (Exception ex)
            {
                return $"搜索失败: {ex.Message}";
            }
        }

        [KernelFunction, Description("获取 Mod 的版本列表以找到合适的下载链接。")]
        public async Task<string> GetModVersionsAsync(
            [Description("Mod 的项目 ID")] string projectId,
            [Description("Minecraft 版本号")] string mcVersion,
            [Description("模组加载器")] string loader)
        {
            try
            {
                string url = $"https://api.modrinth.com/v2/project/{projectId}/version?loaders=[\"{loader.ToLower()}\"]&game_versions=[\"{mcVersion}\"]";
                var response = await _httpClient.GetStringAsync(url);
                return response;
            }
            catch (Exception ex)
            {
                return $"获取版本列表失败: {ex.Message}";
            }
        }

        [KernelFunction, Description("根据下载链接和文件名下载 Mod。")]
        public async Task<string> DownloadModAsync(
            [Description("Mod 的下载链接")] string downloadUrl,
            [Description("保存的文件名")] string fileName)
        {
            var selectedVersion = await _mainWindow.Dispatcher.InvokeAsync(() => 
                _mainWindow.ListVersions.SelectedItem?.ToString());

            if (string.IsNullOrEmpty(selectedVersion))
            {
                return "错误：未在首页选择 Minecraft 版本。";
            }

            var task = await _mainWindow.Dispatcher.InvokeAsync(() => 
            {
                var t = new DownloadTask { Name = $"AI 下载 Mod: {fileName}", Progress = 0, Status = "准备下载..." };
                _mainWindow.DownloadTasks.Add(t);
                return t;
            });
            
            try
            {
                string modsPath = Path.Combine(_mainWindow.GetBaseMcPath()?.BasePath ?? "", "versions", selectedVersion, "mods");
                if (!Directory.Exists(modsPath)) Directory.CreateDirectory(modsPath);

                using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                using var fileStream = new FileStream(Path.Combine(modsPath, fileName), FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
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
                    {
                        double progress = (double)totalRead / totalBytes;
                        task.Progress = (int)(progress * 100);
                        _mainWindow.UpdateMainProgress($"正在下载: {fileName}", progress);
                    }
                }

                task.Progress = 100;
                task.Status = "下载完成";
                _mainWindow.UpdateMainProgress("下载完成", 1.0);
                return $"成功：{fileName} 已下载到 {selectedVersion} 的 mods 文件夹。";
            }
            catch (Exception ex)
            {
                task.Status = $"错误: {ex.Message}";
                return $"失败：{ex.Message}";
            }
        }

        [KernelFunction, Description("安装指定版本的 Minecraft 纯净版。")]
        public async Task<string> InstallVanillaAsync(
            [Description("Minecraft 版本号，例如 1.20.1")] string mcVersion)
        {
            // 在 UI 线程外创建任务并添加到列表
            var task = await _mainWindow.Dispatcher.InvokeAsync(() => 
            {
                var t = new DownloadTask { Name = $"AI 安装: {mcVersion}", Progress = 0, Status = "准备中..." };
                _mainWindow.DownloadTasks.Add(t);
                return t;
            });

            try
            {
                var launcher = _mainWindow.GetLauncher();
                if (launcher == null) return "错误：启动器实例未初始化。";

                launcher.FileProgressChanged += (s, args) =>
                {
                    if (args.TotalTasks > 0)
                    {
                        double progress = (double)args.ProgressedTasks / args.TotalTasks;
                        task.Progress = (int)(progress * 100);
                        _mainWindow.UpdateMainProgress($"正在下载: {args.Name}", progress);
                    }
                    task.Status = $"正在下载: {args.Name}";
                };

                try
                {
                    await launcher.InstallAsync(mcVersion);
                }
                finally
                {
                    // 注意：CmlLib.Core 4.x 的 FileProgressChanged 可能无法简单通过 -= 移除 lambda
                    // 如果需要移除，建议定义为变量。这里暂不处理或在以后优化。
                }
                
                task.Progress = 100;
                task.Status = "安装完成";
                _mainWindow.UpdateMainProgress("安装完成", 1.0);
                await _mainWindow.Dispatcher.InvokeAsync(() => _mainWindow.CallRefreshVersionList());
                return $"成功：Minecraft {mcVersion} 已安装。";
            }
            catch (Exception ex)
            {
                task.Status = $"错误: {ex.Message}";
                return $"失败：{ex.Message}";
            }
        }

        [KernelFunction, Description("安装指定版本的 Forge 核心。")]
        public async Task<string> InstallForgeAsync(
            [Description("Minecraft 版本号，例如 1.20.1")] string mcVersion,
            [Description("Forge 版本号，例如 47.2.0")] string forgeVersion)
        {
            var task = await _mainWindow.Dispatcher.InvokeAsync(() => 
            {
                var t = new DownloadTask { Name = $"AI 安装 Forge: {mcVersion} ({forgeVersion})", Progress = 0, Status = "准备中..." };
                _mainWindow.DownloadTasks.Add(t);
                return t;
            });

            try
            {
                var launcher = _mainWindow.GetLauncher();
                if (launcher == null) return "错误：启动器实例未初始化。";

                task.Status = "正在安装 Forge...";
                _mainWindow.UpdateMainProgress("正在安装 Forge...", 0.5); // Forge 安装过程难以细化进度，设为 50%
                var forgeInstaller = new ForgeInstaller(launcher);
                await forgeInstaller.Install(mcVersion, forgeVersion);

                task.Progress = 100;
                task.Status = "安装完成";
                _mainWindow.UpdateMainProgress("安装完成", 1.0);
                await _mainWindow.Dispatcher.InvokeAsync(() => _mainWindow.CallRefreshVersionList());
                return $"成功：Forge {mcVersion}-{forgeVersion} 已安装。";
            }
            catch (Exception ex)
            {
                task.Status = $"错误: {ex.Message}";
                return $"失败：{ex.Message}";
            }
        }

        [KernelFunction, Description("获取指定 Minecraft 版本的 Forge 版本列表。")]
        public async Task<string> GetForgeVersionsAsync(
            [Description("Minecraft 版本号")] string mcVersion)
        {
            try
            {
                var launcher = _mainWindow.GetLauncher();
                if (launcher == null) return "错误：启动器实例未初始化。";

                var forgeInstaller = new ForgeInstaller(launcher);
                var versions = await forgeInstaller.GetForgeVersions(mcVersion);
                var result = versions.Take(5).Select(v => new { v.ForgeVersionName });
                return JsonSerializer.Serialize(result);
            }
            catch (Exception ex)
            {
                return $"获取 Forge 版本失败: {ex.Message}";
            }
        }
    }
}