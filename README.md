# RelayNet

Networking for Unity. RelayNet keeps game objects, their state and RPCs in sync between the clients of a [Relay](https://github.com/stasync/relay-dotnet) room. It runs over [Keel](https://github.com/stasync/keel-dotnet)'s reliable UDP.

There's no game server and clients never connect to each other. Each client sends to the Relay room, and the room forwards the message to the other clients. Every networked object has one owner: the client that has authority over its state. The other clients receive that state and apply it.

Networked state lives in plain data classes owned by the world, apart from MonoBehaviours, and GameObjects are views of it. For how this and other choices differ from Unity's Netcode for GameObjects, see [RelayNet compared with Netcode for GameObjects](Documentation~/comparison-with-netcode-for-gameobjects.md). Known issues and planned work are in the [roadmap](Documentation~/roadmap.md).

## Requirements

- Unity 2022.3 or later.
- A Relay node to connect to. For single-player and for testing, use the offline mode instead (see [Offline mode](#offline-mode)).

## Installation

In the Package Manager, choose **Add package from git URL** and enter:

```
https://github.com/stasync/relaynet-unity.git#v0.1.0
```

Or add the line to `Packages/manifest.json`:

```json
"com.stasync.relaynet": "https://github.com/stasync/relaynet-unity.git#v0.1.0"
```

Keel ships inside the package, in `Plugins/Keel`. Don't add Keel's DLLs to the project a second time, or Unity reports duplicate assemblies.

The code is in the `Keel.RelayNet` namespace and its sub-namespaces. The package's assemblies are `Keel.RelayNet`, `Keel.RelayNet.Behaviours`, `Keel.RelayNet.Debug` and `Keel.RelayNet.Editor`.

## Quick start

1. **Make a networked prefab.** Add a `NetObjectIdentity` component to it, and a `NetObjectTransformSync` if its transform should be synced. The prefab is registered in `Assets/Resources/NetPrefabDatabase.asset` automatically.
2. **Run a session.** Create a `NetSession`, online or offline, and call `Update` every frame.
3. **Start the world once you've joined.** When `MainWorld.Players.LocalPlayer` exists, call `MainWorld.OnSceneLoaded()`. It creates the GameObjects for the world and registers the networked objects placed in the scene.
4. **Create your objects** with `MainWorld.CreateObject`.

This script does all of it. Put it on an object in your scene and set the prefab:

```csharp
using System.Net;
using Keel.RelayNet;
using Keel.RelayNet.GameObjects;
using UnityEngine;

public sealed class Game : MonoBehaviour
{
    [SerializeField] private NetPrefabObjectWeakRef _playerPrefab;
    [SerializeField] private string _host; // Leave empty to play offline.
    [SerializeField] private ushort _port;
    [SerializeField] private string _connectionToken;

    private NetSession _session;
    private NetPrefabDatabaseLoadRequest _prefabs;
    private bool _started;

    private void Start()
    {
        _prefabs = NetPrefabDatabaseLoadRequest.Create(); // Loads the prefab database in the background.
        _session = string.IsNullOrEmpty(_host)
            ? NetSession.CreateOffline()
            : new NetSession(IPAddress.Parse(_host), _port, _connectionToken);
    }

    private void Update()
    {
        _session.Update(Time.deltaTime);

        var world = _session.MainWorld;
        if (_started || world?.Players.LocalPlayer == null || _prefabs is { IsDone: false })
            return;

        world.OnSceneLoaded();
        world.CreateObject(_playerPrefab, shared: false, Vector3.zero, Quaternion.identity);
        _started = true;
    }

    private void OnDestroy() =>
        _session?.Dispose();
}
```

To network your own state, write a `NetObjectStateData` class and a `NetObjectBehaviour` that uses it (see [Networked behaviours](#networked-behaviours)).

### Main APIs

| Type | What it's for |
|---|---|
| `NetSession` | One connection to a Relay room. `new NetSession(address, port, token)` or `NetSession.CreateOffline()`, then `Update(deltaTime)` every frame and `Dispose()` at the end. `MainWorld` is its world. Also `IsConnected`, `Statistics` and the `Disposed` event. |
| `NetWorldRuntime` | The live world, `session.MainWorld`. `OnSceneLoaded()`, `CreateObject(prefab, shared, position, rotation)`, `CreateObjectAsync(...)`, `ObjectStates`, `GetObject(uid)`, `Players`, `Properties` and `View`. Events: `PlayerAdded`, `PlayerRemoved`, `PlayerBecomesReady`, `ObjectCreated`, `ObjectRemoved`, `Updated`, `Disposed`. |
| `NetWorldPlayersCollection` | `world.Players`: `LocalPlayer`, `MasterPlayer`, `All`, `Get(uid)`, `Contains(uid)`. |
| `NetWorldPlayer` | A client in the room: `Uid`, `Name`, `IsLocal()`, `IsMaster()`, `IsReady`, `Platform`, `RttMs` and `Properties`. |
| `NetProperties` | Key-value properties of the world, written by the master, or of a player, written by that player: `SetInt` and `GetInt`, `SetFloat`, `SetBool`, `SetString`, `SetVector` and more. |
| `NetObjectIdentity` | The component that makes a GameObject networked: `IsLocal`, `IsShared`, `Owner`, `InstanceId`, `State`, `OwnObject()`, `DestroyViaNetwork()`, `GetBehaviour<T>()` and the RPC methods. |
| `NetObjectBehaviour<TData>` | Base class for networked behaviours: `Data`, `IsLocal`, `Owner`, `Identity`, the RPC methods, `DestroyObjectViaNetwork()` and the lifecycle methods. |
| `NetObjectState` | An object's state in the world, `identity.State` or `world.GetObject(uid)`: `Uid`, `Owner`, `IsOwnedByLocalPlayer`, `IsShared`, `DataCollection` and `RequestRemoval()`. |
| `NetObjectDataCollection` | An object's data: `GetOrCreateData<T>()` and `RemoveData<T>()` on the owner, `TryGetData<T>()`, `GetData<T>()` and `HasData<T>()` everywhere. |
| `NetObjectStateData` | Base class for networked data. Its fields are `NetworkValue<T>`, `NetworkString`, `NetworkList<T>`, `NetworkHashSet<T>` or `NetworkDictionary<TKey, TValue>`. |
| `NetObjectSceneIdentity` | Marks a networked object that's placed in a scene (see [Scene objects](#scene-objects)). |
| `NetPrefabObjectWeakRef` | A serialized reference to a network prefab, for `CreateObject`. |

## Sample

The **Capsules** sample shows the whole flow in a few small scripts. Import it from **Package Manager > RelayNet > Samples**, open the `Capsules` scene and press Play:

- You get a capsule in a random color, which you move with WASD or the arrow keys. The color is a `NetworkValue<Color>` in `CapsuleData`, picked by the owner when the capsule spawns, and every client paints the capsule with it.
- A bar spins in the middle. It's placed in the scene with a `NetObjectSceneIdentity`, so the master owns it and spins it, and the others see it turn. When the master leaves, another client takes it over.

The scene plays offline by default. To play with other clients, select the `Demo` object and set **Host**, **Port** and **Connection Token** to a Relay room, then start a second client the same way. Each client needs its own token from your backend. Every client sees the others' capsules move.

The sample reads the keyboard through the legacy Input Manager, so **Active Input Handling** must be set to **Input Manager** or **Both**.

## Connecting

A `NetSession` is one connection to a Relay room. Your backend opens the room and gives the client the room's address and a connection token (see Relay's [HTTP API guide](https://github.com/stasync/relay-dotnet/blob/main/docs/http-api-guide.md)). Pass both to `new NetSession(address, port, token)`.

Call `Update` every frame. It sends and receives packets and steps the world. Call `Dispose` when you leave the room.

### Offline mode

`NetSession.CreateOffline()` creates a session that emulates the relay in-process. The local client is the only client in the room, so it's also the master client. Everything above the transport works the same as online, so the same game code runs in both modes.

## Worlds

A world is the shared state of a room: the players, the networked objects and their data, and world-wide properties. `NetWorldRuntime` is the live world of a session, `session.MainWorld`. Ten times per second, it sends the state of the objects you own and applies the updates the other clients sent.

A world runs without GameObjects. Its view, `NetWorldView`, turns the state into GameObjects. `OnSceneLoaded()` creates the view in a scene of its own. From then on, the view creates, updates and destroys a GameObject for every object state. Views of remote objects more than 50 meters from the main camera are destroyed while they're out of range, and created again when they come back, while their state keeps updating. `world.View.SetDrawDistance(meters)` changes that distance.

Each world records the last 10 seconds of its state. `Replicate()` creates a `NetWorldReplica`, a separate world that replays that history with its own view. The Debug Window's playback uses it, and so can a kill cam. `NetWorld.All` lists every world, live or replayed.

Worlds also keep their physics apart. The live world's view uses Unity's default physics scene, like the rest of your game. A replay's view gets a physics scene of its own (`LocalPhysicsMode.Physics3D`), so replayed objects never collide with the live game or show up in its raycasts. Unity doesn't simulate a separate physics scene by itself. If a replay needs physics, step it with `scene.GetPhysicsScene().Simulate(deltaTime)`.

## Networked objects

Add a `NetObjectIdentity` component to a prefab to make it networked. In the editor, every prefab with a `NetObjectIdentity` is added to `Assets/Resources/NetPrefabDatabase.asset` automatically. Clients find prefabs by their entry in that database, so all clients must be built with the same one.

To spawn an object, reference the prefab with a `NetPrefabObjectWeakRef` field and create it through the world:

```csharp
using Keel.RelayNet;
using Keel.RelayNet.GameObjects;
using UnityEngine;

public sealed class Spawner : MonoBehaviour
{
    [SerializeField] private NetPrefabObjectWeakRef _prefab;

    public NetObjectIdentity Spawn(NetSession session, Vector3 position) =>
        session.MainWorld.CreateObject(_prefab, shared: false, position, Quaternion.identity);
}
```

The client that creates an object owns it. The `shared` argument decides what happens to the ownership later:

- **`shared: false`**: the object belongs to its creator. Its owner never changes, and it's removed when the creator leaves. Use it for things like a player's character.
- **`shared: true`**: the object belongs to the room. When its owner leaves, the master takes it over. Any client can take it with `OwnObject()`. Every 5 seconds, the world also spreads shared objects among the clients; to keep an object where it is, add a rule derived from `NetObjectOwnershipDistributionRule`.

### Scene objects

An object that you place in a scene, instead of creating it at runtime, needs a `NetObjectSceneIdentity` next to its `NetObjectIdentity`, and must be an instance of a network prefab. Every client loads the same scene, so every client has its own copy. `OnSceneLoaded()` collects them. The master registers each one in the world and owns it. The other clients bind their copy to that state when it arrives. Scene objects are always shared.

Each scene object needs a scene id that's unique among the scenes loaded at the same time. **Tools > RelayNet > Fix scene objects** assigns them. It keeps every valid, unique id, and gives each new or duplicated object the lowest free id, in a stable order, so running it again gives the same result. Run it after you place or duplicate scene objects, with all the scenes that load together open. **Validate scene objects** only reports problems. Scenes loaded together can hold about 1000 scene objects.

### Removing objects

Only the owner removes an object. Call `DestroyViaNetwork()` on its `NetObjectIdentity`, `DestroyObjectViaNetwork()` from one of its behaviours, or `State.RequestRemoval()` when you only have the state. `DestroyViaNetwork()` also removes the networked objects nested under it, unless you pass `considerChildren: false`.

Removal takes two world updates. In the first, the owner's behaviours get `OnSoftDestroyRequestedLocally()`, and the other clients learn that the object is going. In the second, the state is removed on every client, and its GameObjects are destroyed.

Don't destroy a networked GameObject with `Destroy`. The GameObject is only a view of the state: as long as the state exists, the view creates the GameObject again, a few frames later. To get rid of an object, remove its state.

Objects also go away without a removal. An object created with `shared: false` is removed when its owner leaves, and a remote object is removed if no update arrives for it for 6 seconds.

## Networked behaviours

A networked behaviour derives from `NetObjectBehaviour<TData>`. `TData` is a `NetObjectStateData` class whose `NetworkValue<T>` fields are synced to every client. Only the owner (`IsLocal`) may write them:

```csharp
using Keel.RelayNet;
using Keel.RelayNet.GameObjects;
using Keel.RelayNet.StateManagement;
using Keel.RelayNet.StateManagement.Values;

public sealed class HealthData : NetObjectStateData
{
    public readonly NetworkValue<float> Health = new();
}

public sealed class Health : NetObjectBehaviour<HealthData>
{
    public float Value => Data.Health.Value;

    public void ApplyDamage(float damage)
    {
        if (IsLocal)
            Data.Health.Value -= damage;
        else
            ToOwnerRpc(ApplyDamageRequest, data: System.BitConverter.GetBytes(damage), DeliveryMethod.Reliable);
    }

    [AutoRegisterRpcHandler]
    private void ApplyDamageRequest(byte[] data) =>
        Data.Health.Value -= System.BitConverter.ToSingle(data, startIndex: 0);
}
```

Besides `NetworkValue<T>`, state data can hold `NetworkString`, `NetworkList<T>`, `NetworkHashSet<T>` and `NetworkDictionary<TKey, TValue>`.

A behaviour can override these methods:

- `OnPostInitializeOnce()` runs once per object, on the client that creates it. Set the initial data here.
- `OnPostInitialize()` runs on every client when the object's GameObject gets its state, including when a view is created again.
- `OnAuthorityUpdate()` runs on the owner right before each state update is sent.
- `OnOwnerChanged()` runs when the owner changes.
- `OnDisconnected(clientUid)` runs when another client leaves.
- `OnSoftDestroyRequestedLocally()` runs on the owner when the object starts being removed.

### Data without a behaviour

You don't have to derive from `NetObjectBehaviour<TData>` to network data. That class is a shortcut: when the object spawns, it adds its `TData` to the object on the owner, and exposes it as `Data`. Every object has a data collection, `State.DataCollection`, and the owner can add and remove data there at any time:

```csharp
var dataCollection = identity.State.DataCollection; // or world.GetObject(uid).DataCollection

// On the owner: add the data, or get it if it's already there, and change it.
dataCollection.GetOrCreateData<HealthData>().Health.Value = 100f;

// On the owner: remove it.
dataCollection.RemoveData<HealthData>();

// On any client: read it, if the owner added it.
if (dataCollection.TryGetData<HealthData>(out var health))
    Debug.Log(health.Health.Value);
```

Added and removed data reaches the other clients with the object's next state update. `GetOrCreateData` and `RemoveData` throw if the local client doesn't own the object. Reading works on every client. An object holds at most one instance of each data type.

### RPCs

An RPC handler is a `void Handler(byte[] data)` method marked `[AutoRegisterRpcHandler]`. There are three ways to send one:

- `ToOwnerRpc` sends to the object's owner. On the owner, it runs locally.
- `OwnerToOthersRpc` runs on the owner, and goes to every other client with the object's next state update.
- `ToAllRpc` sends to every client that has the object.

RPC and behaviour ids are derived from the full type names. A client built with renamed types can't play with one built before the rename.

### Ready-made behaviours

The package ships these behaviours in `Keel.RelayNet.Behaviours`:

- `NetObjectTransformSync` syncs the transform.
- `NetObjectAnimatorSync` syncs Animator parameters.
- `NetObjectLifetime` is a synced countdown that can destroy its object.
- `NetObjectTransactionBehaviour` locks an object while one client interacts with it.

## Debugging

- **Tools > RelayNet > Debug Window** shows the worlds, players and object states while playing, and can replay a world's recorded history.
- **Tools > RelayNet > Enable debug** adds the `NETWORK_DEBUG` define, and **Disable debug** removes it. With the define, every behaviour's serialized data carries a type hash that the receiver checks, and RPCs for missing objects are logged with their handler names. The hash changes the wire format, so all clients must be built with the same setting.
- In play mode in the editor, every networked object that syncs its transform gets a marker in the owner's color, plus its axes.

## Updating Keel

`Plugins/Keel` holds Keel's netstandard2.1 build. The version is recorded in [CHANGELOG.md](CHANGELOG.md). To replace it, run:

```sh
sh "Tools~/update-keel.sh" <release-tag>
```

## License

MIT, see [LICENSE.md](LICENSE.md). Keel is MIT too; its license is in `Plugins/Keel/LICENSE.md`.
