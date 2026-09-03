namespace Jellyfin.Plugin.Simkl.API.Objects;

/// <summary>
/// The scrobble call being made to Simkl.
/// </summary>
public enum ScrobbleAction
{
    /// <summary>
    /// Playback has begun or resumed (/scrobble/start).
    /// </summary>
    Start,

    /// <summary>
    /// Playback has been paused (/scrobble/pause).
    /// </summary>
    Pause,

    /// <summary>
    /// Playback has ended (/scrobble/stop).
    /// </summary>
    Stop
}
