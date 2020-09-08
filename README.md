<h1 align="center">Jellyfin SIMKL Plugin</h1>
<h3 align="center">Part of the <a href="https://jellyfin.org/">Jellyfin Project</a></h3>

###
Repository Url:
https://repo.codyrobibero.dev/manifest.json

## How to enable notifications when something is marked as watched
1. On Jellyfin's dashboard, you'll have to go to the bottom and, on expert options, select "Notifications"
2. There, on section "Simkl Scrobbling", enable both notifications

## How to enable debugging
To report a bug or an error, we'll need more info to know how to fix it. To send us the needed reports you'll need to
first enable debug logging.

1. On Jellyfin's dashboard, scroll to the bottom and, on expert options, select "Logs" (right above "Notifications")
2. Click on "Enable debug logging"
3. Restart the server and reproduce the error

Now you can contact us to try and fix the problem

## Current features
- Multi-user support
- Auto scrobble Movies and TV Shows at given percentage to Simkl
- Easy login using pin (no more putting passwords with the TV remote)
- If scrobbling fails, search it using filename using Simkl's API and then scrobble it
- Send notifications about scrobbling

Modified for Jellyfin from https://github.com/SIMKL/Emby/