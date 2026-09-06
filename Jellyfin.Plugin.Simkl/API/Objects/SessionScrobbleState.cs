namespace Jellyfin.Plugin.Simkl.API.Objects;

/// <summary>
/// Represents the current scrobble state for a session.
/// </summary>
public enum SessionScrobbleState
{
    /// <summary>
    /// No scrobbling has started for this session.
    /// </summary>
    NotStarted,

    /// <summary>
    /// Scrobbling has started and is currently active.
    /// </summary>
    Started,

    /// <summary>
    /// Scrobbling is paused.
    /// </summary>
    Paused,

    /// <summary>
    /// Scrobbling has been stopped/completed.
    /// </summary>
    Stopped
}
