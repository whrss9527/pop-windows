# 工作约定

## 改完怎么合并、怎么发版

- 在自己的分支上改，推上去开合并请求。合并进 main 之前 CI 要全部通过：版本号检查、单元测试和编译、Windows Server 2022 / 2025 上的启动测试、长按手势测试和界面截图、一键更新的端到端测试。
- 要发版本，就在 CHANGELOG.md 最上面加一节新版本，格式：`## 0.2.0（2026-10-15）`，`Directory.Build.props` 里的 `Version` 跟着改（CI 会检查两者一致）。推到 main、CI 通过后，CI 发现这个版本还没有标签，就自动打标签、打包并发布正式版，已安装的 Pop 会提示更新。用户能感觉到的改动都要写进去；只改 CI、文档或测试的可以不加，不加就只合并、不发版。
- 版本号：新功能升次版本号（0.1.0 升到 0.2.0），只修问题升修订号（0.2.0 升到 0.2.1）。先看 main 上最新的版本和标签，别和别的分支撞号。
- 想先给少数人试用：在 Actions 页面手动运行 Release 工作流，勾选「作为测试版」，版本号是 `<Version>-beta.<运行编号>`，只有在菜单里打开「接收测试版」的用户会收到。

## 写法

- 代码注释、界面文字、README 和更新日志用中文；提交信息用英文。
- 项目里不要写「类似某某」「相当于某某的……」这种拿别的产品作比较的话，直接说功能本身。
- 和系统无关的逻辑放在 `src/Pop.Core` 并写单元测试；`src/Pop` 里只放调用 Windows 接口和界面的代码。
- 钩子回调（`InputHook`）里不能做耗时的事，也不能直接碰界面，统一 `Dispatcher.BeginInvoke` 到界面线程。
- 读取其他 App 的界面用原生 UI Automation（`NativeAutomation.cs`，`Interop.UIAutomationClient` 包），不要用 .NET 自带的 `System.Windows.Automation`：它读 Chrome 时会在原生代码里崩溃，整个进程直接退出。
- CI 脚本用 PowerShell 7（`shell: pwsh`），Windows PowerShell 5.1 会把不带 BOM 的中文脚本读成乱码。

## 在 Linux / macOS 上开发

- `dotnet build src/Pop` 能编译（`EnableWindowsTargeting`），但要用微软官方的 .NET SDK：一些发行版自己编译的 SDK 没有 `Microsoft.NET.Sdk.WindowsDesktop`，会报找不到 WindowsDesktop.targets。
- 运行和界面效果只能在 Windows 上看：CI 的 `launch-smoke` 会把截图（圆盘弹出、高亮、结果卡片、短按弹出的系统菜单、深色外观）和 Pop 的日志推到 `ci-screenshots/windows-2022`、`ci-screenshots/windows-2025` 两个分支（每次覆盖），`git fetch origin ci-screenshots/windows-2022` 就能看到。
- 改了界面就看 `ui` 文件夹里的截图：CI 用演示模式（`Pop.exe --demo-shots 文件夹`）在一张示例文档前面把圆盘、各种卡片、列表、剪贴板历史、托盘面板、设置窗口每一页、贴图、框选依次显示出来，浅色、深色各拍一张（`light-*.png`、`dark-*.png`）。新加的界面要在 `Demo.cs` 里加一个场景。

## 界面

- 配色、字号、圆角都在 `Theme.cs`，浮窗（圆盘、卡片、列表、托盘面板）用它；设置窗口用 WPF-UI 的主题资源（`SetResourceReference` 引用 `TextFillColorPrimaryBrush`、`CardBackgroundFillColorDefaultBrush` 这些键），跟着深浅色自动变。
- 毛玻璃在 `Frost.cs`：浮窗是透明的分层窗口，用不了系统的亚克力，弹出前截下后面那块屏幕模糊当底。
- 图标用 WPF-UI 带的 Fluent System Icons（`Icons.Make`，名字是 `SymbolRegular` 的枚举名，比如 `Copy24`）；功能的图标名写在 `Pop.Core` 里。
- 字体：`Theme.TextFont` 先用 Segoe UI Variable / Segoe UI 显示西文，中文落到随包带的 Noto Sans CJK SC（`src/Pop/Assets/Fonts`，常用字子集，Regular 和 Medium 两个字重，`scripts/make-fonts.py` 生成），子集里没有的字再用微软雅黑。强调用 `FontWeights.Medium` 或 `SemiBold`，别用 `Bold`：中文只带到 Medium，Bold 会被系统加粗得发糊。
- 不要调用 WPF-UI 的 `ApplicationThemeManager.Apply` 而不先换掉 `Application.MainWindow`：它会改主窗口的窗口样式，而主窗口默认是第一个创建的浮窗。`App.ApplyAppearance` 里已经处理好了。
- 自定义插件的核心在 `src/Pop.Core/Plugins`（格式、模板、匹配、运行、文件夹），文件格式要和 macOS 版逐字节一致（有测试对比）；界面在 `SettingsWindow` 的「我的插件」和 `PluginEditorWindow`。插件的 PopAction 由 `Actions.SetPlugins` 放进 `Actions.List`，判断内置功能的 ID 用 `Actions.IsBuiltIn`，不要用 `Actions.Find`（它也找得到插件）。改到 `Actions` 的全局插件列表的测试放在 `Actions registry` 这个不并行的测试集合里。
- 长按手势测试把 `directKinds` 里的 `foreign` 去掉了（选中英文默认直接翻译，不弹圆盘），要测直接翻译得另外改设置。
