using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.SkinManager.Services
{
    public class PatchRequestPayload
    {
        [JsonPropertyName("contents")]
        public string Contents { get; set; }
    }
}