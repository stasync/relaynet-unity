# RelayNet

Networking for Unity. RelayNet keeps game objects, their state and RPCs in sync between the clients of a [Relay](https://github.com/stasync/relay-dotnet) room. It runs over [Keel](https://github.com/stasync/keel-dotnet)'s reliable UDP.

There's no game server and clients never connect to each other. Each client sends to the Relay room, and the room forwards the message to the other clients. Every networked object has one owner: the client that has authority over its state. The other clients receive that state and apply it.

Networked state lives in plain data classes owned by the world, apart from MonoBehaviours, and GameObjects are views of it. For how this and other choices differ from Unity's Netcode for GameObjects, see [RelayNet compared with Netcode for GameObjects](Documentation~/comparison-with-netcode-for-gameobjects.md).

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

## Sample

The **Capsules** sample is the quickest way to see RelayNet run. Import it from **Package Manager > RelayNet > Samples**, open the `Capsules` scene and press Play. You get a capsule that you move with WASD or the arrow keys.

The scene plays offline by default. To play with other clients, select the `Demo` object and set **Host**, **Port** and **Connection Token** to a Relay room, then start a second client the same way. Each client needs its own token from your backend. Every client sees the others' capsules move.

The sample reads the keyboard through the legacy Input Manager, so **Active Input Handling** must be set to **Input Manager** or **Both**.

## Connecting

A `NetSession` is one connection to a Relay room. Your backend opens the room and gives the client the room's address and a connection token (see Relay's [HTTP API guide](https://github.com/stasync/relay-dotnet/blob/main/docs/http-api-guide.md)). Pass both to the session:

```csharp
using System.Net;
using Keel.RelayNet;
using UnityEngine;

public sealed class SessionRunner : MonoBehaviour
{
    private NetSession _session;

    public void Connect(string host, ushort port, string connectionToken) =>
        _session = new NetSession(IPAddress.Parse(host), port, connectionToken);

    private void Update() =>
        _session?.Update(Time.deltaTime);

    private void OnDestroy() =>
        _session?.Dispose();
}
```

Call `Update` every frame. It sends and receives packets and steps the replicated world. `MainWorld` is that world: it holds the players (`NetWorldPlayer`) and the state of every networked object.

### Offline mode

`NetSession.CreateOffline()` creates a session that emulates the relay in-process. The local client is the only client in the room, so it's also the master client. Everything above the transport works the same as online, so the same game code runs in both modes.

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
        session.MainWorld.CreateObject(_prefab, isSceneObject: false, position, Quaternion.identity);
}
```

The client that creates an object owns it. To destroy an object, the owner calls `DestroyObjectViaNetwork()` instead of `Destroy`.

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

An RPC handler is a `void Handler(byte[] data)` method marked `[AutoRegisterRpcHandler]`. There are three ways to send one:

- `ToOwnerRpc` sends to the object's owner. On the owner, it runs locally.
- `OwnerToOthersRpc` runs on the owner and is sent to every other client.
- `ToAllRpc` sends to every client that has the object.

RPC and behaviour ids are derived from the full type names. A client built with renamed types can't play with one built before the rename.

The package ships ready-made behaviours in `Keel.RelayNet.Behaviours`:

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
