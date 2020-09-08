using Jellyfin.Plugin.Simkl.API.Responses;
using MediaBrowser.Model.Services;

namespace Jellyfin.Plugin.Simkl.API
{
    /// <summary>
    /// Get pin status.
    /// </summary>
    [Route("/Simkl/oauth/pin/{user_code}", "GET")]
    public class GetPinStatus : IReturn<CodeStatusResponse>
    {
        /// <summary>
        /// Gets or sets user code.
        /// </summary>
        [ApiMember(Name = "user_code", Description = "pin to be introduced by the user", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
        public string UserCode { get; set; }
    }
}