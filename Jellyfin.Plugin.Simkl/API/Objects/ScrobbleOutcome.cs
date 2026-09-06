namespace Jellyfin.Plugin.Simkl.API.Objects;

/// <summary>
/// The result of attempting a scrobble call, used to decide whether the session state may
/// advance and whether the call is worth retrying.
/// </summary>
public enum ScrobbleOutcome
{
    /// <summary>
    /// Simkl accepted the call. The session state may advance.
    /// </summary>
    Succeeded,

    /// <summary>
    /// Nothing was sent because the item was not scrobbleable yet (for example no playback
    /// position). Not a failure: the state must not advance and no backoff applies.
    /// </summary>
    Skipped,

    /// <summary>
    /// The call failed for a reason that may resolve on its own (network error, timeout,
    /// rate limit, server error). Worth retrying after a backoff.
    /// </summary>
    Transient,

    /// <summary>
    /// The call failed for a reason that will not resolve by retrying the same request
    /// (unusable credentials, or Simkl could not identify the item).
    /// </summary>
    Permanent
}
