# Pop for Windows

Pop 的 Windows 版原型：在任意 App 里**长按鼠标右键**，Pop 读取你选中的文字，弹出**圆形功能菜单**，按住右键往某个方向一划、松开就执行那一格的功能，在圆心松开就关闭。

这是第一个原型，先验证最关键的一条链路：长按右键 → 读取选中内容 → 圆盘 → 执行，外加一键更新。

## 现在能做什么

| 模块 | 说明 |
| --- | --- |
| 唤起 | 长按右键（默认 250 毫秒，可在设置里调整）；短按仍是系统右键菜单，按住拖动超过 6 像素也照常交给 App |
| 读取选中内容 | 先用 Windows 原生的 UI Automation 读（带超时）；读不到再模拟 Ctrl+C，读完把剪贴板原样放回去，临时内容不进 Windows 的剪贴板历史；终端窗口里不模拟 Ctrl+C（那会结束正在运行的程序） |
| 直接出结果 | 选中外文、算式、带单位的数值（5 km、100°F、2 斤、16 GB）、颜色值（#RRGGBB、rgb()、hsl()）、Unix 时间戳时，长按右键直接弹出结果卡片（外文直接显示译文），不弹圆盘；哪些内容直接出结果可以逐项开关 |
| 内容识别 | 中文 / 外文、单个词、链接、邮箱、JSON、算式、带单位的数值、数字（含 0x/0b/0o）、颜色值、日期时间、Unix 时间戳、本机路径 |
| 圆盘 | 默认 6 格：复制、搜索（选中链接、邮箱或路径时换成「打开」）、翻译、大写、字数、全部功能，可以改成 4–8 格、每格换成别的功能。划向一格松开执行，圆心松开关闭；右键按着时按数字键直选，Esc 关闭。读不到文字时需要文字的格子变灰 |
| 翻译 | 译文显示在结果卡片里，可以复制或替换原文，不用离开当前的 App。默认用必应翻译（不用设置）；也可以填自己的 Microsoft Translator Key（加密保存在本机），或者改成在浏览器里打开。默认中文译成英语、其他语言译成简体中文，也可以固定译成某种语言 |
| 功能开关 | 每个功能都可以单独关掉：关掉的功能不出现在圆盘和「全部功能」列表里，圆盘上空出来的格子自动换成别的功能，也不再直接出它的结果 |
| 全部功能 | 圆盘左上角那一格：功能列表，直接打字搜索（拼音首字母或英文），方向键选择、回车执行，也可以用鼠标点；当前内容用不了的排在后面。列表不抢焦点，按键由键盘钩子转给它，所以不能用输入法打中文搜索 |
| 文字工具 | 大小写写法、编码转换（Base64、URL、Unicode、HTML）、JSON 格式化（保持键的顺序）、文字整理、按行处理、提取信息（链接、邮箱、电话、IP）、哈希、随机生成、数字转换、日期转换 |
| 截图识字 | Win+Alt+O：框选屏幕区域，系统自带的离线文字识别认出文字，显示在卡片上 |
| 截图贴图 | Win+Alt+P：框选的区域贴在所有窗口最前面；拖动、滚轮缩放、Alt+滚轮调透明度，双击或 Esc 关闭，右键识别文字或存到「下载」 |
| 剪贴板历史 | Win+Alt+V 打开：文字、图片、文件都能记；打字搜索，回车粘贴，Ctrl+1–9 快速粘贴，Delete 删除，Ctrl+P 固定；本机 SQLite 存储，默认保存 30 天、最多 500 条，固定的不清理；标了「不要记录」的内容和密码管理器复制的内容不记 |
| 结果卡片 | 一行一个结果，点一行复制这一行，回车复制主要结果；算式结果和大写、小写可以直接替换原文（粘贴回原来的 App，粘贴完剪贴板恢复原样）；Esc 或点别处关闭 |
| 界面 | 圆盘、卡片和列表是毛玻璃底，图标用 Fluent System Icons；西文用 Segoe UI Variable（Windows 10 上是 Segoe UI），中文用随包带的 Noto Sans CJK；圆盘从指针处弹开，高亮沿着圆环滑到指针所指的那一格；卡片从指针所在的角长出来；不抢焦点，靠近屏幕边缘自动内移；跟随系统的深浅色和主题色；多显示器、不同缩放比例下位置准确 |
| 托盘面板 | 点任务栏右下角的 Pop 图标弹出：长按唤起、直接出结果、剪贴板记录三个开关，剪贴板历史、截图识字、截图贴图的入口和快捷键，有新版本时一键更新，设置和退出 |
| 设置窗口 | 从托盘面板打开，或者 Pop 已经在运行时再运行一次 Pop.exe。Windows 11 上是 Mica 窗口。分通用、圆盘（带预览）、功能、翻译（可以当场试一试）、剪贴板、关于几页 |
| 一键更新 | 启动 15 秒后检查一次，之后每 6 小时一次，直接读 GitHub Releases；发现新版本时托盘图标右下角加一个橙色圆点并发一条通知，不打断你。点「更新到……」后下载安装包、比对 SHA-256 校验和、确认版本号，再替换 Pop.exe 并自动重新启动 |

## 安装

到 [Releases](https://github.com/whrss9527/pop-windows/releases) 下载最新的 `Pop-<版本>-win-x64.zip`，解压出 `Pop.exe`，放到一个自己的文件夹里（例如 `%LOCALAPPDATA%\Programs\Pop`），双击运行。Pop 自带运行时，不用另装 .NET。

- 一键更新需要能写入 Pop.exe 所在的文件夹，**不要放在 Program Files 里**；
- 安装包还没有代码签名，第一次运行时 Windows 可能提示「已保护你的电脑」，点「更多信息」→「仍要运行」；
- 需要 Windows 10 1809 或更新的版本（64 位）。

## 工作原理（关键点）

**长按右键怎么做到不影响正常右键？** 用低级鼠标钩子（`WH_MOUSE_LL`）先把「右键按下」扣住并开始计时：

```
右键按下 → 扣住，开始计时（默认 250ms）
 ├─ 计时内松开          → 用 SendInput 按原顺序补发「按下 + 松开」→ 系统右键菜单照常弹出
 ├─ 按住拖动超过 6 像素 → 补发按下并放行拖动
 └─ 计时到              → 这次按压归 Pop：弹出圆盘，同时读取选中内容
```

补发的事件带有标记（`dwExtraInfo`），回到钩子时直接放行。钩子跑在单独的线程上，回调里只做判定，界面上的事都交给界面线程，钩子不会因为超时被系统摘掉。判定逻辑在 `src/Pop.Core/PressTracker.cs`，有单元测试。

**浮窗为什么不抢焦点？** 圆盘和卡片带 `WS_EX_NOACTIVATE`：原来的 App 一直在前台，选区不会丢，模拟的 Ctrl+C / Ctrl+V 也发给它。圆盘还带 `WS_EX_TRANSPARENT`，鼠标穿过去，手势全靠钩子。

**一键更新怎么替换正在运行的 exe？** Windows 不允许覆盖正在运行的程序，但允许改名：先把自己改名成 `Pop.exe.old`，把校验过的新文件放到原来的位置，启动新版本后退出；新版本启动时删掉 `.old`。放新文件失败时把原来的改回来，保证 Pop 还能启动。当前版本带代码签名时，还要求新版本是同一个证书主体签的有效签名。

**毛玻璃怎么做的？** 浮窗是透明的分层窗口，用不了系统的亚克力效果，所以弹出前先截下它后面的那块屏幕，模糊之后当底，再叠一层半透明的底色和一圈细描边（`Frost.cs`）。这样在 Windows 10 和 11 上看起来一样。设置窗口是普通窗口，Windows 11 上直接用系统的 Mica。

**已知的限制**

- 以管理员身份运行的窗口里读不到选中内容、也收不到模拟按键（系统的权限隔离），需要 `uiAccess` 和代码签名，原型先不做；
- 有的 App 在没有选中内容时按 Ctrl+C 会复制整行（例如一些代码编辑器），这时会被当成选中了这一行；
- 安装包大约 60 MB（自带 .NET 运行时和 WPF）。

## 数据存在哪里

| 数据 | 位置 |
| --- | --- |
| 设置 | `%APPDATA%\Pop\settings.json` |
| Microsoft Translator 的 Key | `%APPDATA%\Pop\secrets.json`，用 Windows 的数据保护接口（DPAPI）加密，只有当前用户能解开 |
| 剪贴板历史 | `%LOCALAPPDATA%\Pop\Clipboard\history.sqlite`（WAL 模式），图片存成 PNG 放在旁边的 `Images` 文件夹 |
| 日志 | `%LOCALAPPDATA%\Pop\logs\pop.log`（超过 1 MB 轮换）；选中的文字不写进日志 |
| 更新下载 | `%LOCALAPPDATA%\Pop\updates`，替换完成后删除 |
| 开机启动 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 里的 `Pop` |

## 开发

需要 .NET 8 SDK。界面部分只能在 Windows 上运行，但在 Linux / macOS 上也能编译（`EnableWindowsTargeting`），和系统无关的逻辑（`src/Pop.Core`）的单元测试在任何系统上都能跑。

```bash
dotnet test tests/Pop.Core.Tests          # 单元测试：长按判定、圆盘几何、版本号、发布列表、校验和、更新包、替换 exe、设置
dotnet build src/Pop                       # 编译
dotnet run --project src/Pop               # 在 Windows 上运行
dotnet publish src/Pop -c Release -o out   # 打包成单个 Pop.exe
```

| 目录 | 内容 |
| --- | --- |
| `src/Pop.Core` | 和系统无关的逻辑，都有单元测试 |
| `src/Pop` | Windows 部分：钩子（`InputHook`）、模拟输入、读取选中内容、剪贴板、圆盘和卡片、托盘、一键更新、代码签名检查 |
| `tests/Pop.Core.Tests` | 单元测试 |
| `scripts` | CI 用的 PowerShell 脚本；`make-icon.py` 生成图标，`make-fonts.py` 生成中文字体的常用字子集 |

调试时可以用这些环境变量：

| 变量 | 作用 |
| --- | --- |
| `POP_APPEARANCE=dark` / `light` | 强制深色 / 浅色外观 |
| `POP_ANIMATION_SCALE=6` | 动画放慢 6 倍，方便看中间帧 |
| `POP_UPDATE_FEED` | 更新信息的地址，可以是本机的 JSON 文件（格式同 GitHub Releases 接口），测试一键更新用 |
| `POP_LOG_SELECTION=1` | 把读到的选中文字写进日志 |
| `POP_SMOKE_MARKER` / `POP_SMOKE_EXIT=1` | 自动化测试：把启动和更新结果写进这个文件；启动完立即退出 |

`Pop.exe --demo-shots 文件夹` 是演示模式：不装钩子、不改设置，在一张示例文档前面把圆盘、各种结果卡片、全部功能列表、剪贴板历史、托盘面板、设置窗口的每一页、贴图和框选依次显示出来，每个截一张图（`light-ring.png`、`dark-settings-general.png`……）存到这个文件夹，然后退出；`--demo-scenes ring,cards` 只拍其中几组（ring、cards、list、history、flyout、settings、pin、region）。

## CI 和发版

每次推送，GitHub Actions 会在 Windows 上：跑单元测试、编译 Release 版 Pop.exe；在 Windows Server 2022 和 2025 上真正启动一次，再用模拟的鼠标在记事本、Chrome 和 Edge 里走一遍长按右键（替换原文、结果卡片、翻译、圆心关闭、短按弹出系统右键菜单、浏览器里读取选中文字），然后用演示模式把每个界面在浅色、深色下各截一张图；截图和 Pop 的日志推到 `ci-screenshots/windows-2022`、`ci-screenshots/windows-2025` 两个分支（每次覆盖，界面截图在 `ui` 文件夹里，`git fetch origin ci-screenshots/windows-2022` 就能看到）；最后用本机的假发布把一键更新完整走一遍（校验和不对要拒绝、正常版本要替换并重新启动、已是最新时不更新）。

发版：在 `CHANGELOG.md` 最上面加一节新版本，`Directory.Build.props` 里的 `Version` 跟着改，合并进 main、CI 通过后会自动打标签并发布，已安装的 Pop 会提示更新。

## 第三方组件

| 组件 | 用途 | 许可 |
| --- | --- | --- |
| [Noto Sans CJK](https://github.com/notofonts/noto-cjk) | 界面里的中文字体（常用字子集，Regular 和 Medium） | SIL Open Font License 1.1，全文在 `src/Pop/Assets/Fonts/LICENSE-OFL.txt`，设置窗口「关于」里也能打开 |
| [WPF-UI](https://github.com/lepoco/wpfui) | 设置窗口的控件和 Mica、Fluent System Icons 图标 | MIT |
| [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore) 和 [SQLitePCLRaw](https://github.com/ericsink/SQLitePCL.raw) | 剪贴板历史的数据库 | MIT；SQLitePCLRaw 是 Apache 2.0 |
| [Interop.UIAutomationClient](https://github.com/Roemer/UIAutomation-Interop) | 原生 UI Automation 的 COM 接口 | MIT |
