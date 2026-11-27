# 柔和日光场景（Unity 6.5 / URP 17.5）

本目录提供日照预设、天空材质和支持顶点涂层的建筑表面。它可以随 URP 场景独立使用。普通建筑使用项目自带的 Unity Toon Shader。

## 在另一个项目使用

1. 复制本目录及其 Unity 元数据。使用普通卡通材质时，一并复制 `Packages/com.unity.toonshader`，由 Unity Package Manager 解析其依赖。
2. 对普通建筑创建 `DaylitSurface.mat` 的材质变体，设置底色、两级阴影色与贴图。广播站和训练场的 15 款普通材质共用这个父材质。父材质的 Step、Feather 和 GI Intensity 控制过渡位置、过渡宽度和烘焙反光。建筑的静态光照 UV 使用 UV1（模型的第二套 UV）。
3. 需要两层贴图或顶点混色的建筑选择 `Soft Environment/Layered Surface`。顶点 Alpha 控制涂层。ORM 的 R、G、B 分别保存遮蔽、粗糙度与金属度。可分别调整明暗过渡、间接光下限、墙根渐变的世界高度以及纹理细节。
4. 在场景成为活动场景后，将其方向光传给 `DaylightPalette.ApplyToActiveScene(sun)`。复制 `OpenDaylight.asset` 可为另一张地图单独调日照、三色环境光与雾。`HighClouds.mat` 可调整云层全景的朝向、仰角、饱和度和天空底色。`DayClouds.png` 是本项目用 ImageGen 生成的上半球天空画。加法加载的项目在进出地图时切换活动场景，Unity 会随场景恢复天空与雾。
5. 创建或重新烘焙静态场景时，用 `PrepareSunForBake(sun)` 应用该预设保存的太阳朝向和反弹强度，再烘焙光照贴图和探针。本项目的地图烘焙入口会读取地图资源上的“场景日照预设”。沙城使用单独的 `SandstoneDaylight.asset`，保留了街道原本的日照方向。

## 上游代码

- 普通表面使用 Unity Toon Shader 0.16.1-preview。随项目提供的嵌入包保留官方运行时、材质编辑器和 Companion License。URP 适配修正了 Unity 6.5 的 SurfaceType 引用、静态 lightmap UV 及 mixed lighting 的 shadowmask 传递。上游含可选的演示资源。[Unity 官方仓库](https://github.com/Unity-Technologies/com.unity.toonshader/tree/38a74682a177601cde011739ea20ec8c09a051b8)
- 分层表面的直射光直接调用 NiloCat 原版 `ShadeSingleLight`。URP 的光照贴图、探针和阴影遮罩由 `SoftLighting.hlsl` 接入。原始文件和 MIT 许可随目录提供。[固定版本源码](https://github.com/ColinLeung-NiloCat/UnityURPToonLitShaderExample/blob/fbe276e0df1d1f9379e13581c20b6a080a1a6977/SimpleURPToonLitOutlineExample_LightingEquation.hlsl)
- Flat Kit 的公开资料用于参考分色、渐变天空与雾的美术控制。其软件由 Dustyroom 在商店单独授权。[Flat Kit 使用手册](https://flatkit.dustyroom.com/stylized-surface/)
