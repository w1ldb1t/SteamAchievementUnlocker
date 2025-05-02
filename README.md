# Steam Achievement Unlocker

## Summary

Steam Achievement Unlocker (SAU) is a C# console application. While it's more lightweight and less feature-rich than SAM, it lets you unlock Steam achievements gradually without inflating your recorded playtime (unlike [SamRewritten](https://github.com/PaulCombal/SamRewritten)). Achievements are unlocked in order of global popularity - for example, those earned by 90% of players will be unlocked before those earned by 80%.

## Usage

The application does not need any special parameters. You just need to put a `settings.json` file in the same folder as the built application:

```json
{
    "apiKey": "A98XXXXXXXXXXXXXXXXXXXXXXXXXXXXX",
    "steamId64": "7684XXXXXXXXXXXXX",
    "appId": ["1569040", "1238860"],
    "minMinutes": "5",
    "maxMinutes": "150"
}
```
The `apiKey` you get it directly through [the Steam portal](https://steamcommunity.com/dev/apikey). The `appId` is the unique Steam identifier for the game you want to unlock achievements for, and you can find it [through SteamDB](https://steamdb.info/apps/).

In case you want to build the project from source, keep in mind that you will need a copy of the `steam_api64.dll` for the final build to work, which can be found in pretty much all Steam games.

## Project Overview
In order to query and alter a game's statistics on Steam, we need to use the Steam API to disguise our program as the actual game we want to fake. Steam will then increase our playtime for that title while the process remains alive. To keep our overall runtime to a minimum, the program is split into two cooperating components:

### Server
A long-running program that sits in the background, communicating with the Client over an IPC channel. Every _X_ minutes the Server:

1. Sends a launch request to a Client process
2. Waits for it to unlock an achievement

### Client

A short-lived process whose only job is to:

1. Connect to Steam  
2. Unlock the next achievement  
3. Exit immediately once that achievement has been granted  

By moving to this Server/Client model with IPC, we ensure that each achievement-unlocking instance lives only as long as it needs to, and we keep our total playtime bump on Steam as low as possible.  
