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

                result +=
                    "<script id=\"skinmanager-loader\">\n" +
                    "(function() {\n" +
                    "    var cssUrl  = \"" + escapedUrl + "\";\n" +
                    "    var varBlock = \"" + EscapeJs(varBlock) + "\";\n" +
                    "    var vars    = " + varJson + ";\n" +
                    "\n" +
                    "    function substituteVars(code, v) {\n" +
                    "        Object.keys(v).forEach(function(key) {\n" +
                    "            code = code.split('{{' + key + '}}').join(v[key]);\n" +
                    "        });\n" +
                    "        return code;\n" +
                    "    }\n" +
                    "\n" +
                    "    function injectCss(code) {\n" +
                    "        var existing = document.getElementById('skinmanager-theme');\n" +
                    "        if (existing) existing.remove();\n" +
                    "        var s = document.createElement('style');\n" +
                    "        s.id = 'skinmanager-theme';\n" +
                    "        s.textContent = substituteVars(code, vars) + varBlock;\n" +
                    "        document.head.appendChild(s);\n" +
                    "    }\n" +
                    "\n" +
                    "    fetch(cssUrl)\n" +
                    "        .then(function(r) { return r.text(); })\n" +
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