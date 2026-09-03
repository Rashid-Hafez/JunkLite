# Project Overview
- Game Title: JunkLite
- High-Level Concept: 2.5D action combat platformer in a cubic, rotatable world.
- Render Pipeline: Universal Render Pipeline (URP).
- Performance Issues: Severe frame drops during world rotation and volumetric lighting usage.
- Lighting State: Mixed realtime and baked lighting; baked lighting currently appears "broken".

# Game Mechanics
## Core Lighting Goals
Maintain high performance during world rotation while providing moody, volumetric lighting effects and realistic character shadows that interact with the environment.

## Lighting Strategy
- **Volumetric Effects**: Replace compute-heavy raymarched volumetric lights (30+ instances) with high-performance "Light Cone" geometry and shaders.
- **Environment Lighting**: Fix the baking workflow to ensure a clean, performant base layer of lighting.
- **Character Shadows**: Implement a single, optimized Realtime Directional Light for character shadows, using Shadowmask to integrate with baked environment data.

# UI
N/A

# Key Asset & Context
- `Assets/RASHID WORLD/beffio/The Hunt/Content/Plugins/VolumetricLighting-master/`: Existing volumetric lighting plugin (to be replaced/optimized).
- `t:MeshRenderer`: Assets needing Lightmap UV (UV2) audit for baking.
- `UniversalRenderPipelineAsset`: Project-wide shadow and quality settings.

# Implementation Steps

### Step 1: Diagnose Rendering & Rotation Performance
- **Description**: 
  1. Inspect the `HIGH ASSET RENDER FEATURES` URP asset to identify shadow distance, resolution, and additional light limits.
  2. Determine if the "world rotation" is achieved by rotating the Camera or the World Geometry (rotating geometry is significantly more expensive for lighting/physics).
  3. Identify the specific meshes that appear "horrible" when baked to check for missing UV2.
- **Assigned role**: explorer
- **Dependencies**: None
- **Parallelizable**: Yes

### Step 2: Smooth and Improve Fake Volumetric Shader
- **Description**: 
  1. Update `GodRayCone.shader` to include a Fresnel/Rim-fade effect to soften the harsh edges of the cone mesh.
  2. Add a `_SideFade` parameter to control how much the light disperses at the boundaries.
  3. Ensure the shader handles both top-down and radial falloff for a more natural "volumetric" look.
- **Assigned role**: developer
- **Dependencies**: None
- **Parallelizable**: Yes

### Step 3: Implement Proximity-Based Light LOD System
- **Description**: 
  1. Create a `VolumetricLOD` script that monitors distance to the player.
  2. When the player is within a `HighQualityRange`, enable the `VolumetricAdditionalLight` (Real Volumetric) and disable the God Ray mesh.
  3. When the player is outside the range, swap them (enable God Ray mesh, disable `VolumetricAdditionalLight`).
  4. Implement a small hysteresis/buffer to prevent rapid flickering at the range boundary.
- **Assigned role**: developer
- **Dependencies**: Step 2
- **Parallelizable**: Yes

### Step 4: Optimize Character Shadows with Realtime Directional Light
- **Description**: 
  1. Set one Directional Light to `Mixed` mode.
  2. Configure the URP Asset to use `Shadowmask` or `Subtractive` mode.
  3. Tune the shadow distance (e.g., 30-50 units) to only render shadows near the player.
  4. Use a `Shadow Only` light layer if necessary to ensure only the character casts realtime shadows on the environment.
- **Assigned role**: developer
- **Dependencies**: None
- **Parallelizable**: Yes

### Step 5: Fix Environment Baking (UVs and Resolution)
- **Description**: 
  1. Audit the meshes: Ensure "Generate Lightmap UVs" is checked in the Import Settings for all environment FBXs.
  2. Adjust Lighting Settings: 
     - Set a reasonable `Lightmap Resolution` (e.g., 10-20 texels per unit).
     - Increase `Lightmap Padding` to 4 or 8 to prevent bleeding between objects.
  3. Rebake using Progressive GPU/CPU Lightmapper.
- **Assigned role**: developer
- **Dependencies**: Step 1
- **Parallelizable**: No

### Step 6: Final Performance Validation
- **Description**: 
  1. Verify FPS during world rotation.
  2. Confirm character shadows are visible on baked surfaces.
  3. Ensure "God Ray" effects are visually consistent across faces of the cubic world.
- **Assigned role**: developer
- **Dependencies**: Steps 2, 3, 4, 5
- **Parallelizable**: No

# Verification & Testing
- **Profiler Audit**: Use the Unity Profiler to confirm a reduction in "Additional Light" and "Compute Shader" overhead after replacing volumetric scripts.
- **Visual Check**: Inspect baked textures for black splotches (UV overlaps) or hard edges.
- **Shadow Check**: Ensure the character casts a shadow when moving across different faces of the cubic world.
