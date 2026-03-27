using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jellyfin.Plugin.SkinManager.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SkinManager.Services
{
    /// <summary>
    /// Handles C# server-side compilation of Server themes.
    /// Downloads the main CSS, substitutes variables, resolves @sm-import-if conditional addons,
    /// and caches the result so the browser doesn't have to do it via Javascript.
    /// </summary>
    public static class ServerThemeCompiler
    {

        
        private static string _cachedKey = null;
        private static string _cachedCss = string.Empty;
        private static readonly object _compileLock = new object();

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly Regex ImportIfRegex = new Regex(
            @"\/\*\s*@sm-import-if\s+(\S+)\s+(\S+)\s*\*\/",
            RegexOptions.Compiled);

        public static void InvalidateCache()
        {
            lock (_compileLock)
            {
                _cachedKey = null;
            }
        }

        public static async Task<string> GetCompiledThemeAsync(PluginConfiguration config, ILogger logger)
        {
            if (config == null || string.IsNullOrWhiteSpace(config.SelectedCssUrl) || config.SelectedCssUrl == "Default")
                return string.Empty;

            string key = $"{config.SelectedCssUrl}|{config.SelectedVersion}|{config.ThemeVars}|{config.HasConditionalImports}";

            lock (_compileLock)
            {
                if (_cachedKey == key && !string.IsNullOrEmpty(_cachedCss))
                    return _cachedCss;
            }

            string resultCss = string.Empty;

            try
            {
                resultCss = await SkinResourceProxy.GetResourceAsync(config.SelectedCssUrl, logger);
                if (string.IsNullOrEmpty(resultCss))
                {
                    logger?.LogWarning("[SkinManager] Failed to fetch server theme CSS from proxy. Url: {Url}", config.SelectedCssUrl);
                    return string.Empty;
                }

                var vars = new Dictionary<string, string>();
                try
                {
                    if (!string.IsNullOrWhiteSpace(config.ThemeVars) && config.ThemeVars != "{}")
                    {
                        vars = JsonSerializer.Deserialize<Dictionary<string, string>>(config.ThemeVars, JsonOpts);
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "[SkinManager] Failed to parse ThemeVars for compilation.");
                }

                if (vars != null)
                {
                    foreach (var kv in vars)
                    {
                        if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
                        {
                            resultCss = resultCss.Replace("{{" + kv.Key + "}}", kv.Value);
                        }
                    }
                }

                if (config.HasConditionalImports && vars != null && vars.Count > 0)
                {
                    var matches = ImportIfRegex.Matches(resultCss);
                    var addonChunks = new List<string>();

                    foreach (Match match in matches)
                    {
                        string varKey = match.Groups[1].Value;
                        string url = match.Groups[2].Value;

                        if (vars.TryGetValue(varKey, out string enabledVal) &&
                            (string.Equals(enabledVal, "true", StringComparison.OrdinalIgnoreCase) || enabledVal == "1"))
                        {
                            try
                            {
                                string addonCss = await SkinResourceProxy.GetResourceAsync(url, logger);
                                if (!string.IsNullOrEmpty(addonCss))
                                {
                                    addonChunks.Add(addonCss);
                                }
                                else
                                {
                                    logger?.LogWarning("[SkinManager] Addon proxy fetch returned empty for URL {Url}", url);
                                }
                            }
                            catch (Exception ex)
                            {
                                logger?.LogWarning(ex, "[SkinManager] Failed to fetch addon CSS from proxy: {Url}", url);
                            }
                        }
                    }

                    resultCss = ImportIfRegex.Replace(resultCss, string.Empty);

                    if (addonChunks.Count > 0)
                    {
                        resultCss += "\n" + string.Join("\n", addonChunks);
                    }
                }

                lock (_compileLock)
                {
                    _cachedCss = resultCss;
                    _cachedKey = key;
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "[SkinManager] Exception compiling server theme: {Url}", config.SelectedCssUrl);
            }

            return resultCss;
        }
    }
}
