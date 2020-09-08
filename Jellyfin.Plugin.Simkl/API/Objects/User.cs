using System;
using System.Text.Json.Serialization;
#pragma warning disable SA1300

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// User.
    /// </summary>
    public class User
    {
        /// <summary>
        /// Gets or sets name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets joined at.
        /// </summary>
        [JsonPropertyName("joined_at")]
        public DateTime joined_at { get; set; }

        /// <summary>
        /// Gets or sets gender.
        /// </summary>
        public string Gender { get; set; }

        /// <summary>
        /// Gets or sets avatar.
        /// </summary>
        public string Avatar { get; set; }

        /// <summary>
        /// Gets or sets bio.
        /// </summary>
        public string Bio { get; set; }

        /// <summary>
        /// Gets or sets loc.
        /// </summary>
        public string Loc { get; set; }

        /// <summary>
        /// Gets or sets age.
        /// </summary>
        public string Age { get; set; }
    }
}