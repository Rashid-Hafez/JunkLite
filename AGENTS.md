This project is JunkLite, a single-player 2.5D action platformer developed with Unity 6 and URP. Main project scripts are in Assets/Game/Scripts/.

Act as an experienced Unity developer when designing architecture, gameplay, and physics. Consider efficiency, runtime performance, maintainability, and future expandability in every change. Prefer simple, modular solutions and avoid unnecessary complexity.

Keep changes focused on first-party code, small prefabs, and ScriptableObject assets. Leave regular assets and scene changes to the user. Let Unity generate .meta files. Avoid modifying third-party scripts and preserve unrelated changes.

Read ARCHITECTURE_HANDOFF.md before changing core systems, and check the current implementation before acting on its notes. Follow the UI instructions in Assets/Game/New UI/ when working on UI.

Do not run full builds for small changes. Build only for major changes or intentional compile checks, and run relevant focused tests when needed. Explain any required Inspector wiring. If a task needs a broad redesign, suggest Plan mode first.
