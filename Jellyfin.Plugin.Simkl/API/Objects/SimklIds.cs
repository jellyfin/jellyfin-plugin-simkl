using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Simkl Ids.
    /// </summary>
    public class SimklIds
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SimklIds"/> class.
        /// </summary>
        /// <param name="providerIds">The provider ids.</param>
        public SimklIds(Dictionary<string, string> providerIds)
        {
            foreach (var (key, value) in providerIds)
            {
                var prop = GetType().GetProperty(key, BindingFlags.IgnoreCase);
                if (prop.PropertyType == typeof(int?))
                {
                    prop.SetValue(this, int.Parse(value, NumberStyles.Any, CultureInfo.InvariantCulture));
                }
                else if (prop.PropertyType == typeof(string))
                {
                    prop.SetValue(this, value);
                }
            }
        }

        /// <summary>
        /// Gets or sets simkl.
        /// </summary>
        public int? Simkl { get; set; }

        /// <summary>
        /// Gets or sets the imdb id.
        /// </summary>
        public string Imdb { get; set; }

        /// <summary>
        /// Gets or sets the slug.
        /// </summary>
        public string Slug { get; set; }

        /// <summary>
        /// Gets or sets the netflix id.
        /// </summary>
        public string Netflix { get; set; }

        /// <summary>
        /// Gets or sets the TMDb id.
        /// </summary>
        public string Tmdb { get; set; }
    }
}