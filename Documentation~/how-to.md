# How to use RelayNet

This page shows how to build common game features with RelayNet. The [README](../README.md) describes each API, and this page puts them to work. The examples assume a running session with a started world, as in the README's [Quick start](../README.md#quick-start).

- [Keep match state in world properties](#keep-match-state-in-world-properties)
- [Give players a team or a loadout](#give-players-a-team-or-a-loadout)
- [Let any player pick up an item](#let-any-player-pick-up-an-item)
- [Let one player at a time use an object](#let-one-player-at-a-time-use-an-object)
- [Spawn objects that expire](#spawn-objects-that-expire)
- [Show a kill cam](#show-a-kill-cam)
- [Show an instant replay](#show-an-instant-replay)
- [Step back through a bug](#step-back-through-a-bug)

## Keep match state in world properties

State that belongs to the match, not to an object, such as the phase, the time left or the score, fits in the world's properties. Only the master writes them, and every client reads them. Give each property an id:

```csharp
public static class MatchProperties
{
    public const uint TimeLeft = 1;
    public const uint RedScore = 2;
    public const uint BlueScore = 3;
}
```

The master counts the time down, and every client shows it:

```csharp
var world = session.MainWorld;

if (world.Players.LocalPlayer.IsMaster())
    world.Properties.SetFloat(MatchProperties.TimeLeft, world.Properties.GetFloat(MatchProperties.TimeLeft) - Time.deltaTime);

_timerLabel.text = $"{world.Properties.GetFloat(MatchProperties.TimeLeft):0}";
```

A property that was never set reads as the type's default value. The properties live in the world, not on the master: when the master leaves, the new master reads the same values and carries on from there.

## Give players a team or a loadout

Each player has properties of their own, which only that player writes. Use them for what other clients need to know about a player, such as their team, character or loadout:

```csharp
public static class PlayerProperties
{
    public const uint Team = 1;
}

// On the local client, once it has joined.
world.Players.LocalPlayer.Properties.SetInt(PlayerProperties.Team, 1);

// On any client.
foreach (var player in world.Players.All)
    Debug.Log($"{player.Name} plays for team {player.Properties.GetInt(PlayerProperties.Team)}");
```

## Let any player pick up an item

An item that changes hands, such as a weapon on the floor or a ball, is a shared object (`shared: true`). The client that picks it up takes ownership with `OwnObject()`, and then writes its data as the owner:

```csharp
public sealed class ItemData : NetObjectStateData
{
    public readonly NetworkValue<NetObjectUid> Holder = new();
}

public sealed class Item : NetObjectBehaviour<ItemData>
{
    public void PickUp(NetObjectIdentity character)
    {
        Identity.OwnObject();
        Data.Holder.Value = character.InstanceId;
    }
}
```

The world spreads shared objects among the clients every 5 seconds, so an item someone holds can move to another client. A rule keeps it in place. Every class derived from `NetObjectOwnershipDistributionRule` is picked up automatically:

```csharp
public sealed class KeepHeldItems : NetObjectOwnershipDistributionRule
{
    public override bool CanRequestOwnership(NetObjectState state) =>
        !(state.DataCollection.TryGetData<ItemData>(out var item) && item.Holder.Value.IsValid);
}
```

If two clients can grab the item at the same moment, both take ownership. When only one of them may get it, lock it with a transaction (see the next section).

## Let one player at a time use an object

A door, a chest or a vehicle seat should be used by one player at a time. Add a `NetObjectTransactionBehaviour` to its prefab. The client that wants to use it asks the object's owner for the lock, on behalf of an object it owns, such as its character:

```csharp
var chest = chestIdentity.GetBehaviour<NetObjectTransactionBehaviour>();

chest.ChangeTransactionRequest(character.InstanceId, true, success =>
{
    if (success)
        OpenChestUi();
});

// When the player is done.
chest.ChangeTransactionRequest(character.InstanceId, false);
```

The owner grants the lock to one client and refuses the others until it's released. If the object holding the lock goes away, because its player left for example, the lock is released too. The owner can turn the object off for everyone with `SetInteractionsEnabledWithAuthority(false)`.

## Spawn objects that expire

Projectiles, pickups and effects often live for a few seconds. Add a `NetObjectLifetime` to the prefab, set **Lifetime**, and turn on **Auto Destroy** to remove the object when the time is up. The owner counts the time down and removes the object, and the countdown is synced, so every client sees the same time left.

Every client can read `NormalizedLifetime` to fade or shrink the object as it expires. The owner also gets the `TimePassed` event, for example to spawn an explosion. To change the lifetime at runtime, call `SetLifetimeWithAuthority(seconds)` on the owner.

## Show a kill cam

A kill cam replays the last few seconds of the match from the killer's side. The world records the last 10 seconds of its state, and `Replicate()` turns that history into a `NetWorldReplica`, a separate world with its own GameObjects. The live world keeps running and recording while the replay plays.

This component plays a kill cam on the camera it's on. Call `Play` when the local player dies, with the killer's object id:

```csharp
using Keel.RelayNet.StateManagement;
using UnityEngine;

public sealed class KillCam : MonoBehaviour
{
    [SerializeField] private float _seconds = 4f;
    [SerializeField] private Vector3 _offset = new(0f, 3f, -5f);

    private NetWorldRuntime _world;
    private NetWorldReplica _replica;
    private NetObjectUid _killer;

    public void Play(NetWorldRuntime world, NetObjectUid killer)
    {
        if (world.HistoryBuffer.Count == 0)
            return;

        _world = world;
        _killer = killer;

        _replica = world.Replicate();
        _replica.Disposed += OnReplayEnded;

        // A replica starts at the oldest snapshot. Skip to the last few seconds.
        var startTime = world.LocalTime - _seconds;
        _replica.MoveToSnapshotSafe(_replica.FindFirstSnapshotIndex(snapshot => snapshot.LocalTime >= startTime), updateImmediate: true);

        // The replayed objects stand where the live ones did. Hide and mute the live ones.
        world.View.SetEnabledImmediate(false);
    }

    private void Update()
    {
        // Nothing updates a replica but you. It disposes itself after its last snapshot.
        if (_replica is { IsValid: true })
            _replica.Update(Time.deltaTime);
    }

    private void LateUpdate()
    {
        if (_replica is not { IsValid: true })
            return;

        // The killer's GameObject in the replay, once its view has been created.
        var killer = _replica.View.GetObjectView(_killer);
        if (killer == null)
            return;

        transform.position = killer.transform.TransformPoint(_offset);
        transform.LookAt(killer.transform);
    }

    private void OnReplayEnded()
    {
        _world.View.SetEnabledImmediate(true);
        _replica = null;
    }
}
```

Things to know about replays:

- **Every replayed object is remote,** and the replay has no local player (`Players.LocalPlayer` is `null`). Behaviours run in the replay too, so a behaviour that sends RPCs, changes game state or updates the HUD should check `Identity.World.IsReplica` first.
- **Replays step at 10 snapshots per second.** Behaviours that interpolate on their own, such as `NetObjectTransformSync`, look smooth. Data applied directly, such as a color, changes in steps.
- **The replay has its own physics scene,** so replayed objects never hit the live game or show up in its raycasts. Nothing simulates that scene: for ragdolls or other physics in the replay, step it with `_replica.View.gameObject.scene.GetPhysicsScene().Simulate(Time.deltaTime)`.
- **The replay's view also skips objects far from `Camera.main`.** If the kill cam isn't the main camera, or the action is more than 50 meters away, raise the distance with `_replica.View.SetDrawDistance(meters)`.

## Show an instant replay

An instant replay, such as the last goal or the end of a round, works like the kill cam, with more control over the playback:

- **Play all of it.** A new replica plays the whole recorded history, up to 10 seconds, from the oldest snapshot.
- **Slow motion.** Pass a smaller delta, such as `_replica.Update(Time.deltaTime * 0.5f)`.
- **Pause and scrub.** `SetPaused(true)` stops the playback. `MoveToPreviousSnapshot(updateImmediate: true)`, `MoveToNextSnapshot(updateImmediate: true)` and `MoveToSnapshotSafe(index, updateImmediate: true)` step through it. `CurrentSnapshotIndex` and `BufferCount` drive a timeline slider.
- **Keep it after the end.** A replica from `Replicate()` disposes itself after its last snapshot. To loop or to hold the last frame, create it with `new NetWorldReplica(world, autoDestroy: false)`, and call `Dispose()` yourself when you're done.

A replay copies the history when you create it. To replay a moment later on, such as a goal at the end of the round, create the replica right after the moment happens, pause it and hide it with `_replica.View.SetEnabledImmediate(false)`. When you're ready, show it, hide the live world's view, and unpause it.

## Step back through a bug

When something goes wrong in play mode, such as an object jumping or a value going out of sync, the last 10 seconds are still in the world's history. The **Debug Window** replays them:

1. Open **Tools > RelayNet > Debug Window** before you reproduce the bug.
2. Within 10 seconds of the bug, press **Playback** under **Main World History Info**. The replay copies the history at that moment, so the live game can keep running.
3. Under **Replica info**, press **Pause**, then step with **<< Previous** and **Next >>**, or drag the **Snapshot** slider.
4. Inspect the replayed objects in the Hierarchy and the Scene view, and their states in the window.
5. Press **Stop Playback** when you're done.

The replayed objects stand on top of the live ones. In the Hierarchy, they're in the replica's own scene. Their GameObjects are created asynchronously, so keep the game running, not paused in the editor, while you step through the replay.
