# AnimeCS Demo 联机与部署

同一套服务可供本机两个客户端、局域网的两台电脑或 Debian 13 x86-64 主机使用。一个 Data 进程管理同一世界的账号和队伍。当前一个 Battle 进程承载一场十席位对局，每队大厅最多五人。热更新内容可以单独部署。

| 角色 | 默认地址 | 持久数据 |
| --- | --- | --- |
| Data | HTTP 127.0.0.1:27841 | SQLite 账号、会话、库存、配置、好友、队伍、聊天、每日任务、胜场 |
| Battle | UDP 127.0.0.1:27840 | 50 Hz 权威模拟，25/50 Hz 快照，持久化入场、回合任务事件和终局结算 |
| HotUpdate | HTTP 127.0.0.1:27842 | 不可变内容、各版本清单、当前发布指针 |

## Windows 本地试玩

在包含 `Services/Data`、`Services/HotUpdate`、`Services/Battle` 的游戏目录启动 `AnimeCS.exe`。编辑器优先使用工程根目录的 `Services`，也支持指向本机配套构建的目录联接。该目录不存在时使用 `Builds/Windows/Services`。默认端点下，登录时启动数据服务，随后检查热更新，队长发起联机时启动战斗服务。服务在后台运行，同一 Windows 用户启动的两个客户端共享数据库。首次没有热更清单时直接使用内置资源。

Unity 的 Windows 客户端构建（包括 Build And Run）会把已构建的三项本机服务复制到游戏旁的 `Services`。首次使用选择“注册并进入”，玩家 ID 支持 2 至 32 个字母、汉字、数字或 `.`、`-`、`_`，密码长度为 8 至 128 个字符。再次启动后用同一组凭据登录即可。

注册时填写 2–32 个字符的唯一 ID 和 8–128 个字符的密码。成员 A 用 B 的 ID 添加好友、邀请，B 在社交面板接受。两人能在大厅看见相同的队伍，队长开始后，两人各持有一个绑定自己席位的票据。密码使用独立随机盐和 PBKDF2 保存。新账号的星币、抽取、商品与每日任务取自发布包 Data 目录里的 `game-catalog.json`，购买只消耗 Demo 星币。任务按 UTC 零点换日，经验与奖励在同一数据库事务内发放。

本机持久数据放在 `%LOCALAPPDATA%/AnimeCS/Services`，其中 `Data/players.db` 是账号库，`HotUpdate` 是发布目录，`battle.key` 是本机服务间密钥，`battle.log` 是战斗日志。游戏退出后保留同机服务，其他客户端可继续联机。需要更新本机服务时，通过任务管理器退出相应的 AnimeCS.Data、AnimeCS.HotUpdate 和 AnimeCS.Battle 进程再替换程序。游戏的已验证更新文件在 Unity 的 `persistentDataPath/Updates`。

## 两台 Windows 电脑联机

主机在 PowerShell 7 运行下面的脚本，将 `192.168.1.20` 替换为自己网卡的 IPv4 地址。脚本将服务绑定到该地址，生成独立的 LAN 密钥，状态保存在 `%LOCALAPPDATA%/AnimeCS/LanServices`。控制台按 Ctrl+C 停止本次启动的进程。两台电脑应运行同一版游戏和资源。

```powershell
./Servers/start-lan.ps1 -LanAddress 192.168.1.20
```

将脚本在主机控制台显示的配置通过可信渠道复制到两台电脑各自 `AnimeCS.exe` 旁的 `animecs-services.json`。也可用进程环境变量 `ANIMECS_DATA_ENDPOINT`、`ANIMECS_UPDATE_ENDPOINT` 和 `ANIMECS_ALLOW_PRIVATE_HTTP=1` 配置。HTTP 会在这个网段内以明文传送账号和会话凭证，只在自己控制的可信局域网开启。客户端接受回环地址，以及显式启用的私有 IPv4 地址或 IPv6 唯一本地地址。公网与其他主机名使用 HTTPS。账号、热更和战斗服回传使用配置的最终地址，网关应直接返回响应。主机的专用网络防火墙应允许来自本机和同网段客户端的 UDP 27840、TCP 27841 和 TCP 27842。

```json
{"dataUrl":"http://192.168.1.20:27841","updateUrl":"http://192.168.1.20:27842","allowPrivateHttp":true}
```

## 构建

联机发布时把客户端、Data 和 Battle 配套更新。入场票据签入地图 ID 和玩法内容版本。Data 的 `ANIMECS_BATTLE_MAP` 须与 Battle 烘焙的地图一致。客户端和战斗服默认读取 `MatchFormat.ContentRevision`，发布不兼容的玩法数据时同步递增。Data 默认使用同一个值，独立配置时可设置 `ANIMECS_BATTLE_CONTENT_VERSION`。

真人首次持有效票据入场时获得一次“进入对局”进度。每回合比分增加时，战斗服为当时连接的真人席位记录一次“完成回合”。Data 按事件发生的 UTC 日期对照冻结名单登记，以房间、回合序号和席位去重。战斗服先将事件写入状态目录，数据服务恢复后可补传。终局按实际入场名单给获胜方记录胜场。升级前暂存的终局文件仍可重传，旧文件按其中可用的冻结名单信息结算一次。

房间预订的空闲窗口为一小时。已分配席位首次入场、回合首次完成时，Data 以权威事件时间延长当前有效房间的截止时间。刷新大厅或重新连接时签发的票据使用同一房间、代次和最新截止时间。超过空闲窗口或主动结束房间后，迟到的任务事件仍按发生日入账，房间继续处于关闭状态。

小队可停留在结果页，队长点击“重新开赛”后会取得新房间，队员自动跟随。对局菜单里的“返回大厅”只退出个人客户端，队长另有结束房间操作。Windows 客户端自动启动的本机数据服务启用 `ANIMECS_LOCAL_PRACTICE_PROGRESS=1` 以记录训练和人机任务。独立部署默认只接收战斗服的联机任务事件。

在 Unity 菜单 `AnimeCS/任务/每日任务编辑器` 编辑目标、事件、经验和星币奖励。保存会更新 `Servers/Data/game-catalog.json`。重新构建即可带入新配置，也可以替换已部署 Data 目录内的同名文件并重启数据服务。任务 ID 在日进度和领奖记录中用作稳定标识。

Debian 完整重编需要 .NET 10 SDK、Python 3、Unity 6000.5.5f1 和 Linux Dedicated Server Build Support。若编辑器已打开该工程，可传 `-UnityProject` 指向独立工作副本。通过 `-BattleMap map.dust2` 选择战斗服场景；若改地图，同步修改下方 Data 配置中的 `ANIMECS_BATTLE_MAP`。

```powershell
./Servers/build.ps1 -UnityEditor 'C:/Program Files/Unity/Hub/Editor/6000.5.5f1/Editor/Unity.exe' -BattleMap map.dust2
```

安装 Windows Dedicated Server Build Support 后，可以用同一脚本重建 Windows 本地联机所需的三个服务。

```powershell
./Servers/build.ps1 -Platform Windows -UnityEditor 'C:/Program Files/Unity/Hub/Editor/6000.5.5f1/Editor/Unity.exe' -BattleMap map.dust2
```

战斗服务使用 Unity 的无图形构建，Windows 入口是 `Services/Battle/AnimeCS.Battle.exe`；启动参数为 `-batchmode -nographics`。

## Debian 13 安装

从 `Builds/Debian13/Packages` 复制三个 tar.gz 到服务器。以下命令适用于首次在同一台 Debian 13 主机安装。要将服务拆到不同的机器，分别安装需要的包，将相同的战斗密钥文件安全分发到 Data 与 Battle 两台主机，并设置 Battle 到 Data 的内网地址。

```sh
sudo apt install ca-certificates libc6 libgcc-s1 libgssapi-krb5-2 libicu76 libssl3t64 libstdc++6 tzdata openssl
sudo install -d /opt/animecs /etc/animecs
for role in Battle Data HotUpdate; do
  sudo tar -xzf "AnimeCS-${role}-debian13-x64.tar.gz" -C /opt/animecs
  sudo install -m 644 /opt/animecs/"$role"/*.service /etc/systemd/system/
done
sudo sh -c 'umask 077; if test ! -e /etc/animecs/battle-key.env; then echo "ANIMECS_BATTLE_SECRET=$(openssl rand -hex 32)" > /etc/animecs/battle-key.env; fi'
```

.NET 服务自带 10.0.15 LTS 运行时，系统库随发行版安装。以后更新服务包时，可在 `Servers/Directory.Build.props` 升级自包含运行时补丁并重新发布。[Microsoft 的 Debian 依赖说明](https://learn.microsoft.com/en-us/dotnet/core/install/linux-debian#dependencies) · [运行时维护版本](https://dotnet.microsoft.com/en-us/platform/support/policy)

`/etc/animecs/data.env` 的示例内容如下。将 `battle.example.com` 换成客户端实际可访问的战斗主机域名或 IPv4 地址。

```ini
ANIMECS_DATA_URL=http://127.0.0.1:27841
ANIMECS_BATTLE_PUBLIC_HOST=battle.example.com
ANIMECS_BATTLE_PORT=27840
ANIMECS_BATTLE_MAP=map.dust2
```

`/etc/animecs/battle.env` 的示例内容如下。若跨机配置，结算地址应指向受保护的数据服务。

```ini
ANIMECS_BATTLE_ADDRESS=0.0.0.0
ANIMECS_BATTLE_PORT=27840
ANIMECS_BATTLE_SNAPSHOT_HZ=25
ANIMECS_BATTLE_DATA_ENDPOINT=http://127.0.0.1:27841
```

`/etc/animecs/hotupdate.env` 可设置 `ANIMECS_UPDATE_URL=http://127.0.0.1:27842`。三个 systemd 单元各自创建 `/var/lib/animecs-data`、`/var/lib/animecs-battle`、`/var/lib/animecs-hotupdate`。密钥文件仅由 systemd 读取，无需给服务用户放宽文件权限。

```sh
sudo systemctl daemon-reload
sudo systemctl enable --now animecs-data animecs-hotupdate animecs-battle
curl -fsS http://127.0.0.1:27841/health
curl -fsS http://127.0.0.1:27842/health
sudo journalctl -u animecs-battle -n 30
```

对外提供 HTTPS 数据和资源端点。反向代理仅转发账号域名的 `/v2/` 及资源域名的 `/v1/`，战斗结算的 `/internal/` 与旧版管理接口仅限内网。网关需转发 UDP 27840 到 Battle。客户端配置文件可写为下面的形状。

两个 Web 服务也支持由 Kestrel 直接终止 TLS。将对应的 `ANIMECS_DATA_URL` 或 `ANIMECS_UPDATE_URL` 设为 HTTPS 监听地址，并为该服务进程配置 `Kestrel__Certificates__Default__Path` 与受保护的 `Kestrel__Certificates__Default__Password`。证书域名和信任链应与客户端使用的服务地址相符。

```json
{"dataUrl":"https://account.example.com","updateUrl":"https://assets.example.com"}
```

## 热更新发布与恢复

启动 HotUpdate 后，用同一内容目录发布只读资源。路径指向服务用户可读的数据源目录，与发布存储目录分开。版本 ID 一经发布即固定，可用旧版本和原始文件再次发布以回退。发布命令在主机控制台输出 `updateManifestSha256`，内容为当次完整清单的 SHA-256。客户端先验证清单来源，再逐一验证文件长度和摘要，全部通过后才原子切换 `AccountClient.UpdateDirectory`。各更新地址使用独立缓存，验证失败时保留上一个已验证版本。

HTTPS 发布使用客户端配置的域名和系统证书信任链。需要固定某次发行内容时，将发布命令输出的 64 位十六进制指纹随受信安装包或其他可信渠道交付，在 `animecs-services.json` 设置 `updateManifestSha256`，或设置进程环境变量 `ANIMECS_UPDATE_MANIFEST_SHA256`。指纹配置在客户端本机，远端清单无法更换它。每次发布或回退到不同内容时，固定指纹的客户端需要通过同一可信渠道更换配置。

默认同机服务从当前用户的本地发布目录独立读取清单指纹。显式指定的 HTTP 服务有新资源时需要预先配置指纹。可信 LAN 在主机已发布资源时运行 `start-lan.ps1`，脚本会将本地读取的指纹加入显示的客户端配置。首次启动且没有发布清单时，两个客户端可直接使用内置资源。战斗服跨机回传默认使用 HTTPS。需要在可信私网使用 HTTP 时将 `ANIMECS_ALLOW_PRIVATE_HTTP=1` 设到 Battle 服务进程。

```sh
sudo -u animecs-hotupdate env ANIMECS_UPDATE_DIR=/var/lib/animecs-hotupdate \
  /opt/animecs/HotUpdate/AnimeCS.HotUpdate publish demo-v1 /srv/animecs-staging/demo-v1
curl -fsS http://127.0.0.1:27842/v1/manifest
```

升级服务程序前，用 SQLite 的在线备份命令备份完整资料库，或停机后备份整个数据目录，包含 WAL 与 SHM。保留热更新目录和密钥。替换程序后重启对应单元。在线备份示例使用单独安装的 `sqlite3`。

```sh
sudo sqlite3 /var/lib/animecs-data/players.db ".backup '/var/backups/animecs-players.db'"
```

## 协议与当前范围

- 数据服务将账号会话令牌的摘要入库，校验资料展示和装备的所有权。旧版 `/v1/players/{id}` 的修订号/ETag 管理接口继续兼容原有独立资料；要开放该接口，需另设受信服务用的 `ANIMECS_DATA_TOKEN`。新版注册保留旧玩家已占用的 ID。
- Data 预订战斗服，冻结队员库存和出战配置，签发带房间代次、账号、席位和有效时间的 HMAC 票据。Battle 校验后发放 UDP 会话标识，从受保护的内部接口取出该场衣柜，按阵营解析服装与涂装。对局使用固定 tick、子 tick 时间戳、客户端预测校正和快照缓冲。公网对战的 UDP 加密、跨区调度和多个战斗实例的匹配可在正式上线阶段扩展。
- 个人战绩以胜场为单位，段位分预留。联机入场和回合任务由战斗服通过内部认证接口实时报告。重复登录同一战局、服务重启及延迟重传均按原事件去重。
- 一台战斗服一次接受一个大厅的票据，同一队伍成员进入同阵营，空席由目标型机器人补位。真人接管获配席位后即可输入，断连超过时限后机器人补位；全部真人离线时暂停模拟。队长可结束联机预订，过期预订会自动释放。商店商品、角色随机权重及任务编辑结果以 Data 的游戏目录为准。
- 新版本游戏校验内容并准备缓存；新增的资源类型仍需关联对应的运行时加载器。热更新端点支持按内容摘要下载与 Range 续传。

## 复验

测试使用独立临时数据库、随机回环端口及随机凭证，会自行启动并停止子进程。以下命令分别覆盖旧协议、热更发布/回退能力，以及两账号联机。Windows 也可将目录替换为 `Builds/Windows/Services`。测试工具需随源代码中的 `Servers/verify.py` 与 `Servers/verify_accounts.py` 一起使用。

```sh
python3 Servers/verify.py /opt/animecs
python3 Servers/verify_accounts.py /opt/animecs --battle
```

安装 OpenSSL 命令行后，执行 `python3 Servers/verify.py /opt/animecs Data HotUpdate --tls` 可复验两个服务直接使用 HTTPS。测试仅在临时目录生成证书，通过测试进程自己的信任链建立连接，系统证书库保持原配置。

Windows 可进一步用两套实际游戏窗口复验。先用 `AnimeCS.Editor.DesktopClientBuild.BuildNetworkQA` 构建 `Builds/QAClient`，再运行下面的命令。脚本将两端截图及 `verified.json` 写入 `Builds/RenderChecks/GraphicalParty`。

```powershell
py -3 Servers/verify_graphical_party.py --player Builds/QAClient/AnimeCS.exe --services Builds/Windows/Services
```

运行器支持三种复测方式。加 `--finish-match` 会验证结算与队长重开。加 `--verify-facing` 会让两端各转向队友一次，随后核对停手时的本地镜头、权威朝向和对方模型。要自己操作两个窗口，可在工程根目录运行下面的命令。账号完成注册、组队和入场后，鼠标与键盘由当前选中的窗口接收，关闭两个窗口即结束这次会话。

```powershell
py -3 -B Servers/verify_graphical_party.py --player Builds/QA/Fix-20261127-01/Client/AnimeCS.exe --services Builds/Windows-Fixed/Services --manual-play
```
