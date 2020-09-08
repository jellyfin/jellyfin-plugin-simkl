using Jellyfin.Plugin.Simkl.API.Responses;
using MediaBrowser.Model.Services;

namespace Jellyfin.Plugin.Simkl.API
{
    /// <summary>
    /// Get oauth pin.
    /// </summary>
    [Route("/Simkl/oauth/pin", "GET")]
    public class GetPin : IReturn<CodeResponse>
    {
        // Doesn't receive anything
    }
}