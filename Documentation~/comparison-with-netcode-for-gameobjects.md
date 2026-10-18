# RelayNet compared with Netcode for GameObjects

[Netcode for GameObjects](https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@latest) (NGO) is Unity's official networking package. RelayNet solves the same problem, keeping GameObjects in sync between clients, but it's built on different choices. This page explains those differences, so you can decide which one fits your game.

The comparison covers NGO 1.x (Unity 2021.3 to 6000.2) and 2.x (Unity 6 and later), as documented in October 2026, and RelayNet 0.1.0.

## At a glance

| | Netcode for GameObjects | RelayNet |
|---|---|---|
| Where networked state lives | In `NetworkVariable` fields of `NetworkBehaviour` components | In plain C# data classes held by the world, apart from GameObjects |
| World model | The `NetworkManager` and the `NetworkObject`s it has spawned | A world object that holds the players, object states and properties, with a view that maps them to GameObjects |
| Topology | Client-server, with a host or a dedicated server. 2.x adds distributed authority. | Clients in a Relay room. There's no game server. |
| Authority | The server, by default | The client that owns the object |
| Transport and hosting | Unity Transport, with Unity's Relay or Multiplayer Services | Keel's reliable UDP and a Relay server you host yourself |
| Spawning | The server calls `NetworkObject.Spawn` on prefabs listed in a `NetworkPrefabsList` | Any client calls `CreateObject` and owns the result. Prefabs are registered automatically. |
| State updates | `NetworkVariable` changes are sent to clients | Each owner sends the full state of its objects 10 times per second |
| Interest management | Per-client visibility of objects | None. Every client receives every object. |
| RPCs | `[Rpc(SendTo...)]` methods with typed parameters and generated code | `byte[]` handlers marked `[AutoRegisterRpcHandler]`, found by reflection |
| Status | Official Unity package | Open-source package, version 0.1.0 |

## Data is separated from MonoBehaviours

This is the biggest difference between the two packages.

In NGO, networked state is part of the GameObject. A `NetworkVariable` has to be a field of a `NetworkBehaviour`, on a GameObject with a `NetworkObject`, and it's tied to that behaviour when the object spawns:

```csharp
// Netcode for GameObjects
public class Health : NetworkBehaviour
{
    public NetworkVariable<float> Value = new(100f);
}
```

In RelayNet, networked state is plain data that the world owns. Each networked object has a `NetObjectState` in the `NetWorldRuntime`. That state holds a collection of `NetObjectStateData` objects, which are plain C# classes with `NetworkValue` fields. A MonoBehaviour only gets a typed view of that data:

```csharp
// RelayNet
public sealed class HealthData : NetObjectStateData
{
    public readonly NetworkValue<float> Value = new();
}

public sealed class Health : NetObjectBehaviour<HealthData>
{
    public float Value => Data.Value.Value;
}
```

The behaviour is optional. The owner of an object can add and remove data through its `State.DataCollection` at any time, with `GetOrCreateData<T>()` and `RemoveData<T>()`, and the change reaches the other clients with the next state update.

GameObjects are views of the state. `NetWorldView` creates, updates and destroys them to match the world. The world doesn't need them to run.

This separation lets RelayNet do things that are hard when the state lives inside components:

- **State without GameObjects.** The world receives and applies state before the scene and its views exist. Views can be instantiated asynchronously, and the state is complete in the meantime.
- **Reading state from anywhere.** Game logic, tools and tests can read the state of every object without finding its GameObject:

  ```csharp
  foreach (var state in session.MainWorld.ObjectStates.Values)
  {
      if (state.DataCollection.TryGetData<HealthData>(out var health))
          Debug.Log($"{state.Uid}: {health.Value.Value}");
  }
  ```

- **Snapshots and replay.** The whole world can be serialized as a snapshot. The runtime world keeps the last 10 seconds of snapshots, and a `NetWorldReplica` replays them into a separate world. The **Debug Window** uses this to scrub through recent history.
- **One write rule.** Only the owner of an object can change its data. A `NetworkValue` throws if any other client writes it.

There's a cost: you learn two layers, a data class and a behaviour, where NGO has one. In RelayNet, a MonoBehaviour field is never networked by itself. The state has to go through the object's data.

## Worlds

In NGO, a session is the `NetworkManager` and the `NetworkObject`s it has spawned. Networked state exists as part of those spawned GameObjects.

RelayNet puts that state in a world. A world holds the players, every networked object's state and data, and world-wide properties. `NetWorldRuntime` is the live world of a session (`session.MainWorld`): ten times per second it sends the state of the objects this client owns and applies what the others sent. A world has a view, `NetWorldView`, which creates a GameObject for each object state in a scene of its own. The world doesn't depend on that view: it runs before the view exists, and the view may skip the GameObjects of distant remote objects while their state keeps updating.

Because worlds are plain objects, there can be more than one. The runtime world records the last 10 seconds of its state, and `Replicate()` creates a `NetWorldReplica`, a second world that replays that history with its own view, next to the live one.

Each world's view lives in a scene of its own, so worlds can also keep their physics apart. A replay's view gets its own physics scene: replayed objects don't collide with the live game or show up in its raycasts, which is what a kill cam needs. NGO doesn't come with a replay world: a replay there is something you build yourself.

## Topology and authority

NGO assumes a client-server topology by default. The server owns most objects and decides what spawns and despawns, which suits server-authoritative games, client prediction and rollback. NGO 2.x adds a distributed authority mode. In that mode authority is spread across clients, and one client, the session owner, manages global state. Unity documents distributed authority with its distributed authority service in the Multiplayer Services package.

RelayNet always works like distributed authority. Clients join a room on a [Relay](https://github.com/stasync/relay-dotnet) server, which only forwards their messages, and no machine runs the simulation for everyone. Each object has one owner, at first the client that created it, and only the owner changes the object's state. An object created with `shared: false` belongs to its creator for good: its owner never changes, and it leaves with its creator. A shared object belongs to the room: it stays when its owner leaves, and its ownership can move.

The client with the lowest id is the master. It registers the networked objects placed in the scene, which are always shared, and takes over shared objects whose owner leaves. Every 5 seconds, clients also spread the ownership of shared objects among themselves, so the work doesn't pile up on one client. A player's tag objects never move, and you can keep other objects in place with a rule derived from `NetObjectOwnershipDistributionRule`.

Without a central authority, every client is trusted with the objects it owns. If your game needs server-side validation to resist cheating, NGO's client-server mode is the better fit.

## Transport and hosting

NGO sends data over Unity Transport. To connect players across the internet, you typically use Unity's Relay and Lobby services, or Multiplayer Services.

RelayNet sends data over [Keel](https://github.com/stasync/keel-dotnet)'s reliable UDP to a Relay server that you host. Your backend opens rooms through Relay's HTTP API and gives each client a connection token. There's no dependency on Unity's online services.

## Spawning and prefabs

In NGO, you add network prefabs to a `NetworkPrefabsList` that the `NetworkManager` references. In client-server mode, only the server or host spawns objects, by instantiating a prefab and calling `NetworkObject.Spawn`.

In RelayNet, any client creates objects with `CreateObject(prefabRef, ...)` and owns what it creates. Prefabs don't need to be listed by hand. An asset postprocessor adds every prefab with a `NetObjectIdentity` to `Assets/Resources/NetPrefabDatabase.asset`, and a prefab's id comes from its asset GUID.

## State updates and bandwidth

NGO sends `NetworkVariable` changes to clients. A client that joins late gets the current values before `OnNetworkSpawn` runs. Per-client visibility (`CheckObjectVisibility`, `NetworkShow`, `NetworkHide`) lets the server decide which clients receive which objects. That's how you limit traffic in large worlds.

RelayNet streams state. Ten times per second, each client sends the full state of every object it owns, unreliably unless a reliable change is pending. A client that joins late gets everything within one tick, and a remote object is removed if no data arrives for it for 6 seconds. There's no per-client filtering: every client receives every object. Traffic grows with the number of objects and clients, so RelayNet suits small rooms better than large worlds.

## RPCs

NGO RPCs are methods marked `[Rpc(SendTo.Target)]` with typed parameters. Code generation turns them into network messages. Older versions use `[ServerRpc]` and `[ClientRpc]`.

A RelayNet RPC handler is a `void Handler(byte[] data)` method marked `[AutoRegisterRpcHandler]`. You write and read the payload yourself with `NetWriter` and `NetReader`. Handlers are found by reflection, and there's no code generation step. There are three ways to send one:

- `OwnerToOthersRpc` runs on the owner and goes out with the object's next state update.
- `ToOwnerRpc` sends to the object's owner.
- `ToAllRpc` sends to every client that has the object.

RPC and behaviour ids come from type and method names. If you rename them, clients built before the rename can't play with clients built after it.

## Offline play and tools

You can play NGO alone by starting a host on the local machine. Unity's Multiplayer Tools package adds profiling and network simulation.

`NetSession.CreateOffline()` emulates the Relay server in-process, so offline play runs the same code as online play. RelayNet's **Debug Window** shows worlds, players and object states, and replays recorded history. The transfer statistics show the traffic. The `NETWORK_DEBUG` define adds serialization checks.

## Which one to choose

Choose Netcode for GameObjects if:

- you want an officially supported package with Unity's documentation, samples and community;
- your game needs a server-authoritative model, for example to resist cheating or to run client prediction against a server;
- you need per-client visibility to keep traffic down in large worlds;
- you plan to use Unity's online services.

Choose RelayNet if:

- you don't want to run game servers, and a self-hosted relay is enough;
- your rooms are small and every client can see every object;
- you want networked state kept apart from GameObjects, for headless logic, async views or replaying recent history;
- you're on Unity 2022.3 or later and are happy to depend on a young package.
