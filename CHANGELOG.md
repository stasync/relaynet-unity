# Changelog

## Unreleased

### Removed

- The persistent half of the RPC buffer. Nothing could fill it since buffered RPCs were removed, so it was always empty. This removes `NetObjectIdentity.RemoveRpcFromBuffer`, `NetObjectRpcBuffer.PersistentBuffer`, `NetObjectRpcBufferSnapshot.PersistentBuffer` and the `RpcBufferChangeMethod` enum. `OwnerToOthersRpc` works as before.
- Leftover debug logs: an error on every ownership change, and a log on every connect and disconnect.

### Changed

- State updates no longer carry the empty persistent RPC list. This changes the wire format: clients on 0.1.0 can't play with clients built from this version.
- The `isSceneObject` parameter of `CreateObject` and `CreateObjectAsync` is now `shared`, and `IsSceneObject` is now `IsShared` on `NetObjectState`, `NetObjectIdentity` and `NetObjectStateSnapshot`. The meaning is unchanged: a shared object stays when its owner leaves and its ownership can move; an object that isn't shared belongs to its creator. The wire format is unchanged too.
- `NetWorldEvaluator` is now `NetWorldReplica`, to match `NetWorldRuntime.Replicate()` and `NetWorld.IsReplica`. It works as before.
- **Tools > RelayNet > Fix scene objects** assigns ids deterministically. It keeps valid, unique ids, and gives every new or duplicated object the lowest free id in a stable order, instead of a random one. It now also fixes invalid ids, includes inactive objects, and marks the changed scenes dirty.
- `[FormerlySerializedAs]` attributes were removed from `NetObjectLifetime`, `NetObjectTransformSync` and `NetObjectAnimatorSync`. Assets still saved with the old `m_` field names lose those values: open and save them, or reserialize them, before you upgrade.

### Fixed

- Automatic ownership distribution no longer moves objects that aren't shared. Before, the client that won such an object threw an exception from the world update.
- A prefab with a `NetObjectSceneIdentity` that was created from code could bake the prefab id `32` (the hash of an empty GUID) before it was saved. Unsaved objects are now left alone.
- `WriteColor32` wrote four floats, while `ReadColor32` reads four bytes, so everything read after a `Color32` was garbage. It now writes four bytes. This changes the wire format of any data that holds a `Color32`.
- `Equals(object)` on `NetObjectUid`, `NetWorldId` and `NetPrefabObjectWeakRef` returned `false` even for equal values.
- The prefab database could drop a deleted prefab without marking itself dirty, so the change wasn't saved, and deleting several network prefabs at once could remove the wrong path from its list of pending deletions.

### Samples

- Capsules: the capsule's color is networked through a `NetObjectStateData` class, `CapsuleData`. `CapsuleMover` became `Capsule`, a `NetObjectBehaviour<CapsuleData>`.
- Capsules: a bar placed in the scene with a `NetObjectSceneIdentity` spins on its owner, the master at first.

## 0.1.0 - 2026-10-18

First release as a Unity package.

- Transport: Keel 1.0.0.0, `latest-build` of 2026-10-09, netstandard2.1.
  - `Keel.Networking.dll` SHA-256 `cb3534ba57fe72be1f779a4fd7caa4abc7b1c7b6da60dcf906f26d05732b0ff3`
  - `Keel.Utils.dll` SHA-256 `a35393a13fb7de3fe092b93eac0f31787eb3a0c03c8a75e374c80580d1402463`
- Renamed from `LROTech.Network`. Namespaces and assemblies are now `Keel.RelayNet`, `Keel.RelayNet.Behaviours`, `Keel.RelayNet.Debug` and `Keel.RelayNet.Editor`, and the menus are under **Tools > RelayNet**. RPC and behaviour ids come from type names, so clients from before the rename can't play with clients after it.
- `NetWorldPlayer.Platform` is a `DevicePlatform`. The values are the same as before, so the wire format doesn't change.
- The world view debug overlay draws with `Debug.DrawLine`, so it only shows in the editor.
- A **Capsules** sample: each client spawns a capsule and moves it with WASD, online through a Relay room or offline.
