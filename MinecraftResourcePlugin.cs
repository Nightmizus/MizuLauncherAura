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
using CmlLib.Core.Installer.NeoForge;

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

        #region Local Info

        [KernelFunction, Description("获取当前选择版本的本地已安装模组列表。")]
        public async Task<string> ListLocalModsAsync()
        {
            return await ListLocalFilesAsync("mods", "*.jar");
        }

        [KernelFunction, Description("获取当前选择版本的本地已安装材质包列表。")]
        public async Task<string> ListLocalResourcePacksAsync()
        {
            return await ListLocalFilesAsync("resourcepacks", "*");
        }

        [KernelFunction, Description("获取当前选择版本的本地已安装光影包列表。")]
        public async Task<string> ListLocalShaderPacksAsync()
        {
            return await ListLocalFilesAsync("shaderpacks", "*");
        }

        private async Task<string> ListLocalFilesAsync(string folderName, string searchPattern)
        {
            var selectedVersion = await _mainWindow.Dispatcher.InvokeAsync(() => 
                _mainWindow.ListVersions.SelectedItem?.ToString());

            if (string.IsNullOrEmpty(selectedVersion))
            {
                return "错误：未选择 Minecraft 版本。";
            }

            try
            {
                string path = Path.Combine(_mainWindow.GetBaseMcPath()?.BasePath ?? "", "versions", selectedVersion, folderName);
                if (!Directory.Exists(path))
                {
                    return $"当前版本尚未创建 {folderName} 文件夹。";
                }

                var files = Directory.GetFiles(path, searchPattern)
                                     .Select(Path.GetFileName)
                                     .ToList();

                if (files.Count == 0)
                {
                    return $"当前版本未在 {folderName} 文件夹中找到任何文件。";
                }

                return $"已安装 {folderName} 列表 ({selectedVersion}):\n" + string.Join("\n", files!);
            }
            catch (Exception ex)
            {
                return $"获取 {folderName} 列表失败: {ex.Message}";
            }
        }

        [KernelFunction, Description("安装指定版本的 Quilt 加载器。")]
        public async Task<string> InstallQuiltAsync(
            [Description("Minecraft 版本号")] string mcVersion,
            [Description("Quilt 加载器版本")] string quiltVersion)
        {
            var task = await CreateDownloadTask($"AI 安装 Quilt: {mcVersion} ({quiltVersion})");

            try
            {
                var launcher = _mainWindow.GetLauncher();
                if (launcher == null) return "错误：启动器实例未初始化。";

                task.Status = "正在从 Quilt Meta 获取配置...";
                string url = $"https://meta.quiltmc.org/v2/versions/loader/{mcVersion}/{quiltVersion}/profile/json";
                string jsonContent = await _httpClient.GetStringAsync(url);

                string versionName = $"{mcVersion}-quilt-{quiltVersion}";
                string versionDir = Path.Combine(_mainWindow.GetBaseMcPath()?.Versions ?? "", versionName);
                if (!Directory.Exists(versionDir)) Directory.CreateDirectory(versionDir);

                string jsonPath = Path.Combine(versionDir, $"{versionName}.json");
                await File.WriteAllTextAsync(jsonPath, jsonContent);

                task.Status = "正在安装依赖资源...";
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
                await launcher.InstallAsync(versionName);

                CompleteTask(task);
                return $"成功：Quilt {versionName} 已安装。";
            }
            catch (Exception ex)
            {
                return FailTask(task, ex);
            }
        }

        [KernelFunction, Description("安装指定版本的 Fabric 加载器。")]
        public async Task<string> InstallFabricAsync(
            [Description("Minecraft 版本号")] string mcVersion,
            [Description("Fabric 加载器版本")] string fabricVersion)
        {
            var task = await CreateDownloadTask($"AI 安装 Fabric: {mcVersion} ({fabricVersion})");

            try
            {
                var launcher = _mainWindow.GetLauncher();
                if (launcher == null) return "错误：启动器实例未初始化。";

                task.Status = "正在从 Fabric Meta 获取配置...";
                string url = $"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}/{fabricVersion}/profile/json";
                string jsonContent = await _httpClient.GetStringAsync(url);

                string versionName = $"{mcVersion}-fabric-{fabricVersion}";
                string versionDir = Path.Combine(_mainWindow.GetBaseMcPath()?.Versions ?? "", versionName);
                if (!Directory.Exists(versionDir)) Directory.CreateDirectory(versionDir);

                string jsonPath = Path.Combine(versionDir, $"{versionName}.json");
                await File.WriteAllTextAsync(jsonPath, jsonContent);

                task.Status = "正在安装依赖资源...";
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
                await launcher.InstallAsync(versionName);

                CompleteTask(task);
                return $"成功：Fabric {versionName} 已安装。";
            }
            catch (Exception ex)
            {
                return FailTask(task, ex);
            }
        }

        #endregion

        #region Mod Management

        [KernelFunction, Description("搜索 Minecraft 的 Mod。")]
        public async Task<string> SearchModAsync(
            [Description("Mod 的名称关键词，例如 JEI, OptiFine")] string query,
            [Description("Minecraft 版本号，例如 1.20.1")] string mcVersion,
            [Description("模组加载器，例如 forge, fabric, quilt, neoforge")] string loader)
        {
            return await SearchResourceAsync(query, mcVersion, loader, "mod");
        }

        [KernelFunction, Description("搜索 Minecraft 的材质包。")]
        public async Task<string> SearchResourcePackAsync(
            [Description("关键词")] string query,
            [Description("Minecraft 版本号")] string mcVersion)
        {
            return await SearchResourceAsync(query, mcVersion, "", "resourcepack");
        }

        [KernelFunction, Description("搜索 Minecraft 的光影包。")]
        public async Task<string> SearchShaderPackAsync(
            [Description("关键词")] string query,
            [Description("Minecraft 版本号")] string mcVersion)
        {
            return await SearchResourceAsync(query, mcVersion, "", "shader");
        }

        private async Task<string> SearchResourceAsync(string query, string mcVersion, string loader, string projectType)
        {
            try
            {
                var facetList = new List<string>();
                facetList.Add($"\"versions:{mcVersion}\"");
                facetList.Add($"\"project_type:{projectType}\"");
                if (!string.IsNullOrEmpty(loader))
                {
                    facetList.Add($"\"categories:{loader.ToLower()}\"");
                }

                string facets = "[" + string.Join(",", facetList.Select(f => $"[{f}]")) + "]";
                string url = $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(query)}&facets={Uri.EscapeDataString(facets)}&limit=5";
                
                var response = await _httpClient.GetStringAsync(url);
                return response;
            }
            catch (Exception ex)
            {
                return $"搜索失败: {ex.Message}";
            }
        }

        [KernelFunction, Description("获取 Mod、光影或材质包的版本列表。")]
        public async Task<string> GetResourceVersionsAsync(
            [Description("项目的项目 ID")] string projectId,
            [Description("Minecraft 版本号")] string mcVersion,
            [Description("加载器 (可选，模组必填)")] string loader = "")
        {
            try
            {
                string url = $"https://api.modrinth.com/v2/project/{projectId}/version?game_versions=[\"{mcVersion}\"]";
                if (!string.IsNullOrEmpty(loader))
                {
                    url += $"&loaders=[\"{loader.ToLower()}\"]";
                }
                var response = await _httpClient.GetStringAsync(url);
                return response;
            }
            catch (Exception ex)
            {
                return $"获取版本列表失败: {ex.Message}";
            }
        }

        [KernelFunction, Description("下载 Mod、光影包或材质包。")]
        public async Task<string> DownloadResourceAsync(
            [Description("资源的下载链接")] string downloadUrl,
            [Description("保存的文件名")] string fileName,
            [Description("资源类型: mod, shader, resourcepack")] string type)
        {
            var selectedVersion = await _mainWindow.Dispatcher.InvokeAsync(() => 
                _mainWindow.ListVersions.SelectedItem?.ToString());

            if (string.IsNullOrEmpty(selectedVersion))
            {
                return "错误：未在首页选择 Minecraft 版本。";
            }

            string folderName = type switch
            {
                "mod" => "mods",
                "shader" => "shaderpacks",
                "resourcepack" => "resourcepacks",
                _ => "mods"
            };

            var task = await CreateDownloadTask($"AI 下载 {type}: {fileName}");
            
            try
            {
                string path = Path.Combine(_mainWindow.GetBaseMcPath()?.BasePath ?? "", "versions", selectedVersion, folderName);
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);

                string filePath = Path.Combine(path, fileName);
                if (File.Exists(filePath))
                {
                    return $"跳过：{fileName} 已存在。";
                }

                using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
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
                return $"成功：{fileName} 已下载到 {selectedVersion} 的 {folderName} 文件夹。";
            }
            catch (Exception ex)
            {
                task.Status = $"错误: {ex.Message}";
                return $"失败：{ex.Message}";
            }
        }

        [KernelFunction, Description("根据下载链接和文件名下载 Mod。")]
        public async Task<string> DownloadModAsync(
            [Description("Mod 的下载链接")] string downloadUrl,
            [Description("保存的文件名")] string fileName)
        {
            return await DownloadResourceAsync(downloadUrl, fileName, "mod");
        }

        [KernelFunction, Description("删除 Mod、光影包或材质包。")]
        public async Task<string> DeleteResourceAsync(
            [Description("要删除的文件名（必须是完整的文件名，如 example-mod.jar）")] string fileName,
            [Description("资源类型: mod, shader, resourcepack")] string type)
        {
            var selectedVersion = await _mainWindow.Dispatcher.InvokeAsync(() => 
                _mainWindow.ListVersions.SelectedItem?.ToString());

            if (string.IsNullOrEmpty(selectedVersion))
            {
                return "错误：未在首页选择 Minecraft 版本。";
            }

            string folderName = type switch
            {
                "mod" => "mods",
                "shader" => "shaderpacks",
                "resourcepack" => "resourcepacks",
                _ => "mods"
            };

            try
            {
                string path = Path.Combine(_mainWindow.GetBaseMcPath()?.BasePath ?? "", "versions", selectedVersion, folderName);
                string filePath = Path.Combine(path, fileName);

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    return $"成功：已删除 {selectedVersion} 的 {folderName} 中的 {fileName}。";
                }
                else
                {
                    return $"错误：在 {folderName} 中未找到文件 {fileName}。";
                }
            }
            catch (Exception ex)
            {
                return $"删除失败: {ex.Message}";
            }
        }

        [KernelFunction, Description("确保当前环境已安装合适的光影加载器（如 Iris 或 Oculus）。")]
        public async Task<string> EnsureShaderLoaderAsync(
            [Description("Minecraft 版本号")] string mcVersion,
            [Description("模组加载器，例如 forge, fabric, quilt, neoforge")] string loader)
        {
            string targetMod = "";
            if (loader.ToLower() == "fabric" || loader.ToLower() == "quilt")
            {
                targetMod = "iris";
            }
            else if (loader.ToLower() == "forge" || loader.ToLower() == "neoforge")
            {
                targetMod = "oculus";
            }
            else
            {
                return "错误：纯净版不支持直接安装光影加载器，请先安装 Fabric 或 Forge 等加载器。";
            }

            try
            {
                // 1. 检查是否已安装
                var localMods = await ListLocalModsAsync();
                if (localMods.ToLower().Contains(targetMod))
                {
                    return $"提示：检测到已安装 {targetMod}，无需重复安装。";
                }

                // 2. 搜索并安装
                string searchRes = await SearchModAsync(targetMod, mcVersion, loader);
                using var doc = JsonDocument.Parse(searchRes);
                var hits = doc.RootElement.GetProperty("hits").EnumerateArray();
                if (!hits.Any())
                {
                    return $"错误：在 Modrinth 上未找到适用于 {mcVersion} {loader} 的 {targetMod}。";
                }

                var project = hits.First();
                string projectId = project.GetProperty("project_id").GetString()!;

                string versionsRes = await GetResourceVersionsAsync(projectId, mcVersion, loader);
                using var versionsDoc = JsonDocument.Parse(versionsRes);
                var versions = versionsDoc.RootElement.EnumerateArray();
                if (!versions.Any())
                {
                    return $"错误：未找到 {targetMod} 的有效版本。";
                }

                var bestVersion = versions.First();
                string versionId = bestVersion.GetProperty("id").GetString()!;

                return await InstallModWithDependenciesAsync(versionId, mcVersion, loader);
            }
            catch (Exception ex)
            {
                return $"确保光影加载器失败: {ex.Message}";
            }
        }

        [KernelFunction, Description("安装 Mod 及其所有必需的前置依赖。")]
        public async Task<string> InstallModWithDependenciesAsync(
            [Description("Mod 的版本 ID (version_id)")] string versionId,
            [Description("Minecraft 版本号")] string mcVersion,
            [Description("模组加载器")] string loader)
        {
            var installedVersions = new HashSet<string>();
            var results = new List<string>();
            
            async Task ProcessVersion(string vId)
            {
                if (installedVersions.Contains(vId)) return;
                installedVersions.Add(vId);

                try
                {
                    string url = $"https://api.modrinth.com/v2/version/{vId}";
                    var response = await _httpClient.GetStringAsync(url);
                    var version = JsonSerializer.Deserialize<ModrinthVersion>(response);

                    if (version == null || version.files == null || version.files.Count == 0) return;

                    var primaryFile = version.files.FirstOrDefault(f => f.primary) ?? version.files[0];
                    if (primaryFile.url != null && primaryFile.filename != null)
                    {
                        var res = await DownloadModAsync(primaryFile.url, primaryFile.filename);
                        results.Add($"{version.name ?? vId}: {res}");
                    }

                    if (version.dependencies != null)
                    {
                        foreach (var dep in version.dependencies)
                        {
                            if (dep.dependency_type == "required")
                            {
                                if (!string.IsNullOrEmpty(dep.version_id))
                                {
                                    await ProcessVersion(dep.version_id);
                                }
                                else if (!string.IsNullOrEmpty(dep.project_id))
                                {
                                    string depUrl = $"https://api.modrinth.com/v2/project/{dep.project_id}/version?loaders=[\"{loader.ToLower()}\"]&game_versions=[\"{mcVersion}\"]";
                                    var depResponse = await _httpClient.GetStringAsync(depUrl);
                                    var depVersions = JsonSerializer.Deserialize<List<ModrinthVersion>>(depResponse);
                                    if (depVersions != null && depVersions.Count > 0)
                                    {
                                        await ProcessVersion(depVersions[0].id!);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    results.Add($"处理版本 {vId} 时出错: {ex.Message}");
                }
            }

            await ProcessVersion(versionId);
            return string.Join("\n", results);
        }

        #endregion

        #region Client Installation

        [KernelFunction, Description("安装指定版本的 Minecraft 纯净版。")]
        public async Task<string> InstallVanillaAsync(
            [Description("Minecraft 版本号，例如 1.20.1")] string mcVersion)
        {
            var task = await CreateDownloadTask($"AI 安装: {mcVersion}");

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

                await launcher.InstallAsync(mcVersion);
                
                CompleteTask(task);
                return $"成功：Minecraft {mcVersion} 已安装。";
            }
            catch (Exception ex)
            {
                return FailTask(task, ex);
            }
        }

        [KernelFunction, Description("安装指定版本的 Forge 核心。")]
        public async Task<string> InstallForgeAsync(
            [Description("Minecraft 版本号，例如 1.20.1")] string mcVersion,
            [Description("Forge 版本号，例如 47.2.0")] string forgeVersion)
        {
            var task = await CreateDownloadTask($"AI 安装 Forge: {mcVersion} ({forgeVersion})");

            try
            {
                var launcher = _mainWindow.GetLauncher();
                if (launcher == null) return "错误：启动器实例未初始化。";

                task.Status = "正在安装 Forge...";
                _mainWindow.UpdateMainProgress("正在安装 Forge...", 0.5);
                var forgeInstaller = new ForgeInstaller(launcher);
                await forgeInstaller.Install(mcVersion, forgeVersion);

                CompleteTask(task);
                return $"成功：Forge {mcVersion}-{forgeVersion} 已安装。";
            }
            catch (Exception ex)
            {
                return FailTask(task, ex);
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
                return JsonSerializer.Serialize(versions.Take(5).Select(v => new { v.ForgeVersionName }));
            }
            catch (Exception ex)
            {
                return $"获取 Forge 版本失败: {ex.Message}";
            }
        }

        [KernelFunction, Description("安装指定版本的 NeoForge 核心。")]
        public async Task<string> InstallNeoForgeAsync(
            [Description("Minecraft 版本号")] string mcVersion,
            [Description("NeoForge 版本号")] string neoforgeVersion)
        {
            var task = await CreateDownloadTask($"AI 安装 NeoForge: {mcVersion} ({neoforgeVersion})");

            try
            {
                var launcher = _mainWindow.GetLauncher();
                if (launcher == null) return "错误：启动器实例未初始化。";

                task.Status = "正在安装 NeoForge...";
                _mainWindow.UpdateMainProgress("正在安装 NeoForge...", 0.5);
                var neoInstaller = new NeoForgeInstaller(launcher);
                await neoInstaller.Install(mcVersion, neoforgeVersion);

                CompleteTask(task);
                return $"成功：NeoForge {mcVersion}-{neoforgeVersion} 已安装。";
            }
            catch (Exception ex)
            {
                return FailTask(task, ex);
            }
        }

        [KernelFunction, Description("获取指定 Minecraft 版本的 NeoForge 版本列表。")]
        public async Task<string> GetNeoForgeVersionsAsync(
            [Description("Minecraft 版本号")] string mcVersion)
        {
            await Task.Yield();
            try
            {
                var launcher = _mainWindow.GetLauncher();
                if (launcher == null) return "错误：启动器实例未初始化。";

                // 暂时返回一个通用的提示，因为不同版本的 NeoForge 安装器 API 可能不同
                return "请访问 NeoForge 官网查看版本，或尝试输入具体版本号进行安装。";
            }
            catch (Exception ex)
            {
                return $"获取 NeoForge 版本失败: {ex.Message}";
            }
        }

        [KernelFunction, Description("获取指定 Minecraft 版本的 Fabric 加载器版本列表。")]
        public async Task<string> GetFabricVersionsAsync(
            [Description("Minecraft 版本号")] string mcVersion)
        {
            try
            {
                var response = await _httpClient.GetStringAsync($"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}");
                using var doc = JsonDocument.Parse(response);
                var versions = doc.RootElement.EnumerateArray()
                    .Take(5)
                    .Select(item => item.GetProperty("loader").GetProperty("version").GetString())
                    .ToList();
                return JsonSerializer.Serialize(versions);
            }
            catch (Exception ex)
            {
                return $"获取 Fabric 版本失败: {ex.Message}";
            }
        }

        [KernelFunction, Description("获取指定 Minecraft 版本的 Quilt 加载器版本列表。")]
        public async Task<string> GetQuiltVersionsAsync(
            [Description("Minecraft 版本号")] string mcVersion)
        {
            try
            {
                var response = await _httpClient.GetStringAsync($"https://meta.quiltmc.org/v2/versions/loader/{mcVersion}");
                using var doc = JsonDocument.Parse(response);
                var versions = doc.RootElement.EnumerateArray()
                    .Take(5)
                    .Select(item => item.GetProperty("loader").GetProperty("version").GetString())
                    .ToList();
                return JsonSerializer.Serialize(versions);
            }
            catch (Exception ex)
            {
                return $"获取 Quilt 版本失败: {ex.Message}";
            }
        }

        #endregion

        #region Helpers

        private async Task<DownloadTask> CreateDownloadTask(string name)
        {
            return await _mainWindow.Dispatcher.InvokeAsync(() => 
            {
                var t = new DownloadTask { Name = name, Progress = 0, Status = "准备中..." };
                _mainWindow.DownloadTasks.Add(t);
                return t;
            });
        }

        private void CompleteTask(DownloadTask task)
        {
            task.Progress = 100;
            task.Status = "安装完成";
            _mainWindow.UpdateMainProgress("安装完成", 1.0);
            _mainWindow.Dispatcher.Invoke(() => _mainWindow.CallRefreshVersionList());
        }

        private string FailTask(DownloadTask task, Exception ex)
        {
            task.Status = $"错误: {ex.Message}";
            return $"失败：{ex.Message}";
        }

        #endregion
    }

    #region API Models

    public class ModrinthVersion
    {
        public string? id { get; set; }
        public string? project_id { get; set; }
        public string? name { get; set; }
        public List<ModrinthDependency>? dependencies { get; set; }
        public List<ModrinthFile>? files { get; set; }
    }

    public class ModrinthDependency
    {
        public string? version_id { get; set; }
        public string? project_id { get; set; }
        public string? dependency_type { get; set; }
    }

    public class ModrinthFile
    {
        public string? url { get; set; }
        public string? filename { get; set; }
        public bool primary { get; set; }
    }

    #endregion
}