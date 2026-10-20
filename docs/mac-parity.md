# macOS 使用体验对齐清单

基准：`whrss9527/pop` 的 `3a76d5c`。对齐目标是用户操作和结果处理方式；Windows 界面继续使用 WPF、Fluent 控件、Mica 与现有毛玻璃浮窗。

## 本次基础流程

- 原有长按右键、短按系统菜单、拖动放行、无焦点读取和剪贴板恢复继续使用。
- 圆盘扩展到 4–12 格；增加方向键与回车、数字 0、在圆心松开后继续点击选择、快捷键唤起。按应用程序名保存独立布局，没有配置的应用继承默认布局。
- 鼠标唤起可选择长按右键、修饰键右键、中键或关闭；点击唤起进入点击选择模式。
- 选择工具条支持拖选、双击和三击，沿用当前应用圆盘顺序、最多五个适用功能；「更多」沿用原选区继续打开圆盘。默认关闭，可在唤起设置中启用；只读辅助功能选区，必须有鼠标附近的选区位置，不自动模拟复制。输入、滚动、点击别处、切换应用和八秒不用会收起。
- 应用排除按程序名保存，同时覆盖鼠标唤起和工具条；全局快捷键仍可用。程序名在后台解析，钩子只读缓存；新窗口的身份未就绪时保留原来的点击。
- 设置与自定义 JSON 插件可以一并导出、预览后恢复；导入前自动备份，插件写入失败或设置保存失败时回滚。同 ID 更新，其他插件保留；本机凭据、剪贴板历史和开机启动状态不导出。设置迁移限定 Windows 备份，macOS JSON 插件仍可单独导入。
- 直达规则可以指定内置功能或自定义插件、调整顺序和开关；旧 directKinds 设置继续兼容。
- 圆盘和每个功能均可配置全局快捷键，支持冲突提示、清除、恢复默认和按功能搜索。
- 设置拆分为通用、唤起、规则、快捷键、圆盘等页面，窄窗口中的侧栏和编辑区可滚动或换行。
- 圆盘靠边朝内展开为弧，角落保留「更多」和返回菜单入口。菜单移动不会改变鼠标位置。
- 标注、取色、框选测量使用同一套功能注册、设置开关、圆盘与托盘入口。标注使用原图像素坐标，导出与预览共用绘制逻辑。
- 常用短语可带原始选区、剪贴板和日期变量回填原 App，设置中管理并随备份迁移。文字对比与保持唤醒使用统一的可输入工具窗口框架；JWT 在本机解码。浮窗支持粘贴中文查询和按字素退格。
- macOS 的 Liquid Glass 使用 Windows 毛玻璃浮窗和 Mica 编辑窗口表达；系统关闭动画时不播放展开动画。

## 尚未对齐的基础体验

英文界面、插件商店和按需下载插件仍待实现。直达规则已覆盖 Windows 当前支持的内容类型；选择图片、文件列表等更完整的内容输入仍需移植。Windows 原生编译插件不能直接加载 macOS 的 Swift 插件包；现有 JSON 插件继续通用。

屏幕标尺已支持自动边缘与手动拖动；标注已支持六种渐变背景、留白、圆角与阴影。截图标注编辑器需要激活以输入中文，圆盘、取色和结果卡片保持不激活。

## 验证

Linux 验证：Release 核心测试 364 项通过、3 项跳过（PowerShell/cmd 平台运行器未加入测试路径）；Release 编译和 win-x64 单文件发布无警告、无错误。Windows 的真实手势、新窗口和导出验证由 Windows Server 2022、2025 两套 CI 执行，必须检查对应提交的结果。本地编译成功不能代替实际操作；混合 DPI、多显示器及复杂第三方 App 仍需要人工体验检查。

`scripts/interaction-e2e.ps1` 已接入 Windows CI，覆盖独立圆盘、功能快捷键替换、Ctrl/Alt/Win 右键、中键直达、圆心保持与划选执行、角落更多及返回，以及工具条选词替换、更多保持原选区、Esc、拖选复制、输入收起和应用排除，并增加中文粘贴搜索、短语粘贴和剪贴板恢复、文字对比、保持唤醒、键盘取色、自动与手动标尺，共二十二项功能检查。工具条检查同时断言原应用焦点和剪贴板未被自动读取改动，点击后处理真实记事本选区。PowerShell 脚本在 Linux 使用 PowerShell 7 检查语法，实际执行状态单独报告。

Windows 演示命令：`Pop.exe --demo-shots artifacts --demo-scenes ring,toolbar,annotate,picker,snippets,utilities`。完整演示包含短语编辑、文字对比、保持唤醒、备份页面和工具条的深浅色截图；完整 CI 还覆盖真实手势和更新，标注场景验证导出尺寸和像素。

## 全部功能对照

“已有”表示存在对应实现，不表示所有细节均一致；“部分”需继续对齐子功能。“待移植”尚无对应 Windows 功能。

| macOS 功能 ID | Windows 功能 ID | 状态 |
| --- | --- | --- |
| `translate` | `translate` | 已有 |
| `search` | `search` | 已有 |
| `openURL` | `open` | 已有 |
| `calculate` | `calc` | 已有 |
| `copyPlain` | `copy` | 已有 |
| `formatJSON` | `json` | 已有 |
| `timestamp` | `time` | 已有 |
| `copyPath` | — | 待移植 |
| `revealInFinder` | — | 需要 Windows 适配 |
| `settings` | `settings` | 已有 |
| `dictionary` | — | 待移植 |
| `speak` | — | 待移植 |
| `changeCase` | `case` | 部分 |
| `encodeDecode` | `codec` | 已有 |
| `textStats` | `count` | 部分 |
| `hash` | `hash` | 已有 |
| `numberConvert` | `number` | 已有 |
| `colorConvert` | `color` | 已有 |
| `random` | `random` | 已有 |
| `qrCode` | — | 待移植 |
| `ocr` | — | 待移植 |
| `screenshotOCR` | `ocr` | 已有 |
| `colorPicker` | `colorPicker` | 已有 |
| `openInTerminal` | — | 待移植 |
| `quickNote` | — | 待移植 |
| `clipboardHistory` | `clipboard` | 部分 |
| `allPlugins` | `all` | 部分 |
| `unitConvert` | `unit` | 已有 |
| `textCleanup` | `cleanup` | 已有 |
| `screenshotTranslate` | — | 待移植 |
| `pin` | `pin` | 部分 |
| `removeBackground` | — | 待移植 |
| `airDrop` | — | 需要 Windows 适配 |
| `aiAssistant` | — | 待移植 |
| `aiPolish` | — | 待移植 |
| `aiSummarize` | — | 待移植 |
| `aiExplain` | — | 待移植 |
| `windowLayout` | — | 待移植 |
| `imageConvert` | — | 待移植 |
| `linkInspect` | — | 待移植 |
| `jwtDecode` | `jwtDecode` | 已有 |
| `markdownCopy` | — | 待移植 |
| `tableConvert` | `tableConvert` | 已有 |
| `zip` | — | 待移植 |
| `unzip` | — | 待移植 |
| `snippets` | `snippets` | 已有 |
| `annotate` | `annotate` | 已有 |
| `textDiff` | `textDiff` | 已有 |
| `pdf` | — | 待移植 |
| `scanCode` | — | 待移植 |
| `keepAwake` | `keepAwake` | 已有 |
| `palette` | — | 待移植 |
| `openWith` | — | 待移植 |
| `shelf` | — | 待移植 |
| `numberStats` | `numberStats` | 已有 |
| `spellCheck` | — | 待移植 |
| `ruler` | `ruler` | 已有 |
| `cron` | — | 待移植 |
| `contrast` | `color` | 已有 |
| `markdownPreview` | — | 待移植 |
| `timer` | — | 待移植 |
| `fileInfo` | — | 待移植 |
| `codeImage` | — | 待移植 |
| `extractInfo` | `extract` | 部分 |
| `lineTools` | `lines` | 部分 |
| `toMarkdown` | — | 待移植 |
| `jsonTypes` | — | 待移植 |
| `charInfo` | — | 待移植 |
| `regexTest` | — | 待移植 |
| `reminder` | — | 待移植 |
| `tableOCR` | — | 待移植 |
| `stitchImages` | — | 待移植 |
| `videoConvert` | — | 待移植 |
| `batchRename` | — | 待移植 |
| `yamlJSON` | — | 待移植 |
| `formatXML` | — | 待移植 |
| `formatSQL` | — | 待移植 |
| `base64Image` | — | 待移植 |
| `vocabulary` | — | 待移植 |
| `dateSpan` | — | 待移植 |
| `folderTree` | — | 待移植 |
| `markdownTOC` | — | 待移植 |
| `findDuplicates` | — | 待移植 |
| `diskUsage` | — | 待移植 |
| `codeStats` | — | 待移植 |
| `trimMedia` | — | 待移植 |
| `watermark` | — | 待移植 |
| `compareFolders` | — | 待移植 |
| `compareFiles` | — | 待移植 |
| `sendToPhone` | — | 待移植 |
| `idNumber` | — | 待移植 |
| `transcribe` | — | 待移植 |
| `idPhoto` | — | 待移植 |
| `webCapture` | — | 待移植 |
| `cropImage` | — | 待移植 |
| `redact` | — | 待移植 |
| `screenRecord` | — | 待移植 |
| `systemActions` | — | 待移植 |
| `textImage` | — | 待移植 |
| `menuShortcuts` | — | 需要 Windows 适配 |
| `scrollCapture` | — | 待移植 |
| `largeType` | — | 待移植 |
| `cleanKeyboard` | — | 待移植 |
| `showKeystrokes` | — | 待移植 |
| `screenPen` | — | 待移植 |
| `cameraBubble` | — | 待移植 |
| `pointerHighlight` | — | 待移植 |
| `teleprompter` | — | 待移植 |
| `spotlight` | — | 待移植 |
| `beautify` | — | 待移植 |
| `zoom` | — | 待移植 |
| `compareImages` | — | 待移植 |
| `splitImage` | — | 待移植 |
| `quitApps` | — | 待移植 |
| `tidyFolder` | — | 待移植 |
| `appIcon` | — | 待移植 |
| `uninstallApp` | — | 需要 Windows 适配 |
| `newFile` | — | 待移植 |
| `fileEncoding` | — | 待移植 |
| `appInfo` | — | 待移植 |
| `speakToFile` | — | 待移植 |
| `similarPhotos` | — | 待移植 |
| `mediaInfo` | — | 待移植 |
| `subtitles` | — | 待移植 |
| `fontPreview` | — | 待移植 |
| `encryptFiles` | — | 待移植 |
| `batteryInfo` | — | 待移植 |
| `voiceRecorder` | — | 待移植 |
| `systemInfo` | — | 待移植 |
| `soundDevices` | — | 待移植 |
| `resolution` | — | 待移植 |
| `diskSpeed` | — | 待移植 |
| `worldTime` | — | 待移植 |
| `chart` | — | 待移植 |
| `focusSounds` | — | 待移植 |
| `emojiSymbols` | — | 待移植 |
| `bluetooth` | — | 待移植 |
| `calendar` | — | 待移植 |
| `breakReminder` | — | 待移植 |
| `windowPiP` | — | 待移植 |
| `mouseWheel` | — | 待移植 |
| `holdToQuit` | — | 需要 Windows 适配 |
