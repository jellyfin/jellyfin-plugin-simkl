using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Notifications;

namespace Jellyfin.Plugin.Simkl.Services
{
    /// <inheritdoc />
    public class SimklNotificationsFactory : INotificationTypeFactory
    {
        /// <summary>
        /// Notification category.
        /// </summary>
        public const string NotificationCategory = "Simkl Scrobbling";

        /// <summary>
        /// Notification movie type.
        /// </summary>
        public const string NotificationMovieType = "SimklScrobblingMovie";

        /// <summary>
        /// Notification show type.
        /// </summary>
        public const string NotificationShowType = "SimklScrobblingShow";

        /// <inheritdoc />
        public IEnumerable<NotificationTypeInfo> GetNotificationTypes()
        {
            yield return new NotificationTypeInfo
            {
                Type = NotificationMovieType,
                Name = "Scrobbling Movie",
                Category = NotificationCategory,
                Enabled = true,
                IsBasedOnUserEvent = false
            };

            yield return new NotificationTypeInfo
            {
                Type = NotificationShowType,
                Name = "Scrobbling TV Show",
                Category = NotificationCategory,
                Enabled = true,
                IsBasedOnUserEvent = false
            };
        }

        /// <summary>
        /// Get notification request.
        /// </summary>
        /// <param name="item">Item.</param>
        /// <param name="userId">USer id.</param>
        /// <returns>The notification request.</returns>
        public static NotificationRequest GetNotificationRequest(BaseItemDto item, Guid userId)
        {
            var nr = new NotificationRequest
            {
                Date = DateTime.UtcNow,
                UserIds = new[] { userId },
                SendToUserMode = SendToUserType.Custom
            };

            // TODO: Set url parameter to simkl's movie url
            if (item.IsMovie == true || item.Type == "Movie")
            {
                nr.NotificationType = NotificationMovieType;
                nr.Name = "Movie Scrobbled to Simkl";
                nr.Description = "The movie " + item.Name;
                nr.Description += " has been scrobbled to your account";
            }

            if (item.IsSeries == true || item.Type == "Episode")
            {
                nr.NotificationType = NotificationShowType;
                nr.Name = "Episode Scrobbled to Simkl";
                nr.Description = item.SeriesName;
                nr.Description += " S" + item.ParentIndexNumber + ":E" + item.IndexNumber;
                nr.Description += " - " + item.Name;
                nr.Description += " has been scrobbled to your account";
            }

            return nr;
        }
    }
}