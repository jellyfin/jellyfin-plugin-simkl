using System.Collections.Generic;
using Jellyfin.Plugin.Simkl.API.Objects;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Simkl.API
{
    /// <summary>
    /// Helper class for handling and logging Simkl API errors.
    /// </summary>
    public static class SimklErrorHandler
    {
        /// <summary>
        /// Logs detailed information about Simkl API errors with helpful guidance.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="error">Error code from Simkl API.</param>
        /// <param name="item">Jellyfin media item that failed to scrobble.</param>
        /// <param name="request">Scrobble request that was sent to Simkl.</param>
        public static void LogError(ILogger logger, string error, BaseItemDto item, ScrobbleRequest request)
        {
            switch (error.ToLowerInvariant())
            {
                case "id_err":
                    logger.LogWarning(
                        "Simkl could not identify '{ItemName}' - missing or invalid external IDs. " +
                        "Item type: {ItemType}, Title sent: '{Title}', " +
                        "External IDs: {ExternalIds}. " +
                        "Ensure the media has proper IMDb, TMDb, or TVDb IDs in Jellyfin metadata.",
                        item.Name,
                        item.Type,
                        GetRequestTitle(request),
                        System.Text.Json.JsonSerializer.Serialize(item.ProviderIds ?? new Dictionary<string, string>()));
                    break;
                case "invalid_token":
                    logger.LogError("Invalid Simkl token for user - token may have expired or been revoked");
                    break;
                case "rate_limit":
                    logger.LogWarning("Simkl API rate limit exceeded - scrobbling will be retried");
                    break;
                default:
                    logger.LogWarning("Unknown Simkl API error '{Error}' for '{ItemName}'", error, item.Name);
                    break;
            }
        }

        /// <summary>
        /// Determines whether an error code returned in a Simkl response body is worth retrying.
        /// </summary>
        /// <param name="error">Error code from the Simkl API.</param>
        /// <returns><c>true</c> when the same request could succeed later; otherwise <c>false</c>.</returns>
        public static bool IsTransient(string? error)
        {
            // Anything else (id_err, invalid_token, ...) means Simkl understood the request and
            // refused it; repeating the identical request will not change the answer.
            return string.Equals(error, "rate_limit", System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets a formatted title string from a scrobble request for logging purposes.
        /// </summary>
        /// <param name="request">Scrobble request.</param>
        /// <returns>Formatted title string.</returns>
        private static string GetRequestTitle(ScrobbleRequest request)
        {
            if (request.Movie != null)
            {
                return $"{request.Movie.Title} ({request.Movie.Year})";
            }

            if (request.Show != null && request.Episode != null)
            {
                return $"{request.Show.Title} S{request.Episode.Season:D2}E{request.Episode.Number:D2}";
            }

            return "Unknown";
        }
    }
}