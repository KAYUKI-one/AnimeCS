# Codex 接续开发交接与提示词记录（`codex/danbaidong-rp`）

> 最后更新：2026-11-28（已完成 v6 视觉精修：米什缇眼部混合模式修复 + 仿蓝色星原柔和肤质/着色描边 + 建筑摄影棚展厅背景，0 报错，可直接交由 Codex 接续后续工作）

## 一、本轮已完成全部里程碑

1. **DanbaidongRP 最小侵入修复（仅 4 个文件 +42/-8 行，全部是 Unity 6000.5 兼容与打包修复，未魔改核心渲染逻辑）**：
   - `PerObjectShadowProjector.cs`：`#if UNITY_EDITOR` 保护 + 运行时 fallback + 自动剥离 Layer 1 粗阴影 + 排除不投阴影网格。
   - `UniversalRenderPipelineGlobalSettings.cs` + `Danbaidong_GlobalSettings.asset`：修复 Unity 6000.5 版本号 `10` 与 `k_LastVersion == 8` 拦截 Player Build。
   - `UniversalRenderPipeline.cs` + `DefaultVolumeProfile.asset`：补全全部 30 个受支持的 `VolumeComponent` 类型 + `CheckPostProcessForDepth` 判空，彻底解决打包后黑屏。
   - `PBRToonStockings.shader`：补齐漏乘的 `Lambert()` 漫反射归一化。

2. **v6 整体视觉和谐度、柔和肤质、着色描边与眼部修复（对标蓝色星原 / DanbaidongRP 示例）**：
   - **彻底修复米什缇眼部异常 (`Mishty_Lash_04`)**：定位到 `c_Mishty_slg_eyeblend.png` 是带透明通道的眼睑叠加层（之前被误归类为不透明 `Face` 导致白色色块遮挡上半眼球）；现改为 `DanbaidongRP/PBRToon/Transparent` + Premultiplied Multiply (`Blend DstColor OneMinusSrcAlpha`, `ZWrite Off`, `RenderQueue = Transparent`)，并同步修正 `DanbaidongMaterialPreset.InferRole` 与 `ArtSource/Blender/import_pmx_to_danbaidong.py` 对 `eyeblend` 的自动分类；同时将 `Eye` 默认 `_ParallaxScale` 设为 `0`，并重新截取清晰眼部的 `Assets/Content/UI/Portraits/MishtyIcon.png`。
   - **柔和瓷感皮肤、消除肩部过曝与生硬橙色阴影**：全面提亮柔化 `DanbaidongLookMaps.cs` 的 8 张 sRGB Ramp（`FaceRamp` 深暗部 `0.90/0.80/0.83`、`SkinRamp` `0.86/0.75/0.78`、`HairRamp` `0.62/0.60/0.76`、`ClothRamp` `0.64/0.66/0.78`）；降低皮肤 `_ShadowStrength` (`0.72`)、`_ShadowSmoothNdotL` (`0.24`)、`_DirectRimWidth` (`0.30`) 与 `_SelfLight` (`1.90`)，彻底消除肩膀亮白光边与生硬暗块。
   - **材质自适配着色描边（恢复头发/衣服/皮肤体积感）**：在 `DanbaidongLookMaps.EstimateOutlineColor()` 中自动采样各材质反照率 HSV 生成和谐着色描边——皮肤/面部为暖粉棕 `(0.56, 0.36, 0.36)`，银发为冷钢紫蓝，紫发为梅紫，奶白/格纹服装自动适配柔和灰米色描边（不再是死黑描边）；修复 `PBRToon/Stockings` 无 `_MainTex` 的告警（统一通过 `GetAlbedoTexture()` 读取 `_BaseMap`）。
   - **全角色同步刷新**：已重新生成 8 张 sRGB Ramp 并同步应用至 `Aegis_Lily`、`GFL2_Mishty`、`Firefly`、`StarTraveler`。

3. **建筑感展厅背景 + DanbaidongRP 官方 SkySystem SH 环境光 (`RenderReviewStage.cs`)**：
   - 告别纯黑虚空：将 `RenderReviewStage.cs` 的 `_camera.clearFlags` 切换为 `Skybox` 以呈现 DanbaidongRP `GradientSky`，并程序化构建 **双层圆台底座 + 金属装饰环 + 半圆建筑廊柱与背墙 + 顶部光束过梁**（对标 DanbaidongRP 示例的建筑光影展厅），为展厅墙壁添加柔和漫反射补光（棚拍灰蓝 / 日光暖白 / 逆光深蓝紫），让浅色头发与服装的着色描边清晰可读。
   - 保留 3/4 侧顶主光（`intensity = 2.20`）+ 阴影侧补光 + 纯背光轮廓光 + `GradientSky.exposure = -1.35 EV`（棚拍）/ `-0.85 EV`（日光）/ `-1.95 EV`（逆光）。

4. **少女前线 2 原生模型（米什缇 `Mishty`）验证导入**：
   - 零外部插件纯 Python PMX 2.0 → Humanoid FBX 转换脚本 [ArtSource/Blender/import_pmx_to_danbaidong.py](file:///E:/project/AnimeCS/ArtSource/Blender/import_pmx_to_danbaidong.py)（已同步修复 `eyeblend` 分类）。
   - `Assets/Content/Art/Characters/GFL2_Mishty/Mishty.fbx`（72,023 顶点、Humanoid 骨骼、8 张原生法线、14 张原生 RMO → `PBRMask`、原生 `HairSpec`、蕾丝 AlphaClip），已注册至 `ContentCatalog.asset`（`_seatOutfits` 第 3 位，紧挨莉莉）。

5. **二次元 RPG 展示动画（已修复倒走）+ 防穿模二次物理升级**：
   - 已修正 `ArtSource/Blender/anime_showcase_motions.py` 中 `stride_y` 符号反转，重烘 `AnimeShowcaseMotions.fbx` 与 `Assets/Resources/AnimeShowcase.controller`（站立 / 跑动 / 下蹲 / 漫步 / 展示 5 动作）。
   - `HumanoidSecondaryMotion.cs` & `SecondaryMotion.asset` 已升级面部/颈部/前胸/双肩碰撞体（迭代 `6` 次、间隙 `0.024m`）。

---

## 二、可直接复制给 Codex 的下一步接续提示词

```text
请先阅读 Design/Handoff/CODEX_HANDOFF.md 和 Design/DanbaidongRP_Portable_Pipeline.md，当前 git 分支为 codex/danbaidong-rp。

当前已完成：
- DanbaidongRP 最小侵入修复 + 莉莉 & 少前2米什缇（含眼部 eyeblend 透明混合修复、柔和瓷感肤质、材质自适配着色描边、清晰头像 MishtyIcon.png）；
- RenderReviewStage 建筑感廊柱展厅背景（告别纯黑背景）+ 3/4 侧顶光与 GradientSky 标定；
- 修复正向走路的 AnimeShowcase.controller 与升级防穿模碰撞体。

接下来请专注于“打包验证、展示交互面板增强、动画与物理精修”：
1. 先执行一轮 EditMode 测试 + Windows 64 位增量打包（Builds/Windows/AnimeCS.exe），确保最新的 RenderReviewStage 建筑展厅背景、米什缇眼部与 v6 着色描边全部进入可执行包且 0 报错。
2. 展示体验与实时调参面板：保留 FPS 代码不删除，可在大厅一键直达（或默认进入）Render Review，并在 Render Review 面板增加：
   - 主光源水平角 / 俯仰角滑块，方便实时旋转光照检查 Face SDF 与刘海阴影；
   - 描边粗细 / 边缘光强度 / Bloom 强度的实时预览滑块；
   - 展厅背景切换（建筑棚拍 / 户外天空 / 深色逆光）。
3. 动画与表情进一步升级：为 Assets/Resources/AnimeShowcase.controller 引入更自然的二次元 RPG 动作（含眨眼 BlendShape、轻松待机呼吸、自然慢走/跑动/转身过渡），保持 RenderReviewTests 通过。
4. 防穿模物理实机精修：检查莉莉麻花辫/头纱与米什缇头发/裙摆在「漫步」「跑动」「转身」下的贴合度，微调 HumanoidSecondaryMotion.cs 中前刘海角度上限与肩胸/大腿胶囊尺寸。
5. 每次完成阶段后把最新改动与验证结果追加到 Design/Handoff/CODEX_HANDOFF.md。
```
