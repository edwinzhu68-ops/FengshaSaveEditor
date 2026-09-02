# 烽沙存档修改器

Windows x64 存档修改工具。打开一个《烽沙》存档槽后，在单位、建筑、玩家和高级功能页面中修改数值，最后点击一次“保存修改”统一写入并完整回读校验。

## 功能

| 模块 | 可以修改什么 |
| --- | --- |
| 单位属性 | 移速、生命、最大生命、攻击、护甲、护甲减伤、士气、质量、护盾、感知范围、搜索敌人范围、攻击距离、技能伤害、技能距离倍率、代理半径、寻路距离、维护值、模型比例等已确认整数属性 |
| 民夫 | 一次修改当前存档中全部已识别民夫的属性，不是只改一个民夫 |
| 自有兵种 | 批量修改已识别的民夫、常规兵种和攻城器械；支持全部匹配区域或单个匹配区域 |
| 玩家属性 | 搬运容量、搬运效率、采集效率、制作效率、政策移速等已确认的玩家全局 `AT_` 属性 |
| 资源 | 铁矿、银矿、铜矿、锡矿、石料、原玉、盐、草药、枣子林、黏土、木材/树林、木炭、狩猎区域、兽材 |
| 资源规模 | 按资源类别和小型/中型/大型规模修改全部匹配资源点 |
| 资源批量 | 按 1/2/5/10/20/50/100 倍提高资源点最大上限，并同步补满当前数量 |
| 真正仓库 | 辎重库、粮仓、军械库；每座建筑按自己的现有储存类别上限分别放大，不补出不存在的类别 |
| 生产建筑 | 已确认每座建筑有不同的产物、原料、消耗品容量；当前只做结构研究，不猜测写入 |
| 备份恢复 | 支持列出、校验和恢复已有备份；工具不会自动备份 |
| 文件校验 | 校验 VSOM CRC、Oodle 分块、GVAS 长度/魔数，并对写回文件完整解压回读 |

## 图形界面

1. 先手动复制一份存档槽作为备份，再双击 `FengshaSaveEditor.exe`。
2. 工具默认读取 `%LOCALAPPDATA%\\MOProject\\Saved\\SaveGames`。
3. 顶部选择存档槽；如果没有找到，再选择整个 `SaveGames` 文件夹或具体槽位文件夹。
4. 在“单位属性”“建筑”“玩家属性”或“高级功能”页面选择修改范围，在顶部“修改倍数”下拉框选择 1/2/5/10/20/50/100 倍；所有修改暂存后，点击顶部“保存修改”统一写入。

“建筑”页面把资源和仓库放在同一页：上半区是资源，下半区是真正仓库。仓库范围固定为全部辎重库、粮仓和军械库。每个仓库只修改存档里已经存在的储存类别；例如某座生产建筑没有消耗品仓库，不会因为放大而凭空创建。

一个存档槽由多个 `.sav` 文件组成，不要只选择某一个文件。工具不会修改游戏安装目录。

## 常用命令

```text
FengshaSaveEditor.exe --list
FengshaSaveEditor.exe --slot 新存档_3 --speed 2000 --yes
FengshaSaveEditor.exe --slot 新存档_3 --unit 民夫 --attribute 移速 --value 2000 --yes
FengshaSaveEditor.exe --slot 新存档_3 --unit 民夫 --attribute 生命 --value 1000 --yes
FengshaSaveEditor.exe --slot 新存档_3 --resource all --resource-max --yes
FengshaSaveEditor.exe --slot 新存档_3 --resource all --resource-multiplier 100 --yes
FengshaSaveEditor.exe --slot 新存档_3 --resource 铁矿 --resource-multiplier 10 --yes
FengshaSaveEditor.exe --slot 新存档_3 --building-multiplier 2 --yes
FengshaSaveEditor.exe --slot 新存档_3 --player-attribute 搬运容量 --player-value 100 --yes
```

只读检查：

```text
FengshaSaveEditor.exe --slot 新存档_3 --list-units
FengshaSaveEditor.exe --slot 新存档_3 --list-attributes --unit 民夫
FengshaSaveEditor.exe --slot 新存档_3 --list-player-attributes
FengshaSaveEditor.exe --slot 新存档_3 --list-resources
FengshaSaveEditor.exe --slot 新存档_3 --list-building-storage
FengshaSaveEditor.exe --slot 新存档_3 --verify
FengshaSaveEditor.exe --list-backups
```

预览不写文件：在修改命令末尾加 `--dry-run`。`--yes` 只跳过确认，不会跳过写回后的完整校验；工具不会自动备份，请先手动备份存档。

`--resource-amount` 仅为旧命令保留，填写的是存档原始值；普通用户请在图形界面使用顶部倍数下拉框，或使用 `--resource-multiplier`。

## 当前边界

* 修改对象是当前存档中已经存在的区域；以后新生成的民夫不会自动继承旧修改。
* “全部自有兵种”按已识别的单位类型筛选，不包含野兽、建筑和城防设施。
* 顶部倍数会按当前存档中每个属性/资源的读取值计算；资源倍数会同时修改资源点最大上限和当前数量。1 倍资源操作表示补满当前上限，2/5/10/20/50/100 倍会扩大上限。
* 真正仓库倍数会按每个仓库、每个现有储存类别的当前上限分别计算；0 容量项目保持未启用，不会被创建成新仓库。
* 当前存档的“流寇入侵”不属于资源点扫描范围。资源修改只接受通过 `ResourceSaveID + ConfigID + Capacity + Items` 完整校验的资源记录，不会把 `Invasion/Bandit/Raid` 事件字段当成资源写入。
* 资源存档内部使用固定换算值，工具会自动按游戏规则换算，不要直接填写原始值 99,999。
* 单位、玩家和高级属性中的开关类字段不是倍率属性，会保持开/关值；其他已识别数值属性按当前读取值乘以所选倍数。
* 生产建筑的 `Capacity` 字段已经定位，但产物、原料、消耗品数量因建筑类型而异；在完成逐类验证前只读，不猜测写入。
* 道路速度、单位模板、BuffConfig 等配置层字段目前只读，不猜测写入。
* 游戏运行中可以写入，但如果游戏同时保存，游戏保存可能覆盖工具刚写入的结果。

## 从源码构建

需要 Windows x64 和 .NET 8 SDK：

```powershell
dotnet build FengshaSaveEditor.csproj
dotnet run --project FengshaSaveEditor.csproj -- --help
```

GitHub 的 Windows x64 压缩包已经配好运行所需的 Oodle DLL，解压后可直接运行；源码仓库不包含该运行库，也不上传真实存档。自行从源码构建时，请准备《烽沙》运行所需的 `oo2core_9_win64.dll` 并放在 EXE 同目录，或使用 `--oodle` 指定路径。

## 许可证

当前仓库没有附加开源许可证。代码可以查看和审核，但未授权他人自动商用、再发布或制作衍生发行包。
