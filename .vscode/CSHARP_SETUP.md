# C# / OmniSharp setup (Go to Definition, Find References)

## 1. Generate / regenerate the solution from Unity

**If you use Cursor:** Unity may not show “Regenerate project files” when Cursor is set as the external editor. Set **Edit → Preferences → External Tools → External Script Editor** to **Visual Studio Code** instead. The **Regenerate project files** button will appear; click it. You can then open the project in Cursor as usual.

**Otherwise:** In Unity use **Assets → Open C# Project** (or **Regenerate project files** if visible). That creates/updates `JunkLite.sln` and the `.csproj` files (gitignored). If Unity opens another app, close it and open this folder in Cursor.

Do this again whenever you add/remove scripts or packages so the solution stays in sync.

---

## 2. Use the right C# extension in Cursor

- Install: **C#** (`ms-dotnettools.csharp`) — the one that uses OmniSharp.
- For this repo, **C# Dev Kit** is not recommended (it can conflict with Unity). If you have it, disable it for this workspace.

---

## 3. Restart the C# language server (no “OmniSharp” command)

There is no “OmniSharp: Restart OmniSharp” in Cursor. Use:

- **Cmd+Shift+P** → run **“Developer: Reload Window”**

That reloads the editor and restarts the C#/OmniSharp server.

---

## 4. After that

- **Cmd+click** on a method/symbol → Go to Definition.
- **Shift+F12** (or right‑click → Find All References) → list of references.

If it still doesn’t work, run **Assets → Open C# Project** again, then **Developer: Reload Window**, and try Cmd+click once more.
