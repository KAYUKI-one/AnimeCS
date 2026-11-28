# DanbaidongRP 二次元角色渲染与导入移植指南（通用模板）

本管线基于 [DanbaidongRP (urp17)](https://github.com/danbaidong1111/DanbaidongRP) 官方文档构建，实现了从 **Blender 模型预处理（分材质/球形法线/UV1-UV2/光滑描边法线/Face SDF）** → **Unity 材质与贴图自动化（sRGB ShadowRamp / 各向异性 HairSpecTex / Albedo 智能 PBRMask / 微细节法线 / 刘海投影 FringeShadow）** → **渲染器与后处理（PerObjectShadow / SubSurface 阴影散射 / GranTurismo Tonemapping / BloomDanbaidong）** 的完整闭环。

整套工具 **零项目业务耦合**，可直接复制到任意 Unity 6（URP 17 / DanbaidongRP）二次元 3D 项目中复用。

---

## 一、移植到新项目需要复制的文件清单

将以下 4 部分复制到目标 Unity 6 项目中即可：

1. **渲染管线包（完全离线自包含）**
   - `Packages/com.unity.render-pipelines.danbaidong`
   - `Packages/com.unity.render-pipelines.danbaidong-config`
   - 在目标项目 `Packages/manifest.json` 中添加：
     ```json
     "com.unity.render-pipelines.danbaidong": "file:com.unity.render-pipelines.danbaidong",
     "com.unity.render-pipelines.danbaidong-config": "file:com.unity.render-pipelines.danbaidong-config"
     ```
   - 注意：`PerObjectShadowProjector.cs` 已修复 `UNITY_EDITOR` 条件编译与运行时 fallback，可直接打包 Standalone Player；`PBRToonStockings.shader` 已补齐 `Lambert()` 物理漫反射归一化。

2. **渲染管线与后处理预设（直接复制并在 Graphics / Quality 中指定）**
   - `Assets/Settings/Danbaidong_PC.asset`（RenderPipelineAsset：`m_POShadowExcludeLayer = Layer 1`、4096 主阴影、2048 PerObjectShadow、Linear 色彩空间）
   - `Assets/Settings/Danbaidong_PC_Renderer.asset`（RendererData：已挂载 `PerObjectShadowFeature`）
   - `Assets/Settings/RenderReviewVolume.asset` / `Assets/DefaultVolumeProfile.asset`（VolumeProfile：已配置 `GranTurismo Tonemapping`、`BloomDanbaidong`、`Shadows(SubSurface)`、`ColorAdjustments`）

3. **Blender 通用预处理脚本**
   - `ArtSource/Blender/danbaidong_character_prep.py`（支持 Blender 4.x / 5.x 无头批处理，NumPy 向量化，15 万面模型仅需 ~4 秒）

4. **Unity 通用角色配置工具（无 `AnimeCS` 依赖）**
   - `Assets/Editor/DanbaidongCharacterPipeline/`
     - `DanbaidongLookMaps.cs`（生成 sRGB 4-band ShadowRamp、512×512 各向异性 `AnimeHairSpec.png`、Albedo 智能 `PBRMask`、布料/金属微细节法线）
     - `DanbaidongMaterialPreset.cs`（自动识别中/日/英材质名角色部位并配置 `Base/Face/Eye/Hair/Stockings/Transparent` 参数与关键字）
     - `DanbaidongCharacterSetupTool.cs`（一键菜单 `DanbaidongRP/Character Setup/Prepare Selected Character FBX` + Unity MikkTSpace 光滑法线烘焙 + 自动刘海投影提取）

---

## 二、导入任意新二次元 / MMD 模型的标准 3 步流程

### 第 1 步：Blender 预处理（修复法线 / UV / 顶点色 / Face SDF）

若源模型是 `.pmx` / `.pmd`，先在 Blender 中使用 `MMD Tools` 导入并导出为 `.fbx`（或在 Blender 中直接执行脚本处理 `.fbx`）：

```powershell
blender -b --python ArtSource/Blender/danbaidong_character_prep.py -- ^
  "输入模型.fbx" ^
  "Assets/Characters/NewChar/NewChar.fbx" ^
  --actor NewChar ^
  --profile generic
```

脚本会自动完成：
- **UV1 / UV2**：脸部/眼部/睫毛生成对称正面 Face SDF 投影 UV；头发生成环绕头部的圆柱投影 UV1（供 `PBRToon/Hair` 采样 `_HairSpecTex` 天使环高光）。
- **风格化法线**：头发混合球形法线（默认 `0.62`）、脸部轻度柔化球形法线（默认 `0.35`），保留所有 Shape Keys（BlendShapes）与骨骼权重。
- **光滑描边法线与分部位描边权重**：向量化烘焙角度/面积加权光滑法线到顶点色 RGB，并把分部位描边宽度遮罩写入顶点色 Alpha（脸/眼/睫/纱 = `0.0`，皮肤/丝袜 = `0.6`，头发 = `0.72`，布料/金属 = `0.88–0.90`）。
- **512×512 Sculpted Face SDF 与眼部锚点**：输出 `NewCharFaceSDF.png`（R=方向光 SDF，G/B=鼻梁微高光，A=遮罩）与 `NewCharFace.txt`。
- （可选）若模型把睫毛/皮肤/丝袜混在同一材质槽，可像 `--profile lily` 一样按 UV/高度区间拆分独立材质槽，以获得 `EyelashOutter` 透刘海和 `PBRToon/Stockings` 丝袜菲涅尔质感。

### 第 2 步：Unity 一键配置材质、光滑法线与刘海投影

1. 将处理后的 `.fbx`、贴图、`*FaceSDF.png`、`*Face.txt` 放入 Unity 角色目录（例如 `Assets/Characters/NewChar/`）。
2. 确保 `ModelImporter` 满足：`Normals = Import`、`Tangents = Calculate MikkTSpace`、`Import Vertex Colors = true`（工具会自动校验）。
3. 在 Project 窗口选中该 `.fbx`，点击顶部菜单：
   **`DanbaidongRP -> Character Setup -> Prepare Selected Character FBX`**
4. 工具会自动：
   - 用 Unity 真实 MikkTSpace 切线空间重新校准光滑法线到 `UV4` 与顶点色 RGB（同时保留 Blender 烘焙的 Alpha 描边宽度），彻底避免左右手坐标系翻转造成的描边断裂；
   - 根据材质名自动分配 `Face / Eye / Lash / Hair / Skin / Cloth / Leather / Metal / Stockings / Transparent` 着色器与参数；
   - 生成 **sRGB** `ShadowRamp`（哑光部位高光通道全黑，彻底消除硬塑料感）、`AnimeHairSpec.png`、Albedo 智能 `PBRMask`（自动区分哑光布料、微缎面皮革与高光金银金属扣）和布料微细节法线；
   - 从前发网格提取 `NewCharFringe.asset` 与 `HairFringe.mat`。

### 第 3 步：场景灯光与运行时组件

1. **主方向光（Directional Light）**：
   - `Intensity` 设为物理值 `2.8 – 4.0`（因 `PBRToon` 漫反射乘以 `Lambert() = 1/PI`）。
   - 开启 `Custom Shadow Layers`，`Shadow Rendering Layers` 仅勾选 `Layer 1 (MainLightShadows)`，场景地面/环境开启 `Layer 0 + Layer 1`。
2. **角色实例挂载组件**：
   - 挂载 `PerObjectShadowProjector`（调用 `CollectRenderers()`，会自动剥离角色 `Layer 1` 防止粗精度主阴影产生棋盘格自阴影噪点，仅使用 2048 精细逐物体阴影）。
   - 挂载 `CharacterRenderHelper`（DanbaidongRP 内置）或项目的头部朝向更新脚本，将 `_FaceFrontDirWS` / `_FaceRightDirWS` / `_HeadCenterWS` 同步给脸部、眼部与 `Fringe shadow` 子网格。
3. **全局后处理 Volume**：
   - 使用配置好的 `VolumeProfile`：`Tonemapping = GranTurismo`、`Bloom = BloomDanbaidong (threshold ~0.88–0.92, intensity ~0.26–0.32)`、`Shadows (rayTracing=false, perObjectShadowPenumbra=0.14, shadowScatterMode=SubSurface)`。

---

## 三、常见问题排查清单（防止“硬塑料”和阴影噪点）

| 症状 | 根本原因 | 正确配置 |
| --- | --- | --- |
| 角色发灰、偏暗 | 方向光强度 `< 1.5` 且 `_SelfLight = (1,1,1)`，被 `1/PI` 压暗 3 倍 | 主光 `2.8–3.5` 或 `_SelfLight` 设为 HDR `(2.45, 2.39, 2.33)` |
| 皮肤/衣服像硬塑料反光 | `_PBRMask.g` 过高、`SpecularRamp` 非黑、`_IndirSpecIntensity` 过高 | 哑光部位 `PBRMask.g = 0.08–0.16`、`SpecularRamp = (0,0,0)`、`_IndirSpecIntensity = 0.04–0.16` |
| 赛璐璐明暗交界糊成一片 | 误把 `_ShadowSmoothNdotL` 调小（实际 `SigmoidSharp` 越大越锐利） | `_ShadowSmoothNdotL = 0.64–0.76` |
| 脖子/衣领/裙摆出现棋盘格噪点 | 角色同时投射到粗精度主阴影（Layer 1）或 `_ShadowSmoothScene` 过大 | 角色剥离 `Layer 1` 仅用 PerObjectShadow，`perObjectShadowPenumbra = 0.14`，`_ShadowSmoothScene = 0.16` |
| ShadowRamp 颜色发白失真 | Ramp 贴图导入误关了 `sRGBTexture` | Ramp 必须保持 `sRGBTexture = true`、`Uncompressed`、`Clamp` |
| 脸部 SDF 光影方向不对 | 传入的头部基准 Transform 未对齐角色正前方 `+Z` | 用与角色根朝向对齐的 `HeadMount` 子物体提供 `forward/right` |
| Editor 正常但打包后黑屏 (`CheckPostProcessForDepth` 空引用) | Unity 6000.5 Player 构建仅从 `DefaultVolumeProfile.asset` 加载已注册的 `VolumeComponent` 类型 | 确保 `DefaultVolumeProfile.asset` 包含全部 `VolumeManager.instance.baseComponentTypeArray` 组件类型（共 30 个） |
