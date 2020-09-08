using System;
using Jellyfin.Plugin.Simkl.API.Objects;
using MediaBrowser.Model.Services;

namespace Jellyfin.Plugin.Simkl.API
{
    /// <summary>
    /// Get user settings.
    /// </summary>
    [Route("/Simkl/users/settings/{userId}", "GET")]
    public class GetUserSettings : IReturn<UserSettings>
    {
        /// <summary>
        /// Gets or sets user id.
        /// </summary>
        /// <remarks>
        /// Note: In the future, when we'll have config for more than one user, we'll use a parameter.
        /// </remarks>
        [ApiMember(Name = "id", Description = "user id", IsRequired = true, DataType = "Guid", ParameterType = "path", Verb = "GET")]
        public Guid UserId { get; set; }
    }
}