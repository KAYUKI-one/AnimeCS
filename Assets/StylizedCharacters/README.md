# 通用 3D 角色套件

将本目录复制到目标 Unity 6 URP 项目的 `Assets/StylizedCharacters` 下，将套件中的 `Packages/com.unity.springbone` 复制到目标项目的同级位置，并通过 Package Manager 安装 Input System 和 lilToon 2.4.1。将 **Active Input Handling** 设为 **Input System Package (New)** 或 **Both**。lilToon 的项目版本固定在下方链接的提交。对接自己的输入时可直接使用 Runtime 程序集的角色马达、动画及相机接口。[lilToon 2.4.1 源码](https://github.com/lilxyzw/lilToon/tree/e698a2f791bd28dd1aabf227c74f14bedeca3587)

## 制作可玩的角色

1. 导入带蒙皮骨架的 FBX 或 Prefab。PMX 可先用套件的 `Tools/export_humanoid.py` 和已安装 mmd_tools 的 Blender 转成 FBX。
2. 单选或多选模型，执行 **Assets → Stylized Characters → Create Playable Humanoid**。工具接受有效的 Humanoid Avatar，也可为常见 MMD 日文、Bip001、Bip01 和标准 Humanoid 骨骼名创建 Avatar。生成的预制体包含胶囊控制器、单相机、第一人称可见网格和通用动作控制器。在 AnimeCS 内容库导入角色时会自动生成供对局使用的表现预制体。
3. 将预制体拖入有地面碰撞体的场景，启用该角色相机。点击 Game 视图捕获鼠标，WASD 移动，Shift 慢走，空格跳跃，Ctrl 或 C 下蹲。按 V 切换视角，第三人称下用鼠标环绕、滚轮缩放，Esc 释放鼠标。
4. 导入器会根据蒙皮权重与骨骼命名识别头发、头纱和衣摆。Console 会报告骨链数量、缺失的人形关节和没有手臂蒙皮的情况。特殊命名可在 `HumanoidSecondaryMotion` 的 **Hair Roots** 或 **Cloth Roots** 添加根节点。目标项目添加 `Local Body Hidden` Layer 可启用相机剔除，完整角色继续投射阴影。

## 卡通材质

大厅和对局使用同一套 URP 材质。迁移保留原始颜色贴图、颜色、平铺和偏移，以及面纱和腿甲的透明模式。面部使用略暖的阴影，头发和衣料使用偏冷的暗部，边缘光集中在轮廓附近。

## 导入新角色

通过项目内容库导入或绑定角色模型时会准备材质。也可在 Project 中选择模型或 Prefab，使用 **Assets → Stylized Characters → Prepare Toon Materials**。

- FBX 等 Unity ModelImporter 资源会在同级 `<模型名> Toon Materials` 中生成材质，由导入器保存重映射。已有外部材质的内容用作转换来源，原材质资源仍可复用。
- Prefab 和由脚本导入的模型会生成带材质覆盖的 Prefab Variant。通过右键菜单单独准备时，角色定义应绑定到生成的 Variant。内容库导入或绑定会自动选用它。
- 通用流程按材质名识别脸、发束、金属和缎面，其他材质使用衣料预设。现有八人阵容的贴图角色与透明例外定义在 [CastSurfaceAssignments.json](../Editor/CastSurfaceAssignments.json)，可从 **AnimeCS → 美术 → 应用角色卡通材质** 补齐尚未迁移的阵容材质。

生成后可直接在 lilToon Inspector 调整阴影、描边和高光。再次准备模型会保留已转换材质上的人工调整。

## 头发、衣料与足底

运行时程序集 `StylizedCharacters.Runtime` 只依赖 Unity 与 MIT 授权的 UnityChanSpringBone。迁移时复制本目录、`Packages/com.unity.springbone`，并在目标项目的 Package Manager 中添加相同版本的 lilToon。内嵌 SpringBone 保留上游运行时代码，将 Inspector 程序集的平台限定为 Editor，使 Windows 播放器能独立编译。[SpringBone 上游版本](https://github.com/unity3d-jp/UnityChanSpringBone/tree/9415071549aee47c094657d9ef5af239b96c201f)

- 蒙皮骨须实际影响对应网格。保留可访问的 Transform，在 Humanoid Animator 上添加 `HumanoidSecondaryMotion`，将现有发束和衣摆的根骨分别拖入 Hair Roots 和 Cloth Roots。链末端应有用于标记长度的子节点。项目导出的 `SpringHair_`、`SpringCloth_` 骨链可以直接调用 `HumanoidSecondaryMotion.Attach(model)` 自动装配。已有 UnityChan SpringManager 的模型使用自身配置。
- 胶囊和头部球体依照 Humanoid 骨长建立，随姿态移动。Chest、Hips 和两侧 UpperLeg 胶囊的半径加 0.015 米余量。Hair Motion、Cloth Motion 和 Gravity 控制摆动。大范围交叠仍需要根据服装的权重和样式校准。
- `CharacterMotor` 接收输入方向与动作，`HumanoidLocomotion` 接收动画状态，`CharacterCamera` 与 `FirstPersonVisibility` 管理视角。姿态修正可使用 `UprightStance`、`GroundedFeet`、`IFootSurface` 和 `TwoBonePose`。在 Animator 采样前恢复上一帧的修正，在采样后求解。射击、持枪轨迹、网络和对局模拟通过 AnimeCS 的适配层接入。
- 本项目的 PMX 导出器从原有骨链与顶点权重生成实时关节。替换 AnimeCS 角色身体后，执行 **AnimeCS → 美术 → 重烘角色身体与第一人称下身蒙皮**，重新生成换装分区与第一人称可见区域。

## 验证

EditMode 的 `CharacterModelMaterialsTests` 覆盖 Prefab Variant、FBX 导入器重映射，以及重复执行后保持材质路径与人工设置。八名角色的改造前后图和实际大厅截图位于 `Builds/RenderChecks/CharacterToon`。
