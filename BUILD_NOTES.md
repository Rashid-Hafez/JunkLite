# Build notes & fixes (JunkLite)

Notes that helped fix build/rendering issues on this project. For future reference (you or others).

---

## macOS / Metal: "maximum ps_5_0 sampler register index (16) exceeded" (URP Lit)

**Error:**  
`Shader error in 'Universal Render Pipeline/Lit': maximum ps_5_0 sampler register index (16) exceeded at ... LightCookieInput.hlsl (on metal)`

**Cause:**  
One URP Lit shader variant (lightmaps + light cookies + LOD cross fade + reflection probes, etc.) uses more than 16 texture samplers. Metal limits fragment shaders to 16 samplers.

**What we changed (to reduce variants / cookie usage):**

1. **URP pipeline assets** – Disable light cookies so the `_LIGHT_COOKIES` variant can be stripped.  
   In each URP Asset used in the build (e.g. `Assets/Settings/High_PipelineAsset.asset`), set in the YAML:
   - `m_SupportsLightCookies: 0`
2. **Baked cookies** – In `ProjectSettings/EditorSettings.asset` set:
   - `m_DisableCookiesInLightmapper: 1`  
   (equivalent to Edit → Project Settings → Editor → Graphics → uncheck "Enable baked cookies support")
3. **Per-light** – No cookie textures on any Light in scenes/prefabs (Cookie = None).

**Fix that made the build succeed:**  
**Reimport everything.**  
In Unity: **Assets → Reimport All** (or right‑click the `Assets` folder in the Project window → **Reimport**). After a full reimport, the build completed successfully.

If the error persists after reimport, try disabling **LOD Cross Fade** in the URP Asset: in the same `.asset` file set `m_EnableLODCrossFade: 0`.

---

## RayFire: "The type 'Utils' exists in both RFUtilsNet and RFUtilsNet_osx" (CS0433)

**Cause:**  
On macOS, both the Windows and the OSX RayFire plugin DLLs were loaded in the Editor, so `Utils` was defined twice.

**Fix:**  
In `Assets/RayFire/Plugins/Win/x86_x64/RFUtilsNet.dll.meta`, the Windows DLL was disabled for the Editor and for OSXUniversal/Linux64 (so only the Mac plugin is used when editing on Mac). Windows builds still use the Windows DLL for the Win64 target.

---

*Last updated from session where reimport-all fixed the Metal URP Lit build.*
