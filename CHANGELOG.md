# Changelog

## Unreleased

### Removed

- The persistent half of the RPC buffer. Nothing could fill it since buffered RPCs were removed, so it was always empty. This removes `NetObjectIdentity.RemoveRpcFromBuffer`, `NetObjectRpcBuffer.PersistentBuffer`, `NetObjectRpcBufferSnapshot.PersistentBuffer` and the `RpcBufferChangeMethod` enum. `OwnerToOthersRpc` works as before.
- Leftover debug logs: an error on every ownership change, and a log on every connect and disconnect.

### Changed

- State updates no longer carry the empty persistent RPC list. This changes the wire format: clients on 0.1.0 can't play with clients built from this version.

## 0.1.0 - 2026-10-18

First release as a Unity package.

- Transport: Keel 1.0.0.0, `latest-build` of 2026-10-09, netstandard2.1.
  - `Keel.Networking.dll` SHA-256 `cb3534ba57fe72be1f779a4fd7caa4abc7b1c7b6da60dcf906f26d05732b0ff3`
  - `Keel.Utils.dll` SHA-256 `a35393a13fb7de3fe092b93eac0f31787eb3a0c03c8a75e374c80580d1402463`
- Renamed from `LROTech.Network`. Namespaces and assemblies are now `Keel.RelayNet`, `Keel.RelayNet.Behaviours`, `Keel.RelayNet.Debug` and `Keel.RelayNet.Editor`, and the menus are under **Tools > RelayNet**. RPC and behaviour ids come from type names, so clients from before the rename can't play with clients after it.
- `NetWorldPlayer.Platform` is a `DevicePlatform`. The values are the same as before, so the wire format doesn't change.
- The world view debug overlay draws with `Debug.DrawLine`, so it only shows in the editor.
- A **Capsules** sample: each client spawns a capsule and moves it with WASD, online through a Relay room or offline.
