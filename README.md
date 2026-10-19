<h1 align="center">Jellyfin SIMKL Plugin</h1>
<h3 align="center">Part of the <a href="https://jellyfin.org/">Jellyfin Project</a></h3>

###

## Features
- Multi-user: every user connects their own Simkl account
- Reports play, pause and stop to Simkl as they happen, so your profile shows what you are watching now
- Marks an item as watched when playback stops past 80%; stopping earlier saves your position on Simkl, where the Playback Progress Manager lists it
- Mirrors marking played or unplayed in Jellyfin to your Simkl history, and can be switched off per user
- Imports your Simkl watch history into Jellyfin, on demand or on a schedule, with the date you watched each item
- Logs a rewatch when you finish something already in your history (needs Simkl Pro or VIP)
- Identifies an item by its file name when Simkl cannot match it by id
- Skips anything shorter than a runtime you choose

## Setup
Open Dashboard, then Plugins, then Simkl. Pick a user, click Log In, and enter the code it shows at
[simkl.com/pin](https://simkl.com/pin).

Jellyfin serves plugin settings to administrators only, so an administrator connects each user's account from
that one page. Accounts connected with the older sign-in keep working and can be reconnected from the same
page whenever convenient.
