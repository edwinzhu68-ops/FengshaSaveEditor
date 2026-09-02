# 烽沙存档修改器

Windows x64 存档修改工具。打开一个《烽沙》存档槽后，可以批量修改单位、资源和玩家属性，并在统一保存后完整校验。

## 功能

| 模块 | 可以修改什么 |
| --- | --- |
| 单位属性 | 移速、生命、最大生命、攻击、护甲、护甲减伤、士气、质量、护盾、感知范围、搜索敌人范围、攻击距离、技能伤害、技能距离倍率、代理半径、寻路距离、维护值、模型比例等已确认整数属性 |
| 民夫 | 一次修改当前存档中全部已识别民夫的属性，不是只改一个民夫 |
| 自有兵种 | 批量修改已识别的民夫、常规兵种和攻城器械；支持全部匹配区域或单个匹配区域 |
| 玩家属性 | 搬运容量、搬运效率、采集效率、制作效率、政策移速等已确认的玩家全局 `AT_` 属性 |
| 资源 | 铁矿、银矿、铜矿、锡矿、石料、原玉、盐、草药、枣子林、黏土、木材/树林、木炭、狩猎区域、兽材 |
| 资源规模 | 按资源类别和小型/中型/大型规模修改全部匹配资源点 |
| 资源批量 | 一次把全部已识别资源点补满到各自最大容量，不修改资源最大容量 |
| 备份恢复 | 支持列出、校验和恢复已有备份；工具不会自动备份 |
| 文件校验 | 校验 VSOM CRC、Oodle 分块、GVAS 长度/魔数，并对写回文件完整解压回读 |

## 图形界面

1. 先手动复制一份存档槽作为备份，再双击 `FengshaSaveEditor.exe`。
2. 工具默认读取 `%LOCALAPPDATA%\\MOProject\\Saved\\SaveGames`。
3. 顶部选择存档槽；如果没有找到，再选择整个 `SaveGames` 文件夹或具体槽位文件夹。
4. 在“单位属性”“资源”“玩家属性”“高级功能”标签中选择对象、输入目标值、预览；所有修改暂存后，点击顶部“保存修改”统一写入。

一个存档槽由多个 `.sav` 文件组成，不要只选择某一个文件。工具不会修改游戏安装目录。

## 常用命令

```text
FengshaSaveEditor.exe --list
FengshaSaveEditor.exe --slot 新存档_3 --speed 2000 --yes
FengshaSaveEditor.exe --slot 新存档_3 --unit 民夫 --attribute 移速 --value 2000 --yes
FengshaSaveEditor.exe --slot 新存档_3 --unit 民夫 --attribute 生命 --value 1000 --yes
FengshaSaveEditor.exe --slot 新存档_3 --resource all --resource-max --yes
FengshaSaveEditor.exe --slot 新存档_3 --resource 铁矿 --resource-amount 9999999 --yes
FengshaSaveEditor.exe --slot 新存档_3 --player-attribute 搬运容量 --player-value 100 --yes
```

只读检查：

```text
FengshaSaveEditor.exe --slot 新存档_3 --list-units
FengshaSaveEditor.exe --slot 新存档_3 --list-attributes --unit 民夫
FengshaSaveEditor.exe --slot 新存档_3 --list-player-attributes
FengshaSaveEditor.exe --slot 新存档_3 --list-resources
FengshaSaveEditor.exe --slot 新存档_3 --verify
FengshaSaveEditor.exe --list-backups
```

预览不写文件：在修改命令末尾加 `--dry-run`。`--yes` 只跳过确认，不会跳过写回后的完整校验；工具不会自动备份，请先手动备份存档。

## 当前边界

* 修改对象是当前存档中已经存在的区域；以后新生成的民夫不会自动继承旧修改。
* “全部自有兵种”按已识别的单位类型筛选，不包含野兽、建筑和城防设施。
* 全部资源补满到各自最大容量是一次性存档修改，采集后仍可能减少，不是常驻内存锁定。
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
