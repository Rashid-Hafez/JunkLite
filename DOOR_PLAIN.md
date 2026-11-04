# Door without a cinematic

A normal door. The camera does nothing special.
Want the camera to glide to the next room? Use [DOOR_CINEMATICS.md](DOOR_CINEMATICS.md) instead.

There are two kinds. Pick yours:

```
  Is the player in a FREE-MOVE room (can walk up/down in depth) before this door?

        NO  ──▶  Type A: lane door      (just place the door)
        YES ──▶  Type B: exit door      (door + Lane Entry Trigger, press E)
```

---

## Type A: Lane door (between two lane rooms)

The player walks along the lane, the door slides open, and they walk through. No button press.

```
      LANE ROOM                │ DOOR │                LANE ROOM
                               │      │
   ═══════════ Lane ═══════════╪══════╪═══════════════════════▶
                               │      │
                     opens when the player is near
```

**Setup**
1. Drag in `Prefabs/Environment/Doors/Door4_Sliding.prefab` and line it up with the lane.
2. On its leaves (`Door4.2`, `Door4.3`), make sure **Batching Static** is **off**. If it's on, the door won't slide.
3. Optional: add a **Door Access Rule** to lock it until an objective or key (see Type B, Step 2).

That's it.

---

## Type B: Exit door (free-move room → lane, press E)

The player roams the room, walks up to the door and presses **E**. She steps onto the lane and **always walks through the door**. The door opens on the way.

```
            FREE-MOVE ROOM                   │ DOOR │              NEXT ROOM
   ┌─ Z Movement Zone (whole floor) ───────┐ │      │
   │  ┌───────────────────┐                │ │      │
   │  │  LaneEntryTrigger │                │ │      │
   │  │   (box, press E)  │                │ │      │
   │  └───────────────────┘                │ │      │
   ════════════════════════════ Lane Path ═══╪══════╪══●════════════════════▶
   └───────────────────────────────────────┘ │      │  └ Walk Through (empty object,
                                             │      │    just past the door,
      the door stays LOCKED until E          │      │    OUTSIDE the zone)
```

**What the player sees**

```
  ┌────────────────┐   ┌────────────────┐   ┌────────────────┐   ┌────────────────┐
  │ Walk into the  │ → │ "E  Proceed"   │ → │ Press E: input │ → │ Door opens,    │
  │ box by the door│   │ prompt shows   │   │ locks, player  │   │ she walks to   │
  │                │   │                │   │ steps on lane  │   │ Walk Through   │
  └────────────────┘   └────────────────┘   └────────────────┘   └────────────────┘
```

**Setup**

**Step 1. The door:** same as Type A (prefab, Batching Static off on the leaves).

**Step 2. Precondition (optional):** add a **Door Access Rule** to the door.

```
┌─ Door Access Rule ───────────────────────────────────────┐
│ Required Objective   [GET_HEALTH]       ← finish this    │
│ Required Key Tag     (empty)            ← or need a key  │
│ Locked Dialogue      [a dialogue]       ← plays if locked│
│ Locked Prompt Label  Locked                              │
│ Locked Forever       ☐                                   │
└──────────────────────────────────────────────────────────┘
```

**Step 3. The trigger**
- Create an empty object under `[Gameplay]/Lanes`.
- Add a **Box Collider** and a **Lane Entry Trigger**.
- Size the box to cover the floor in front of the door, inside the room.

```
┌─ Lane Entry Trigger ─────────────────────────────────────┐
│ Lane Path        [LanePath_...]  ← the lane to join      │
│ Gate Door        [the door]      ← locked until E        │
│ Prompt Label     Proceed         ← text after "E"        │
│ Lane Camera      None            ← only if the lane      │
│                                     faces a new way      │
│ Step Speed       4               ← m/s onto lane (0=snap)│
│ Walk Through     [WalkThrough]   ← REQUIRED, past door   │
│ Walk Speed       3               ← m/s through the door  │
│ ── Cinematic (optional) ──────────────────────────────── │
│ Transition       None            ← LEAVE EMPTY           │
│ Max Wait                         ← not used              │
└──────────────────────────────────────────────────────────┘
```

- Make an empty child on the trigger called `WalkThrough`. Put it just past the door, on the lane, and **outside** the room's Z Movement Zone. Then drag it into **Walk Through**.

**Step 4. The room's free movement:** the room needs a **Z Movement Zone** box covering its floor, so the player can roam in depth. Stop the box just before the door. Set its **Lane Path** to the same lane. Every free-move room needs one, the spawn room too: when she walks back in from the lane, the zone sets her free again.

**Step 5. Check:** press Play, walk to the door and press E. She should walk through as the door slides open. Walk back in: you can move in depth again, and E shows again. **Tools ▸ JunkLite ▸ Systems ▸ Validate Cameras** flags a trigger with no Walk Through.

**Optional: checkpoint at the door.** Add an empty object with a **Spawn Point** inside the room, in front of the door, facing the lane. Set **Priority** to `1` (the level start stays `0`). The player respawns at the spawn point nearest to where she died. Example: `Checkpoint_ServiceDoor` in Service Floor.

---

## If something looks wrong

| You see | Fix |
| --- | --- |
| No "E Proceed" prompt | The player isn't inside the trigger box, or the Door Access Rule isn't met yet. |
| E does nothing | The Door Access Rule isn't met. It shows "Locked" and plays its dialogue. |
| Door doesn't slide | Clear **Batching Static** on `Door4.2` and `Door4.3`. |
| E does nothing after a respawn, enemy behind the door | The door's `Blocker` must be on the **Wall** layer, so enemies can't see through a closed door (E is blocked in combat). |
| Player can walk past the door without E | Make sure **Gate Door** is set on the trigger. |
| E puts her on the lane but she stays in the room | Set **Walk Through**, past the door. |
| Back in the room she's stuck on the lane | The room needs a **Z Movement Zone**, and Walk Through must be outside it. |
| Camera faces the wrong way after E | Set **Lane Camera** to the camera for that lane direction (e.g. `NegXCam`). |

**Scripts involved:** `SlidingDoor` opens the door. `DoorAccessRule` is the precondition. `LaneEntryTrigger` handles E and the step onto the lane. `ZMovementZone` handles free roam.
