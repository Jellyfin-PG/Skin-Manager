# Skin Manager

A Jellyfin plugin that lets you browse and apply community CSS themes from the server dashboard. Themes are injected at request time — your Jellyfin install is never modified on disk.

---

## Requirements

- Jellyfin 10.11+
- [File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation) plugin

Both plugins are available from the repository below.

---

## Installation

**1. Add the plugin repository**

In Jellyfin, go to **Dashboard > Plugins > Repositories** and add:

```
https://raw.githubusercontent.com/Jellyfin-PG/Repository/refs/heads/main/manifest.json
```

**2. Install the plugins**

Go to the **Catalogue** tab. Install **File Transformation** first, then **Skin Manager**. Restart Jellyfin after each install, or restart once after both.

**3. Open the theme store**

Navigate to **Dashboard > Skin Manager** in the sidebar. The default theme list loads automatically. Select a theme and click **Save & Apply**, then hard-refresh your browser (`Ctrl+Shift+R`).

---

## Theme Repository

Themes are loaded from a separate JSON file hosted at:

```
https://raw.githubusercontent.com/Jellyfin-PG/Skin-Manager/main/skins.json
```

This file is fetched live, so new themes appear without a plugin update. To submit a theme, open an issue using the **Theme Submission** template in the [theme repository](https://github.com/Jellyfin-PG/Skin-Manager-Themes).

---

## How it works

When you select and save a theme, Skin Manager stores the CSS URL in its configuration. On every request for `index.html`, the File Transformation plugin invokes a callback in Skin Manager which injects a `@import` tag pointing to the selected theme's CSS. The original file on disk is never touched.

Removing a theme clears the stored URL. The next page load returns to the default Jellyfin stylesheet.

---

## License

GPL-3.0
