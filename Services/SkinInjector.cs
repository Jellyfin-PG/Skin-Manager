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
                bool isRawGithub = config.SelectedCssUrl.Contains("raw.githubusercontent.com")
                                 || config.SelectedCssUrl.Contains("gist.githubusercontent.com");
                bool hasVars = !string.IsNullOrEmpty(varBlock);
                bool hasAddons = config.HasConditionalImports;

                if (hasVars)
                    result += "<style id=\"skinmanager-vars\">\n" + varBlock + "</style>\n";

                string sharedFunctions =
                    "    function substituteVars(code, v) {\n" +
                    "        Object.keys(v).forEach(function(key) {\n" +
                    "            code = code.split('{{' + key + '}}').join(v[key]);\n" +
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
                    "    function cachedFetch(url, cacheName) {\n" +
                    "        if (!window.caches) return fetch(url).then(function(r) { return r.text(); });\n" +
                    "        return caches.open(cacheName).then(function(cache) {\n" +
                    "            return cache.match(url).then(function(cached) {\n" +
                    "                var networkFetch = fetch(url).then(function(r) {\n" +
                    "                    cache.put(url, r.clone());\n" +
                    "                    return r.text();\n" +
                    "                }).catch(function() { return null; });\n" +
                    "                if (cached) { networkFetch.catch(function() {}); return cached.text(); }\n" +
                    "                return networkFetch.then(function(text) { return text || ''; });\n" +
                    "            });\n" +
                    "        });\n" +
                    "    }\n" +
                    "\n" +
                    "    function injectBlob(code) {\n" +
                    "        var existing = document.getElementById('skinmanager-theme');\n" +
                    "        if (existing) existing.remove();\n" +
                    "        var css = substituteVars(code, vars);\n" +
                    "        try {\n" +
                    "            var blob = new Blob([css], { type: 'text/css' });\n" +
                    "            var link = document.createElement('link');\n" +
                    "            link.rel = 'stylesheet'; link.id = 'skinmanager-theme';\n" +
                    "            link.href = URL.createObjectURL(blob);\n" +
                    "            document.head.appendChild(link);\n" +
                    "        } catch(e) {\n" +
                    "            var s = document.createElement('style');\n" +
                    "            s.id = 'skinmanager-theme';\n" +
                    "            s.textContent = substituteVars(code, vars);\n" +
                    "            document.head.appendChild(s);\n" +
                    "        }\n" +
                    "    }\n" +
                    "\n" +
                    "    function checkForUpdate() {\n" +
                    "        if (!repoUrl || !themeName) return;\n" +
                    "        fetch(repoUrl).then(function(r){return r.text();}).then(function(text) {\n" +
                    "            var themes; try { themes = JSON.parse(text); } catch(e) { return; }\n" +
                    "            var entry = themes.find(function(t) { return t.name === themeName; });\n" +
                    "            if (!entry || !entry.version || entry.version === themeVer) return;\n" +
                    "            console.log('[SkinManager] Update: ' + themeVer + ' -> ' + entry.version);\n" +
                    "            if (window.caches) caches.delete(CACHE_CSS);\n" +
                    "            ApiClient.getPluginConfiguration(pluginId).then(function(cfg) {\n" +
                    "                cfg.SelectedVersion = entry.version;\n" +
                    "                if (entry.cssUrl) cfg.SelectedCssUrl = entry.cssUrl;\n" +
                    "                return ApiClient.updatePluginConfiguration(pluginId, cfg);\n" +
                    "            }).then(function() { window.location.reload(); })\n" +
                    "              .catch(function(e) { console.warn('[SkinManager] Update save failed: ', e); });\n" +
                    "        }).catch(function() {});\n" +
                    "    }\n" +
                    "\n" +
                    "    function evictOldCaches() {\n" +
                    "        if (!window.caches) return;\n" +
                    "        caches.keys().then(function(keys) {\n" +
                    "            keys.forEach(function(key) {\n" +
                    "                if (key.startsWith('skinmanager-v') && key !== CACHE_CSS) caches.delete(key);\n" +
                    "            });\n" +
                    "        });\n" +
                    "    }\n";

                string executionTail;
                if (isRawGithub || hasVars || hasAddons)
                {
                    executionTail =
                        "    evictOldCaches(); checkForUpdate();\n" +
                        "    cachedFetch(cssUrl, CACHE_CSS)\n" +
                        "        .then(function(code) { return resolveConditionalImports(code); })\n" +
                        "        .then(function(code) { injectBlob(code); })\n" +
                        "        .catch(function(e) { console.warn('[SkinManager] Load failed: ' + cssUrl, e); });\n";
                }
                else
                {
                    // CDN, no vars, no conditional imports — direct <link>, fastest path,
                    // no fetch needed, browser handles HTTP caching natively
                    executionTail =
                        "    function injectDirectLink() {\n" +
                        "        var existing = document.getElementById('skinmanager-theme');\n" +
                        "        if (existing) existing.remove();\n" +
                        "        var link = document.createElement('link');\n" +
                        "        link.rel = 'stylesheet'; link.id = 'skinmanager-theme'; link.href = cssUrl;\n" +
                        "        document.head.appendChild(link);\n" +
                        "    }\n" +
                        "    evictOldCaches(); checkForUpdate(); injectDirectLink();\n";
                }

                result +=
                    "<script id=\"skinmanager-loader\">\n" +
                    "(function() {\n" +
                    "    var cssUrl    = \"" + escapedUrl + "\";\n" +
                    "    var repoUrl   = \"" + EscapeJs(config.SkinUrl) + "\";\n" +
                    "    var themeName = \"" + EscapeJs(config.Skin) + "\";\n" +
                    "    var themeVer  = \"" + escapedVer + "\";\n" +
                    "    var vars      = " + varJson + ";\n" +
                    "    var pluginId  = \"e10fb9d4-c941-4c6e-8260-2641031c2618\";\n" +
                    "    var CACHE_CSS  = 'skinmanager-v' + (themeVer || '0');\n" +
                                        "\n" +
                    sharedFunctions +
                    executionTail +
                    "})();\n" +
                    "</script>\n";
            }

            if (!string.IsNullOrWhiteSpace(config.CustomCss))
                result += "<style id=\"skinmanager-custom\">\n" + config.CustomCss + "\n</style>\n";

            return result;
        }

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
                string propName = "--" + kv.Key.Trim().TrimStart('-');
                string value = kv.Value.Trim();
                bool isCssFunction = value.Contains("(");
                if (value.Contains(" ") && !isCssFunction && !value.StartsWith("\"") && !value.StartsWith("'"))
                    value = "\"" + value + "\"";
                sb.Append("    " + propName + ": " + value + ";\n");
                hasAny = true;
            }
            sb.Append("}\n");
            return hasAny ? sb.ToString() : string.Empty;
        }

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