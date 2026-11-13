# ProjectDock

按项目给窗口分组的小工具：托盘图标点一下，面板从屏幕右下角向上展开，Unity、VSCode、
文件夹、终端、浏览器按项目自动归类，点一下就能切过去。

**简体中文** | [English](#english)

![ProjectDock 面板](docs/images/panel.png)

拖拽窗口行到别的分组：目标组高亮，松手即归组。

![拖动分组](docs/images/drag.png)

---

## 中文

### 为什么是托盘图标

Win11 的任务栏已经删掉了 deskband 扩展点，第三方没法把自定义控件塞进任务栏本体。
硬改任务栏（注入 explorer）不稳定，也不适合日常开发用。

所以做法是：**常驻托盘 + 点击图标在任务栏正上方弹面板**。视觉上仍然贴着屏幕右下角，
一个图标就能呼出所有项目分组。

### 功能

- 托盘图标左键开合面板，右键菜单：打开面板 / 刷新 / 开机启动 / 退出
- **全局快捷键**：默认 `Alt+F2`，按一下呼出面板，再按一下收起；可自定义
- 面板从工作区右下角向上弹出，高度按分组数量自适应，最多 720px，超出滚动
- **自动分组**：从窗口标题里识别项目名
  - VSCode / VSCode Insiders / Cursor / Windsurf：取标题中倒数第二段
  - Unity / Unity Hub：取 `项目名 - 场景 - Unity 版本` 里的项目名
  - 资源管理器：取非系统目录的文件夹名
  - Visual Studio：取 `解决方案 - Visual Studio` 里的解决方案名
  - 自动组最多 10 个，空组会自动回收
- **手动分组**：右键任意窗口 → 移到「某组」或新建组并移入
- **拖拽分组**：按住窗口行拖到别的分组即可移动；组内拖动可调顺序；目标组会高亮
- **拖拽排序**：按住分组标题上下拖动，可调整分组顺序
- 手动移动会写成规则（进程名 + 关键词），下次同类窗口自动归位，重启后仍然生效
- 分组右键：重命名、删除、折叠 / 展开、新建项目组
- 窗口行左键：切到前台并收起面板；右侧 `×`：关闭该窗口
- 当前前台窗口所在的分组会高亮
- 单实例运行，重复启动只会唤出已有面板
- 中文深色界面，Per-Monitor V2 高 DPI

### 使用

1. 运行 `dist\ProjectDock.exe`，程序常驻托盘，不占任务栏。
2. 按 `Alt+F2` 或左键点托盘图标，面板在屏幕右下角向上弹出。
3. 点窗口行即可切换；`Esc` 或点面板外收起。
4. 想按项目整理：右键窗口 → 「新建项目组并移入…」，输入项目名。
5. 也可以直接按住窗口行拖到目标分组上松手；拖到分组标题处会放到该组末尾。
6. 下次打开同项目的窗口，会自动落到这个组里。

### 快捷键说明

默认 `Alt+F2`，不会和 Windows 自带快捷键冲突。点面板右上角的 ⌨ 图标，
或托盘右键 →「快捷键…」，按下新的组合键即可
（至少包含 `Ctrl` / `Alt` / `Shift` / `Win` 中的一个）。

如果把快捷键设成 `Alt+F4`，它会被本程序接管——窗口不再被关闭，界面上会有橙色提示。

改完之后旧的组合键立刻恢复系统默认行为，新组合键全局生效，保存在
`%APPDATA%\ProjectDock\settings.json`。

![快捷键设置](docs/images/hotkey.png)

### 配置

分组与规则保存在：

```
%APPDATA%\ProjectDock\groups.json
```

快捷键等设置保存在：

```
%APPDATA%\ProjectDock\settings.json
```

写入采用临时文件 + 覆盖的方式，写坏不会影响已有配置。删掉这个文件即可恢复默认。

开机启动通过 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 注册，写在当前用户下，
不需要管理员权限；托盘右键菜单里可以随时开关。

### 从源码构建

需要 .NET 10 SDK（WPF + WinForms）。

```powershell
dotnet build src\ProjectDock.csproj -c Debug
dotnet run --project src\ProjectDock.csproj -- --show
```

发布单文件：

```powershell
.\publish.ps1                 # 依赖已安装的 .NET 10 桌面运行时，体积小
.\publish.ps1 -SelfContained  # 自带运行时，体积大，拷走即用
```

输出在 `dist\`。

### 已知限制

- 任务栏本身不可定制，所以入口是托盘图标，不是任务栏按钮。
- 枚举的是顶层可见窗口：UWP / 商店应用、部分游戏全屏窗口可能不出现。
- 以管理员权限运行的窗口，需要本程序同样以管理员身份启动才能激活或关闭。
- 只做了单屏定位（取主屏工作区）；多屏时面板固定出现在主屏右下角。

---

## English

ProjectDock groups your open windows by project and keeps them one click away in the
system tray. Click the tray icon and a panel slides up from the bottom-right corner,
listing Unity, VSCode, Explorer, terminal and browser windows under the project they
belong to.

### Why a tray icon

Windows 11 removed the taskbar deskband extension point, so third-party apps can no
longer host custom UI inside the taskbar itself. Patching explorer is fragile and not
worth it for a daily tool. ProjectDock therefore lives in the notification area and
opens its panel directly above the taskbar corner — visually the same place, without
touching the shell.

### Features

- Tray left click toggles the panel; right click offers open / refresh / run at startup / exit
- Global hotkey (default `Alt+F2`, customizable) toggles the panel
- Panel grows upward from the work-area corner, auto-sizes, scrolls past 720px
- Automatic grouping from window titles (VSCode, Cursor, Unity, Explorer, Visual Studio)
- Manual grouping: right click a window, move it to a group or create one
- Drag a window row onto another group to move it, drag within a group to reorder,
  drag group headers to reorder the groups themselves
- Manual moves persist as rules (process name + keyword) and survive restarts
- Rename, delete, collapse groups; close or focus a single window
- The group owning the foreground window is highlighted
- Single instance, dark UI, Per-Monitor V2 DPI aware

### Config

`%APPDATA%\ProjectDock\groups.json` — written atomically, safe to delete.

### Build

```powershell
dotnet build src\ProjectDock.csproj -c Debug
.\publish.ps1                 # framework-dependent
.\publish.ps1 -SelfContained  # self-contained single file
```
