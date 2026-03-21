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

## Theme Variables

As of version 1.2, themes can expose configurable variables that users can adjust without editing any CSS. When a theme supports variables, a **⚙** button appears next to the Select button on its card. Clicking it opens a popup where you can set values such as accent colors, font sizes, or toggle options. Changes take effect after clicking **Save & Apply**.

Theme authors declare variables in `skins.json` using a `vars` array. Each variable maps to a CSS custom property in the theme stylesheet — `ACCENT_COLOR` becomes `var(--accent-color)`, `FONT_SIZE` becomes `var(--font-size)`, and so on. See the [theme repository](https://github.com/Jellyfin-PG/Skin-Manager-Themes) for authoring documentation.

---

## Theme Repository

Themes are loaded from a separate JSON file hosted at:

```
https://raw.githubusercontent.com/Jellyfin-PG/Skin-Manager-Themes/refs/heads/main/skins.json
```

This file is fetched live, so new themes appear without a plugin update. To submit a theme, open an issue using the **Theme Submission** template in the [theme repository](https://github.com/Jellyfin-PG/Skin-Manager-Themes).

---

## How it works

When you select and save a theme, Skin Manager stores the CSS URL and any variable values in its configuration. On every request for `index.html`, the File Transformation plugin invokes a callback in Skin Manager which injects the following before `</body>`:

- A `<style>` block containing a `:root { }` declaration with the user's variable values as CSS custom properties, if any variables are configured
- A `<style>` block with an `@import` pointing to the selected theme's CSS

The `:root` block is injected in a separate tag before the `@import` so the imported stylesheet can reference the custom properties via `var()`. The original files on disk are never touched.

Removing a theme clears the stored URL and variables. The next page load returns to the default Jellyfin stylesheet.

---

## Changelog

**1.3.0** — Skin Versions, Cache and css addons. Introduces functional skin versions, with browser cache for faster loading and proper version checking to pull newer updates, also includes css addons, with variables to allow people to choose what they want with the theme.

**1.2.1** — Fixes. Fixes for injection, versioning of the plugin, and different configuration versions.

**1.2.0** — Theme variables. Themes can now declare configurable CSS custom properties. Users can set values from the Skin Manager settings page without editing CSS.

**1.0.0** — Initial release.

---

## License

GPL-3.0
