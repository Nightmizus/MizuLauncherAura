using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace MizuLauncher
{
    public class LittleSkinFetcher
    {
        private static readonly HttpClient client = new HttpClient();
        private const string ApiRoot = "https://littleskin.cn/api/yggdrasil";
        private static readonly string CacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", "skins");

        public static async Task<string?> GetSkinUrlAsync(string username)
        {
            try
            {
                // 1. 优先尝试从正版 (Mojang) 获取
                string mojangUuidUrl = $"https://api.mojang.com/users/profiles/minecraft/{username}";
                HttpResponseMessage mojangResponse = await client.GetAsync(mojangUuidUrl);

                if (mojangResponse.IsSuccessStatusCode)
                {
                    string mojangUuidJson = await mojangResponse.Content.ReadAsStringAsync();
                    using JsonDocument mojangUuidDoc = JsonDocument.Parse(mojangUuidJson);
                    string uuid = mojangUuidDoc.RootElement.GetProperty("id").GetString() ?? "";

                    string mojangProfileUrl = $"https://sessionserver.mojang.com/session/minecraft/profile/{uuid}";
                    string mojangProfileJson = await client.GetStringAsync(mojangProfileUrl);
                    using JsonDocument mojangProfileDoc = JsonDocument.Parse(mojangProfileJson);

                    string base64Textures = "";
                    foreach (JsonElement prop in mojangProfileDoc.RootElement.GetProperty("properties").EnumerateArray())
                    {
                        if (prop.GetProperty("name").GetString() == "textures")
                        {
                            base64Textures = prop.GetProperty("value").GetString() ?? "";
                            break;
                        }
                    }

                    if (!string.IsNullOrEmpty(base64Textures))
                    {
                        byte[] decodedBytes = Convert.FromBase64String(base64Textures);
                        string textureJson = Encoding.UTF8.GetString(decodedBytes);
                        using JsonDocument textureDoc = JsonDocument.Parse(textureJson);

                        if (textureDoc.RootElement.GetProperty("textures").TryGetProperty("SKIN", out JsonElement skinElement))
                        {
                            return skinElement.GetProperty("url").GetString();
                        }
                    }
                }

                // 2. 如果正版获取失败，尝试从 LittleSkin 获取
                string uuidUrl = $"{ApiRoot}/api/users/profiles/minecraft/{username}";
                HttpResponseMessage uuidResponse = await client.GetAsync(uuidUrl);

                if (uuidResponse.IsSuccessStatusCode)
                {
                    string uuidJson = await uuidResponse.Content.ReadAsStringAsync();
                    using JsonDocument uuidDoc = JsonDocument.Parse(uuidJson);
                    string uuid = uuidDoc.RootElement.GetProperty("id").GetString() ?? "";

                    string profileUrl = $"{ApiRoot}/sessionserver/session/minecraft/profile/{uuid}";
                    string profileJson = await client.GetStringAsync(profileUrl);
                    using JsonDocument profileDoc = JsonDocument.Parse(profileJson);

                    string base64Textures = "";
                    foreach (JsonElement prop in profileDoc.RootElement.GetProperty("properties").EnumerateArray())
                    {
                        if (prop.GetProperty("name").GetString() == "textures")
                        {
                            base64Textures = prop.GetProperty("value").GetString() ?? "";
                            break;
                        }
                    }

                    if (!string.IsNullOrEmpty(base64Textures))
                    {
                        byte[] decodedBytes = Convert.FromBase64String(base64Textures);
                        string textureJson = Encoding.UTF8.GetString(decodedBytes);
                        using JsonDocument textureDoc = JsonDocument.Parse(textureJson);

                        if (textureDoc.RootElement.GetProperty("textures").TryGetProperty("SKIN", out JsonElement skinElement))
                        {
                            return skinElement.GetProperty("url").GetString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Skin fetch error: {ex.Message}");
                return null;
            }

            return null;
        }

        private static readonly string SteveCachePath = Path.Combine(CacheDir, "steve.png");

        public static async Task<BitmapImage?> GetAvatarAsync(string username)
        {
            try
            {
                if (!Directory.Exists(CacheDir))
                {
                    Directory.CreateDirectory(CacheDir);
                }

                string cachePath = Path.Combine(CacheDir, $"{username}.png");

                if (!File.Exists(cachePath))
                {
                    string? skinUrl = await GetSkinUrlAsync(username);
                    if (string.IsNullOrEmpty(skinUrl))
                    {
                        // 如果获取不到皮肤，检查是否有 Steve 缓存，没有就下载一个
                        if (!File.Exists(SteveCachePath))
                        {
                            try
                            {
                                byte[] steveBytes = await client.GetByteArrayAsync("https://littleskin.cn/textures/7399453957597893963"); // 一个经典的 Steve 皮肤 URL
                                await File.WriteAllBytesAsync(SteveCachePath, steveBytes);
                            }
                            catch { return null; }
                        }
                        return ExtractAvatarFromSkin(SteveCachePath);
                    }

                    byte[] skinBytes = await client.GetByteArrayAsync(skinUrl);
                    await File.WriteAllBytesAsync(cachePath, skinBytes);
                }

                // 从皮肤图片中提取头像区域 (8,8,8,8)
                return ExtractAvatarFromSkin(cachePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Avatar fetch error: {ex.Message}");
                return null;
            }
        }

        private static BitmapImage? ExtractAvatarFromSkin(string skinPath)
        {
            try
            {
                BitmapImage fullSkin = new BitmapImage();
                fullSkin.BeginInit();
                fullSkin.UriSource = new Uri(skinPath, UriKind.Absolute);
                fullSkin.CacheOption = BitmapCacheOption.OnLoad;
                fullSkin.CreateOptions = BitmapCreateOptions.None;
                fullSkin.EndInit();

                if (fullSkin.PixelWidth == 0)
                {
                    // 如果图片还没加载完，强制同步加载（或者至少等待）
                    // 在 OnLoad 模式下，EndInit 之后应该已经加载了，但预防万一
                }

                // 提取基础头部 (8x8)
                CroppedBitmap baseHead = new CroppedBitmap(fullSkin, new System.Windows.Int32Rect(8, 8, 8, 8));

                // 提取帽子层 (8x8)
                CroppedBitmap hatLayer = new CroppedBitmap(fullSkin, new System.Windows.Int32Rect(40, 8, 8, 8));

                // 读取像素数据
                int stride = 8 * 4; // 8 pixels * 4 bytes per pixel (BGRA)
                byte[] basePixels = new byte[8 * stride];
                byte[] hatPixels = new byte[8 * stride];
                baseHead.CopyPixels(basePixels, stride, 0);
                hatLayer.CopyPixels(hatPixels, stride, 0);

                // --- 重构：头像加载逻辑 ---
                // 你可以直接调整这个值来缩放底层大小
                int baseRenderSize = 72; 
                // 顶层（hatLayer）：你可以直接调整这个值来缩放顶层大小（当前觉得86合适）
                int hatRenderSize = 86; 
                
                // 最终画布大小：必须至少能容纳最大的那一层（也就是帽子层）
                int targetSize = hatRenderSize; 
                int targetStride = targetSize * 4;
                byte[] targetPixels = new byte[targetSize * targetStride];

                // 1. 渲染基础头部
                int baseOffset = (targetSize - baseRenderSize) / 2; // (86 - 72) / 2 = 7

                for (int y = 0; y < baseRenderSize; y++)
                {
                    for (int x = 0; x < baseRenderSize; x++)
                    {
                        // 映射回 8x8 的基础图 (最近邻插值)
                        int srcX = (x * 8) / baseRenderSize;
                        int srcY = (y * 8) / baseRenderSize;

                        int srcIndex = (srcY * stride) + (srcX * 4);
                        int destIndex = ((y + baseOffset) * targetStride) + ((x + baseOffset) * 4);

                        targetPixels[destIndex] = basePixels[srcIndex];
                        targetPixels[destIndex + 1] = basePixels[srcIndex + 1];
                        targetPixels[destIndex + 2] = basePixels[srcIndex + 2];
                        targetPixels[destIndex + 3] = basePixels[srcIndex + 3];
                    }
                }

                // 2. 渲染帽子层 (顶层居中覆盖)
                int hatOffset = (targetSize - hatRenderSize) / 2; // (86 - 86) / 2 = 0

                for (int y = 0; y < hatRenderSize; y++)
                {
                    for (int x = 0; x < hatRenderSize; x++)
                    {
                        // 映射回源帽子层 (8x8 完整映射)
                        int srcX = (x * 8) / hatRenderSize;
                        int srcY = (y * 8) / hatRenderSize;

                        int srcIndex = (srcY * stride) + (srcX * 4);
                        int destIndex = ((y + hatOffset) * targetStride) + ((x + hatOffset) * 4);

                        byte b = hatPixels[srcIndex];
                        byte g = hatPixels[srcIndex + 1];
                        byte r = hatPixels[srcIndex + 2];
                        byte alpha = hatPixels[srcIndex + 3];
                        
                        if (alpha > 0)
                        {
                            if (alpha == 255)
                            {
                                targetPixels[destIndex] = b;
                                targetPixels[destIndex + 1] = g;
                                targetPixels[destIndex + 2] = r;
                                targetPixels[destIndex + 3] = 255;
                            }
                            else
                            {
                                float a = alpha / 255f;
                                float invA = 1f - a;
                                
                                targetPixels[destIndex] = (byte)((b * a) + (targetPixels[destIndex] * invA));
                                targetPixels[destIndex + 1] = (byte)((g * a) + (targetPixels[destIndex + 1] * invA));
                                targetPixels[destIndex + 2] = (byte)((r * a) + (targetPixels[destIndex + 2] * invA));
                                targetPixels[destIndex + 3] = (byte)Math.Min(255, targetPixels[destIndex + 3] + alpha);
                            }
                        }
                    }
                }

                // 创建最终的 WriteableBitmap
                WriteableBitmap finalBitmap = new WriteableBitmap(
                    targetSize, targetSize, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
                
                finalBitmap.WritePixels(new System.Windows.Int32Rect(0, 0, targetSize, targetSize), targetPixels, targetStride, 0);
                finalBitmap.Freeze();

                // 冻结以确保跨线程安全
                // finalBitmap is already frozen above

                // 将 RenderTargetBitmap 转回 BitmapImage 方便 UI 使用
                BitmapImage avatar = new BitmapImage();
                using (MemoryStream ms = new MemoryStream())
                {
                    PngBitmapEncoder encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(finalBitmap));
                    encoder.Save(ms);
                    ms.Seek(0, SeekOrigin.Begin);

                    avatar.BeginInit();
                    avatar.StreamSource = ms;
                    avatar.CacheOption = BitmapCacheOption.OnLoad;
                    avatar.EndInit();
                    avatar.Freeze(); // 冻结以确保跨线程安全
                }
                return avatar;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Avatar extraction error: {ex.Message}");
                return null;
            }
        }
    }
}
