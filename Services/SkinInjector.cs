using System;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.SkinManager.Configuration;

namespace Jellyfin.Plugin.SkinManager.Services
{
    public static class SkinInjector
    {
        private const string StartMarker = "<!-- SkinManager-Start -->";
        private const string EndMarker = "<!-- SkinManager-End -->";

        public static string InjectTheme(PatchRequestPayload payload)
        {
            try
            {
                string html = payload?.Contents;
                if (string.IsNullOrEmpty(html))
                    return html ?? string.Empty;

                html = Regex.Replace(
                    html,
                    $@"{Regex.Escape(StartMarker)}[\s\S]*?{Regex.Escape(EndMarker)}\n?",
                    string.Empty);

                PluginConfiguration config = Plugin.Instance?.Configuration;
                if (config != null)
                {
                    string injection = BuildInjection(config);
                    if (!string.IsNullOrEmpty(injection))
                    {
                        string block = $"\n{StartMarker}\n{injection}{EndMarker}\n";
                        html = Regex.Replace(html, @"(</body>)", block + "$1");
                    }
                }

                return html;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SkinManager] InjectTheme error: {ex.Message}");
                return payload?.Contents ?? string.Empty;
            }
        }

        private static string BuildInjection(PluginConfiguration config)
        {
            string result = string.Empty;

            if (!string.IsNullOrWhiteSpace(config.SelectedCssUrl)
                && config.SelectedCssUrl != "Default")
            {
                result += $"<style id=\"skinmanager-theme\">\n@import url(\"{config.SelectedCssUrl}\");\n</style>\n";
            }

            if (!string.IsNullOrWhiteSpace(config.CustomCss))
            {
                result += $"<style id=\"skinmanager-custom\">\n{config.CustomCss}\n</style>\n";
            }

            return result;
        }
    }
}