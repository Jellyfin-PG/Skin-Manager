using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SkinManager.Services
{
    /// <summary>
    /// Acts as an offline proxy cache. Downloads CSS lines from external URLs, 
    /// caches them to the local HDD using MD5 hashes, and serves them instantly on subsequent
    /// requests. Protects both Server Themes and User Themes from external CDN drops.
    /// </summary>
    public static class SkinResourceProxy
    {
        private static readonly HttpClient _httpClient = CreateHttpClient();
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate | System.Net.DecompressionMethods.Brotli
            };
            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            client.DefaultRequestHeaders.Add("User-Agent", "Jellyfin-SkinManager/1.5.0");
            client.DefaultRequestHeaders.Add("Accept", "*/*");
            return client;
        }

        private static string GetHashString(string input)
        {
            using (var md5 = MD5.Create())
            {
                byte[] bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
                var sb = new StringBuilder();
                foreach (var b in bytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }

        public static async Task<string> GetResourceAsync(string url, ILogger logger = null)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;

            try
            {
                string dataPath = Plugin.Instance.DataFolderPath;
                string cacheDir = Path.Combine(dataPath, "Cache");
                if (!Directory.Exists(cacheDir))
                {
                    Directory.CreateDirectory(cacheDir);
                }

                string hash = GetHashString(url);
                string filePath = Path.Combine(cacheDir, hash + ".css");

                if (File.Exists(filePath))
                {
                    return await File.ReadAllTextAsync(filePath);
                }

                await _semaphore.WaitAsync();
                try
                {
                    if (File.Exists(filePath))
                    {
                        return await File.ReadAllTextAsync(filePath);
                    }

                    var response = await _httpClient.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        logger?.LogWarning("[SkinManager] Failed to fetch proxy resource. Status: {Status} Url: {Url}", response.StatusCode, url);
                        return string.Empty;
                    }

                    string content = await response.Content.ReadAsStringAsync();
                    await File.WriteAllTextAsync(filePath, content, Encoding.UTF8);
                    
                    return content;
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "[SkinManager] Exception in SkinResourceProxy for url: {Url}", url);
                return string.Empty;
            }
        }
        
        public static void ClearCache()
        {
            try
            {
                string dataPath = Plugin.Instance.DataFolderPath;
                string cacheDir = Path.Combine(dataPath, "Cache");
                if (Directory.Exists(cacheDir))
                {
                    Directory.Delete(cacheDir, true);
                }
            }
            catch { }
        }
    }
}
