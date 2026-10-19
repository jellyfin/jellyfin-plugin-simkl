using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Jellyfin.Plugin.Simkl.Configuration;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Simkl.Services
{
    /// <summary>
    /// Reads and writes the per-user <see cref="ImportState"/>.
    /// </summary>
    /// <remarks>
    /// The state lives in one file of its own, NOT in the plugin configuration. The configuration page saves by posting
    /// the whole configuration document back, and storing cursors in there would mean every cursor write rewrote every
    /// user's token as well, while still racing a page save this class cannot see. The path is under
    /// <c>PluginConfigurationsPath</c> and deliberately not under <c>DataFolderPath</c>, which is version suffixed and
    /// wiped by the plugin updater - that would silently drop the cursors on every update. Writes are serialized here,
    /// go to a temporary file and are then moved over the real one, so a crash mid-write cannot leave a half-written
    /// file. Callers only ever see a copy.
    /// </remarks>
    public sealed class ImportStateStore
    {
        private const string FileName = "import_state.json";

        private readonly object _gate = new object();
        private readonly string _filePath;
        private readonly ILogger<ImportStateStore> _logger;
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true };

        // every read and write goes through the lock, so one cached copy is enough
        private Dictionary<string, ImportState>? _cache;

        /// <summary>
        /// Initializes a new instance of the <see cref="ImportStateStore"/> class.
        /// </summary>
        /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{ImportStateStore}"/> interface.</param>
        public ImportStateStore(IApplicationPaths applicationPaths, ILogger<ImportStateStore> logger)
        {
            ArgumentNullException.ThrowIfNull(applicationPaths);
            _filePath = Path.Combine(applicationPaths.PluginConfigurationsPath, "Simkl", FileName);
            _logger = logger;
        }

        /// <summary>
        /// Reads the import state of one user.
        /// </summary>
        /// <param name="userId">The Jellyfin user id.</param>
        /// <returns>A copy of the stored state, or a blank one.</returns>
        public ImportState Load(Guid userId)
        {
            lock (_gate)
            {
                try
                {
                    return Read().TryGetValue(Key(userId), out var stored) ? stored.Clone() : new ImportState();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // already logged in Read; a blank state costs this user one full import, which is far cheaper
                    // than treating an unreadable file as "nobody has ever imported"
                    return new ImportState();
                }
            }
        }

        /// <summary>
        /// Stores the import state of one user.
        /// </summary>
        /// <param name="userId">The Jellyfin user id.</param>
        /// <param name="userName">The user name, for the log line when saving fails.</param>
        /// <param name="state">The state to store.</param>
        public void Save(Guid userId, string userName, ImportState state)
        {
            ArgumentNullException.ThrowIfNull(state);

            lock (_gate)
            {
                try
                {
                    var all = Read();
                    all[Key(userId)] = state.Clone();
                    Write(all);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    _logger.LogError(ex, "Couldn't save the import state for {UserName}", userName);
                }
            }
        }

        private static string Key(Guid userId)
        {
            return userId.ToString("N", CultureInfo.InvariantCulture);
        }

        // Throws on an unreadable file, deliberately: an empty map must never be cached because of a transient lock or
        // a permission problem, because the next Save would then serialize that empty map over every user's cursors.
        // Only a file that is genuinely absent or corrupt starts empty.
        private Dictionary<string, ImportState> Read()
        {
            if (_cache != null)
            {
                return _cache;
            }

            if (!File.Exists(_filePath))
            {
                return _cache = new Dictionary<string, ImportState>(StringComparer.Ordinal);
            }

            string text;
            try
            {
                text = File.ReadAllText(_filePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Couldn't read {Path}; leaving the stored import state untouched", _filePath);
                throw;
            }

            try
            {
                _cache = JsonSerializer.Deserialize<Dictionary<string, ImportState>>(text, _jsonOptions);
            }
            catch (JsonException ex)
            {
                // unparseable is not recoverable by waiting, so starting over is the only way forward
                _logger.LogError(ex, "{Path} is corrupt, starting from an empty import state", _filePath);
            }

            return _cache ??= new Dictionary<string, ImportState>(StringComparer.Ordinal);
        }

        private void Write(Dictionary<string, ImportState> all)
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporary = _filePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(all, _jsonOptions));
            File.Move(temporary, _filePath, true);
            _cache = all;
        }
    }
}
