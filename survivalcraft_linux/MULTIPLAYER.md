# LAN Multiplayer

Survivalcraft 2 Linux can host a world on the local network and let up to eight
players join it. The host keeps simulating a completely normal single-player
world; every other machine is a *replica* that mirrors the state it is sent.

This document explains how to use it, how it works, and how to hack on it.

---

## Playing

Both machines must run the same build (`MultiplayerProtocol.Version` has to
match) and be on the same LAN.

### Host

1. Load a world normally (`Play` -> select a world -> `Play!`).
2. Open the in-game menu (the **More** button at the bottom of the HUD).
3. Choose **Open World To LAN**. The menu bar now reads
   `Multiplayer: Hosting on port 27020 (n/8 players)`.

The host keeps playing exactly as in single player. Client avatars are ordinary
entities in the host's world, driven by the input the host receives.

### Join

1. On the `Existing Worlds` screen press **Join LAN**.
2. Pick the world from the list (it refreshes every 1.5 s), type a name, press
   **Join**.
3. The client downloads a full world snapshot, imports it as a normal world and
   loads it, then starts mirroring the host.

To leave, quit the world as usual. The imported world is a cache and is deleted
automatically.

### Network ports

| Port | Protocol | Purpose |
|------|----------|---------|
| 27020 | TCP | Game traffic (framed, reliable, ordered) |
| 27021 | UDP | LAN discovery (broadcast query / unicast reply) |

`27020/tcp` must be reachable on the host for LAN play; `27021/udp` only makes
the host show up in the browser. A host can always be joined by typing its
address, but the current UI only lists discovered games, so open the UDP port or
add the address manually in code while debugging.

Discovery is deliberately boring: the client broadcasts `SC2MP_DISCOVER_V1` to
the broadcast address of every IPv4 interface (plus `255.255.255.255` and
loopback), and every host answers with a single UTF-8 line:

```
SC2MP_HOST_V1|<world>|<host>|<tcpPort>|<players>|<maxPlayers>|<protocolVersion>
```

Entries that have not been seen for 5 s are dropped.

---

## Architecture

```
             input (30 Hz)                  world deltas (20 Hz)
  client  ────────────────────►  host  ─────────────────────────►  client
          cell edits, inventory           snapshot, entities, cells,
          edits, join/leave               player state, time, inventories
```

**The host is authoritative.** It runs the only real simulation: physics, AI,
creatures, damage, spawning, weather, block behaviour, crafting progress,
inventories, needs, time. Clients never simulate the world.

A client sends:

* its `PlayerInput` (movement, look, dig/aim, item use, ...);
* block edits it performed locally (see below);
* block-entity inventory edits made in the client's container screens;
* its own inventory after the local inventory UI changed something.

...and applies everything the host sends. Its only local prediction is mouse
look, so the camera does not lag behind the mouse.

### Freezing the client simulation

`MultiplayerManager.SkipUpdate(IUpdateable)` is called for every updateable by
`SubsystemUpdate`. On a client it skips everything except a small allowlist:

*Subsystems that keep running:*

`SubsystemTerrain`, `SubsystemGameInfo`, `SubsystemGameWidgets`,
`SubsystemPlayers`, `SubsystemAudio`, `SubsystemSky`, `SubsystemSeasons`,
`SubsystemAnimatedTextures`, `SubsystemAmbientSounds`, `SubsystemParticles`.

*Components that keep running:*

`ComponentInput`, `ComponentGui`, `ComponentScreenOverlays`,
`ComponentAimingSights`, `ComponentBlockHighlight`, `ComponentCreatureModel`,
`ComponentFirstPersonModel`, `ComponentVrHandsModel`.

Everything else — bodies, creatures, AI, inventories, health, needs, block
behaviour, weather, spawning — is authored by the host. The allowlist is
`is`-based on purpose: a new simulation system is skipped by default instead of
silently running on clients.

Clients also skip `ComponentPlayer.Update` and `PlayerData.Update` entirely, and
`PlayerData.IsRemote` is set for every player except the local one, which makes
the normal spawn dialog / camera / GUI updates stay out of the way.

### Remote players on the host

A joining client gets a real `PlayerData` (`IsRemote = true`) and a real player
entity on the host. Its input arrives over the network and is applied to that
entity's `ComponentInput` right before `ComponentPlayer.Update`:

* `Look` is a per-frame **rate**, so it must not be re-applied: the host zeroes
  the consumed parts with `ComponentInput.ConsumePlayerInput()` after the player
  updated.
* Look samples are summed between network ticks, one-shot actions are preserved,
  and `Dig`/`Aim` are treated as held states (newest sample wins).
* Input that already ran is cleared, so a packet that arrives twice cannot dig
  twice.

Remote avatars are **not** written to normal host saves; they are included only
while a snapshot is generated for a joining client
(`MultiplayerManager.IncludeRemotePlayersInSave`). Otherwise avatars of players
that are no longer connected would accumulate in the save file.

Each remote player also gets a `GameWidget` because `ComponentGui.Load` requires
one, but the widget is detached from `GamesWidget.Children`, so it is never
drawn, never receives input and never triggers split-screen.

Terrain is generated around cameras. Remote players have no camera being drawn,
so the host explicitly tells `TerrainUpdater` to keep the area around every
remote player loaded.

### Entity identity

Entities need an identity that survives the snapshot/delta boundary.
`EntityNetRegistry` (in the EntitySystem assembly) assigns stable integer ids and
writes them into the saved entity data as `MultiplayerNetId` while hosting.
`Project.SaveEntities` honours `EntityNetRegistry.SaveFilter` so a snapshot can
omit or include entities selectively.

The client recreates entities from `EntityAdd` messages (XML plus net id) and
matches later `EntityUpdate`/`EntityRemove` messages by that id.

### World snapshot

When a client joins, the host:

1. creates its `PlayerData` and player entity;
2. calls `GameManager.SaveProject(wait: true)` — this blocks the game thread, so
   the host briefly hitches;
3. exports the world directory with `WorldsManager.ExportWorld` into a zip in
   memory;
4. sends `Welcome` (protocol version, assigned player index, name, world name)
   followed by `Snapshot` (the zip).

The client imports the zip with `WorldsManager.ImportWorld`, then loads the world
through the normal `GameLoadingScreen`. `GameManager.LoadProject` calls
`MultiplayerManager.OnProjectLoaded`, which is where the live client session is
attached. It then sends `Ready`.

Cell edits that happened while the snapshot was being produced are queued per
client and flushed when it becomes ready, so no edit is lost.

### Live deltas

| Data | Direction | Rate | Message |
|------|-----------|------|---------|
| Player input | client -> host | 30 Hz | `Input` |
| Entity transforms / health / look | host -> client | 20 Hz | `EntityUpdate` |
| Entity creation / removal | host -> client | on change | `EntityAdd` / `EntityRemove` |
| Block edits | both ways | per frame, batched | `CellChange` |
| Player list, name, level, health, needs, inventory | host -> client | 5 Hz | `PlayerState` |
| Player list changes | host -> client | on change | `PlayerJoined` |
| World time / weather state | host -> client | 2 s | `TimeSync` |
| Block-entity inventories | both ways | 1 Hz diff | `BlockInventory` |
| Local player's inventory edits | client -> host | on change, max 20 Hz | `PlayerInventory` |
| Join / leave notices | host -> client | on change | `ChatMessage` |
| Disconnect reason | host -> client | on change | `Disconnect` |

`PlayerState` is also the reconciliation point: a client creates the mirror of a
player it has never seen and drops mirrors the host no longer reports. That is
what makes players joining *after* the snapshot work.

Entity adds are applied to the client's `Project`; the host polls
`Project.Entities` 20 times per second to detect additions and removals and only
streams entities near the client (players always, everything else within 384
blocks). Entities that were already in the snapshot are not sent again: the host
records the snapshot's net ids per client when it saves the world.

### Inventories

There are three distinct inventory paths:

1. **The local player's inventory** lives on both machines. The client's
   inventory UI is rendered locally, so `PlayerInventory` forwards the resulting
   slots to the host, which applies them to the host-side avatar. Host-side
   changes travel back in `PlayerState`. Last writer wins if both edit at once.
2. **Block-entity inventories** (chests, furnaces, dispensers, ...) are polled
   every second on both machines. Only changed inventories are sent. The host
   relays an edit it received from one client to all other clients, so a chest
   opened by two players stays consistent. A client records what it applied so
   host state is not echoed back.
3. **Crafting / machine progress** is host-side only and is not mirrored; see
   limitations.

---

## Wire format

Every frame on the TCP stream is:

```
+----------------+--------------+----------------------------+
| length (int32) | type (uint8) | payload (length - 1 bytes) |
+----------------+--------------+----------------------------+
```

* `length` is little-endian and counts the type byte plus payload.
* Payload fields are written with `EngineBinaryWriter` (`Write(int)`,
  `Write(string)`, `WriteVector3`, ...), so both sides share the game's
  serializer.
* Maximum frame size is 256 MiB (`MultiplayerProtocol.MaxFrameSize`).
* `NetChannel` reads on one background thread and writes on another, using a
  blocking queue; the game thread only ever touches the receive queue. TCP
  `NoDelay` is enabled.

The join handshake before the `NetChannel` exists is synchronous: the client
writes `Hello` and then reads frames until it gets `Snapshot` or `Disconnect`.

### Message types

| # | Name | Direction | Payload |
|---|------|-----------|---------|
| 1 | `Hello` | C -> H | protocol version, player name |
| 2 | `Welcome` | H -> C | version, assigned player index, name, world name |
| 3 | `Snapshot` | H -> C | byte count, world zip |
| 4 | `Ready` | C -> H | - |
| 5 | `Input` | C -> H | serialized `PlayerInput` |
| 6 | `CellChange` | both | count, then (x, y, z, value) |
| 7 | `EntityUpdate` | H -> C | count, then per entity: net id, flags, transform/health/look |
| 8 | `EntityAdd` | H -> C | entity XML (includes `MultiplayerNetId`) |
| 9 | `EntityRemove` | H -> C | net id |
| 10 | `PlayerState` | H -> C | count, then per player: index, name, level, remote flag, present flag, health, air, food, temperature, wetness, slot count, active slot, slots |
| 11 | `TimeSync` | H -> C | elapsed game time, time-of-day offset, time of year, precipitation, fog |
| 12 | `ChatMessage` | H -> C | sender, text (also used for join/leave notices) |
| 13 | `PlayerJoined` | H -> C | complete player list (index, name, remote flag) |
| 14 | `PlayerLeft` | H -> C | reserved (the full list in `PlayerJoined` is used instead) |
| 15 | `Disconnect` | H -> C | reason |
| 16 | `Ping` | - | reserved |
| 17 | `Pong` | - | reserved |
| 18 | `BlockInventory` | both | count, then per inventory: net id, active slot, slot count, (value, count) pairs |
| 19 | `PlayerInventory` | C -> H | player index, active slot, slot count, (value, count) pairs |

`Version` is currently **1**. Bump it whenever a payload changes: hosts reject
clients with a different version instead of misparsing them.

---

## File map

All of this is compiled into the game assembly from `prewritten/Multiplayer/`
(see the build section below).

| File | Responsibility |
|------|----------------|
| `MultiplayerProtocol.cs` | message ids, version, ports, discovery strings, `PlayerInput`/ray serialization helpers |
| `MultiplayerConnection.cs` | `NetChannel`: framed TCP transport, reader/writer threads |
| `MultiplayerDiscovery.cs` | UDP announcer on the host, probe + game list on the client |
| `MultiplayerManager.cs` | facade: role, lifecycle, client-freeze allowlist, local look prediction, cell-edit hooks, cached-world cleanup |
| `MultiplayerServer.cs` | accept loop, handshake, snapshot, entity/cell/player/time/inventory sync, timeouts |
| `MultiplayerClient.cs` | handshake + download, message application, input accumulation, player-list reconciliation, inventory forwarding |
| `MultiplayerEntities.cs` | template deep-copy, player entity creation, entity XML (de)serialization |
| `MultiplayerInventorySync.cs` | block-entity inventory diffing, encoding and applying |
| `MultiplayerScreen.cs` | `Join LAN` browser (discovery list, name box) |
| `prewritten/EntitySystem/EntityNetRegistry.cs` | stable net ids, `Enabled`, `SaveFilter` |

Patches under `patches/` hook this into the decompiled game:

| Patch | Hook |
|-------|------|
| `EntitySystem.Android.Net_GameEntitySystem_Entity.cs` | register `MultiplayerNetId` on load, write it on save |
| `EntitySystem.Android.Net_GameEntitySystem_Project.cs` | apply `EntityNetRegistry.SaveFilter` |
| `Survivalcraft_Game_ComponentInput.cs` | `SetPlayerInput`, `ConsumePlayerInput`; desktop starts in mouse mode; client submits input |
| `Survivalcraft_Game_ComponentPlayer.cs` | client early-out, remote input consumption, client look prediction |
| `Survivalcraft_Game_PlayerData.cs` | `IsRemote`, replica readiness, no state machine on clients, no spawn dialog for remote players |
| `Survivalcraft_Game_SubsystemPlayers.cs` | 8 players, `AddPlayerDataAt`, remote marking on load, save filtering |
| `Survivalcraft_Game_SubsystemGameWidgets.cs` | 8 widgets, detached remote widgets |
| `Survivalcraft_Game_SubsystemUpdate.cs` | `MultiplayerManager.SkipUpdate` |
| `Survivalcraft_Game_SubsystemTerrain.cs` | stream `ChangeCell` edits |
| `Survivalcraft_Game_SubsystemGameInfo.cs` | `SetNetTotalElapsedGameTime` |
| `Survivalcraft_Game_ComponentGui.cs` | no GUI input/widgets for remote players |
| `Survivalcraft_Game_ComponentHealth.cs` | `SetNetHealth` |
| `Survivalcraft_Game_ComponentVitalStats.cs` | `SetNetVitals` |
| `Survivalcraft_Game_ComponentInventoryBase.cs` | `SetSlotNet` |
| `Survivalcraft_Game_ComponentLocomotion.cs` | `AddLookAngles` / `SetLookAngles` |
| `Survivalcraft_Game_GameWidget.cs` | remote players get no input device; local client player gets all devices |
| `Survivalcraft_Game_GameManager.cs` | `OnProjectLoaded` / `OnProjectDisposing` / per-frame `Update` |
| `Survivalcraft_Game_GameMenuDialog.cs` | LAN status line, host/stop/status menu entries |
| `Survivalcraft_Game_PlayScreen.cs` | `Join LAN` button |
| `Survivalcraft_Game_ScreensManager.cs` | register the `Multiplayer` screen |

The remaining patches in `patches/` are the desktop port (device info, storage,
screenshots, web, external content); see `README.md`.

---

## Building and changing the code

`src/` is regenerated from the pristine decompile on **every** build, so never
edit it and expect the change to survive.

```bash
cd survivalcraft_linux
./build_survivalcraft_linux.sh
./build/Survivalcraft          # SC_DATA_DIR / XDG_CONFIG_HOME are honoured
```

Workflow:

* **New or heavily rewritten file** -> put it in `prewritten/`. Files under
  `prewritten/Multiplayer/` are copied to `src/Survivalcraft/Game/`,
  `prewritten/EntitySystem/EntityNetRegistry.cs` to
  `src/EntitySystem.Android.Net/GameEntitySystem/`.
* **Small change to an existing decompiled file** -> add a unified diff to
  `patches/` (paths are `a/<Project>/<path>` / `b/<Project>/<path>`, applied with
  `patch -p1`). A patch that does not apply cleanly stops the build; it is never
  skipped.

If you edit `src/` for a quick experiment, the change is lost on the next build
unless you turn it into a patch first. The patch files follow
`<Project>_<path with underscores>.patch`.

Both sides must run the same protocol version. When you change a message layout,
bump `MultiplayerProtocol.Version`; when you add a message, append it to
`MultiplayerMessage` (the numeric values are the wire format, so do not reorder
existing entries).

---

## Testing

Two machines on one LAN is the real test, but a single machine works too: host in
one instance, join `127.0.0.1` in another (loopback is included in the discovery
broadcast list).

Minimum checklist:

- [ ] Host opens a world to LAN; the status line shows the port.
- [ ] Client joins; the world looks like the host's (terrain, time of day).
- [ ] Both players see each other move, look around and animate.
- [ ] Client digs/places blocks; the host sees the edit immediately.
- [ ] Host digs/places blocks; the client sees the edit.
- [ ] Client takes damage / eats / sleeps; the host sees health and needs.
- [ ] Item moves in the client's inventory persist on the host and on other clients.
- [ ] A chest edited by one player is visible to the others.
- [ ] A second client joining later works (snapshot + player list reconciliation).
- [ ] Disconnecting a client removes its avatar and the others keep playing.
- [ ] Leaving the world deletes the cached client world from the world list.

Logs: `~/.config/Survivalcraft2/Logs/Game.log`, lines prefixed `[Multiplayer]`.

---

## Known limitations

These are deliberate for the first version, not bugs to rediscover:

* **Weather visuals on clients** are frozen; `SubsystemWeather` is skipped
  because the host decides precipitation and fog. State changes are still
  applied through `TimeSync`.
* **Crafting and machine progress** (furnace smelting, fire, growth, ...) are
  host-only. The container UI on a client shows the item slots but not the
  progress of the machine.
* **Simultaneous inventory edits** are last-writer-wins (both for the local
  player's inventory and for block inventories).
* **The host hitches** while `SaveProject(wait: true)` produces a snapshot, so
  joining during heavy play pauses the host briefly. The snapshot is written
  from the live world, so the host's save file briefly contains the avatars of
  connected clients; the next regular save drops them again.
* **No authentication or encryption.** The protocol trusts every peer on the
  LAN; a modified client can send arbitrary block edits, positions or inventory
  contents. Do not expose port 27020 to the internet.
* **No dedicated server**; the host must be a player with the world loaded.
* **`Ping`/`Pong` and `PlayerLeft` are reserved** and currently unused.
* **Interpolation is simple lerp**, so a client on a lossy/high-latency link
  will see some rubber-banding of remote bodies.

---

## Ideas for contributors

* Replace per-second inventory polling with change events on
  `ComponentInventoryBase`.
* Replicate block-entity non-inventory state (furnace progress, dispenser
  contents) by serializing the entity's values dictionary deltas.
* Add a chat input box and use `ChatMessage` for real chat.
* Move discovery to mDNS/DNS-SD for zero-configuration discovery across subnets.
* Compress the snapshot (`GZipStream`) before sending; it is currently a raw zip.
* Add a server-side rate limiter for `Input` and `CellChange`.
