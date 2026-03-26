using System.IO;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.SkinManager.Api
{
    /// <summary>
    /// Provides lightweight API endpoints so the user theme picker page can retrieve
    /// the manifest URL and AllowUserThemes flag without needing admin credentials.
    /// Theme selections themselves are stored in browser localStorage — no server-side
    /// storage is used.
    /// </summary>
    [ApiController]
    [Route("api/SkinManager")]
    public class UserThemeController : ControllerBase
    {
        // Cached once at first request — the embedded HTML never changes between restarts.
        private static byte[] _pageBytes;
        private static readonly object _pageLock = new();

        /// <summary>
        /// Returns the manifest URL configured by the admin and whether
        /// user-level theme overrides are currently permitted.
        /// Intentionally unauthenticated so the injected JS can call it
        /// before the user has navigated to a page that requires login.
        /// </summary>
        [HttpGet("ManifestInfo")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<ManifestInfoResponse> GetManifestInfo()
        {
            var config = Plugin.Instance?.Configuration;
            return Ok(new ManifestInfoResponse
            {
                ManifestUrl     = config?.SkinUrl ?? string.Empty,
                AllowUserThemes = config?.AllowUserThemes ?? false,
                AdminSkinName   = config?.Skin ?? string.Empty
            });
        }

        /// <summary>
        /// Serves the user theme picker page as a standalone HTML document.
        /// Open to any visitor — the page HTML is not sensitive; all data
        /// it fetches internally still requires authentication via authHeaders().
        /// </summary>
        [HttpGet("UserThemePage")]
        [AllowAnonymous]
        [Produces("text/html")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult GetUserThemePage()
        {
            var bytes = GetPageBytes();
            if (bytes is null)
                return NotFound();

            return File(bytes, "text/html");
        }

        private static byte[] GetPageBytes()
        {
            if (_pageBytes is not null) return _pageBytes;

            lock (_pageLock)
            {
                if (_pageBytes is not null) return _pageBytes;

                var asm          = typeof(UserThemeController).Assembly;
                var resourceName = $"{asm.GetName().Name}.Configuration.userThemePage.html";

                using var stream = asm.GetManifestResourceStream(resourceName);
                if (stream is null) return null;

                var buf = new byte[stream.Length];
                stream.ReadExactly(buf, 0, buf.Length);
                _pageBytes = buf;
                return _pageBytes;
            }
        }
    }

    /// <summary>Response DTO for <see cref="UserThemeController.GetManifestInfo"/>.</summary>
    public class ManifestInfoResponse
    {
        /// <summary>The JSON manifest URL saved by the admin.</summary>
        [Required]
        public string ManifestUrl { get; set; } = string.Empty;

        /// <summary>True when the admin has enabled per-user theme overrides.</summary>
        [Required]
        public bool AllowUserThemes { get; set; }

        /// <summary>The admin-selected skin name (e.g. for display in the user page).</summary>
        [Required]
        public string AdminSkinName { get; set; } = string.Empty;
    }
}
