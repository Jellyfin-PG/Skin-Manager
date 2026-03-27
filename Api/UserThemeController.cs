using System.IO;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.SkinManager.Services;

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
        private static byte[] _pageBytes;
        private static readonly object _pageLock = new();
        private readonly ILogger<UserThemeController> _logger;

        public UserThemeController(ILogger<UserThemeController> logger)
        {
            _logger = logger;
        }

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
        /// Serves the fully compiled, optimized Server Skin.
        /// Variable substitution and addons are pre-processed by the backend.
        /// </summary>
        [HttpGet("ServerTheme.css")]
        [AllowAnonymous]
        [Produces("text/css")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async System.Threading.Tasks.Task<IActionResult> GetServerThemeCss()
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || string.IsNullOrWhiteSpace(config.SelectedCssUrl) || config.SelectedCssUrl == "Default" || config.AllowUserThemes)
            {
                return Content(string.Empty, "text/css; charset=utf-8");
            }

            string compiledCss = await ServerThemeCompiler.GetCompiledThemeAsync(config, _logger);

            Response.Headers.Append("Cache-Control", "public, max-age=3600");
            
            return Content(compiledCss, "text/css; charset=utf-8");
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

        /// <summary>
        /// Proxies external CSS requests (User Themes and Addons) through the server's
        /// local disk cache, preventing CDN timeouts and offline breakages.
        /// </summary>
        [HttpGet("Resource")]
        [AllowAnonymous]
        [Produces("text/css")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async System.Threading.Tasks.Task<IActionResult> GetResource([FromQuery] string url, [FromQuery] string v = null)
        {
            if (string.IsNullOrWhiteSpace(url))
                return Content(string.Empty, "text/css; charset=utf-8");

            string css = await SkinResourceProxy.GetResourceAsync(url, v, _logger);
            
            Response.Headers.Append("Cache-Control", "public, max-age=3600");
            return Content(css, "text/css; charset=utf-8");
        }

        /// <summary>
        /// Instantly clears all externally downloaded CSS files from the server's
        /// internal proxy disk cache. Requires an authenticated session.
        /// </summary>
        [HttpPost("ClearCache")]
        [Authorize(Policy = "DefaultAuthorization")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult ClearCache()
        {
            SkinResourceProxy.ClearCache();
            return Ok();
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
