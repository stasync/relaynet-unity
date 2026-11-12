# Debugging

RelayNet has two views of a running session: a window in the editor, and an overlay in the game itself, which also works in builds. This page shows what each one shows and how to set them up.

- [The debug window](#the-debug-window)
- [The in-game overlay](#the-in-game-overlay)
- [Use the overlay in a build](#use-the-overlay-in-a-build)
- [Draw the markers your own way](#draw-the-markers-your-own-way)
- [Check what each client sends](#check-what-each-client-sends)

## The debug window

Open **Tools > RelayNet > Debug Window**, then enter Play Mode. The toolbar shows the connection, picks a session when there's more than one, sets the mode of the [in-game overlay](#the-in-game-overlay), and has a **Reset** button. Reset counts the totals from that moment, so you can measure a single action, such as spawning a wave of enemies.

The window has four tabs:

- **Overview**:
  - The connection: this client, the master client, the players and the round trip, if the game sets `NetWorldPlayer.RttMs`.
  - The traffic: a one-minute graph, with what was sent above the line and what was received below it, then the rates, the totals and the average message size.
  - Compression: the bytes before and after the compressor, what it saves, and how much of what's sent is packet headers rather than payload. See [Compression](compression.md).
  - The world: its time, how many objects are yours and how many are remote, the updates waiting to be applied, and its hash.
- **Players**: every player in their color, with whether they're ready, their platform, how many objects they own and their round trip. Expand a player for their tag objects and properties. The world's properties are at the bottom.
- **Objects**: every object, in its owner's color. Search by id, prefab or owner, and filter to yours or remote ones. A remote object shows how long ago its owner last updated it. Expand an object for its data, its size and its RPCs. **Select** selects its GameObject in the Hierarchy. **Copy JSON** in the tab's toolbar copies the whole world as JSON: its properties, its players and all of its objects. An expanded object has its own **Copy JSON**, for that object alone. The JSON is for reading and diffing: a data field's value is the text the window shows.
- **History**: the world's last 10 seconds of history. **Play back** replays them in a separate world that you can pause, step through and scrub.

## The in-game overlay

Press **F3** in Play Mode to step through the overlay's modes. **Shift+F3** steps back.

| Mode | Shows |
|---|---|
| Off | Nothing |
| Stats | A panel in the corner: the connection, the traffic with a graph, and what compression saves |
| World | The panel, plus a marker and a label on every networked object, in its owner's color |
| Inspector | All of that, plus a window with the session, the players and every object's data |

The overlay remembers its mode between runs. It's drawn with IMGUI, and it reads the hotkey from IMGUI events, so it works with either input system. On dense or large screens it scales up to stay readable.

To use another key, or to switch modes from your own button, for example on a touch screen:

```csharp
#if UNITY_EDITOR || RELAYNET_DEBUG
NetDebugOverlay.Hotkey = KeyCode.F9;
NetDebugOverlay.NextMode();
#endif
```

`NetDebugOverlay` only exists where the overlay does, so wrap code that uses it in the same `#if`.

## Use the overlay in a build

The overlay is part of the editor, and of a build only when you ask for it: **Tools > RelayNet > Enable debug overlay in builds** adds the `RELAYNET_DEBUG` define, for Standalone, Android and iOS. Without the define, the overlay isn't compiled into the build at all.

Keep it out of the builds you ship. The overlay shows where every player and object is, which in a released game works like a wallhack. The define doesn't change the wire format, so a build with the overlay can play with one without it.

## Draw the markers your own way

The overlay draws its markers through `NetDebugDraw.Drawer`. The default drawer, `NetGizmoDrawer`, draws with `Debug.DrawLine`, which works like gizmos: the lines show in the Scene view, and in the Game view while **Gizmos** is on, but not in a build.

To see the markers in a build, or to draw them with your game's own debug renderer, implement `INetDebugDrawer`, and install it when the game starts:

```csharp
using Keel.RelayNet.Drawing;
using UnityEngine;

public sealed class GameDebugDrawer : INetDebugDrawer
{
    [RuntimeInitializeOnLoadMethod]
    private static void Install() =>
        NetDebugDraw.Drawer = new GameDebugDrawer();

    // Called for every line of every marker, every frame, after Update. Draw it on top of the scene, for one frame.
    public void DrawLine(Vector3 start, Vector3 end, Color color) =>
        GameDebugLines.Add(start, end, color);
}
```

`GameDebugLines` stands for whatever draws lines in your game, for example with `GL` after the camera renders. `INetDebugDrawer` and `NetDebugDraw` are in every build, with or without `RELAYNET_DEBUG`, so the drawer needs no `#if`. Set the drawer to null to draw no markers.

## Check what each client sends

**Tools > RelayNet > Enable debug** adds the `NETWORK_DEBUG` define, and **Disable debug** removes it. With the define, every behaviour's serialized data carries a type hash that the receiver checks, and RPCs for missing objects are logged with their handler names. The hash changes the wire format, so all clients must be built with the same setting.
