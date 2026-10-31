# Roadmap and known issues

This page collects RelayNet's known issues and planned work, to guide future development. It describes the state of version 0.3.0. When an item is done, remove it here and note it in [CHANGELOG.md](../CHANGELOG.md).

The tracks below are ordered by priority. Within each track, items name the code they concern, so you can start from there.

## 1. Zero GC allocations

**Goal:** after warm-up, a running session allocates no managed memory per tick, per message or per RPC. Today several things allocate on every tick:

- **Per-tick collections.** `NetWorldRuntime` creates four new dictionaries and queues at the start of every world update (marked `// TODO: Avoid allocations.`). It also copies key collections with `new List<>(… .Keys)` so it can iterate while changing them, in nine places.
- **A new array per message.** Every outgoing message is copied out with `_writer.ToArray()` before `DataSendRequested`. Every incoming payload is read into a new array with `ReadBytesAndSize()`, in `RelaySession` and in the snapshot readers.
- **Snapshots per object.** Each tick, every object the client owns is turned into a `NetObjectStateSnapshot`, with a new list for its RPC buffer and a new array for its data. Each player becomes a `NetWorldPlayerSnapshot`, with a new list of tag objects.
- **History.** The runtime world stores a full `NetWorldSnapshot` every tick and keeps 10 seconds of them, so about 100 snapshots are alive at any time.
- **RPC calls.** Passing a method group as `Action<byte[]>` creates a new delegate on every call, and every payload is a new `byte[]`.

**Direction:**

- Pass pooled buffers, or `ArraySegment<byte>`, from the writer to the transport, instead of copying.
- Serialize state straight from the object into the writer, without an intermediate snapshot object.
- Reuse the lists used for iteration, and keep history in a preallocated ring buffer.
- Add tests that fail when a tick allocates. Unity Test Framework can check this with `Is.Not.AllocatingGCMemory()`.

## 2. Unmanaged memory and collections

**Today:** all state lives in managed objects. `NetObjectState` holds a `NetObjectDataCollection` of `NetObjectStateData` classes. Every networked field is its own `NetworkValue<T>` object, found through a `Dictionary<FieldInfo, NetworkValueBase>`.

**Direction:**

- Keep each object's state in an unmanaged block (a `NativeArray`, `UnsafeList` or similar from `com.unity.collections`), with each value at a fixed offset.
- Serialize blittable data by copying memory. `MemCpySerializer` already does this for single unmanaged values.
- Snapshots and history then become block copies, and state updates can run in Jobs with Burst.
- Keel's `NetWriter` and `NetReader` work on managed `byte[]`. They need span-based or unmanaged variants in Keel.

## 3. Code generation instead of reflection

RelayNet finds most of its wiring by reflection at runtime:

- **RPC handlers.** `NetObjectRpcUtils.GetAutoRegisterRpcHandlers` scans each behaviour type with `GetMethods` and `GetCustomAttribute<AutoRegisterRpcHandler>`. `NetObjectIdentity` creates a delegate per handler with `CreateDelegate` every time an object spawns.
- **RPC ids.** `NetObjectRpcUtils.ActionToHash` hashes the type and method names at runtime, and checks each call's `MethodInfo` against a cache.
- **Networked fields.** `NetworkValueBase.GetFields` finds the `NetworkValue` fields of each data type with `GetFields`.
- **Type registries.** State data types, value serializers (`VariableSerializer`) and ownership rules (`NetObjectOwnershipDistributionRule`) are found by `Types.Lookup`, which scans every loaded assembly, and are created with `Activator.CreateInstance`.
- **Behaviour checks.** With `NETWORK_DEBUG`, behaviour type hashes come from `Types.TryGetTypeStableHashCode`.

This costs startup time and allocations, and it's fragile under IL2CPP code stripping. It's also behind several wire-format problems:

- RPC and behaviour ids come from type and method names, so renaming one breaks compatibility with older clients.
- A state data type is sent as its one-byte index in the `Types.Lookup` scan (`NetObjectStateData.GetTypeIndex`). Clients built with different sets of data types can disagree on the indexes, and there's room for only 256 types.

**Direction:** a Roslyn source generator, which Unity supports as an analyzer DLL. It would generate:

- RPC registration with constant ids, and typed RPC send methods, so you call an RPC with typed parameters instead of writing a `byte[]`;
- per-data-class `Serialize`, `Deserialize` and field tables, with no `FieldInfo` lookups;
- an explicit registry of data types, serializers and rules, with stable ids that you can set with an attribute, so renames stop breaking the wire format.

Generated code is easy to read and debug, which is why source generators are preferred over IL post-processing here.

## 4. Bandwidth

- **Unchanged objects are sent anyway.** Ten times per second, each owner sends the full state of every object it owns, whether it changed or not. Send only objects and values that changed, as deltas against the last state the receivers acknowledged.
- **No quantization or compression.** Positions are three full floats and rotations are full quaternions. Add quantized types and optional compression.
- **No interest management.** `RelaySession.SendData` always sends to everyone (receiver count `0`). Relay can already route a packet to a list of receivers, so per-client relevance can be built on that.
- **Fixed rates.** The world tick (`NetWorldRuntime.MIN_UPDATE_INTERVAL_S`, 10 Hz) and the transport tick (`RelaySession.Update`, 20 Hz) are hard-coded. Make them configurable.

## 5. Correctness and robustness

- **Senders aren't checked.** `NetSession` passes incoming world updates on without the sender's id, so a client can overwrite the state of objects it doesn't own. Validate every update against the object's owner.
- **Networking follows `Time.timeScale`.** The session only advances by the `deltaTime` passed to `NetSession.Update`. The README and the sample pass `Time.deltaTime`, so pausing the game with `Time.timeScale = 0` stops ticks and heartbeats, and a long pause can drop the connection. Track real time inside the session.
- **Remote objects time out.** A remote object is removed after 6 seconds without data (`AUTO_REMOVE_REMOTE_OBJECT_TIME_S`), which also happens during long hitches or while a client is in the background.
- **Connection errors aren't exposed.** When the connection fails, is rejected or loses reliability, `RelaySession` logs an error and disposes the session. `NetSession` only raises `Disposed`, with no reason and no way to reconnect.
- **No protocol version check.** Clients with incompatible wire formats, for example one built with `NETWORK_DEBUG` and one without, can join the same room and misread each other's data. Send a protocol version when joining.
- **A spurious error on teardown.** `NetWorld.Dispose` logs "View already destroyed" whenever the world has no view. That happens if the session never got as far as creating one, or if its scene was unloaded first.
- **Errors for normal situations.** `NetObjectState.ChangeObjectRemotely` and `NetObjectIdentity` log errors for cases their own messages call normal. `NetObjectIdentity` has a TODO to handle data that arrives during an ownership change.
- **Clock estimate.** The world time estimate doesn't account for the relay round trip (TODO in `NetWorldRuntime`).
- **Animator sync is incomplete.** Per-layer sync flags and remote layer-weight smoothing in `NetObjectAnimatorSync` are commented out.

## 6. Replays

`NetWorldReplica` (`NetWorldRuntime.Replicate()`) replays the runtime world's last 10 seconds in a separate world, with its own view and physics scene. It can show a kill cam or an instant replay, or step back through a bug you just saw. Keep the feature, and grow it into a more complete replay API:

- **Controls.** The API is index-based (`MoveToSnapshot(int)`, `FindLastSnapshotIndexAndPeekNext`, `GetRemainTimeFromSnapshot`) and stops or disposes itself at the end (`autoDestroy`). Add time-based controls: `Duration`, `Time`, `Seek(time)`, `Play`, `Pause`, `Speed`, looping, and an event when it ends.
- **Smooth playback.** The replay jumps from snapshot to snapshot at 10 Hz, and only behaviours that interpolate on their own look smooth. Interpolate between snapshots in the replay itself.
- **Recording.** History is always on, always 10 seconds long, and stores a full snapshot per tick, about 100 snapshots alive at a time (see Zero GC allocations). `Replicate()` copies all of it. Make recording optional, with a configurable length, store it compactly (deltas, a ring buffer), and let a replay read the recording without copying.
- **Saved replays.** Snapshots are already serializable. Add saving a recording to a file and loading it, for match replays and bug reports.
- **Replay mode for behaviours.** Replayed objects run their behaviours as remote objects. Behaviours that talk to the session or send RPCs need to know they're in a replay; `NetWorld.IsReplica` exists, but nothing tells behaviours about it. Add a clear signal, such as a property on the behaviour or a callback.
- **Physics.** The replay's physics scene is never simulated, so physics-driven visuals, such as ragdolls, don't move. Add an option to step it.

## 7. Debug tools

- `NetWorldViewDebug` creates itself in every build, release builds included, and starts enabled.
- Its toggle only works when Left Shift and V go down in the same frame.
- It reads the keyboard with the legacy `Input` class, which throws when a project uses only the Input System package. The Capsules sample has the same dependency.
- Small TODOs: `NetDebugWindow` keeps all its drawers in one file, and `NetObjectRpcUtils` detects anonymous delegates by their name.

## 8. Platforms, packaging and tests

- **Untested platforms.** RelayNet has only been tested in the editor on Unity 2022.3, with Mono. Test Unity 6, IL2CPP and code stripping. `[RequireDerived]` and `[RequireAttributeUsages]` already protect some reflection targets from stripping.
- **Deprecated API.** `ScriptingDefineSymbols` uses the `BuildTargetGroup` overloads of `PlayerSettings`, which newer Unity versions deprecate in favour of `NamedBuildTarget`.
- **Keel packaging.** Keel is vendored as its `latest-build` pre-release, and a project that already contains Keel gets duplicate assemblies. Pin a stable Keel release, and publish Keel as its own UPM package, for example on OpenUPM.
- **Resources folder.** `NetPrefabDatabase` loads from `Resources`. Add an Addressables option.
- **No tests in the package.** CI only creates version tags. Add EditMode and PlayMode tests (the Capsules play-mode check is a start), and a CI job that compiles and tests the package, for example with GameCI.
- **One offline client.** The offline transport emulates a room with a single client. A shared in-process relay would let tests and the editor run several sessions against each other.
