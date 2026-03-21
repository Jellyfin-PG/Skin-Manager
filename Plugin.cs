using System;
using System.Collections.Generic;
using Jellyfin.Plugin.SkinManager.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SkinManager
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        /// <summary>
        /// The current config schema version. Increment this whenever a new
        /// field is added to PluginConfiguration, then add a migration case
        /// in the switch block below to apply safe defaults for that version.
        /// </summary>
        private const int CurrentConfigVersion = 4;

        public override string Name => "SkinManager";

        public override Guid Id => Guid.Parse("e10fb9d4-c941-4c6e-8260-2641031c2618");

        public override string Description => "Theme manager that integrates with File Transformation.";

        public static Plugin Instance { get; private set; }

        public IServiceProvider ServiceProvider { get; private set; }

        public Plugin(
            IApplicationPaths applicationPaths,
            IXmlSerializer xmlSerializer,
            ILogger<Plugin> logger,
            IServiceProvider serviceProvider)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            ServiceProvider = serviceProvider;
            MigrateConfig();
        }

        /// <summary>
        /// Applies incremental migrations for any config version behind the current one.
        /// Each case sets safe defaults for fields introduced in that version.
        /// Never removes or renames existing fields — only adds missing ones.
        /// </summary>
        private void MigrateConfig()
        {
            bool dirty = false;

            for (int v = Configuration.ConfigVersion + 1; v <= CurrentConfigVersion; v++)
            {
                switch (v)
                {
                    case 1:
                        break;

                    case 2:
                        if (string.IsNullOrWhiteSpace(Configuration.ThemeVars))
                            Configuration.ThemeVars = "{}";
                        break;

                    case 3:
                        if (string.IsNullOrWhiteSpace(Configuration.SelectedVersion))
                            Configuration.SelectedVersion = string.Empty;
                        break;

                    case 4:
                        break;
                }

                Configuration.ConfigVersion = v;
                dirty = true;
            }

            if (dirty)
                SaveConfiguration();
        }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = Name,
                    DisplayName = "Skin Manager",
                    EnableInMainMenu = true,
                    EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
                }
            };
        }
    }
}