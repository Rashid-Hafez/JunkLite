# Mod stats

Durability and damage for every mod asset in `Assets/Game/New Prefabs/Mods/`.
Type comes from the mod's script base class: `ActiveModData` = active, `PassiveModData` = passive.

| Mod | Type | Max Durability | Durability per Use | Uses | Damage | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| Dont Blink | Active | 10 | 1 | 10 | Strike 30 × 1.5 bonus = **45** | Cooldown 10 s. Teleport VFX out and in, Flash VFX on the strike. VFX destroyed after 2 s. |
| Electric | Passive | 10 | 2 | 5 | Zap **5** per 0.3 s for 5 s; area zap **2.5** within 1 m | Area damage = 5 × 0.5. The status effect asset can override the zap timing. |
| Energy Wave | Active | 10 | 5 | 2 | **15** per 0.5 s tick | Cooldown 3 s, pulse lasts 5 s |
| Fire | Passive | 10 | 2 | 5 | **5** per 0.5 s for 3 s = **30** total | Burn over time |
| Life Steal | Passive | 10 | 1 | 10 | **0** (no damage) | Heals 30% of damage dealt |
| Phantom Strike | Active | 10 | 1 | 10 | Slam **50**, crit × 3 = **150** | Needs 3 hits to charge |
| Pogo | Passive | 10 | 1 | 10 | **0** (no damage) | Pushes player up, force 10 |
| Pulse Barrier | Active | 10 | 1 | 10 | **0** (no damage) | Shield 100 HP for 10 s, cooldown 40 s |
| Social Distance | Active | 10 | 2 | 5 | Push **20** to all enemies in 5 m | Push force 35 |
| Phantom Strike (tutorial copy) | Active | 10 | **3** | 3 | Slam 50, crit × 3 = 150 | `phantomstrike tutorial.asset`, not the main one |

## Notes

- The **Base Damage** field is 0 on every mod asset. The damage numbers above come from each mod's own fields.
- **Uses** = Max Durability ÷ Durability per Use. This assumes durability drops by a fixed amount each use.
- **Electric's total** is not listed. Its status effect asset can change the zap timing, and that asset was not checked.
- The tutorial Phantom Strike asset uses 3 durability per use, not 1. Check which one the tutorial loads before changing either.
