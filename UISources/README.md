# AnimeCS 界面样稿

用浏览器打开任一页面的 `index.html`，可通过顶部导航切换四页。整目录可离线浏览，布局按电脑端 16∶9 等比缩放。

| 界面 | 代码入口 | 预览图 |
| --- | --- | --- |
| 大厅 | [lobby/index.html](lobby/index.html) | [大厅](lobby/preview.png) |
| 角色 | [character/index.html](character/index.html) | [角色](character/preview.png) |
| 库存 | [inventory/index.html](inventory/index.html) | [库存上部](inventory/preview.png) · [滚动后](inventory/preview-scrolled.png) |
| 装备 | [equipment/index.html](equipment/index.html) | [余烬配置](equipment/preview.png) · [苍盾配置](equipment/preview-aegis.png) |

每页的 `view.js` 负责该页交互。顶栏、通用样式和库存数据在 `shared/`，依赖图片在 `assets/`。项目角色模型以预渲染图展示，库存与装备使用项目素材。装备页的试装数据和大厅展示配置由浏览器本地保存。

大厅的「配置」可切换展示角色和手持枪械。角色页可切换档案。库存可在固定顶栏下滚动。装备支持按阵营查看可用物品，并通过点击或拖拽放入对应槽位。

头像、邀请好友、商店及其他待设计入口在这版作为静态元素呈现。图标使用 Lucide，许可见 [assets/icons/LICENSE.txt](assets/icons/LICENSE.txt)。
