# 验证唤起、应用圆盘、直达规则、功能快捷键和在圆心松开后继续选择。
param(
    [Parameter(Mandatory)][string]$Exe,
    [Parameter(Mandatory)][string]$OutDir
)
. "$PSScriptRoot\ci-helpers.ps1"
New-Item -ItemType Directory -Force $OutDir | Out-Null
$settingsPath = Join-Path $env:APPDATA 'Pop\settings.json'
$settingsExisted = Test-Path $settingsPath
$savedSettings = if (Test-Path $settingsPath) { [IO.File]::ReadAllBytes($settingsPath) } else { $null }
$sample = 'hello pop world'
$file = Join-Path $env:RUNNER_TEMP "pop-interaction-$([guid]::NewGuid().ToString('N')).txt"
$report = [Collections.Generic.List[string]]::new()
$pop = $null
$notepad = $null

function Wait-Window([string]$Title, [bool]$Visible) {
    $deadline = (Get-Date).AddSeconds(12)
    while ($true) {
        $hwnd = [PopCi.Native]::FindWindow([NullString]::Value, $Title)
        $shown = $hwnd -ne [IntPtr]::Zero -and [PopCi.Native]::IsWindowVisible($hwnd)
        if ($shown -eq $Visible) { return }
        if ((Get-Date) -gt $deadline) { throw "窗口状态不符合预期：$Title，应该显示=$Visible" }
        if ($script:pop.HasExited) { throw 'Pop 意外退出' }
        Start-Sleep -Milliseconds 100
    }
}

function Reset-Selection {
    Set-Foreground $script:notepad.MainWindowHandle
    Set-Clipboard -Value $sample
    Invoke-Key 0x41 -Ctrl
    Invoke-Key 0x56 -Ctrl
    Start-Sleep -Milliseconds 200
    Invoke-Key 0x41 -Ctrl
    $r = New-Object PopCi.Native+RECT
    [PopCi.Native]::GetWindowRect($script:notepad.MainWindowHandle, [ref]$r) | Out-Null
    $script:cx = [int](($r.Left + $r.Right) / 2)
    $script:cy = [int](($r.Top + $r.Bottom) / 2)
    [PopCi.Native]::SetCursorPos($script:cx, $script:cy) | Out-Null
}

function Invoke-CtrlAlt([byte]$Key) {
    [PopCi.Native]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)
    [PopCi.Native]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
    Invoke-Key $Key
    [PopCi.Native]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
    [PopCi.Native]::keybd_event(0x11, 0, 2, [UIntPtr]::Zero)
}

function Invoke-ModifierRight([byte]$Modifier) {
    [PopCi.Native]::keybd_event($Modifier, 0, 0, [UIntPtr]::Zero)
    Invoke-RightDown
    Start-Sleep -Milliseconds 80
    Invoke-RightUp
    [PopCi.Native]::keybd_event($Modifier, 0, 2, [UIntPtr]::Zero)
}

function Assert-Uppercase {
    $deadline = (Get-Date).AddSeconds(12)
    do {
        $text = [PopCi.Native]::ReadEditText($script:notepad.MainWindowHandle)
        if ($null -ne $text -and $text.Trim() -ceq $sample.ToUpperInvariant()) { return }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    throw '功能没有把选中内容替换为大写'
}

function Start-Scenario([string]$Mode, [string]$Modifier = 'Control', [switch]$HashDirect, [switch]$Toolbar, [switch]$ExcludeNotepad) {
    if ($null -ne $script:pop -and !$script:pop.HasExited) { $script:pop.Kill(); $script:pop.WaitForExit() }
    $rules = @()
    if ($HashDirect) { $rules = @(@{ kind = 'foreign'; actionId = 'hash'; enabled = $true }) }
    $settings = @{
        triggerMode = $Mode; triggerModifier = $Modifier; keepRingOpen = $true
        rules = $rules; checkForUpdates = $false
        selectionToolbar = [bool]$Toolbar
        triggerExcludedApps = @()
        appRings = @{ notepad = @('count', 'upper', 'copy', 'all') }
        actionShortcuts = @{ '$ring' = @{ modifiers = 3; key = 75 }; upper = @{ modifiers = 3; key = 85 } }
    }
    if ($ExcludeNotepad) { $settings.triggerExcludedApps = @('Notepad.EXE') }
    New-Item -ItemType Directory -Force (Split-Path $settingsPath) | Out-Null
    [IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 8))
    $marker = Join-Path $env:RUNNER_TEMP "pop-interaction-$([guid]::NewGuid().ToString('N')).marker"
    $env:POP_SMOKE_MARKER = $marker
    try {
        $script:pop = Start-Process -FilePath $Exe -ArgumentList '--silent' -PassThru
        Wait-FileContains $marker 'started.*hooks=ok' 60 | Out-Null
    } finally { Remove-Item Env:POP_SMOKE_MARKER -ErrorAction SilentlyContinue }
    Reset-Selection
}

function Passed([string]$Name) {
    $script:report.Add($Name)
    if (!(Test-Path (Join-Path $OutDir "$Name.png"))) { Save-Screenshot (Join-Path $OutDir "$Name.png") }
    Write-Host "✓ $Name"
}

function Assert-HashCard([string]$ShotName) {
    Wait-Window 'Pop 结果' $true
    # 浮窗不能抢焦点，回车应由 Pop 钩子处理，并把 MD5 结果写入剪贴板。
    if ([PopCi.Native]::GetForegroundWindow() -ne $script:notepad.MainWindowHandle) { throw '结果卡片抢走了原 App 的焦点' }
    Save-Screenshot (Join-Path $OutDir "$ShotName.png")
    Set-Clipboard -Value 'before hash copy'
    Invoke-Key 0x0D
    $expected = [Convert]::ToHexString([Security.Cryptography.MD5]::HashData([Text.Encoding]::UTF8.GetBytes($sample))).ToLowerInvariant()
    Start-Sleep -Milliseconds 250
    if ((Get-Clipboard -Raw).Trim() -cne $expected) { throw '直达规则没有执行哈希，或者复制了错误结果' }
    Wait-Window 'Pop 结果' $false
}

function Get-NotepadSelectionBounds {
    # 记事本的原生 Edit 在 UIA 中可以暴露为 Document，不能按 ControlType.Edit 查找。
    $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
    if ($null -eq $focused -or $focused.Current.ProcessId -ne $script:notepad.Id) { throw '当前输入焦点不在记事本' }
    $pattern = $focused.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern)
    $ranges = @($pattern.GetSelection())
    if ($ranges.Count -eq 0) { throw '记事本没有提供选区' }
    $bounds = @($ranges[0].GetBoundingRectangles())
    if ($bounds.Count -eq 0) { throw '记事本没有提供选区位置' }
    return $bounds
}

function Select-WordWithMouse {
    Reset-Selection
    $bounds = @(Get-NotepadSelectionBounds)
    $script:wordX = [int]($bounds[0].Left + 15)
    $script:wordY = [int]($bounds[0].Top + $bounds[0].Height / 2)
    Set-Clipboard -Value 'toolbar must not copy automatically'
    Move-Pointer $cx $cy $wordX $wordY
    Invoke-LeftDown; Invoke-LeftUp
    Start-Sleep -Milliseconds 90
    Invoke-LeftDown; Invoke-LeftUp
}

function Click-ToolbarButton([string]$Id) {
    $hwnd = [PopCi.Native]::FindWindow([NullString]::Value, 'Pop 选择工具条')
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    $button = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "toolbar-$Id"))
    if ($null -eq $button) { throw "工具条缺少按钮：$Id" }
    $r = $button.Current.BoundingRectangle
    Move-Pointer $wordX $wordY ([int]($r.Left + $r.Width / 2)) ([int]($r.Top + $r.Height / 2))
    Invoke-LeftDown; Invoke-LeftUp
}

try {
    Set-Content -Path $file -Value $sample -NoNewline -Encoding UTF8
    $notepad = Start-Process notepad.exe -ArgumentList "`"$file`"" -PassThru
    $notepad.WaitForInputIdle(10000) | Out-Null
    for ($i = 0; $i -lt 40 -and $notepad.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $notepad.Refresh() }
    if ($notepad.MainWindowHandle -eq 0) { throw '记事本没有打开窗口' }

    Start-Scenario 'ModifierRightClick'
    Invoke-CtrlAlt 0x4B # 自定义圆盘快捷键 Ctrl+Alt+K
    Wait-Window 'Pop 圆盘' $true
    if ([PopCi.Native]::GetForegroundWindow() -ne $notepad.MainWindowHandle) { throw '圆盘抢走了原 App 的焦点' }
    Save-Screenshot (Join-Path $OutDir 'per-app-ring.png')
    Invoke-Key 0x31 # 独立圆盘的第一格是「字数」，默认圆盘的第一格是「复制」
    Wait-Window 'Pop 结果' $true
    Invoke-Key 0x0D
    Start-Sleep -Milliseconds 250
    if ((Get-Clipboard -Raw).Trim() -cne '15') { throw '记事本没有使用独立圆盘，或字数统计错误' }
    Wait-Window 'Pop 结果' $false
    Passed 'per-app-ring'

    Reset-Selection
    Invoke-CtrlAlt 0x55 # Ctrl+Alt+U 直接运行大写，不能被 Alt 干扰
    Assert-Uppercase
    Passed 'action-shortcut-replace'

    Reset-Selection
    Invoke-ModifierRight 0x11
    Wait-Window 'Pop 圆盘' $true
    Invoke-Key 0x27; Invoke-Key 0x27; Invoke-Key 0x0D # 方向键选到第二格「大写」
    Assert-Uppercase
    Passed 'modifier-right-click-keyboard'

    Start-Scenario 'MiddleClick' -HashDirect
    [PopCi.Native]::mouse_event(0x0020, 0, 0, 0, [UIntPtr]::Zero)
    [PopCi.Native]::mouse_event(0x0040, 0, 0, 0, [UIntPtr]::Zero)
    Assert-HashCard 'middle-click-direct-rule'
    Passed 'middle-click-direct-rule'

    Start-Scenario 'ModifierRightClick' 'Alt' -HashDirect
    Invoke-ModifierRight 0x12
    Assert-HashCard 'alt-right-without-menu'
    Passed 'alt-right-without-menu'

    Start-Scenario 'ModifierRightClick' 'Windows' -HashDirect
    Invoke-ModifierRight 0x5B
    Assert-HashCard 'win-right-without-start-menu'
    Passed 'win-right-without-start-menu'

    Start-Scenario 'LongPressRight'
    Invoke-RightDown; Wait-Window 'Pop 圆盘' $true; Invoke-RightUp
    Start-Sleep -Milliseconds 350
    Wait-Window 'Pop 圆盘' $true
    Passed 'release-center-keeps-menu'
    Invoke-Key 0x1B; Wait-Window 'Pop 圆盘' $false

    Start-Scenario 'LongPressRight' -Toolbar
    Select-WordWithMouse
    Wait-Window 'Pop 选择工具条' $true
    if ([PopCi.Native]::GetForegroundWindow() -ne $notepad.MainWindowHandle) { throw '工具条抢走了原 App 焦点' }
    if ((Get-Clipboard -Raw).Trim() -cne 'toolbar must not copy automatically') { throw '自动工具条改动了剪贴板' }
    Save-Screenshot (Join-Path $OutDir 'selection-toolbar.png')
    Click-ToolbarButton 'upper'
    Wait-Window 'Pop 选择工具条' $false
    $deadline = (Get-Date).AddSeconds(12)
    while ([PopCi.Native]::ReadEditText($notepad.MainWindowHandle).Trim() -cne 'HELLO pop world') {
        if ((Get-Date) -gt $deadline) { throw '工具条没有替换双击选中的词，或替换了错误选区' }
        Start-Sleep -Milliseconds 100
    }
    Passed 'selection-toolbar-replaces-original-word'

    Select-WordWithMouse
    Wait-Window 'Pop 选择工具条' $true
    Click-ToolbarButton 'more'
    Wait-Window 'Pop 圆盘' $true
    Invoke-Key 0x31 # 沿用缓存的词选区，第一项「字数」
    Wait-Window 'Pop 结果' $true
    Invoke-Key 0x0D; Start-Sleep -Milliseconds 250
    if ((Get-Clipboard -Raw).Trim() -cne '5') { throw '工具条更多没有沿用原来的选词' }
    Passed 'selection-toolbar-more-keeps-selection'

    Select-WordWithMouse
    Wait-Window 'Pop 选择工具条' $true
    Invoke-Key 0x1B
    Wait-Window 'Pop 选择工具条' $false
    Passed 'selection-toolbar-escape'

    Reset-Selection
    $bounds = @(Get-NotepadSelectionBounds)
    $wordX = [int]$bounds[0].Left + 1; $wordY = [int]($bounds[0].Top + $bounds[0].Height / 2)
    Set-Clipboard -Value 'before drag copy'
    Invoke-LeftDrag $wordX $wordY ([int]$bounds[0].Right + 3) $wordY
    Wait-Window 'Pop 选择工具条' $true
    Click-ToolbarButton 'copy'
    Start-Sleep -Milliseconds 250
    if ((Get-Clipboard -Raw).Trim() -cne $sample) { throw '拖选工具条没有复制完整的原选区' }
    Passed 'selection-toolbar-drag-copy'

    Select-WordWithMouse
    Wait-Window 'Pop 选择工具条' $true
    Invoke-Key 0x58
    Wait-Window 'Pop 选择工具条' $false
    Passed 'selection-toolbar-typing-dismisses'

    Start-Scenario 'LongPressRight' -Toolbar -ExcludeNotepad
    Select-WordWithMouse
    Start-Sleep -Milliseconds 900
    Wait-Window 'Pop 选择工具条' $false
    Invoke-RightDown; Start-Sleep -Milliseconds 500; Invoke-RightUp
    Start-Sleep -Milliseconds 300
    Wait-Window 'Pop 圆盘' $false
    $menu = [PopCi.Native]::FindWindow('#32768', [NullString]::Value)
    if ($menu -eq [IntPtr]::Zero -or ![PopCi.Native]::IsWindowVisible($menu)) { throw '排除应用的长按右键没有保留系统菜单' }
    Invoke-Key 0x1B
    Invoke-CtrlAlt 0x4B
    Wait-Window 'Pop 圆盘' $true
    Invoke-Key 0x1B
    Passed 'excluded-app-keeps-mouse-and-shortcuts'

    Start-Scenario 'LongPressRight'
    Invoke-RightDown; Wait-Window 'Pop 圆盘' $true
    Move-Pointer $cx $cy ($cx + 110) $cy
    Invoke-RightUp
    Assert-Uppercase
    Wait-Window 'Pop 圆盘' $false
    Passed 'release-on-action-executes'

    Reset-Selection
    [PopCi.Native]::SetCursorPos(8, 8) | Out-Null
    Invoke-RightDown; Wait-Window 'Pop 圆盘' $true; Invoke-RightUp
    Invoke-Key 0x33 # 角落第三格「更多」
    Wait-Window 'Pop 全部功能' $true
    Invoke-Key 0x25 # 左方向键返回原菜单
    Wait-Window 'Pop 圆盘' $true
    Passed 'corner-more-back'
    Invoke-Key 0x1B; Wait-Window 'Pop 圆盘' $false
}
catch {
    ($_ | Format-List * -Force | Out-String) | Set-Content (Join-Path $OutDir 'error.txt') -Encoding UTF8
    Save-Screenshot (Join-Path $OutDir 'failure.png')
    throw
}
finally {
    if ($null -ne $pop -and !$pop.HasExited) { $pop.Kill(); $pop.WaitForExit() }
    if ($null -ne $notepad -and !$notepad.HasExited) { $notepad.Kill(); $notepad.WaitForExit() }
    if ($settingsExisted) { [IO.File]::WriteAllBytes($settingsPath, [byte[]]$savedSettings) }
    else { Remove-Item $settingsPath -ErrorAction SilentlyContinue }
    $report.ToArray() | ConvertTo-Json -AsArray | Set-Content (Join-Path $OutDir 'passed.json') -Encoding UTF8
    $log = Join-Path $env:LOCALAPPDATA 'Pop\logs\pop.log'
    if (Test-Path $log) { Copy-Item $log (Join-Path $OutDir 'pop.log') }
    Remove-Item $file -ErrorAction SilentlyContinue
}
