using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.SkinManager.Configuration;

namespace Jellyfin.Plugin.SkinManager.Services
{
    public static class SkinInjector
    {
        private const string StartMarker = "<!-- SkinManager-Start -->";
        private const string EndMarker = "<!-- SkinManager-End -->";

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public static string InjectTheme(PatchRequestPayload payload)
        {
            try
            {
                string html = payload?.Contents;
                if (string.IsNullOrEmpty(html))
                    return html ?? string.Empty;

                html = Regex.Replace(
                    html,
                    Regex.Escape(StartMarker) + @"[\s\S]*?" + Regex.Escape(EndMarker) + @"\n?",
                    string.Empty);

                PluginConfiguration config = Plugin.Instance?.Configuration;
                if (config != null)
                {
                    string injection = BuildInjection(config);
                    if (!string.IsNullOrEmpty(injection))
                    {
                        string block = "\n" + StartMarker + "\n" + injection + EndMarker + "\n";
                        html = Regex.Replace(html, @"(</body>)", block + "$1");
                    }
                }

                return html;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[SkinManager] InjectTheme error: " + ex.Message);
                return payload?.Contents ?? string.Empty;
            }
        }

        private static string BuildInjection(PluginConfiguration config)
        {
            string result = string.Empty;

            if (!string.IsNullOrWhiteSpace(config.SelectedCssUrl)
                && config.SelectedCssUrl != "Default")
            {
                string varBlock = BuildCssVarBlock(config.ThemeVars);
                string varJson = BuildVarsJson(config.ThemeVars);
                string escapedUrl = EscapeJs(config.SelectedCssUrl);
                string escapedVer = EscapeJs(config.SelectedVersion ?? string.Empty);

                result +=
                    "<script id=\"skinmanager-loader\">\n" +
                    "(function() {\n" +
                    "    var cssUrl      = \"" + escapedUrl + "\";\n" +
                    "    var repoUrl     = \"" + EscapeJs(config.SkinUrl) + "\";\n" +
                    "    var themeName   = \"" + EscapeJs(config.Skin) + "\";\n" +
                    "    var themeVer    = \"" + escapedVer + "\";\n" +
                    "    var varBlock    = \"" + EscapeJs(varBlock) + "\";\n" +
                    "    var vars        = " + varJson + ";\n" +
                    "    var pluginId    = \"e10fb9d4-c941-4c6e-8260-2641031c2618\";\n" +
                    "    var CACHE_CSS   = 'skinmanager-v' + (themeVer || '0');\n" +
                    "    var CACHE_META  = 'skinmanager-meta';\n" +
                    "\n" +
                    "    function substituteVars(code, v) {\n" +
                    "        Object.keys(v).forEach(function(key) {\n" +
                    "            code = code.split('{{{' + key + '}}}').join(v[key]);\n" +
                    "        });\n" +
                    "        return code;\n" +
                    "    }\n" +
                    "\n" +
                    "    function resolveConditionalImports(code) {\n" +
                    "        var pattern = /\\/\\*\\s*@sm-import-if\\s+(\\S+)\\s+(\\S+)\\s*\\*\\//g;\n" +
                    "        var promises = [];\n" +
                    "        var match;\n" +
                    "        while ((match = pattern.exec(code)) !== null) {\n" +
                    "            (function(key, url) {\n" +
                    "                var enabled = vars[key];\n" +
                    "                if (enabled === 'true' || enabled === true) {\n" +
                    "                    promises.push(cachedFetch(url, CACHE_CSS));\n" +
                    "                } else {\n" +
                    "                    promises.push(Promise.resolve(''));\n" +
                    "                }\n" +
                    "            })(match[1], match[2]);\n" +
                    "        }\n" +
                    "        var stripped = code.replace(/\\/\\*\\s*@sm-import-if\\s+\\S+\\s+\\S+\\s*\\*\\//g, '');\n" +
                    "        return Promise.all(promises).then(function(addons) {\n" +
                    "            return stripped + '\\n' + addons.join('\\n');\n" +
                    "        });\n" +
                    "    }\n" +
                    "\n" +
                    "    // Generic stale-while-revalidate fetch backed by Cache API\n" +
                    "    function cachedFetch(url, cacheName) {\n" +
                    "        if (!window.caches) return fetch(url).then(function(r) { return r.text(); });\n" +
                    "        return caches.open(cacheName).then(function(cache) {\n" +
                    "            return cache.match(url).then(function(cached) {\n" +
                    "                var networkFetch = fetch(url).then(function(r) {\n" +
                    "                    cache.put(url, r.clone());\n" +
                    "                    return r.text();\n" +
                    "                }).catch(function() { return null; });\n" +
                    "                if (cached) {\n" +
                    "                    networkFetch.catch(function() {});\n" +
                    "                    return cached.text();\n" +
                    "                }\n" +
                    "                return networkFetch.then(function(text) { return text || ''; });\n" +
                    "            });\n" +
                    "        });\n" +
                    "    }\n" +
                    "\n" +
                    "    // Check skins.json for a version bump on the active theme.\n" +
                    "    // If the version changed, update SelectedVersion in config and\n" +
                    "    // evict the old CSS cache so the new version loads next page load.\n" +
                    "    function checkForUpdate() {\n" +
                    "        if (!repoUrl || !themeName) return;\n" +
                    "        cachedFetch(repoUrl, CACHE_META).then(function(text) {\n" +
                    "            var themes;\n" +
                    "            try { themes = JSON.parse(text); } catch(e) { return; }\n" +
                    "            var entry = themes.find(function(t) { return t.name === themeName; });\n" +
                    "            if (!entry || !entry.version) return;\n" +
                    "            if (entry.version !== themeVer) {\n" +
                    "                console.log('[SkinManager] Theme update detected: ' + themeVer + ' -> ' + entry.version);\n" +
                    "                // Evict old CSS cache\n" +
                    "                if (window.caches) caches.delete(CACHE_CSS);\n" +
                    "                // Update SelectedVersion and SelectedCssUrl in config silently\n" +
                    "                ApiClient.getPluginConfiguration(pluginId).then(function(cfg) {\n" +
                    "                    cfg.SelectedVersion = entry.version;\n" +
                    "                    if (entry.cssUrl) cfg.SelectedCssUrl = entry.cssUrl;\n" +
                    "                    return ApiClient.updatePluginConfiguration(pluginId, cfg);\n" +
                    "                }).then(function() {\n" +
                    "                    // Reload so the new version is fetched and injected\n" +
                    "                    window.location.reload();\n" +
                    "                }).catch(function(e) {\n" +
                    "                    console.warn('[SkinManager] Failed to save update: ', e);\n" +
                    "                });\n" +
                    "            }\n" +
                    "        }).catch(function() {});\n" +
                    "    }\n" +
                    "\n" +
                    "    // Evict CSS caches from old versions\n" +
                    "    function evictOldCaches() {\n" +
                    "        if (!window.caches) return;\n" +
                    "        caches.keys().then(function(keys) {\n" +
                    "            keys.forEach(function(key) {\n" +
                    "                if (key.startsWith('skinmanager-v') && key !== CACHE_CSS)\n" +
                    "                    caches.delete(key);\n" +
                    "            });\n" +
                    "        });\n" +
                    "    }\n" +
                    "\n" +
                    "    function injectCss(code) {\n" +
                    "        var existing = document.getElementById('skinmanager-theme');\n" +
                    "        if (existing) existing.remove();\n" +
                    "        var css = substituteVars(code, vars) + varBlock;\n" +
                    "        try {\n" +
                    "            var blob = new Blob([css], { type: 'text/css' });\n" +
                    "            var url  = URL.createObjectURL(blob);\n" +
                    "            var link = document.createElement('link');\n" +
                    "            link.rel  = 'stylesheet';\n" +
                    "            link.id   = 'skinmanager-theme';\n" +
                    "            link.href = url;\n" +
                    "            document.head.appendChild(link);\n" +
                    "        } catch(e) {\n" +
                    "            var s = document.createElement('style');\n" +
                    "            s.id = 'skinmanager-theme';\n" +
                    "            s.textContent = css;\n" +
                    "            document.head.appendChild(s);\n" +
                    "        }\n" +
                    "    }\n" +
                    "\n" +
                    "    evictOldCaches();\n" +
                    "    checkForUpdate();\n" +
                    "    cachedFetch(cssUrl, CACHE_CSS)\n" +
                    "        .then(function(code) { return resolveConditionalImports(code); })\n" +
                    "        .then(function(code) { injectCss(code); })\n" +
                    "        .catch(function(e) { console.warn('[SkinManager] Failed to load theme: ' + cssUrl, e); });\n" +
                    "})();\n" +
                    "</script>\n";
            }

            if (!string.IsNullOrWhiteSpace(config.CustomCss))
                result += "<style id=\"skinmanager-custom\">\n" + config.CustomCss + "\n</style>\n";

            return result;
        }

        /// <summary>
        /// Deserializes ThemeVars JSON and builds a CSS :root block string.
        /// VAR_KEY becomes --var-key (UPPER_SNAKE to lower-kebab).
        /// Returned as a plain string to be embedded in the JS loader.
        /// </summary>
        private static string BuildCssVarBlock(string themeVarsJson)
        {
            if (string.IsNullOrWhiteSpace(themeVarsJson) || themeVarsJson == "{}")
                return string.Empty;

            Dictionary<string, string> vars;
            try
            {
                vars = JsonSerializer.Deserialize<Dictionary<string, string>>(themeVarsJson, JsonOpts);
            }
            catch
            {
                return string.Empty;
            }

            if (vars == null || vars.Count == 0)
                return string.Empty;

            var sb = new StringBuilder(":root {\n");
            bool hasAny = false;
            foreach (var kv in vars)
            {
                if (string.IsNullOrWhiteSpace(kv.Value)) continue;
                string propName = "--" + kv.Key.ToLowerInvariant().Replace("_", "-");
                string value = kv.Value.Trim();
                if (value.Contains(" ") && !value.StartsWith("\"") && !value.StartsWith("'"))
                    value = "\"" + value + "\"";
                sb.Append("    " + propName + ": " + value + ";\n");
                hasAny = true;
            }
            sb.Append("}\n");
            return hasAny ? sb.ToString() : string.Empty;
        }

        /// <summary>
        /// Builds a JSON object string of var key/value pairs for the JS loader's
        /// substituteVars function, enabling {{KEY}} token replacement in CSS files.
        /// </summary>
        private static string BuildVarsJson(string themeVarsJson)
        {
            if (string.IsNullOrWhiteSpace(themeVarsJson) || themeVarsJson == "{}")
                return "{}";

            Dictionary<string, string> vars;
            try
            {
                vars = JsonSerializer.Deserialize<Dictionary<string, string>>(themeVarsJson, JsonOpts);
            }
            catch
            {
                return "{}";
            }

            if (vars == null || vars.Count == 0)
                return "{}";

            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var kv in vars)
            {
                if (string.IsNullOrWhiteSpace(kv.Value)) continue;
                if (!first) sb.Append(",");
                sb.Append("\"" + EscapeJs(kv.Key) + "\":\"" + EscapeJs(kv.Value.Trim()) + "\"");
                first = false;
            }
            sb.Append("}");
            return sb.ToString();
        }

        private static string EscapeJs(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r\n", "\\n")
                .Replace("\n", "\\n")
                .Replace("\r", "\\n");
        }
    }
}