using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SkinManager.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// URL pointing to the skins repository JSON file.
        /// </summary>
        public string SkinUrl { get; set; } = "https://raw.githubusercontent.com/Jellyfin-PG/Skin-Manager-Themes/refs/heads/main/skins.json";

        /// <summary>
        /// The display name of the currently selected skin (e.g. "Ultrachromic").
        /// "Default" means no skin is applied.
        /// </summary>
        public string Skin { get; set; } = "Default";

        /// <summary>
        /// The direct URL to the selected skin's CSS file.
        /// This is what gets injected into index.html by SkinInjector.
        /// </summary>
        public string SelectedCssUrl { get; set; } = string.Empty;

        /// <summary>
        /// Optional extra CSS the user wants applied on top of the selected skin.
        /// </summary>
        public string CustomCss { get; set; } = string.Empty;
    }
}
