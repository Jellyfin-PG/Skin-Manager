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
        private const string EndMarker   = "<!-- SkinManager-End -->";

        private static readonly Regex StripPreviousInjection = new(
            Regex.Escape(StartMarker) + @"[\s\S]*?" + Regex.Escape(EndMarker) + @"\n?",
            RegexOptions.Compiled);
        private static readonly Regex BodyCloseTag = new(@"(</body>)", RegexOptions.Compiled);

        private static string _cachedInjectionKey;
        private static string _cachedInjectionValue = string.Empty;
        private static readonly object _injectionLock = new();

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

                html = StripPreviousInjection.Replace(html, string.Empty);

                PluginConfiguration config = Plugin.Instance?.Configuration;
                if (config != null)
                {
                    string injection = GetInjection(config);
                    if (!string.IsNullOrEmpty(injection))
                    {
                        string block = "\n" + StartMarker + "\n" + injection + EndMarker + "\n";
                        html = BodyCloseTag.Replace(html, m => block + m.Value);
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

        /// <summary>
        /// Returns the cached injection block, rebuilding it only when the
        /// config fingerprint changes (i.e. after a save).
        /// </summary>
        private static string GetInjection(PluginConfiguration config)
        {
            string key = $"{config.AllowUserThemes}|{config.SelectedCssUrl}|{config.SelectedVersion}"
                       + $"|{config.ThemeVars}|{config.CustomCss}|{config.HasConditionalImports}"
                       + $"|{config.Skin}|{config.SkinUrl}";

            if (_cachedInjectionKey == key)
                return _cachedInjectionValue;

            lock (_injectionLock)
            {
                if (_cachedInjectionKey == key)
                    return _cachedInjectionValue;

                _cachedInjectionValue = BuildInjection(config);
                _cachedInjectionKey   = key;
                return _cachedInjectionValue;
            }
        }

        /// <summary>
        /// Forces the next request to rebuild the injection block.
        /// Called by <see cref="Plugin.SaveConfiguration"/> after the admin saves settings.
        /// </summary>
        public static void InvalidateInjectionCache()
        {
            lock (_injectionLock) { _cachedInjectionKey = null; }
        }

        private static string BuildInjection(PluginConfiguration config)
        {
            string result = string.Empty;

            if (!config.AllowUserThemes
                && !string.IsNullOrWhiteSpace(config.SelectedCssUrl)
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
                    result += $"<style id=\"skinmanager-vars\">\n{varBlock}</style>\n";

                const string sharedFunctions = @"
    function substituteVars(code, v) {
        Object.keys(v).forEach(function(key) {
            code = code.split('{{' + key + '}}').join(v[key]);
        });
        return code;
    }

    function resolveConditionalImports(code) {
        var pattern = /\/\*\s*@sm-import-if\s+(\S+)\s+(\S+)\s*\*\//g;
        var promises = [];
        var match;
        while ((match = pattern.exec(code)) !== null) {
            (function(key, url) {
                var enabled = vars[key];
                if (enabled === 'true' || enabled === true) {
                    promises.push(cachedFetch(url, CACHE_CSS));
                } else {
                    promises.push(Promise.resolve(''));
                }
            })(match[1], match[2]);
        }
        var stripped = code.replace(/\/\*\s*@sm-import-if\s+\S+\s+\S+\s*\*\//g, '');
        return Promise.all(promises).then(function(addons) {
            return stripped + '\n' + addons.join('\n');
        });
    }

    function cachedFetch(url, cacheName) {
        if (!window.caches) return fetch(url).then(function(r) { return r.text(); });
        return caches.open(cacheName).then(function(cache) {
            return cache.match(url).then(function(cached) {
                var networkFetch = fetch(url).then(function(r) {
                    cache.put(url, r.clone());
                    return r.text();
                }).catch(function() { return null; });
                if (cached) { networkFetch.catch(function() {}); return cached.text(); }
                return networkFetch.then(function(text) { return text || ''; });
            });
        });
    }

    function injectBlob(code) {
        var existing = document.getElementById('skinmanager-theme');
        if (existing) existing.remove();
        var css = substituteVars(code, vars);
        try {
            var blob = new Blob([css], { type: 'text/css' });
            var link = document.createElement('link');
            link.rel = 'stylesheet'; link.id = 'skinmanager-theme';
            link.href = URL.createObjectURL(blob);
            document.head.appendChild(link);
        } catch(e) {
            var s = document.createElement('style');
            s.id = 'skinmanager-theme';
            s.textContent = substituteVars(code, vars);
            document.head.appendChild(s);
        }
    }

    function checkForUpdate() {
        if (!repoUrl || !themeName) return;
        fetch(repoUrl).then(function(r){return r.text();}).then(function(text) {
            var themes; try { themes = JSON.parse(text); } catch(e) { return; }
            var entry = themes.find(function(t) { return t.name === themeName; });
            if (!entry || !entry.version || entry.version === themeVer) return;
            console.log('[SkinManager] Update: ' + themeVer + ' -> ' + entry.version);
            if (window.caches) caches.delete(CACHE_CSS);
            ApiClient.getPluginConfiguration(pluginId).then(function(cfg) {
                cfg.SelectedVersion = entry.version;
                if (entry.cssUrl) cfg.SelectedCssUrl = entry.cssUrl;
                return ApiClient.updatePluginConfiguration(pluginId, cfg);
            }).then(function() { window.location.reload(); })
              .catch(function(e) { console.warn('[SkinManager] Update save failed: ', e); });
        }).catch(function() {});
    }

    function evictOldCaches() {
        if (!window.caches) return;
        caches.keys().then(function(keys) {
            keys.forEach(function(key) {
                if (key.startsWith('skinmanager-v') && key !== CACHE_CSS) caches.delete(key);
            });
        });
    }
";

                string executionTail;
                if (isRawGithub || hasVars || hasAddons)
                {
                    executionTail = @"
    function isDashboard() { var h = window.location.hash; return h === '#/dashboard' || h.indexOf('#/dashboard?') === 0; }
    evictOldCaches(); checkForUpdate();
    if (!isDashboard()) {
        cachedFetch(cssUrl, CACHE_CSS)
            .then(function(code) { return resolveConditionalImports(code); })
            .then(function(code) { injectBlob(code); })
            .catch(function(e) { console.warn('[SkinManager] Load failed: ' + cssUrl, e); });
    }
    window.addEventListener('hashchange', function() {
        if (isDashboard()) {
            var e = document.getElementById('skinmanager-theme'); if (e) e.remove();
            var v = document.getElementById('skinmanager-vars');  if (v) v.remove();
        } else {
            cachedFetch(cssUrl, CACHE_CSS)
                .then(function(code) { return resolveConditionalImports(code); })
                .then(function(code) { injectBlob(code); })
                .catch(function() {});
        }
    });
";
                }
                else
                {
                    executionTail = @"
    function isDashboard() { var h = window.location.hash; return h === '#/dashboard' || h.indexOf('#/dashboard?') === 0; }
    function injectDirectLink() {
        var existing = document.getElementById('skinmanager-theme');
        if (existing) existing.remove();
        var link = document.createElement('link');
        link.rel = 'stylesheet'; link.id = 'skinmanager-theme'; link.href = cssUrl;
        document.head.appendChild(link);
    }
    evictOldCaches(); checkForUpdate();
    if (!isDashboard()) injectDirectLink();
    window.addEventListener('hashchange', function() {
        if (isDashboard()) {
            var e = document.getElementById('skinmanager-theme'); if (e) e.remove();
        } else { injectDirectLink(); }
    });
";
                }

                result += $@"<script id=""skinmanager-loader"">
(function() {{
    var cssUrl    = ""{escapedUrl}"";
    var repoUrl   = ""{EscapeJs(config.SkinUrl)}"";
    var themeName = ""{EscapeJs(config.Skin)}"";
    var themeVer  = ""{escapedVer}"";
    var vars      = {varJson};
    var pluginId  = ""e10fb9d4-c941-4c6e-8260-2641031c2618"";
    var CACHE_CSS  = 'skinmanager-v' + (themeVer || '0');
{sharedFunctions}{executionTail}}})();
</script>
";
            }

            if (!string.IsNullOrWhiteSpace(config.CustomCss))
                result += $"<style id=\"skinmanager-custom\">\n{config.CustomCss}\n</style>\n";

            if (config.AllowUserThemes)
                result += BuildUserOverrideScript();

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

            var sb = new StringBuilder(":root, body {\n");
            bool hasAny = false;
            foreach (var kv in vars)
            {
                if (string.IsNullOrWhiteSpace(kv.Value)) continue;

                string kebabKey = ToKebabCase(kv.Key);
                string propName = "--" + kebabKey;

                string value = kv.Value.Trim();
                bool isCssFunction = value.Contains("(");
                if (value.Contains(" ") && !isCssFunction && !value.StartsWith("\"") && !value.StartsWith("'"))
                    value = "\"" + value + "\"";

                sb.Append("    " + propName + ": " + value + " !important;\n");
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

        private static string ToKebabCase(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return string.Empty;

            key = key.Trim().TrimStart('-');

            key = Regex.Replace(key, "([a-z])([A-Z])", "$1-$2");

            return key.Replace('_', '-').ToLowerInvariant();
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

        /// <summary>
        /// Builds the script block that handles per-user theme overrides.
        /// It runs on every page load and:
        ///  1. Reads the user's theme choice from localStorage.
        ///  2. If AllowUserThemes is on and a choice exists, replaces the
        ///     server-injected theme with the user's selection.
        ///  3. Injects (and keeps alive across SPA navigation via MutationObserver)
        ///     a "My Theme" link inside Jellyfin's user-settings drawer.
        /// </summary>
        private static string BuildUserOverrideScript()
        {
            return @"<script id=""skinmanager-user-override-loader"">
(function() {
    var MENU_ITEM_ID = 'skinmanager-mytheme-menuitem';
    var CACHE_CSS    = 'skinmanager-user-v';

    function getLsKey() {
        try {
            var creds = JSON.parse(localStorage.getItem('jellyfin_credentials') || '{}');
            var srv   = (creds.Servers || [])[0] || {};
            var uid   = srv.UserId      || '';
            var token = srv.AccessToken || '';
            return 'skinmanager-user-override' + (uid && token ? '-' + uid : '');
        } catch(e) { return 'skinmanager-user-override'; }
    }

    function getOverride() {
        try { return JSON.parse(localStorage.getItem(getLsKey()) || 'null'); } catch(e) { return null; }
    }

    function toKebabCase(key) {
        key = (key || '').trim().replace(/^-+/, '');
        key = key.replace(/([a-z])([A-Z])/g, '$1-$2');
        return key.replace(/_/g, '-').toLowerCase();
    }

    function substituteVars(code, vars) {
        Object.keys(vars).forEach(function(key) {
            code = code.split('{{' + key + '}}').join(vars[key]);
        });
        return code;
    }

    function cachedFetch(url, cacheName) {
        if (!window.caches) return fetch(url).then(function(r) { return r.text(); });
        return caches.open(cacheName).then(function(cache) {
            return cache.match(url).then(function(cached) {
                var net = fetch(url).then(function(r) {
                    cache.put(url, r.clone()); return r.text();
                }).catch(function() { return null; });
                if (cached) { net.catch(function(){}); return cached.text(); }
                return net.then(function(t) { return t || ''; });
            });
        });
    }

    var _injectEpoch = 0;

    function injectUserCss(override) {
        var el;
        el = document.getElementById('skinmanager-theme'); if (el) el.remove();
        el = document.getElementById('skinmanager-vars');  if (el) el.remove();
        var cssUrl  = override.cssUrl  || '';
        var version = override.version || '0';
        var vars    = override.vars    || {};
        if (!cssUrl) return;
        var varKeys = Object.keys(vars);
        if (varKeys.length > 0) {
            var cssVars = ':root, body {\n';
            varKeys.forEach(function(k) {
                if (vars[k]) cssVars += '  --' + toKebabCase(k) + ': ' + vars[k] + ' !important;\n';
            });
            cssVars += '}';
            var styleEl = document.getElementById('skinmanager-user-vars') || document.createElement('style');
            styleEl.id = 'skinmanager-user-vars';
            styleEl.textContent = cssVars;
            document.head.appendChild(styleEl);
        }
        var epoch = _injectEpoch;
        cachedFetch(cssUrl, CACHE_CSS + version).then(function(code) {
            if (_injectEpoch !== epoch) return;
            code = substituteVars(code, vars);
            var existing = document.getElementById('skinmanager-user-theme');
            if (existing) existing.remove();
            try {
                var blob = new Blob([code], { type: 'text/css' });
                var link = document.createElement('link');
                link.rel = 'stylesheet'; link.id = 'skinmanager-user-theme';
                link.href = URL.createObjectURL(blob);
                document.head.appendChild(link);
            } catch(e) {
                var s = document.createElement('style');
                s.id = 'skinmanager-user-theme'; s.textContent = code;
                document.head.appendChild(s);
            }
        }).catch(function() {});
    }

    function ensureMenuItem() {
        if (document.getElementById(MENU_ITEM_ID)) return;
        var anchor = document.querySelector(
            '.lnkHomePreferences, .lnkDisplayPreferences, .lnkSubtitlePreferences, .lnkControlsPreferences'
        );
        if (!anchor) return;
        var container = anchor.closest('.verticalSection');
        if (!container) return;
        var a = document.createElement('a');
        a.id = MENU_ITEM_ID;
        a.className = 'emby-button lnkSkinManagerTheme listItem-border';
        a.href = '/api/SkinManager/UserThemePage';
        a.style.cssText = 'display:block;margin:0;padding:0;';
        var listItem = document.createElement('div');
        listItem.className = 'listItem';
        var icon = document.createElement('span');
        icon.className = 'material-icons listItemIcon listItemIcon-transparent palette';
        icon.setAttribute('aria-hidden', 'true');
        var body = document.createElement('div');
        body.className = 'listItemBody';
        var label = document.createElement('div');
        label.className = 'listItemBodyText';
        label.textContent = 'My Theme';
        body.appendChild(label);
        listItem.appendChild(icon);
        listItem.appendChild(body);
        a.appendChild(listItem);
        container.appendChild(a);
    }

    var observer = new MutationObserver(function() { ensureMenuItem(); });
    observer.observe(document.body || document.documentElement, { childList: true, subtree: true });

    function isDashboard() { var h = window.location.hash; return h === '#/dashboard' || h.indexOf('#/dashboard?') === 0; }
    function isUnauthPage() { var h = window.location.hash; return !h || h === '#/' || /^#\/(login|selectserver|selectuser|addserver|signout)/.test(h); }

    function stripUserOverride() {
        ++_injectEpoch;
        var e;
        e = document.getElementById('skinmanager-user-theme'); if (e) e.remove();
        e = document.getElementById('skinmanager-user-vars');  if (e) e.remove();
    }

    var _pollKey = '--';

    function poll() {
        var key = (isDashboard() || isUnauthPage()) ? null : getLsKey();
        if (key === _pollKey) return;
        _pollKey = key;
        stripUserOverride();
        if (key) {
            var o = getOverride();
            if (o && o.cssUrl !== undefined) { injectUserCss(o); return; }
        }
        var e;
        e = document.getElementById('skinmanager-theme'); if (e) e.remove();
        e = document.getElementById('skinmanager-vars');  if (e) e.remove();
    }

    setInterval(poll, 500);
    window.addEventListener('hashchange', function() { _pollKey = '--'; poll(); });
})();
</script>
";
        }
    }
}