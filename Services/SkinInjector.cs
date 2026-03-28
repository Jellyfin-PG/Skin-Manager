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

        private static readonly Regex StripPreviousInjection = new(
            Regex.Escape(StartMarker) + @"[\s\S]*?" + Regex.Escape(EndMarker) + @"\n?",
            RegexOptions.Compiled);
            
        private static readonly Regex HeadCloseTag = new(@"(</head>)", RegexOptions.Compiled);

        private static readonly Regex JellyfinThemeLink = new(
            @"<link\b[^>]*\bhref=[""']themes/[^""']+/theme\.css[""'][^>]*>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
                        html = HeadCloseTag.Replace(html, m => block + m.Value);
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
                _cachedInjectionKey = key;
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
            ServerThemeCompiler.InvalidateCache();
        }

        private static string BuildInjection(PluginConfiguration config)
        {
            string result = string.Empty;

            bool skinActive = config.AllowUserThemes
                || (!string.IsNullOrWhiteSpace(config.SelectedCssUrl)
                    && config.SelectedCssUrl != "Default");

            // Deserialize ThemeVars once here so BuildCssVarBlock and BuildVarsJson
            // don't each pay the JSON parse cost separately.
            Dictionary<string, string> themeVarsDict = DeserializeVars(config.ThemeVars);

            if (skinActive)
            {
                result += BuildThemeGuardScript();
            }

            if (skinActive && !config.AllowUserThemes)
            {
                if (config.PreconnectUrls != null)
                {
                    foreach (var url in config.PreconnectUrls)
                    {
                        if (!string.IsNullOrWhiteSpace(url))
                            result += $"<link rel=\"preconnect\" href=\"{EscapeJs(url.Trim())}\" crossorigin=\"anonymous\">\n";
                    }
                }
                string varBlock = BuildCssVarBlock(themeVarsDict);
                string escapedVer = EscapeJs(config.SelectedVersion ?? string.Empty);
                string serverThemeUrl = $"/api/SkinManager/ServerTheme.css?v={escapedVer}";

                if (!string.IsNullOrEmpty(varBlock))
                    result += $"<style id=\"skinmanager-vars\">\n{varBlock}</style>\n";

                string scriptPayload = @"
    function isDashboard() { return _smDashRe.test(window.location.hash); }
    
    function injectDirectLink() {
        var existing = document.getElementById('skinmanager-theme');
        if (existing) existing.remove();
        var link = document.createElement('link');
        link.rel = 'stylesheet'; link.id = 'skinmanager-theme'; link.href = cssUrl;
        (document.body || document.head).appendChild(link);
    }

    function injectGlobalVars() {
        if (!rawVars || document.getElementById('skinmanager-vars')) return;
        var s = document.createElement('style');
        s.id = 'skinmanager-vars';
        s.textContent = rawVars;
        (document.body || document.head).appendChild(s);
    }
    
    function checkForUpdate() {
        if (!repoUrl || !themeName) return;
        fetch(repoUrl).then(function(r){return r.text();}).then(function(text) {
            var themes; try { themes = JSON.parse(text); } catch(e) { return; }
            var entry = themes.find(function(t) { return t.name === themeName; });
            if (!entry || !entry.version || entry.version === themeVer) return;
            console.log('[SkinManager] Update: ' + themeVer + ' -> ' + entry.version);
            ApiClient.getPluginConfiguration(pluginId).then(function(cfg) {
                if (window.caches) {
                    caches.keys().then(function(keys) {
                        keys.forEach(function(key) { if (key.startsWith('skinmanager-v')) caches.delete(key); });
                    });
                }
                cfg.SelectedVersion = entry.version;
                if (entry.cssUrl) cfg.SelectedCssUrl = entry.cssUrl;
                return ApiClient.updatePluginConfiguration(pluginId, cfg);
            }).then(function() { window.location.reload(); })
              .catch(function(e) { console.warn('[SkinManager] Update save failed: ', e); });
        }).catch(function() {});
    }

    checkForUpdate();
    
    if (isDashboard()) {
        var e = document.getElementById('skinmanager-theme'); if (e) e.remove();
        var v = document.getElementById('skinmanager-vars');  if (v) v.remove();
    } else {
        injectGlobalVars();
        injectDirectLink();
    }

    function handleNavigation() {
        if (isDashboard()) {
            var e = document.getElementById('skinmanager-theme'); if (e) e.remove();
            var v = document.getElementById('skinmanager-vars');  if (v) v.remove();
        } else {
            injectGlobalVars();
            if (!document.getElementById('skinmanager-theme')) { injectDirectLink(); }
        }
    }
    
    window.addEventListener('hashchange', handleNavigation);
    window.addEventListener('popstate', handleNavigation);
    window.addEventListener('sm:navigate', handleNavigation);
";

                result += $@"<script id=""skinmanager-loader"">
(function() {{
    var cssUrl    = ""{serverThemeUrl}"";
    var repoUrl   = ""{EscapeJs(config.SkinUrl)}"";
    var themeName = ""{EscapeJs(config.Skin)}"";
    var themeVer  = ""{escapedVer}"";
    var rawVars   = ""{EscapeJs(varBlock)}"";
    var pluginId  = ""e10fb9d4-c941-4c6e-8260-2641031c2618"";
{scriptPayload}}})();
</script>
";
            }

            if (!string.IsNullOrWhiteSpace(config.CustomCss))
                result += $"<style id=\"skinmanager-custom\">\n{config.CustomCss}\n</style>\n";

            if (config.AllowUserThemes)
            {
                string serverVersion = EscapeJs(config.SelectedVersion ?? "0");
                string serverCssUrl  = $"/api/SkinManager/ServerTheme.css?v={serverVersion}";
                string serverVars    = BuildVarsJson(themeVarsDict);
                result += BuildUserOverrideScript(serverCssUrl, serverVersion, serverVars);
            }

            return result;
        }

        /// <summary>
        /// Injects a tiny script + MutationObserver that acts as a Cascade Enforcer.
        /// It ensures that our custom skins always reside at the absolute bottom
        /// of the &lt;body&gt; so they naturally override Jellyfin's base theme.
        ///
        /// This script also owns the single canonical monkey-patch of
        /// history.pushState / replaceState, dispatching a custom 'sm:navigate'
        /// event so other SkinManager scripts can listen without chaining patches.
        /// </summary>
        private static string BuildThemeGuardScript()
        {
            return @"<script id=""skinmanager-theme-guard"">
(function() {
    // Shared dashboard-route regex — defined once here, referenced via the
    // module-level variable _smDashRe so other SkinManager IIFEs can reuse it
    // without compiling their own copy.
    window._smDashRe = /^#\/(dashboard|configurationpage|configuration|metadata|mypreferences|wizard)(\/|[?]|$)/;

    var OUR_IDS = [
        'skinmanager-vars', 
        'skinmanager-theme', 
        'skinmanager-user-vars', 
        'skinmanager-user-theme', 
        'skinmanager-custom'
    ];

    function isDashboard() { 
        return _smDashRe.test(window.location.hash); 
    }

    // -- Fix #7: cancel any pending rAF before the disconnect/reconnect cycle
    // inside ensureSkinLast, preventing a double-run when the function is
    // called synchronously from a navigation event while a rAF is in flight.
    function ensureSkinLast() {
        if (_guardDebounce) {
            cancelAnimationFrame(_guardDebounce);
            _guardDebounce = null;
            _guardDirty = false;
        }

        if (isDashboard() || !document.body) return;

        var ours = [];
        for (var i = 0; i < OUR_IDS.length; i++) {
            var el = document.getElementById(OUR_IDS[i]);
            if (el) ours.push(el);
        }
        if (ours.length === 0) return;

        var bodyChildren = document.body.children;
        var n = bodyChildren.length;
        var alreadyCorrect = (n >= ours.length);
        if (alreadyCorrect) {
            for (var j = 0; j < ours.length; j++) {
                if (bodyChildren[n - ours.length + j] !== ours[j]) {
                    alreadyCorrect = false;
                    break;
                }
            }
        }
        if (alreadyCorrect) return;

        // Disconnect observer while we reorder to avoid re-entrancy
        guard.disconnect();
        for (var k = 0; k < ours.length; k++) {
            document.body.appendChild(ours[k]);
        }
        guard.observe(document.documentElement, { childList: true, subtree: true });
        
        console.log('[SkinManager] Custom skin priority reasserted at bottom of body.');
    }

    function handleNavigationGuard() { ensureSkinLast(); }
    
    window.addEventListener('hashchange', handleNavigationGuard);
    window.addEventListener('popstate',   handleNavigationGuard);

    // -- Fix #6: single canonical pushState/replaceState patch.
    // Fires a synthetic 'sm:navigate' event so skinmanager-loader and
    // skinmanager-user-override-loader can listen without stacking their
    // own wrappers on top of this one.
    var _origPush = history.pushState;
    history.pushState = function() {
        _origPush.apply(this, arguments);
        window.dispatchEvent(new Event('sm:navigate'));
        handleNavigationGuard();
    };

    var _origReplace = history.replaceState;
    history.replaceState = function() {
        _origReplace.apply(this, arguments);
        window.dispatchEvent(new Event('sm:navigate'));
        handleNavigationGuard();
    };

    var _guardDebounce = null;
    var _guardDirty = false;
    var guard = new MutationObserver(function(mutations) {
        if (isDashboard() || !document.body) return;
        
        var needsReorder = false;
        for (var i = 0; i < mutations.length; i++) {
            var added = mutations[i].addedNodes;
            for (var j = 0; j < added.length; j++) {
                var node = added[j];
                if ((node.nodeName === 'LINK' || node.nodeName === 'STYLE') && OUR_IDS.indexOf(node.id || '') === -1) {
                    needsReorder = true;
                    break;
                }
            }
            if (needsReorder) break; 
        }
        
        if (needsReorder) {
            console.log('[SkinManager] Style injection detected. Reasserting priority...');
            if (_guardDebounce) {
                _guardDirty = true; // another mutation arrived while RAF is pending — re-run after
                return;
            }
            _guardDebounce = requestAnimationFrame(function() {
                _guardDebounce = null;
                ensureSkinLast();
                if (_guardDirty) {
                    _guardDirty = false;
                    ensureSkinLast(); // run once more for any mutations that fired during the first RAF
                }
            });
        }
    });

    guard.observe(document.documentElement, { childList: true, subtree: true });

    if (document.body) {
        ensureSkinLast();
    } else {
        document.addEventListener('DOMContentLoaded', ensureSkinLast);
    }
})();
</script>
";
        }

        /// <summary>Deserializes the ThemeVars JSON once so callers share the parse cost.</summary>
        private static Dictionary<string, string> DeserializeVars(string themeVarsJson)
        {
            if (string.IsNullOrWhiteSpace(themeVarsJson) || themeVarsJson == "{}")
                return null;
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(themeVarsJson, JsonOpts);
            }
            catch
            {
                return null;
            }
        }

        private static string BuildCssVarBlock(Dictionary<string, string> vars)
        {
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

                // Only auto-quote if the variable name strongly suggests it's a text/content string.
                // Multi-part CSS values (like '10px 20px') should NOT be quoted.
                bool isStringHint = kebabKey.Contains("text") || kebabKey.Contains("content") || kebabKey.Contains("brand") || kebabKey.Contains("footer") || kebabKey.Contains("name");
                bool isCssFunction = value.Contains("(");

                if (isStringHint && value.Contains(" ") && !isCssFunction && !value.StartsWith("\"") && !value.StartsWith("'"))
                    value = "\"" + value + "\"";

                sb.Append("    " + propName + ": " + value + " !important;\n");
                hasAny = true;
            }
            sb.Append("}\n");
            return hasAny ? sb.ToString() : string.Empty;
        }

        private static string BuildVarsJson(Dictionary<string, string> vars)
        {
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
        ///
        /// Navigation is driven by the 'sm:navigate' event dispatched by the
        /// theme-guard script's canonical pushState/replaceState patch — this
        /// script does NOT install its own history wrappers.
        /// </summary>
        private static string BuildUserOverrideScript(string serverCssUrl, string serverVersion, string serverVarsJson)
        {
            // Values are pre-escaped by the caller (EscapeJs applied to serverCssUrl/serverVersion;
            // serverVarsJson is JSON-safe by construction), so we interpolate directly rather than
            // using %%PLACEHOLDER%% replacements which are fragile if a value contains the marker text.
            return $@"<script id=""skinmanager-user-override-loader"">
(function() {{
    var MENU_ITEM_ID = 'skinmanager-mytheme-menuitem';
    var CACHE_CSS    = 'skinmanager-user-v';
    
    var FALLBACK_CSS   = ""{serverCssUrl}"";
    var FALLBACK_VER   = ""{serverVersion}"";
    var FALLBACK_VARS  = {serverVarsJson};

    var _cachedLsKey = null;
    function getLsKey() {{
        if (_cachedLsKey) return _cachedLsKey;
        try {{
            var creds = JSON.parse(localStorage.getItem('jellyfin_credentials') || '{{}}');
            var srv   = (creds.Servers || [])[0] || {{}};
            var uid   = srv.UserId      || '';
            var token = srv.AccessToken || '';
            _cachedLsKey = 'skinmanager-user-override' + (uid && token ? '-' + uid : '');
            return _cachedLsKey;
        }} catch(e) {{ return 'skinmanager-user-override'; }}
    }}

    function getOverride() {{
        try {{ return JSON.parse(localStorage.getItem(getLsKey()) || 'null'); }} catch(e) {{ return null; }}
    }}

    var _kebabP1 = /^-+/;
    var _kebabP2 = /([a-z])([A-Z])/g;
    var _kebabP3 = /_/g;
    function toKebabCase(key) {{
        key = (key || '').trim().replace(_kebabP1, '');
        key = key.replace(_kebabP2, '$1-$2');
        return key.replace(_kebabP3, '-').toLowerCase();
    }}

    function substituteVars(code, vars) {{
        return code.replace(/\{{{{([^{{}}]+)\}}}}/g, function(match, key) {{
            return vars[key] !== undefined ? vars[key] : match;
        }});
    }}

    // -- Fix #8: version-stamped URLs are already cache-busted by the ?v= param,
    // so a background revalidation fetch on every cache hit is redundant.
    // Return the cached response immediately; only hit the network on a miss.
    function cachedFetch(url, cacheName, version) {{
        var pUrl = '/api/SkinManager/Resource?url=' + encodeURIComponent(url) + '&v=' + encodeURIComponent(version || '');
        if (!window.caches) return fetch(pUrl).then(function(r) {{ return r.text(); }});
        return caches.open(cacheName).then(function(cache) {{
            return cache.match(pUrl).then(function(cached) {{
                if (cached) return cached.text();
                return fetch(pUrl).then(function(r) {{
                    cache.put(pUrl, r.clone());
                    return r.text();
                }});
            }});
        }});
    }}

    var _injectEpoch = 0;

    function injectUserCss(override) {{
        var el;
        el = document.getElementById('skinmanager-theme'); if (el) el.remove();
        el = document.getElementById('skinmanager-vars');  if (el) el.remove();
        var cssUrl  = override.cssUrl  || '';
        var version = override.version || '0';
        var vars    = override.vars    || {{}};
        var pConns  = override.preconnect || [];
        if (!cssUrl) return;

        pConns.forEach(function(u) {{
            if (!u || document.head.querySelector('link[data-sm-preconnect=""' + u + '""]')) return;
            var l = document.createElement('link');
            l.rel = 'preconnect'; l.href = u; l.crossOrigin = 'anonymous';
            l.setAttribute('data-sm-preconnect', u);
            document.head.appendChild(l);
        }});
        
        var appendTarget = function(el) {{
            (document.body || document.head).appendChild(el);
        }};
        
        var varKeys = Object.keys(vars);
        if (varKeys.length > 0) {{
            var cssVars = ':root, body {{\n';
            varKeys.forEach(function(k) {{
                if (vars[k]) cssVars += '  --' + toKebabCase(k) + ': ' + vars[k] + ' !important;\n';
            }});
            cssVars += '}}';
            var styleEl = document.getElementById('skinmanager-user-vars') || document.createElement('style');
            styleEl.id = 'skinmanager-user-vars';
            styleEl.textContent = cssVars;
            appendTarget(styleEl); 
        }}
        var epoch = _injectEpoch;
        cachedFetch(cssUrl, CACHE_CSS + version, version).then(function(code) {{
            if (_injectEpoch !== epoch) return;
            code = substituteVars(code, vars);
            var existing = document.getElementById('skinmanager-user-theme');
            if (existing) {{
                if (existing.href && existing.href.indexOf('blob:') === 0) URL.revokeObjectURL(existing.href);
                existing.remove();
            }}
            try {{
                var blob = new Blob([code], {{ type: 'text/css' }});
                var link = document.createElement('link');
                link.rel = 'stylesheet'; link.id = 'skinmanager-user-theme';
                link.href = URL.createObjectURL(blob);
                appendTarget(link); 
            }} catch(e) {{
                var s = document.createElement('style');
                s.id = 'skinmanager-user-theme'; s.textContent = code;
                appendTarget(s); 
            }}
        }}).catch(function() {{}});
    }}

    function ensureMenuItem() {{
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
    }}

    var _menuDebounce;
    var observer = new MutationObserver(function() {{
        if (document.getElementById(MENU_ITEM_ID)) return;
        if (_menuDebounce) return;
        _menuDebounce = requestAnimationFrame(function() {{
            ensureMenuItem();
            _menuDebounce = null;
        }});
    }});
    observer.observe(document.body || document.documentElement, {{ childList: true, subtree: true }});

    function isDashboard() {{ return _smDashRe.test(window.location.hash); }}
    function isUnauthPage() {{ var h = window.location.hash; return !h || h === '#/' || /^#\/(login|selectserver|selectuser|addserver|signout)/.test(h); }}

    function stripUserOverride() {{
        ++_injectEpoch;
        var e;
        e = document.getElementById('skinmanager-user-theme'); 
        if (e) {{
            if (e.href && e.href.indexOf('blob:') === 0) URL.revokeObjectURL(e.href);
            e.remove();
        }}
        e = document.getElementById('skinmanager-user-vars');  if (e) e.remove();
    }}

    var _pollKey = null;

    function poll() {{
        var dash = isDashboard();
        var unauth = isUnauthPage();
        
        var key = (dash || unauth) ? null : getLsKey();
        if (key === _pollKey) return;
        _pollKey = key;
        stripUserOverride();
        if (key) {{
            var o = getOverride();
            if (o && o.cssUrl !== undefined) {{ 
                injectUserCss(o); 
                return; 
            }} else {{
                // Fallback to server theme if user themes are allowed but user hasn't picked one
                injectUserCss({{ cssUrl: FALLBACK_CSS, version: FALLBACK_VER, vars: FALLBACK_VARS }});
                return;
            }}
        }}
        var e;
        e = document.getElementById('skinmanager-theme'); if (e) e.remove();
        e = document.getElementById('skinmanager-vars');  if (e) e.remove();
    }}

    function handleNavigationUser() {{
        if (isUnauthPage()) _cachedLsKey = null; 
        poll();
    }}

    // -- Fix #6: listen for the canonical navigation event fired by the
    // theme-guard script instead of wrapping pushState/replaceState again.
    window.addEventListener('hashchange',  handleNavigationUser);
    window.addEventListener('popstate',    handleNavigationUser);
    window.addEventListener('sm:navigate', handleNavigationUser);

    window.addEventListener('storage', function(e) {{
        if (e.key && e.key.indexOf('skinmanager') !== -1) {{
            _pollKey = null; 
            poll();
        }}
    }});

    poll();
}})();
</script>
";
        }
    }
}