# 界面截图：用演示模式（Pop.exe --demo-shots）把圆盘、结果卡片、列表、剪贴板历史、托盘面板、
# 设置窗口的每一页、贴图和框选依次显示出来截图，浅色、深色各一遍。出错或没跑完就失败
param(
    [Parameter(Mandatory)][string]$Exe,
    [Parameter(Mandatory)][string]$OutDir
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# runner 默认关了「透明效果」，设置窗口的 Mica 会变成纯色
try {
    Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name EnableTransparency -Value 1 -Type DWord
} catch {
    Write-Host "打开透明效果失败：$_"
}

$failures = [Collections.Generic.List[string]]::new()
foreach ($appearance in 'light', 'dark') {
    $marker = Join-Path $env:RUNNER_TEMP "pop-demo-$appearance.txt"
    Remove-Item $marker -ErrorAction SilentlyContinue
    $env:POP_SMOKE_MARKER = $marker
    $env:POP_APPEARANCE = $appearance
    $p = $null
    try {
        $p = Start-Process -FilePath $Exe -ArgumentList '--demo-shots', "`"$OutDir`"" -PassThru
        if (-not $p.WaitForExit(240000)) {
            $p.Kill()
            throw "演示模式（${appearance}）四分钟内没有结束"
        }
    } catch {
        $failures.Add("${appearance}: $_")
    } finally {
        Remove-Item Env:POP_SMOKE_MARKER, Env:POP_APPEARANCE -ErrorAction SilentlyContinue
    }
    if (Test-Path $marker) { Copy-Item $marker (Join-Path $OutDir "$appearance-demo.txt") }
    $popLog = Join-Path $env:LOCALAPPDATA 'Pop\logs\pop.log'
    if (Test-Path $popLog) { Copy-Item $popLog (Join-Path $OutDir "$appearance-pop.log") }
    $lines = @(Get-Content $marker -ErrorAction SilentlyContinue)
    Write-Host ($lines -join "`n")
    if ($null -ne $p -and $p.HasExited -and $p.ExitCode -ne 0) { $failures.Add("演示模式（${appearance}）退出码 $($p.ExitCode)") }
    if (-not ($lines -match 'demo-done')) { $failures.Add("演示模式（${appearance}）没有跑完") }
    if (-not ($lines -match 'copy-feedback=ok')) { $failures.Add("演示模式（${appearance}）复制失败与重试验证没有完成") }
    if (-not ($lines -match 'copy-feedback-large=ok')) { $failures.Add("演示模式（${appearance}）长结果完整复制与显示缓存释放验证没有完成") }
    if (-not ($lines -match 'settings-save-feedback=ok')) { $failures.Add("演示模式（${appearance}）设置保存反馈验证没有完成") }
    if (-not ($lines -match 'settings-search=ok')) { $failures.Add("演示模式（${appearance}）设置搜索与跳转验证没有完成") }
    if (-not ($lines -match 'settings-targets=ok')) { $failures.Add("演示模式（${appearance}）具体设置定位、键盘接续、条件区域及原配置保护验证没有完成") }
    if (-not ($lines -match 'result-reading=ok')) { $failures.Add("演示模式（${appearance}）长结果有限排版、全文复制替换、分页及关闭释放验证没有完成") }
    if (-not ($lines -match 'tools-settings=ok')) { $failures.Add("演示模式（${appearance}）功能开关计数与批量状态验证没有完成") }
    if (-not ($lines -match 'window-workarea=ok')) { $failures.Add("演示模式（${appearance}）工作区适配、移动结束、焦点和窗口状态保护验证没有完成") }
    if (-not ($lines -match 'deferred-results=ok')) { $failures.Add("演示模式（${appearance}）异步结果归属、真实请求取消、关闭和重试保护验证没有完成") }
    if (-not ($lines -match 'usage-guide=ok')) { $failures.Add("演示模式（${appearance}）使用指南状态、配置保护与跳转验证没有完成") }
    if (-not ($lines -match 'icons-compatible=ok')) { $failures.Add("演示模式（${appearance}）图标兼容性与回退验证没有完成") }
    if (-not ($lines -match 'clipboard-ownership=ok')) { $failures.Add("演示模式（${appearance}）原剪贴板恢复与新复制内容保护验证没有完成") }
    if (-not ($lines -match 'clipboard-recording=ok')) { $failures.Add("演示模式（${appearance}）暂停记录停止读取、取消旧通知和重新启用验证没有完成") }
    if (-not ($lines -match 'clipboard-policy=ok')) { $failures.Add("演示模式（${appearance}）剪贴板隐私检查与正文读取一致性验证没有完成") }
    if (-not ($lines -match 'selection-copy=ok')) { $failures.Add("演示模式（${appearance}）选区复制来源、锁等待、新复制保留和取消验证没有完成") }
    if (-not ($lines -match 'history-filters=ok')) { $failures.Add("演示模式（${appearance}）剪贴板分类与固定筛选验证没有完成") }
    if (-not ($lines -match 'backup-preview=ok')) { $failures.Add("演示模式（${appearance}）备份变化预览与取消验证没有完成") }
    if (-not ($lines -match 'settings-batching=ok')) { $failures.Add("演示模式（${appearance}）设置连续修改、手动保存与关闭补存验证没有完成") }
    if (-not ($lines -match 'settings-recovery=ok')) { $failures.Add("演示模式（${appearance}）设置原文件保留、导入保护和重新读取验证没有完成") }
    if (-not ($lines -match 'startup-feedback=ok')) { $failures.Add("演示模式（${appearance}）开机启动实际状态、失败反馈和重新读取验证没有完成") }
    if (-not ($lines -match 'list-navigation=ok')) { $failures.Add("演示模式（${appearance}）列表增量更新验证没有完成") }
    if (-not ($lines -match 'list-selection=ok')) { $failures.Add("演示模式（${appearance}）列表搜索与快捷选择验证没有完成") }
    if (-not ($lines -match 'list-content=ok')) { $failures.Add("演示模式（${appearance}）文件与短语性能验证没有完成") }
    if (-not ($lines -match 'native-tray=ok')) { $failures.Add("演示模式（${appearance}）原生托盘与显示器验证没有完成") }
    if (-not ($lines -match 'tray-controls=ok')) { $failures.Add("演示模式（${appearance}）托盘开关状态、辅助切换、键盘接续与设置入口验证没有完成") }
    if (-not ($lines -match 'plugin-lifetime=ok')) { $failures.Add("演示模式（${appearance}）插件运行归属与关闭验证没有完成") }
    if (-not ($lines -match 'update-feedback=ok')) { $failures.Add("演示模式（${appearance}）真实下载进度、准备与界面接续验证没有完成") }
    if (-not ($lines -match 'secret-save=ok')) { $failures.Add("演示模式（${appearance}）凭据写入失败保留、实际重试及加密存储验证没有完成") }
    if (-not ($lines -match 'list-sizing=ok')) { $failures.Add("演示模式（${appearance}）列表尺寸与滚动验证没有完成") }
    if (-not ($lines -match 'plugin-editor=ok')) { $failures.Add("演示模式（${appearance}）插件试运行验证没有完成") }
    if (-not ($lines -match 'plugin-match=ok')) { $failures.Add("演示模式（${appearance}）插件长度匹配性能验证没有完成") }
    if (-not ($lines -match 'plugin-output=ok')) { $failures.Add("演示模式（${appearance}）插件长输出验证没有完成") }
    if (-not ($lines -match 'clipboard-hash=ok')) { $failures.Add("演示模式（${appearance}）剪贴板分块去重验证没有完成") }
    if (-not ($lines -match 'screen-snapshot=ok')) { $failures.Add("演示模式（${appearance}）屏幕快照像素验证没有完成") }
    if (-not ($lines -match 'text-preview=ok')) { $failures.Add("演示模式（${appearance}）文本预览验证没有完成") }
    if (-not ($lines -match 'bounded-settings-preview=ok')) { $failures.Add("演示模式（${appearance}）圆盘与短语设置长文本预览验证没有完成") }
    if (-not ($lines -match 'clipboard-thumbnails=ok')) { $failures.Add("演示模式（${appearance}）缩略图缓存与取消验证没有完成") }
    if (-not ($lines -match 'overlay-release=ok')) { $failures.Add("演示模式（${appearance}）浮窗释放与重新打开验证没有完成") }
    if (-not ($lines -match 'annotation-render=ok')) { $failures.Add("演示模式（${appearance}）标注导出验证没有完成") }
    if (-not ($lines -match 'annotation-frame=ok')) { $failures.Add("演示模式（${appearance}）美化导出验证没有完成") }
    $errors = @($lines | Where-Object { $_ -match 'demo-error|crash=' })
    if ($errors.Count -gt 0) { $failures.Add("演示模式（${appearance}）出错：`n$($errors -join "`n")") }
}
$shots = @(Get-ChildItem $OutDir -Filter '*.png')
Write-Host "一共 $($shots.Count) 张截图"

if ($failures.Count -gt 0) { throw ($failures -join "`n") }
