# Coding with graphify (read this BEFORE `ARCHITECTURE_HANDOFF.md`)

graphify turns the first-party code (`Assets/Game/Scripts`, `Editor`, `Tests`) and the root `.md` docs into a queryable graph. Use it to **find and scope** before reading files. It is optional tooling: if `graphify-out/graph.json` does not exist on your machine, skip this file and use grep/Read as normal.

## Team note (2026-11-09): please install it

Recommended for everyone working on architecture, especially Ajas and the CTO. It is free for code and makes agents find the right files without reading the whole repo.

1. `pip install graphifyy` (two y's: that is the official package).
2. Register it with your AI tool: `graphify install --platform claude` for Claude Code. Other values: `codex`, `cursor`, `gemini`, `copilot`, `agents` (generic `AGENTS.md` tools) and more; run `graphify --help` for the list.
3. In the repo root, build once: `/graphify .` in Claude Code, or `graphify update .` in a terminal (code only, no tokens).
4. After architecture changes, run `graphify update .` again.

If you use a tool that graphify has no platform for, use the CLI directly (`graphify explain`, `path`, `query`): any agent that can run shell commands can call it.

## The rule

**Query the graph before your first grep or file read for any codebase question.** Then read only the lines it points to. Raw files are for the exact lines the graph already located.

```bash
graphify explain "Assets/Game/Scripts/Character/Damageable.cs::Damageable"   # what is it, who touches it
graphify path "<A path>::<A>" "<B path>::<B>"                                # how do A and B connect
graphify query "<question>" --seed "<path>::<Symbol>" --depth 1 --budget 800 # neighbours of a known symbol
```

## What actually works (tested on this repo)

- **Prefer `explain` and `--seed` over free-text questions.** A plain `graphify query "how does a hit reach player health"` matched the words "Reach" and "Health" and returned Brute grab code. Name the class you mean.
- **Use `path::Symbol` names.** Docs and code both create nodes, so `EnemyCharacter` is ambiguous (exit code 3). Write `Assets/Game/Scripts/New Enemies/EnemyCharacter.cs::EnemyCharacter`.
- **Output is capped.** Default is 2000 tokens. Raise `--budget` only when you need more, or narrow with `--depth 1`.
- **Example:** `graphify path` from `EnemyCharacter` to `Damageable` answers in one line: `EnemyCharacter -> EnemyBase -> Damageable`.

## Workflow for a change

1. **Scope:** `explain` the class you will touch, then `path` between it and anything it must not break (damage path, locks, lifecycle).
2. **Read:** open only the files and lines the graph named.
3. **Read the rules:** `ARCHITECTURE_HANDOFF.md`, then `AGENTS.md` hard rules. The graph shows structure, not the rules.
4. **Change** the code.
5. **Refresh:** `graphify update .` (code only, free, seconds). Commits and checkouts also trigger it through the git hooks.
6. **Check:** run the EditMode tests and `Tools > JunkLite > Systems > Validate`.

## Cheap model for execution, big model for judgment

| Who | Does | Example |
| --- | --- | --- |
| Big model (Opus) | Scope with the graph, plan, write one-file tasks, review the diff | "Add field `blastRange` to `BruteBrain.cs` after line 80, default 3" |
| Small model (Haiku/Sonnet) | Follow an exact task | rename, add a serialized field, write a test, mechanical refactor |
| You | Scenes, prefabs, Inspector wiring | anything the Unity Editor owns |

Give the small model the file path, the lines and the exact change, so it does not need to explore. In Claude Code, spawn it with the Agent tool and `model: "haiku"` or `"sonnet"`.

**Keep with the big model:** `Damageable` and the damage path, input and movement lock leases, `PlayerLifecycle`, anything touching more than one system.

## Limits (do not trust the graph blindly)

- It reads **C# and `.md` only**. Prefabs, scenes, ScriptableObject assets and Inspector wiring are not in it.
- Doc nodes came from a model and can be wrong. About 800 doc-to-code links point at nodes that do not exist. Trust code-to-code edges (`EXTRACTED`) over doc links (`INFERRED`).
- 6 large docs were only skimmed, including `ARCHITECTURE_HANDOFF.md`. Read it directly.
- After big refactors that delete code, rebuild with `graphify update . --force`.

## Setup notes

- Install: `pip install graphifyy` (two y's, the official package), then `graphify install --platform claude`. Build once with `/graphify .` in Claude Code.
- Scope is set by the committed `.graphifyignore` at the repo root (first-party code and root docs only). Do not widen it to `Assets/` as a whole: it pulls in about 1,000 third-party files.
- `graphify-out/` is generated and git-ignored. Never commit it.
- Optional, per developer: `graphify claude install` and `graphify hook install` add Claude hooks and git hooks. They edit `CLAUDE.md`, `.gitattributes` and `.claude/settings.json` (machine-specific path). Do not commit those edits.
- Open `graphify-out/graph.html` to browse the graph. `graphify-out/GRAPH_REPORT.md` has the most-connected classes.
