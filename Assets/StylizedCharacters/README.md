# 角色卡通材质

项目通过 Package Manager 固定使用 lilToon 2.4.1（MIT，提交 `e698a2f791bd28dd1aabf227c74f14bedeca3587`）。角色在大厅和对局中使用同一套 URP 材质。迁移沿用原始颜色贴图、颜色、贴图平铺和偏移，按 Skin-Anime、Hair-Anime、Cloth-Anime 和 Inorganic-Metal 预设调节光影。双面发片优先保留原有发束轮廓。镂空、透明面纱和透明腿甲保留对应渲染模式。[lilToon 2.4.1 源码](https://github.com/lilxyzw/lilToon/tree/e698a2f791bd28dd1aabf227c74f14bedeca3587)

## 导入新角色

通过项目内容库导入或绑定角色模型时会准备材质。也可在 Project 中选择模型或 Prefab，使用 **Assets → Stylized Characters → Prepare Toon Materials**。

- FBX 等 Unity ModelImporter 资源会在同级 `<模型名> Toon Materials` 中生成材质，由导入器保存重映射。已有外部材质的内容用作转换来源，原材质资源仍可复用。
- Prefab 和由脚本导入的模型会生成带材质覆盖的 Prefab Variant。通过右键菜单单独准备时，角色定义应绑定到生成的 Variant。内容库导入或绑定会自动选用它。
- 通用流程按材质名识别脸、发束、金属和缎面，其他材质使用衣料预设。现有八人阵容的贴图角色与透明例外定义在 [CastSurfaceAssignments.json](../Editor/CastSurfaceAssignments.json)，可从 **AnimeCS → 美术 → 应用角色卡通材质** 补齐尚未迁移的阵容材质。

生成后可直接在 lilToon Inspector 调整阴影、描边和高光。再次准备模型会保留已转换材质上的人工调整。

## 头发、衣料与足底

运行时程序集 `StylizedCharacters.Runtime` 只依赖 Unity 与 MIT 授权的 UnityChanSpringBone。迁移时复制本目录、`Packages/com.unity.springbone`，并在目标项目的 Package Manager 中添加相同版本的 lilToon。内嵌 SpringBone 保留上游运行时代码，将 Inspector 程序集的平台限定为 Editor，使 Windows 播放器能独立编译。[SpringBone 上游版本](https://github.com/unity3d-jp/UnityChanSpringBone/tree/9415071549aee47c094657d9ef5af239b96c201f)

- 蒙皮骨须实际影响对应网格。保留可访问的 Transform，在 Humanoid Animator 上添加 `HumanoidSecondaryMotion`，将现有发束和衣摆的根骨分别拖入 Hair Roots 和 Cloth Roots。链末端应有用于标记长度的子节点。项目导出的 `SpringHair_`、`SpringCloth_` 骨链可以直接调用 `HumanoidSecondaryMotion.Attach(model)` 自动装配。已有 UnityChan SpringManager 的模型使用自身配置。
- 胶囊和头部球体依照 Humanoid 骨长建立，随姿态移动。Hair Motion、Cloth Motion 和 Gravity 控制摆动。已有身体动画、换装蒙皮和蹲姿修形会共同影响最终外形。连续蒙皮表面的极端交叠仍需要针对服装调整权重、碰撞半径或姿态修形。
- `GroundedFeet`、`IFootSurface` 和 `TwoBonePose` 可单独使用。由宿主提供落点、接地状态与动画更新时机，在每帧 Animator 采样前恢复上次修正，在采样后求解。网络状态、武器与游戏输入均由宿主处理。
- 本项目的 PMX 导出器从原有骨链与顶点权重生成较少的实时关节。替换身体 FBX 后，运行 **AnimeCS → 美术 → 重烘角色身体与第一人称下身蒙皮**，同步换装分区和第一人称下身的骨骼索引。

## 验证

EditMode 的 `CharacterModelMaterialsTests` 覆盖 Prefab Variant、FBX 导入器重映射，以及重复执行后保持材质路径与人工设置。八名角色的改造前后图和实际大厅截图位于 `Builds/RenderChecks/CharacterToon`。
