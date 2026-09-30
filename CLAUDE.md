# 工作约定

## 改完怎么合并、怎么发版

- 在自己的分支上改，推上去开合并请求。合并进 main 之前 CI 要全部通过：版本号检查、单元测试和编译、Windows Server 2022 / 2025 上的启动测试和长按手势测试、一键更新的端到端测试。
- 要发版本，就在 CHANGELOG.md 最上面加一节新版本，格式：`## 0.2.0（2026-10-15）`，`Directory.Build.props` 里的 `Version` 跟着改（CI 会检查两者一致）。推到 main、CI 通过后，CI 发现这个版本还没有标签，就自动打标签、打包并发布正式版，已安装的 Pop 会提示更新。用户能感觉到的改动都要写进去；只改 CI、文档或测试的可以不加，不加就只合并、不发版。
- 版本号：新功能升次版本号（0.1.0 升到 0.2.0），只修问题升修订号（0.2.0 升到 0.2.1）。先看 main 上最新的版本和标签，别和别的分支撞号。
- 想先给少数人试用：在 Actions 页面手动运行 Release 工作流，勾选「作为测试版」，版本号是 `<Version>-beta.<运行编号>`，只有在菜单里打开「接收测试版」的用户会收到。

## 写法

- 代码注释、界面文字、README 和更新日志用中文；提交信息用英文。
- 项目里不要写「类似某某」「相当于某某的……」这种拿别的产品作比较的话，直接说功能本身。
- 和系统无关的逻辑放在 `src/Pop.Core` 并写单元测试；`src/Pop` 里只放调用 Windows 接口和界面的代码。
- 钩子回调（`InputHook`）里不能做耗时的事，也不能直接碰界面，统一 `Dispatcher.BeginInvoke` 到界面线程。
- CI 脚本用 PowerShell 7（`shell: pwsh`），Windows PowerShell 5.1 会把不带 BOM 的中文脚本读成乱码。

## 在 Linux / macOS 上开发

- `dotnet build src/Pop` 能编译（`EnableWindowsTargeting`），但要用微软官方的 .NET SDK：一些发行版自己编译的 SDK 没有 `Microsoft.NET.Sdk.WindowsDesktop`，会报找不到 WindowsDesktop.targets。
- 运行和界面效果只能在 Windows 上看：CI 的 `launch-smoke` 会把截图（圆盘弹出、高亮、结果卡片、短按弹出的系统菜单、深色外观）和 Pop 的日志推到 `ci-screenshots/windows-2022`、`ci-screenshots/windows-2025` 两个分支（每次覆盖），`git fetch origin ci-screenshots/windows-2022` 就能看到。
