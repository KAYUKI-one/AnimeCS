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

## 二、高光/皮肤泛白问题的精确根因诊断（供 Codex 优先修复）

用户实机截图（流萤、莉莉）反馈：**整体服装与深色部分基本达标，但面部/皮肤以及原本偏白的浅色色块（白衣、浅发）叠加光照后仍明显发白发亮，缺乏参考图的柔润通透感。** 经核查 `PBRToonFace.shader`、`PBRToonBase.shader`、`DanbaidongTonemapping.hlsl`、`RenderReviewVolume.asset`、`RenderReviewStage.cs` 与贴图采样，确认由以下 5 个叠加效应共同导致：

1. **多路光照直接相加导致总辐照系数 `> 1.0`（白光堆叠）**：
   - `PBRToonFace.shader`（L421, 504–506, 567–569, 579, 600–601）与 `PBRToonBase.shader` 中：`最终色 = DirectDiffuse + DirectSpec(含NoseSpec) + PunctualFillDiffuse + IndirectDiffuse + IndirectSpec + RimLight`。
   - 当前 Studio 参数下，`_key.intensity = 2.20` 与 `_SelfLight = 1.90`（`Ember_Firefly` 甚至仍为 `2.05`）混合后 `dirLight.lightColor ≈ 2.13`，仅主光漫反射项 `2.13 / PI ≈ 0.678`，再加上点光源补光 `_fill`（`intensity = 1.15, baseContribution = 0.22`）、天空 SH 漫反射（`GradientSky.exposure = -1.35`, `_IndirDiffIntensity = 0.45`）、电介质高光 `F * DV + _NoseSpecColor` 与边缘光，**叠加总亮度增益接近 `1.05 ~ 1.25`**。
   - 此外注意：`PBRToonFace.shader` 第 361 行将 `directOcclusion` 硬编码为 `1`（CBUFFER 里的 `_DirectOcclusion` 仅在 `PBRToonBase.shader` L476 生效，对 `Face` 无效），因此降低 `_DirectOcclusion` 无法压低脸部主光亮度。
2. **高明度贴图碰上线性空间叠加与逐通道 ToneMapping 去饱和（浅色色块越照越白）**：
   - 实测贴图：`Lily_T01.png` 平均 RGB `(0.893, 0.770, 0.755)`（`maxLum = 0.960`）；`Firefly_T01.png` 平均 `(0.917, 0.865, 0.856)`（`maxLum = 1.000`）；`Lily_T03.png` 白裙 `maxLum = 0.976`。
   - 深色布料乘以 `1.15` 仍在合理区间，但浅肤色/白衣/浅发 Linear 本底已达 `0.78–1.00`，乘以 `>1.0` 的叠加光照后整体冲过 `1.0`，进入 `GranTurismoTonemap`（`Common.hlsl` L110–112 逐 R/G/B 通道独立压缩）：当 R 通道先触及 `shoulderStart` 向 `peak = 1.0` 压平时，G/B 通道仍在 `contrast = 1.08` 的线性段上升，导致**高光去饱和漂白**（暖桃肤色褪成白纸色、浅发与白衣褶皱被抹平）。
3. **`RenderReviewVolume.asset` 对比度与 Bloom 放大泛白**：
   - `Tonemapping`（`mode = 4`, `contrast = 1.08`, `linearSectionStart = 0.22`, `linearSectionLength = 0.42`）+ `ColorAdjustments`（`contrast = 2`）会抬高中高亮度；`Bloom`（`threshold = 1.18, preFilterScale = 2, intensity = 0.22`）会对超过阈值的白衣/发梢边缘做 `2x` 预滤波放大。
4. **`v6` `FaceRamp` 阴影底色过浅 + 脸部缺乏立体微阴影**：
   - `DanbaidongLookMaps.cs` 的 `FaceRamp` 目前 `deepShadow = (0.90, 0.80, 0.83)`、`_ShadowStrength = 0.78`，导致即便是背光半张脸明度也维持在 `~0.83` 以上，整张脸趋于扁平泛白。
5. **遗漏路径导致 `Ember_Firefly` 等角色未吃上最新预设**：
   - 实际目录为 `Assets/Content/Art/Characters/Ember_Firefly` 和 `Ember_StarTraveler`（不是 `Firefly` / `StarTraveler`），流萤目前仍残留旧版 `v4` 过亮参数（`_SelfLight = 2.05`, `_DirectRimWidth = 0.85`）。

---

## 三、可直接复制给 Codex 的下一步接续提示词

```text
请先阅读 Design/Handoff/CODEX_HANDOFF.md（重点看第二节“高光/皮肤泛白问题的精确根因诊断”）和 Design/DanbaidongRP_Portable_Pipeline.md，当前 git 分支为 codex/danbaidong-rp。

【本轮首要任务：先彻底解决“面部/皮肤及浅色服装头发叠光泛白”，达到蓝色星原式柔润通透质感，不要魔改 DanbaidongRP 核心 Shader】

请按以下具体方案逐项落地并截屏对比验证（同时兼顾 莉莉 / 米什缇 / 流萤）：
1. 压住白光堆叠与总曝光上限（目标：受光面部与白衣在 HDR Linear 中保持在 ~0.72~0.85，留出高光与红晕层次，不冲破 1.0）：
   - 在 Assets/Scripts/Presentation/Lobby/RenderReviewStage.cs：
     * 降低主光与补光叠加总量：Studio _key.intensity 从 2.20 降到 ~1.45~1.55（Daylight ~1.70，Rim ~1.25）；
     * 将阴影侧 _fill 的 baseContribution 从 0.22 降到 0.08~0.10（或 _fill.intensity 降至 0.65），避免正面/侧面多光源漫反射反复相加冲白；
     * 适当回调 GradientSky.exposure（如 Studio -1.65 EV，Daylight -1.15 EV），让暗部靠天空 SH 保留通透色彩而非整体提白。
   - 在 Assets/Editor/DanbaidongCharacterPipeline/DanbaidongMaterialPreset.cs：
     * 降低材质 _SelfLight（从 1.90 降到 ~1.35~1.42），防止 _MainLightColorLerp 把方向光抬高；
     * 对 Face / Skin / 浅色高明度贴图增加柔和的 _BaseColor 轻微暖肉调压白保护（例如 Face/Skin 若贴图平均亮度 > 0.80，给一个温和防过曝系数 ~0.88~0.92 + 极淡桃色偏转 (0.94, 0.90, 0.88)，浅白布料/浅发最高控制在 ~0.88~0.92），让高光不再一照就成纯白块；
     * Face / Skin 进一步收敛诱发泛白的附加高光：_IndirSpecIntensity = 0.01~0.02、_Smoothness = 0.02、_NoseSpecColor.a 降至 0.08、_DirectRimFrontCol 强度降至 0.22~0.28；Face/Skin 的 _ShadowStrength 调回 ~0.85~0.90，配合柔润但不苍白的暖蜜桃 sRGB FaceRamp/SkinRamp（deepShadow 约 (0.83, 0.69, 0.72), sssBand 约 (0.94, 0.80, 0.77)）恢复面颊红晕与鼻下/颈部立体柔和暗部；
     * 注意在 PBRToonFace.shader 中 _DirectOcclusion 未参与计算（L361 为常量 1），请通过 _SelfLight、_BaseColor、_ShadowRampTex、_IndirDiffIntensity 与场景灯光强度来控制面部亮度，不要修改 DanbaidongRP Shader。
2. 调整后处理曲线避免逐通道压白（Assets/Settings/RenderReviewVolume.asset）：
   - GranTurismo Tonemapping 目前 contrast = 1.08 且 ColorAdjustments.contrast = 2 会把中高亮往白推：请把 Tonemapping.contrast 降到 ~0.95~1.00，linearSectionStart ~0.18，linearSectionLength ~0.48~0.55，ColorAdjustments.contrast 设为 0，Bloom.intensity 降至 ~0.12（preFilterScale 降至 1.0，threshold 提高到 1.35~1.50），保证只有真正的金属高光和自发光才会泛光，白衣和脸颊绝不溢白。
3. 补齐全部角色的预设刷新（修正之前漏掉目录名的问题）：
   - 对 Assets/Content/Art/Characters/ 下全部实际目录（Aegis_Lily、GFL2_Mishty、Ember_Firefly、Ember_StarTraveler 等）统一重新生成 Ramp 并重跑 DanbaidongMaterialPreset.Apply（注意保留 Mishty_Lash_04 眼睑叠加层的 DstColor / OneMinusSrcAlpha 透明混合与蕾丝 AlphaClip）。
   - 用 RenderReviewStage 在 Studio / Daylight 光照下分别截取 莉莉、流萤、米什缇 对比图并用 read_file 肉眼确认面部肤色柔和有层次、白衣与浅发褶皱分明。

【完成泛白修复后，再继续推进展示体验与动画物理】
4. 运行 EditMode 测试、PlayMode (RenderReviewTests) 与 Windows 64 位增量打包（Builds/Windows/AnimeCS.exe），确保 0 报错且打包生效。
5. 展示体验与实时调参面板：保留 FPS 代码不删除，可在大厅一键直达或默认进入 Render Review，并在面板增加主光角度（水平/俯仰）、描边粗细、边缘光强度、Bloom/曝光实时滑块。
6. 动画与防穿模精修：为 AnimeShowcase.controller 引入更自然的二次元 RPG 待机/眨眼/走跑动画，并在实机「漫步」「跑动」「转身」下微调 HumanoidSecondaryMotion.cs 防穿模参数。
7. 每次完成阶段后把最新改动与验证结果追加到 Design/Handoff/CODEX_HANDOFF.md。
```

## 四、2026-11-28 实机复查与分阶段校准

### 灯光、材质与取证

- 在 Unity 6000.5.5f1 的 Render Review 固定特写机位采集三位角色的棚拍、日光基线。每个视图同时保存带后处理的 960×720 PNG 与关闭后处理的 RGBAHalf 线性读回。基线测试通过，机器当次使用 D3D11。
- 首轮把主光设为棚拍 1.50、日光 1.70、逆光 1.25，补光漫反射贡献设为 0.09，天空曝光改为 −1.65 / −1.15 / −1.95 EV。GT 对比 0.98、线性段 0.18 / 0.52，调色对比 0，Bloom 阈值 1.42、预滤波 1、强度 0.12。
- 以 Characters 实际路径枚举 12 个角色目录，重建共享 Ramp 并应用至 123 个角色材质。米什缇眼睑保留 DstColor / OneMinusSrcAlpha、透明队列与关闭深度写入，保留五个已裁切的蕾丝材质。其余八个旧模型缺少 Face SDF，以 Skin Ramp 与网格法线着色。共享刘海阴影仍使用专用材质。
- 首轮读回发现莉莉日光面颊红通道仍到 1.04，流萤白衣接缝有局部尖峰。进一步按图集浅色采样占比设定保护色，缩窄浅布描边附近的轮廓光。脸部着色器的边缘光颜色 alpha 控制主光颜色混合，实际强度还需调整 RGB。
- 编辑器切入 Play Mode 时发现 GGX 天空反射采样纹理在同帧重新创建管线时引用了已延迟销毁的资源。清理后立即置空引用，重新运行基线和两轮对照采集均通过。变更位于 C# 资源生命周期，核心 Shader 无改动。

### 展示、动画与物理

- 保留登录页与大厅一键进入渲染查看。右侧可展开“实时调参”，控制主光水平与俯仰偏移、描边倍率、边缘光、Bloom 及曝光 EV。材质调节通过子网格属性块生效，后处理使用独立 VolumeProfile 实例。
- Blender 重新烘焙了 3.6 秒呼吸待机、36 帧漫步和 24 帧跑步。调整足部俯仰连续性、骨盆上下运动、重心横移及手腕跟随。展示控制器采用独立眼睑层。莉莉、流萤、米什缇在导入阶段生成闭眼形态，实测三套绑定均能达到闭眼权重 90 以上。
- 三人各采集漫步、跑动、转身、闭眼共 12 帧。调整胸前、面部与上臂碰撞余量，检查米什缇骨架时另发现自动 Humanoid 把一根发链映射成 Jaw，已清掉该可选映射。
- 全量 EditMode 历史基线为 147 项，通过 136、失败 11。失败来自对战八人名册、米什缇缺少第一人称配套等。用户随后明确本轮仅验证渲染、材质、物理与展示动画，保留现有对战名册，不再运行 FPS 对战测试。

### 最终线性亮度读回

[棚拍、日光前后对照](RenderReview-Lighting.jpg) · [漫步、跑动、转身与闭眼](RenderReview-Motion.jpg)。原始 PNG 和线性 RGBAHalf 存于本机 Builds/RenderChecks/Review20261128 的 before、after 子目录。以下数据取自 960×720 特写，图像坐标从左上角计。亮度 Y = 0.2126R + 0.7152G + 0.0722B，P95 取固定面颊矩形的第 95 百分位；峰值为该区域所有像素三个通道的最大值。

| 角色与面颊区域（起点含，终点不含） | 棚拍 Y P95 前 → 后 | 日光 Y P95 前 → 后 | 修复后最高通道，棚拍 / 日光 |
| --- | --- | --- | --- |
| 流萤 (482,280)–(548,317) | 0.941 → 0.728 | 0.992 → 0.767 | 0.851 / 0.901 |
| 莉莉 (493,280)–(546,317) | 1.062 → 0.782 | 1.130 → 0.820 | 0.924 / 0.975 |
| 米什缇 (488,282)–(545,311) | 0.821 → 0.738 | 0.879 → 0.781 | 0.872 / 0.922 |

流萤白衣接缝 (405,532)–(472,607) 在日光下的 Y P95 从 1.221 降至 0.753，最高通道从 2.764 降至 0.809。棚拍下这块布料处于阴影，修复后 Y P95 为 0.525。米什缇源脸图带有较浓的琥珀色，因此单独平衡脸与身体材质的绿、蓝通道，使暗部保持暖色并留出红通道余量。以上测量适用于默认预设和记录机位，实时曝光滑块允许主动超出此范围。
