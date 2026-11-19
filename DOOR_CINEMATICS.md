# Door with a camera cinematic

Press **E** at a room exit. The camera glides to a shot of the next room, then the player walks through the door on their own.
Example in Service Floor: **Room 0 → Room 1**.

No cinematic needed? Use [DOOR_PLAIN.md](DOOR_PLAIN.md) instead.

---

## 1. What the player sees

```
  ┌────────────┐   ┌──────────────┐   ┌──────────────┐   ┌──────────────┐   ┌──────────────┐
  │ 1. Press E │ → │ 2. Camera    │ → │ 3. Player    │ → │ 4. Door      │ → │ 5. Player    │
  │ at the door│   │ glides to    │   │ steps onto   │   │ slides open, │   │ gets control │
  │ (input     │   │ the Shot and │   │ the lane     │   │ player walks │   │ back. Camera │
  │ locks)     │   │ stops moving │   │              │   │ through      │   │ returns at   │
  └────────────┘   └──────────────┘   └──────────────┘   └──────────────┘   │ Arrival      │
                                                                            └──────────────┘
```

If the door's precondition isn't met (for example, the health pack hasn't been taken), **E does nothing**.

---

## 2. Where things go (top-down view)

```
            FREE-MOVE ROOM                   │ DOOR │              NEXT ROOM
                                             │      │
                                             │      │        🎥 Shot (camera, inactive)
                                             │      │          looks at the room
      ┌───────────────────┐                  │      │
      │  LaneEntryTrigger │                  │      │      ┌───────────────────┐
      │   (box, press E)  │                  │      │  ●   │      Arrival      │
      └───────────────────┘                  │      │  │   │   (box: camera    │
   ════════════════════════════ Lane Path ═══╪══════╪══╪═══╪═══ returns here) ═══▶
                                             │      │  │   └───────────────────┘
                                             │      │  └ Walk Through (empty object,
                                             │      │    just past the door,
                                             │      │    OUTSIDE Arrival)
```

**Golden rule:** keep **Walk Through outside Arrival**. Otherwise the camera returns before the player is through the door.

---

## 3. Setup in five steps

**Step 1. The door**
- Use `Prefabs/Environment/Doors/Door4_Sliding.prefab`.
- Its leaves (`Door4.2`, `Door4.3`) must **not** be **Batching Static**. If they are, the door won't slide.
- Optional precondition: add a **Door Access Rule** to the door (see the table below).

**Step 2. The camera shot**
- Drag `Prefabs/Camera/Camera Transition.prefab` into the scene, under `[Gameplay]/Camera`.
- Set **Start On = Script**.
- Move its **Shot** child so it frames the next room. Keep Shot **inactive**.
- Move and size its **Arrival** child so it sits inside the next room.

**Step 3. The trigger**
- Select the exit's **Lane Entry Trigger**, in front of the door under `[Gameplay]/Lanes`.
- Fill in its **Cinematic** section, using the table below.

**Step 4. Walk Through**
- Create an empty child on the trigger called `WalkThrough`.
- Place it just past the door, outside Arrival and outside the room's Z Movement Zone.
- The room needs a **Z Movement Zone** that stops just before the door (see [DOOR_PLAIN.md](DOOR_PLAIN.md), Step 4).

**Step 5. Check**
- Run **Tools ▸ JunkLite ▸ Systems ▸ Validate Cameras**. It should pass.
- Press Play, open the dev console (**F10**) and watch the **CAM** line:
  `Lane` → `Cinematic / blending` → `Cinematic` → `Lane`.

---

## 4. Inspector cheat sheet

**Lane Entry Trigger** (on the box in front of the door)

```
┌─ Lane Entry Trigger ─────────────────────────────────────┐
│ Lane Path        [LanePath_Room1-12]  ← the lane to join │
│ Gate Door        [Door_Entry]         ← the door         │
│ Prompt Label     Proceed                                 │
│ Lane Camera      None                 ← leave empty      │
│ Step Speed       4                    ← m/s onto lane    │
│ Walk Through     [WalkThrough]        ← REQUIRED         │
│ Walk Speed       3                    ← m/s through door │
│ ── Cinematic (optional) ──────────────────────────────── │
│ Transition       [CameraTransition_Room1]  ← REQUIRED    │
│ Max Wait         4                    ← s, shot timeout  │
└──────────────────────────────────────────────────────────┘
```

**Camera Transition** (the prefab from Step 2)

```
┌─ Camera Transition ──────────────────────────────────────┐
│ Start On         Script     ← MUST be Script for doors   │
│ ── Shot ──────────────────────────────────────────────── │
│ Shot             [Shot]     ← child camera, inactive     │
│ Look At Player   ☑          ← camera turns to follow her │
│ ── Arrival ───────────────────────────────────────────── │
│ Arrival          [Arrival]  ← box in the next room       │
│ ── Timing ────────────────────────────────────────────── │
│ Blend In         Ease In Out  2.5   ← glide in (seconds) │
│ Blend Out        Ease In Out  1.2   ← glide back         │
│ Max Hold         0          ← 0 = wait for the player    │
│ Release On Retreat ☑        ← ignored in Script mode     │
│ One Shot         ☑          ← plays once per life        │
└──────────────────────────────────────────────────────────┘
```

**Door Access Rule** (optional, on the door)

```
┌─ Door Access Rule ───────────────────────────────────────┐
│ Required Objective   [GET_HEALTH]       ← finish this    │
│ Required Key Tag     (empty)            ← or need a key  │
│ Locked Dialogue  [ServiceFloor_DoorNeedsHealth]          │
│ Locked Prompt Label  Locked                              │
│ Locked Forever       ☐                                   │
└──────────────────────────────────────────────────────────┘
```

---

## 5. If something looks wrong

| You see | Fix |
| --- | --- |
| E does nothing | The Door Access Rule isn't met yet. That's working as intended. |
| Camera doesn't move | **Start On** must be **Script**, and **Transition** must be set on the trigger. |
| Door doesn't slide | Clear **Batching Static** on `Door4.2` and `Door4.3`. |
| Camera returns too early | Move **Walk Through** outside **Arrival**. |
| Player stops before the door | Another lock is holding the door (not this trigger's). She waits on the lane instead of passing through it. |
| Walked back into the room, camera stuck on the Shot | It releases when she re-enters the trigger box. Make sure the box covers the floor in front of the door. |
| Shot shows a wall | Move **Shot** so nothing sits between it and the room. |

**Scripts involved:** `LaneEntryTrigger` runs the sequence. `CameraTransition` owns the shot. `SlidingDoor` opens the door. `DoorAccessRule` is the precondition.
