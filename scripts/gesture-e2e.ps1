# 真正走一遍长按右键：在记事本里选中文字 → 长按 → 圆盘 → 划向一格松开，检查结果并截图。
# 短按右键要照常弹出系统右键菜单；在圆心松开要关闭圆盘。
param(
    [Parameter(Mandatory)][string]$Exe,
    [Parameter(Mandatory)][string]$OutDir
)
. "$PSScriptRoot\ci-helpers.ps1"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$sample = 'hello pop world'
$notepadFile = Join-Path $env:RUNNER_TEMP 'pop-gesture.txt'
$pop = $null
$notepad = $null

function Start-Pop([hashtable]$ExtraEnv = @{}) {
    $marker = Join-Path $env:RUNNER_TEMP "pop-gesture-$([guid]::NewGuid().ToString('N')).txt"
    $env:POP_SMOKE_MARKER = $marker
    $env:POP_LOG_SELECTION = '1'
    $env:POP_ANIMATION_SCALE = '1'
    foreach ($k in $ExtraEnv.Keys) { Set-Item "Env:$k" $ExtraEnv[$k] }
    $p = Start-Process -FilePath $Exe -ArgumentList '--silent' -PassThru
    Wait-FileContains $marker 'started.*hooks=ok' 60 | Out-Null
    foreach ($k in $ExtraEnv.Keys) { Remove-Item "Env:$k" }
    Remove-Item Env:POP_SMOKE_MARKER
    Start-Sleep -Milliseconds 500
    return $p
}

function Get-NotepadText {
    $notepad.Refresh()
    $text = [PopCi.Native]::ReadEditText($notepad.MainWindowHandle)
    if ($null -eq $text) { throw "找不到记事本的编辑区（窗口 $($notepad.MainWindowHandle)）" }
    return $text
}

function Select-AllInNotepad {
    Set-Foreground $notepad.MainWindowHandle
    Invoke-Key 0x41 -Ctrl   # Ctrl+A
    Start-Sleep -Milliseconds 200
}

function Get-NotepadCenter {
    $r = New-Object PopCi.Native+RECT
    [PopCi.Native]::GetWindowRect($notepad.MainWindowHandle, [ref]$r) | Out-Null
    return @([int](($r.Left + $r.Right) / 2), [int](($r.Top + $r.Bottom) / 2))
}

# 长按，往 (dx, dy) 方向划，截图，松开
function Invoke-LongPress([int]$Dx, [int]$Dy, [string]$Shot, [switch]$BackToCenter) {
    $cx, $cy = Get-NotepadCenter
    [PopCi.Native]::SetCursorPos($cx, $cy) | Out-Null
    Start-Sleep -Milliseconds 100
    Invoke-RightDown
    Start-Sleep -Milliseconds 700      # 超过 250ms，圆盘弹出；再等选中内容读完
    Save-Screenshot (Join-Path $OutDir "$Shot-open.png")
    if ($Dx -ne 0 -or $Dy -ne 0) {
        Move-Pointer $cx $cy ($cx + $Dx) ($cy + $Dy)
        Start-Sleep -Milliseconds 300
        Save-Screenshot (Join-Path $OutDir "$Shot-highlight.png")
        if ($BackToCenter) { Move-Pointer ($cx + $Dx) ($cy + $Dy) $cx $cy; Start-Sleep -Milliseconds 200 }
    }
    Invoke-RightUp
    Start-Sleep -Milliseconds 700
}

try {
    Set-Content -Path $notepadFile -Value $sample -NoNewline -Encoding UTF8
    $pop = Start-Pop
    $notepad = Start-Process notepad.exe -ArgumentList "`"$notepadFile`"" -PassThru
    $notepad.WaitForInputIdle(10000) | Out-Null
    for ($i = 0; $i -lt 40 -and $notepad.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $notepad.Refresh() }
    Start-Sleep -Seconds 1

    # 1. 往正下方划（第 3 格「大写」），选中的文字被替换成大写
    Select-AllInNotepad
    Invoke-LongPress 0 110 'upper'
    Wait-FileContains (Join-Path $env:LOCALAPPDATA 'Pop\logs\pop.log') '执行 upper' 10 | Out-Null
    Start-Sleep -Milliseconds 500
    $text = Get-NotepadText
    Write-Host "记事本里现在是：$text"
    if ($text.Trim() -ne $sample.ToUpperInvariant()) { throw "替换原文失败，记事本里是「$text」" }
    $log = Get-PopLog
    if ($log -notmatch '选中内容：来源 (uia|copy).*内容「hello pop world」') { throw "没读到选中的文字。日志：`n$log" }
    Write-Host '✓ 长按 → 划向「大写」→ 替换原文'

    # 2. 往左下方划（第 4 格「字数」），弹出结果卡片；Esc 关掉
    Select-AllInNotepad
    Invoke-LongPress (-95) 55 'count'
    Save-Screenshot (Join-Path $OutDir 'count-card.png')
    Wait-FileContains (Join-Path $env:LOCALAPPDATA 'Pop\logs\pop.log') '执行 count' 10 | Out-Null
    Invoke-Key 0x1B
    Write-Host '✓ 字数统计卡片'

    # 3. 在圆心松开：关闭圆盘，什么都不执行
    Select-AllInNotepad
    Invoke-LongPress 0 0 'center'
    Wait-FileContains (Join-Path $env:LOCALAPPDATA 'Pop\logs\pop.log') '在圆心松开' 10 | Out-Null
    Write-Host '✓ 在圆心松开关闭'

    # 4. 短按右键：系统右键菜单照常弹出
    Set-Foreground $notepad.MainWindowHandle
    $cx, $cy = Get-NotepadCenter
    [PopCi.Native]::SetCursorPos($cx, $cy) | Out-Null
    Invoke-RightDown
    Start-Sleep -Milliseconds 60
    Invoke-RightUp
    Start-Sleep -Milliseconds 800
    Save-Screenshot (Join-Path $OutDir 'short-click-menu.png')
    $menu = [PopCi.Native]::FindWindow('#32768', $null)
    if ($menu -eq [IntPtr]::Zero -or -not [PopCi.Native]::IsWindowVisible($menu)) { throw '短按右键没有弹出右键菜单' }
    Invoke-Key 0x1B
    Write-Host '✓ 短按右键弹出系统菜单'

    # 5. 深色外观下的圆盘截图
    Stop-Process -Id $pop.Id -Force
    Start-Sleep -Seconds 1
    $pop = Start-Pop @{ POP_APPEARANCE = 'dark' }
    Select-AllInNotepad
    Invoke-LongPress 110 (-64) 'dark' -BackToCenter
    Write-Host '✓ 深色外观截图'
}
finally {
    $log = Get-PopLog
    $log | Out-File (Join-Path $OutDir 'pop.log') -Encoding utf8
    Write-Host '---- pop.log ----'
    Write-Host $log
    if ($notepad -and -not $notepad.HasExited) { $notepad.Kill() }
    if ($pop -and -not $pop.HasExited) { $pop.Kill() }
    Remove-Item Env:POP_LOG_SELECTION, Env:POP_ANIMATION_SCALE -ErrorAction SilentlyContinue
}
