# DanbaidongRP Portable Character Pipeline

Drop this folder (`Assets/Editor/DanbaidongCharacterPipeline/`) into any Unity 6 project using DanbaidongRP (`com.unity.render-pipelines.danbaidong`).

## Quick Start
1. Preprocess your anime/MMD FBX in Blender:
   ```powershell
   blender -b --python ArtSource/Blender/danbaidong_character_prep.py -- "input.fbx" "Assets/Characters/Hero/Hero.fbx" --actor Hero --profile generic
   ```
2. Select `Hero.fbx` in Unity's Project window and run:
   **`DanbaidongRP -> Character Setup -> Prepare Selected Character FBX`**
3. Attach `PerObjectShadowProjector` (call `CollectRenderers()`) and DanbaidongRP's `CharacterRenderHelper` to your character prefab, and use `GranTurismo` Tonemapping + `BloomDanbaidong` + `Shadows(SubSurface, perObjectShadowPenumbra=0.14)` in your `VolumeProfile`.

See `Design/DanbaidongRP_Portable_Pipeline.md` for full documentation and troubleshooting.
