# 烽沙存档修改器

这是一个 Windows x64 存档工具，针对当前已验证的 `MOProject` 存档格式工作。它只修改选定存档槽的 `Mass.sav`、`Level.sav` 或 `Player.sav`，不会改游戏安装目录。

源码功能总表见 [`功能说明.md`](功能说明.md)。本项目全部修改功能免费开放；“捐赠”只展示作者提供的二维码和 QQ：35611294，不读取支付结果，也不参与功能解锁。

## 使用前

1. 可以在游戏运行中保存，但建议先暂停并避免游戏同时保存；工具不会替你关闭游戏。
2. 双击 `FengshaSaveEditor.exe`，或按下面的命令行示例运行。
3. 每次写入前都会复制整个槽位并记录每个文件的 SHA-256；候选文件会先完整解压回读，失败时会尽量从本次备份恢复被修改的文件。
4. 修改后再进入游戏并自行保存。存档模式针对当前已经存在的对象；之后新生成的民夫不会自动继承速度修改。

## 民夫与单位属性

工具会扫描 `Mass.sav` 中所有可确认的单位区域，一次性修改全部匹配字段，不是只改第一个民夫。支持民夫、弓锐士、盾锐士、戈锐士、投石车、床弩车、野猪、城防单位等已识别类型。

```text
FengshaSaveEditor.exe --slot 新存档_3 --speed 2000
FengshaSaveEditor.exe --slot 新存档_3 --unit 民夫 --attribute 攻击 --value 999 --yes
FengshaSaveEditor.exe --slot 新存档_3 --unit 民夫 --attribute 生命 --value 1000 --yes
FengshaSaveEditor.exe --slot 新存档_3 --list-attributes --unit 民夫
```

已确认的单位属性包括移速、生命/最大生命、攻击、护甲、护甲减伤、士气、质量、护盾、感知范围、搜索敌人范围、攻击距离、技能伤害参数、技能距离倍率、代理半径、寻路距离、维护值、模型比例等。当前游戏的单位和玩家属性有一部分位于 `FString` 键值表中，不是标准 GVAS `IntProperty`；工具会先校验标准属性头，或校验“名称长度 + 完整字段名 + 0 + 4 字节整数值”的本游戏键值布局，两个校验都不通过就拒绝写入。`--value` 写入的是存档原始整数；倍率或百分比字段请先用列表命令查看当前分布。

## 资源

工具会扫描 `Level.sav` 中当前已经保存的资源点，并将容量与当前数量一起修改。图形界面会把同一类资源按当前存档中的容量从小到大显示为“小型/中型/大型”；当前存档没有出现的规模不会伪造出来。左侧每一行代表一种资源和一种规模，未勾选批量选项时只修改这一行包含的全部资源点，右侧目标数量会优先回填当前值。

```text
FengshaSaveEditor.exe --slot 新存档_3 --list-resources
FengshaSaveEditor.exe --slot 新存档_3 --resource 铁矿 --resource-amount 9999999
FengshaSaveEditor.exe --slot 新存档_3 --resource 枣子林 --resource-lock --yes
FengshaSaveEditor.exe --slot 新存档_3 --resource 狩猎区域 --resource-amount 9999999 --yes
FengshaSaveEditor.exe --slot 新存档_3 --resource 铁矿 --resource-config 33536 --resource-amount 9999999 --yes
```

目前已识别的类别包括：铁矿、银矿、铜矿、锡矿、石料、原玉、盐、草药、枣子林、黏土、木材/树林、木炭、狩猎区域和兽材。命令行输出中的 `ConfigID` 仍用于区分同一类别的不同档位；图形界面不显示这个内部编号。

`--resource-lock` 是“大储量模式”，默认把容量和当前数量写成 `9999999`。它不是持续运行的内存锁定：采集后游戏仍可能扣减，保存后再次运行即可重新补满。图形界面另外提供“全部资源统一为 99,999”，一次处理所有类别和所有规模；若只想设置具体数值，使用 `--resource-amount N`。

## 玩家全局属性

`Player.sav` 中已确认的 `AT_` 参数可以整体修改。搬运相关字段已经找到：`AT_CartCapacity` 当前是 10，另有 `AT_CarryEfficiency`、采集效率和制作效率等。

```text
FengshaSaveEditor.exe --slot 新存档_3 --list-player-attributes
FengshaSaveEditor.exe --slot 新存档_3 --player-attribute 搬运容量 --player-value 100 --yes
FengshaSaveEditor.exe --slot 新存档_3 --player-attribute AT_CarryEfficiency --player-value 20000 --yes
```

工具会把同一玩家属性的全部匹配字段一起修改。`Carrying`、`ContainItems` 等字段目前确认是当前任务/携带清单，不是稳定的全局搬运上限，因此只读研究，不直接写入。

## 只读检查、备份与恢复

```text
FengshaSaveEditor.exe --slot 新存档_3 --verify
FengshaSaveEditor.exe --slot 新存档_3 --list-backups
FengshaSaveEditor.exe --restore <备份目录> --yes
FengshaSaveEditor.exe --slot 新存档_3 --scan-roads
```

`--verify` 会检查 VSOM CRC、Oodle 分块、GVAS 长度/魔数、全部当前民夫、资源点和玩家字段。道路入口仍然只读；当前没有把土路、夯土路、石板路加成与可安全写回的存档字段绑定，因此不会猜测写道路数据。

## Oodle DLL

发布目录中应与 EXE 放置 `oo2core_9_win64.dll`。也可以使用 `--oodle` 指定路径。DLL 只用于当前游戏存档的 Kraken 解压/压缩，不会修改游戏安装文件。

源码仓库不包含游戏 Oodle DLL、真实存档、个人收款二维码和从游戏提取的图标。构建时缺少这些可选资源不会影响源码编译；发行包需要使用者从自己的游戏目录提供 Oodle DLL，作者自己的二维码和图标只放在发行包中。具体见 `Assets/README.md` 和 `.gitignore`。

## 从源码构建

需要 Windows x64 和 .NET 8 SDK：

```powershell
dotnet build FengshaSaveEditor.csproj
dotnet run --project FengshaSaveEditor.csproj -- --help
```

源码默认不带作者的二维码和游戏图标，因此直接构建时捐赠页会显示占位提示；完整发行包中的二维码和图标不影响核心存档修改逻辑。

## 备份位置

图形界面选择的是整个存档文件夹，不是单个 .sav 文件。可以选择 ...\MOProject\Saved\SaveGames\，再从顶部槽位下拉框选择具体存档；也可以直接选择包含 Mass.sav 和 Level.sav 的槽位文件夹。顶部“文件 4/4”表示 Mass.sav、Level.sav、Player.sav、Slot.sav 找到了几个。

图形界面按“暗黑 2 存档编辑器”式逻辑组织：顶部打开/重新读取/保存，下面只保留“单位属性、资源、玩家属性、高级功能、捐赠”五个标签。进入某一页后，左边先选对象，右边再改数值；高级功能单独集中单位和玩家的高级属性修改，但与其他修改一样全部免费开放。界面显示存档当前值，不把 `0×3` 这类统计原样展示；倍率字段按“1 倍=游戏默认值”显示。资源页可按小型/中型/大型选择，也可一键把全部资源设为 99,999；该操作是一次性存档修改，采集后仍可能减少。游戏运行中也可写入，写入前会自动整槽备份并回读校验。

## 免费功能与捐赠

存档扫描、文件校验、资源按类别/规模批量设置、全部资源统一为 99,999、单位和玩家属性、高级功能、备份与恢复均免费开放，不需要支付激活，也没有本地授权开关。

“捐赠”标签只展示作者提供的微信/支付宝二维码和联系 QQ：35611294。捐赠完全自愿，不读取支付结果、不生成授权、不保存付款信息。二维码待作者提供后随发行包加入，详细说明见 `捐赠说明.md`。

备份在所选槽位对应的 `Saved\\FengshaSaveEditorBackups\\` 下。恢复前会再次备份当前槽位，并对备份清单逐文件做 SHA-256 校验。
