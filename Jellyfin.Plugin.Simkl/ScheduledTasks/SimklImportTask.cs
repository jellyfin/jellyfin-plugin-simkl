using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Simkl.Configuration;
using Jellyfin.Plugin.Simkl.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Simkl.ScheduledTasks
{
    /// <summary>
    /// Dashboard, Scheduled Tasks, "Import Simkl watched history". Runs for users who switched automatic import on.
    /// </summary>
    public sealed class SimklImportTask : IScheduledTask, IConfigurableScheduledTask
    {
        private readonly HistoryImporter _importer;
        private readonly IUserManager _userManager;
        private readonly ILogger<SimklImportTask> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="SimklImportTask"/> class.
        /// </summary>
        /// <param name="importer">The history importer.</param>
        /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{SimklImportTask}"/> interface.</param>
        public SimklImportTask(HistoryImporter importer, IUserManager userManager, ILogger<SimklImportTask> logger)
        {
            _importer = importer;
            _userManager = userManager;
            _logger = logger;
        }

        /// <inheritdoc />
        public string Name => "Import Simkl watched history";

        /// <inheritdoc />
        public string Description => "Checks Simkl for users who enabled automatic import and marks what they watched as played. Runs hourly; each user picks how often their account is checked.";

        /// <inheritdoc />
        public string Category => "Simkl";

        /// <inheritdoc />
        public string Key => "SimklHistoryImport";

        /// <inheritdoc />
        public bool IsEnabled => true;

        /// <inheritdoc />
        public bool IsHidden => false;

        /// <inheritdoc />
        public bool IsLogged => true;

        /// <inheritdoc />
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfoType.IntervalTrigger,
                    IntervalTicks = TimeSpan.FromHours(1).Ticks
                }
            };
        }

        /// <inheritdoc />
        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            // Driven off the stored settings rather than the server's list of users, because 10.11.9 replaced
            // IUserManager.Users with GetUsers() midway through a patch line: neither can be named by one
            // assembly that has to load on every 10.11.x. GetUserById has been there unchanged since 10.10,
            // and the only users of interest here are the ones who asked for automatic import anyway.
            var configs = (SimklPlugin.Instance?.Configuration.UserConfigs ?? Array.Empty<UserConfig>())
                .Where(c => c.AutoImport && c.Id != Guid.Empty)
                .ToList();
            if (configs.Count == 0)
            {
                progress.Report(100);
                return;
            }

            for (var i = 0; i < configs.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var config = configs[i];

                // a user the administrator has since deleted or disabled keeps their settings but is not imported for
                var user = _userManager.GetUserById(config.Id);
                if (user != null && !user.HasPermission(PermissionKind.IsDisabled))
                {
                    try
                    {
                        await _importer.RunScheduled(user, config, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Automatic import for {UserName} failed", user.Username);
                    }
                }

                progress.Report((i + 1) * 100.0 / configs.Count);
            }
        }
    }
}
