# Temporal Panic Button

**中文/ENGLISH**

# 中文

`Temporal Panic Button` 是一个给 **Casualties Unknown Demo** 使用的 BepInEx / Harmony 娱乐向时停模组。

它会在玩家触发地雷、炮台类陷阱，或按下手动按键时发动时停。时停期间，发动者可以行动、治疗、投掷和射击；世界、陷阱、怪物、炸药倒计时和未获得时停权限的玩家会被暂停。

## 功能概览

- 支持地雷、炮台 / 枪雷、手动按键触发时停。
- 默认启用智力成长模式：智力越高，时停越久，冷却越短。
- 支持 KrokMP 多人联机同步：多人可以重叠发动时停，只有当前拥有时停权限的玩家能动。
- 时停时有蓝色画面、冲击波、开始 / 结束特效、音效、攻击姿态和 Observer 当作替身。
- 时停期间暂停玩家流血、失去意识、身体数值变化。
- 时停期间可以治疗自己，并可选择无视疼痛和意识降低带来的医疗速度惩罚。
- 时停期间投掷物会停在空中，时停结束后继续按投掷方向和力度飞出。
- 时停期间枪械可以射击并产生延迟弹道表现，伤害在时停结束后结算。
- 时停期间会阻止部分陷阱继续触发或追踪，包括 jumppad、bear trap、coil、spike trap、sound cannon。
- 支持临时炸药功能：暂停 Dynamite 倒计时，并可剪短引线让下一次使用后几乎立刻爆炸。
- 返回主菜单时会清理时停画面、HUD、冷却和临时状态。

## 安装

1. 安装 BepInEx。
2. 将 `TemporalPanicButton.dll` 放入：

```text
BepInEx/plugins/
```

3. 启动游戏，在 `Settings -> Game` 中调整本模组设置。

默认没有开始、结束时停的音效！如需自定义音效，在 DLL 同目录下创建或使用自动生成的资源文件夹：

```text
BepInEx/plugins/TemporalPanicButtonAssets/
```

可放入以下文件：

```text
timestop_start.wav
timestop_start.ogg
timestop_end.wav
timestop_end.ogg
```

同名 `.wav` 和 `.ogg` 同时存在时，模组会优先加载可用文件。

## 设置项

设置位置：`Settings -> Game`

| 设置                         | 默认值 | 说明                                                       |
| ---------------------------- | ------ | ---------------------------------------------------------- |
| `Temporal Panic Button`      | 开启   | 总开关。关闭后不再触发时停。                               |
| `Intelligence Growth Mode`   | 开启   | 智力成长模式。开启后时停时间和冷却由智力决定。             |
| `Time Stop Duration`         | 5 秒   | 手动模式下的时停时长。智力成长模式开启时此项只显示锁定值。 |
| `Time Stop Cooldown`         | 15 秒  | 手动模式下的冷却时间。智力成长模式开启时此项只显示锁定值。 |
| `Trigger On Mines`           | 开启   | 踩到地雷时触发时停。                                       |
| `Trigger On Turrets`         | 开启   | 触发炮台 / 枪雷时触发时停。                                |
| `Manual Time Stop`           | 开启   | 允许按键手动触发时停。                                     |
| `Manual Time Stop Key`       | `V`    | 手动触发时停的按键。                                       |
| `Steady Medical Hands`       | 开启   | 时停中使用医疗物品时，忽略疼痛和意识降低造成的速度惩罚。   |
| `Time Stop Effect Intensity` | `1`    | 时停视觉特效强度，范围 `0 - 2`。                           |
| `Time Stop Sound Volume`     | `0.9`  | 时停音效音量，范围 `0 - 1`。                               |

## 智力成长模式

默认开启。开启后，玩家不能直接通过设置菜单决定时停时长和冷却，而是通过智力等级成长。

| 智力等级 | 效果                                    |
| -------- | --------------------------------------- |
| 低于 7   | 尚未习得时停，不能主动发动。            |
| 7        | 解锁时停，时长约 4 秒，冷却约 240 秒。  |
| 20       | 满级时停，时长约 20 秒，冷却约 120 秒。 |

7 到 20 级之间会缓慢成长，成长曲线不是完全线性，后期提升会更明显一些。

如果关闭 `Intelligence Growth Mode`，则使用 `Time Stop Duration` 和 `Time Stop Cooldown` 中设置的固定数值。

## 玩法说明

### 触发时停

- 踩到地雷。
- 触发炮台 / 枪雷。
- 按下手动时停键，默认是 `V`。

手动时停不会因为枪械菜单打开而被屏蔽，但暂停菜单、交易菜单、容器菜单等非正常游玩界面会阻止手动触发。

### 时停期间

- 发动者可以移动和播放正常移动动画。
- 世界中的大部分物体、陷阱、怪物和倒计时会暂停。
- 玩家身体状态会冻结，流血、失水、失去意识等数值不会继续恶化。
- 可以打开医疗界面治疗自己。
- 如果开启 `Steady Medical Hands`，医疗小游戏速度会按正常状态计算。
- 投掷物脱手后会浮空，时停结束后继续飞出。
- 枪械射击会产生时停弹道表现，实际命中和伤害在时停结束后结算。
- 屏幕顶部会显示自己的时停 / 冷却状态；如果别人正在时停而自己没有权限，会显示其他人的剩余时停时间。

### 时停结束

- 播放结束特效和音效。
- 延迟的炮台、枪械、投掷物和其他排队行为会继续结算。
- 没有其他玩家仍在发动时停时，世界恢复正常。

## 多人联机

本模组可以在单人环境中独立运行，也可以在安装 KrokMP 时尝试同步多人时停。

多人逻辑大致如下：

- A 发动时停时，A 可以动，B 会被暂停。
- 如果 B 在 A 的时停期间也发动时停，则 A 和 B 都可以动。
- 如果 A 的时停先结束，而 B 的时停仍在持续，则 A 会被暂停，B 继续行动。
- 当所有玩家的时停都结束后，世界恢复正常。

炮台 / 枪雷触发会尽量只归属给实际触发的玩家，避免同一个陷阱在多人视角里重复触发时停。

多人同步依赖 KrokMP 的运行状态，因此仍属于实验功能。如果没有安装或没有加载 KrokMP，本模组会按单人逻辑工作。

## 炸药临时功能

本模组包含一个临时的 Dynamite 引线功能，后续可能会删除或改成独立功能。

- 点燃后的 Dynamite 倒计时会在时停期间暂停。
- 手持 Dynamite 时，屏幕右侧会出现 `CUT FUSE` 按钮。
- 点击后，该 Dynamite 下一次使用时倒计时会变成约 `0.1` 秒。
- 该功能只针对 Dynamite，尽量不影响其他物品。

## 自行构建

示例构建命令：

```powershell
dotnet build TemporalPanicButton\TemporalPanicButton.csproj -c Release /p:GameManagedDir="E:\SteamLibrary\steamapps\common\Casualties Unknown Demo\CasualtiesUnknown_Data\Managed" /p:BepInExCoreDir="E:\SteamLibrary\steamapps\common\Casualties Unknown Demo\BepInEx\core"
```

构建产物：

```text
TemporalPanicButton/bin/Release/TemporalPanicButton.dll
```

## 兼容性说明

本模组会 patch 游戏中的多个陷阱、玩家、医疗、相机、设置和音效相关逻辑。主要目标是让时停期间的世界状态尽量稳定，并避免多人联机时同一事件重复触发。

已特别处理的内容包括：

- 地雷、炮台 / 枪雷。
- jumppad、bear trap、coil、spike trap、sound cannon。
- 玩家移动、动画、受伤状态、医疗小游戏。
- 投掷物、枪械延迟结算。
- Observer 演出。
- Dynamite 倒计时。
- KrokMP 相关多人同步和部分陷阱补丁。

如果与其他模组发生冲突，优先检查这些模组是否也修改了相同的陷阱、玩家身体状态、医疗小游戏、相机时间缩放或 KrokMP 同步逻辑。

## 已知说明

- 智力成长模式开启时，时停时间和冷却设置会被成长系统接管。
- KrokMP 多人同步仍然是实验功能，复杂网络环境下可能需要继续调试。
- `CUT FUSE` 是临时功能，后续可能会删除或单独拆分。
- 自定义音效文件不存在时，模组会使用静音或内置后备逻辑，不会阻止游戏启动。











# ENGLISH

# Temporal Panic Button

**Temporal Panic Button** is an entertainment-focused time-stop mod for **Casualties Unknown Demo**, built with BepInEx and Harmony.

The mod can automatically trigger a time stop when the player activates mines or turret-type traps, or manually through a hotkey. During a time stop, the user who triggered it can move, heal, throw items, and shoot normally, while the world, traps, monsters, explosive timers, and players without time-stop permission are frozen.

## Features

- Trigger time stop from mines, turrets/gun traps, or a manual hotkey.
- Intelligence Growth Mode enabled by default: higher Intelligence grants longer time stops and shorter cooldowns.
- Multiplayer synchronization support through KrokMP.
- Visual effects, shockwaves, start/end effects, sound effects, combat poses, and Observer stand-style effects.
- Prevents bleeding, unconsciousness progression, and body stat changes during time stop.
- Allows self-healing during time stop.
- Optional removal of pain and consciousness penalties while using medical items.
- Thrown objects freeze in mid-air and continue flying when time resumes.
- Firearms can shoot during time stop with delayed trajectory and damage resolution.
- Pauses or blocks various traps such as jumppads, bear traps, coils, spike traps, and sound cannons.
- Includes a temporary Dynamite fuse feature.

## Installation

1. Install BepInEx.
2. Place `TemporalPanicButton.dll` into:

```text
BepInEx/plugins/
```

3. Launch the game and configure the mod in:

```text
Settings -> Game
```

### Custom Audio

By default, no start/end sound effects are included.

Create or use the automatically generated folder:

```text
BepInEx/plugins/TemporalPanicButtonAssets/
```

Supported files:

```text
timestop_start.wav
timestop_start.ogg
timestop_end.wav
timestop_end.ogg
```

If both `.wav` and `.ogg` versions exist, the mod loads the first available file.

## Settings

| Setting                    | Default | Description                                    |
| -------------------------- | ------- | ---------------------------------------------- |
| Temporal Panic Button      | Enabled | Master switch for the mod.                     |
| Intelligence Growth Mode   | Enabled | Duration and cooldown scale with Intelligence. |
| Time Stop Duration         | 5 sec   | Fixed duration when growth mode is disabled.   |
| Time Stop Cooldown         | 15 sec  | Fixed cooldown when growth mode is disabled.   |
| Trigger On Mines           | Enabled | Trigger when stepping on mines.                |
| Trigger On Turrets         | Enabled | Trigger when activating turrets/gun traps.     |
| Manual Time Stop           | Enabled | Allows manual activation.                      |
| Manual Time Stop Key       | V       | Hotkey used to activate time stop.             |
| Steady Medical Hands       | Enabled | Ignore pain/consciousness healing penalties.   |
| Time Stop Effect Intensity | 1       | Visual effect intensity (0–2).                 |
| Time Stop Sound Volume     | 0.9     | Sound volume (0–1).                            |

## Intelligence Growth Mode

Enabled by default.

| Intelligence Level | Effect                                            |
| ------------------ | ------------------------------------------------- |
| Below 7            | Cannot use time stop.                             |
| 7                  | Unlocks time stop (~4s duration, ~240s cooldown). |
| 20                 | Maximum level (~20s duration, ~120s cooldown).    |

Progression between levels 7 and 20 is gradual and not completely linear.

When Intelligence Growth Mode is disabled, fixed values from the settings menu are used instead.

## Gameplay

### Triggering Time Stop

- Step on a mine.
- Activate a turret or gun trap.
- Press the manual activation key (default: `V`).

Manual activation is blocked while menus such as pause, trading, or containers are open.

### During Time Stop

- The activator can move freely.
- Most world objects, traps, monsters, and timers are frozen.
- Bleeding, dehydration, unconsciousness, and similar body states stop progressing.
- Self-healing is allowed.
- Medical mini-games can ignore penalties when enabled.
- Thrown items remain suspended in the air.
- Firearms generate delayed trajectory effects and resolve damage when time resumes.
- HUD displays remaining duration and cooldown information.

### When Time Resumes

- End effects and sounds play.
- Delayed projectiles, throws, and trap actions are resolved.
- The world returns to normal once all active time stops have ended.

## Multiplayer

The mod works in single-player and can synchronize time-stop behavior through KrokMP.

Example:

- Player A activates time stop → A can move, B is frozen.
- Player B activates time stop during A's time stop → both can move.
- A's time stop ends first → A freezes, B continues moving.
- Once all active time stops end → the world resumes.

Multiplayer synchronization is experimental and depends on KrokMP being installed and functioning correctly.

## Temporary Dynamite Feature

A temporary feature included in the mod:

- Dynamite countdowns pause during time stop.
- A `CUT FUSE` button appears while holding Dynamite.
- Cutting the fuse reduces the next countdown to approximately `0.1` seconds.
- Only affects Dynamite.

## Building

Example build command:

```powershell
dotnet build TemporalPanicButton\TemporalPanicButton.csproj -c Release /p:GameManagedDir="E:\SteamLibrary\steamapps\common\Casualties Unknown Demo\CasualtiesUnknown_Data\Managed" /p:BepInExCoreDir="E:\SteamLibrary\steamapps\common\Casualties Unknown Demo\BepInEx\core"
```

Output:

```text
TemporalPanicButton/bin/Release/TemporalPanicButton.dll
```

## Compatibility

The mod patches multiple gameplay systems to keep time-stop behavior stable:

- Mines and turret/gun traps.
- Jumppads, bear traps, coils, spike traps, and sound cannons.
- Player movement, animation, injuries, and medical systems.
- Projectiles and delayed firearm resolution.
- Observer effects.
- Dynamite timers.
- KrokMP synchronization patches.

If conflicts occur, check whether another mod modifies the same systems.

## Known Issues

- Intelligence Growth Mode overrides manual duration and cooldown settings.
- KrokMP synchronization is still experimental.
- `CUT FUSE` is a temporary feature and may be removed later.
- Missing custom sound files will not prevent the game from launching.
