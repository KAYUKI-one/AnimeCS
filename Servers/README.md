# Debian 13 服务端

发布目标为 Debian 13 x86-64。三个进程各自启动。首次运行个人数据库为空，热更新服务在发布第一个版本前返回 HTTP 204。离线试玩继续使用本地衣柜。

| 目录 | 进程职责 | 默认监听 | 状态 |
| --- | --- | --- | --- |
| Battle | 运行一场十席位权威对局，接收操作并返回各席位可见的战斗快照 | UDP 127.0.0.1:27840 | 进程内的回合、输入与历史碰撞体 |
| Data | 按稳定玩家 ID 读写库存、装备槽、胜场数、头像 ID 和资料修订号 | HTTP 127.0.0.1:27841 | SQLite，建库版本 1 |
| HotUpdate | 返回当前发布清单，按 SHA-256 提供内容，支持 HTTP Range | HTTP 127.0.0.1:27842 | 不可变内容、历史清单、当前清单 |

## 构建和安装

在装有 .NET 10 SDK、Python 3 和 Unity 6000.5.5f1 的构建机执行以下 PowerShell 7 命令。编辑器需要 Linux Dedicated Server Build Support。批处理使用独立 Unity 工程目录，或在关闭该工程的编辑器后使用工作目录。可传 `-UnityProject` 指向独立工作副本，`-BattleMap map.dust2` 覆盖目录中的默认比赛地图。

```powershell
./Servers/build.ps1 -UnityEditor 'C:/Program Files/Unity/Hub/Editor/6000.5.5f1/Editor/Unity.exe'
```

`Builds/Debian13/Packages` 内有三个可分别传输的 tar.gz。将所需包解压到 `/opt/animecs`，把该目录下对应的 `.service` 文件安装到 `/etc/systemd/system`。例如，在 Debian 13 启动战斗服可以执行以下命令。

```sh
sudo install -d /opt/animecs /etc/animecs
sudo tar -xzf AnimeCS-Battle-debian13-x64.tar.gz -C /opt/animecs
sudo install -m 644 /opt/animecs/Battle/animecs-battle.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now animecs-battle.service
```

另两个单元名为 `animecs-data.service` 和 `animecs-hotupdate.service`。.NET 程序自带运行时。在最小 Debian 镜像中，运行时依赖包括 `libicu76`、`libssl3t64`、`libstdc++6`、`libgssapi-krb5-2` 和 `zlib1g`。各单元的 `StateDirectory` 由 systemd 创建。

| 配置文件 | 环境变量 |
| --- | --- |
| `/etc/animecs/battle.env` | `ANIMECS_BATTLE_ADDRESS`、`ANIMECS_BATTLE_PORT`、`ANIMECS_BATTLE_SNAPSHOT_HZ` |
| `/etc/animecs/data.env` | `ANIMECS_DATA_URL`、`ANIMECS_DATA_DIR`、`ANIMECS_DATA_TOKEN` |
| `/etc/animecs/hotupdate.env` | `ANIMECS_UPDATE_URL`、`ANIMECS_UPDATE_DIR` |

默认监听回环地址。跨主机时在对应环境文件填写实际接口地址，HTTP 服务可放在 HTTPS 反向代理后。玩家资料写入令牌只供受信任后端使用，环境文件权限设为 `0600`。当前战斗握手按空闲席位分配，用于局域网或受控网络联调。公网玩家身份、房间票据和赛后入账留待联机业务接入。

## 战斗同步

战斗服从地图场景烘出碰撞层级，从已有配置生成只含规则和 ID 的资源目录。服务器和离线对局运行同一个 `LocalMatch`。一场比赛使用一张地图，通过同一 UDP 端口分配最多十个席位。固定模拟 50 Hz，权威快照默认 25 Hz。将 `ANIMECS_BATTLE_SNAPSHOT_HZ=50` 可让服务器在每个模拟 tick 后发送快照，发送间隔从 40 ms 缩短为 20 ms。v3 状态含本席位位置、朝向、生命、弹匣、最近事件 tick、比分和当前可见敌人。原有 v2 客户端可以继续使用单席位协议字段。

Unity 接入层为 `SeatDatagramClient`。在主循环 `Poll()`，读取 `View`，使用 `SamplePose` 取得显示姿态，并用 `Submit` 发送操作意图。插值缓冲按到达抖动调整，最大外推两 tick。自己的水平移动另通过 `PredictedPosition` 读取。新快照到达时先恢复权威位置和速度，再用相同的移动加速规则重放最多四个未确认 tick，碰撞和高度由权威位置校正。定制移动参数可在创建客户端时传入。客户端把看到的整数 tick 和 16 位小数连同输入送回。服务器用已发送快照、序号、轮次、RTT 和最多 200 ms 的历史窗口校验。历史命中检测只在同一生命期的相邻快照间插值，当前墙体继续参与遮挡。最近两个未确认输入可随下一次提交补发。

## 个人数据接口

`GET /health` 可单独检查进程。资料读写使用 `Authorization: Bearer <service-token>`。未设置令牌时资料接口返回 503。用 `GET /v1/players/{id}` 读取修订号和 ETag。内部服务使用 `PUT /v1/players/{id}` 写入以下形状的资料。

```json
{"inventory":[],"equipment":{},"victories":0,"avatarId":null}
```

首次创建带 `If-None-Match: *`，修改带上次读取的 `If-Match`。并发更新中只有匹配当前修订号的请求能写入，过期修改返回 412。库存值、装备槽和头像均使用稳定 ID。装备值必须存在于库存中。此版本预留资料格式和存储接口，正式配置表可以在后续数据库版本中迁移。

## 热更新数据发布

以拥有内容目录写权限的身份运行同一个 HotUpdate 程序，把准备好的数据目录发布为一个版本。发布命令和 HTTP 进程使用相同的 `ANIMECS_UPDATE_DIR`。服务运行时即可切换清单。

```sh
sudo -u animecs-hotupdate env ANIMECS_UPDATE_DIR=/var/lib/animecs-hotupdate \
  /opt/animecs/HotUpdate/AnimeCS.HotUpdate publish v1 /path/to/staged-data
```

`GET /v1/manifest` 返回 schema、版本和每个文件的相对路径、长度、SHA-256。下载地址为 `/v1/blobs/{sha256}`。消费端按长度和摘要校验文件后再采用该版本。所有内容写完才替换当前清单，同一版本 ID 禁止覆盖为不同内容。用原版本 ID 和原文件重新发布即可切回已有版本。HTTP 服务只读。

三个发布目录放在同一目录后，可以在 Debian 运行 `python3 Servers/verify.py /opt/animecs`。也可以在路径后指定要测试的服务名。检查在临时目录创建测试资料并启动独立进程，覆盖十个 UDP 席位、乱序与重复输入、资料并发写入及重启读回、热更新版本切换和分段下载。
