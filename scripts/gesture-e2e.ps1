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

# 选中外文默认直接翻译，不弹圆盘；这里的步骤大多要对英文用圆盘，所以测试时关掉外文直接翻译
function Set-PopSettings([string]$Json) {
    $dir = Join-Path $env:APPDATA 'Pop'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Set-Content -Path (Join-Path $dir 'settings.json') -Value $Json -Encoding UTF8
}
$testSettings = '{ "directKinds": ["math", "measurement", "color", "timestamp"] }'

function Start-Pop([hashtable]$ExtraEnv = @{}) {
    $marker = Join-Path $env:RUNNER_TEMP "pop-gesture-$([guid]::NewGuid().ToString('N')).txt"
    $env:POP_SMOKE_MARKER = $marker
    $env:POP_LOG_SELECTION = '1'
    $env:POP_ANIMATION_SCALE = '1'
    # 不去访问 GitHub：共享 runner 的出口经常被接口限流
    $emptyFeed = Join-Path $env:RUNNER_TEMP 'pop-empty-feed.json'
    Set-Content -Path $emptyFeed -Value '[]'
    $env:POP_UPDATE_FEED = $emptyFeed
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

function Get-WindowCenter([IntPtr]$Hwnd) {
    $r = New-Object PopCi.Native+RECT
    [PopCi.Native]::GetWindowRect($Hwnd, [ref]$r) | Out-Null
    return @([int](($r.Left + $r.Right) / 2), [int](($r.Top + $r.Bottom) / 2))
}

function Get-NotepadCenter { Get-WindowCenter $notepad.MainWindowHandle }

# Pop 不能在测试过程中退出（闪退）
function Assert-PopAlive([string]$Step) {
    if ($pop.HasExited) { throw "Pop 在「${Step}」时退出了，退出码 $($pop.ExitCode)" }
}

# 长按，往 (dx, dy) 方向划，截图，松开
function Invoke-LongPress([int]$Dx, [int]$Dy, [string]$Shot, [switch]$BackToCenter, [IntPtr]$Hwnd = [IntPtr]::Zero) {
    if ($Hwnd -eq [IntPtr]::Zero) { $Hwnd = $notepad.MainWindowHandle }
    $cx, $cy = Get-WindowCenter $Hwnd
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
    Assert-PopAlive $Shot
}

try {
    Set-Content -Path $notepadFile -Value $sample -NoNewline -Encoding UTF8
    Set-PopSettings $testSettings
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
    $menu = [IntPtr]::Zero
    for ($i = 0; $i -lt 20; $i++) {
        Start-Sleep -Milliseconds 100
        $menu = [PopCi.Native]::FindWindow('#32768', $null)
        if ($menu -ne [IntPtr]::Zero -and [PopCi.Native]::IsWindowVisible($menu)) { break }
    }
    Save-Screenshot (Join-Path $OutDir 'short-click-menu.png')
    if ($menu -eq [IntPtr]::Zero -or -not [PopCi.Native]::IsWindowVisible($menu)) { throw '短按右键没有弹出右键菜单' }
    Invoke-Key 0x1B
    Write-Host '✓ 短按右键弹出系统菜单'

    # 5. 直达结果：选中算式、带单位的数值，长按直接弹出结果卡片（不出圆盘）；回车复制结果
    function Set-NotepadText([string]$Text) {
        Set-Foreground $notepad.MainWindowHandle
        Set-Clipboard -Value $Text
        Invoke-Key 0x41 -Ctrl   # Ctrl+A
        Invoke-Key 0x56 -Ctrl   # Ctrl+V
        Start-Sleep -Milliseconds 200
        Invoke-Key 0x41 -Ctrl
        Start-Sleep -Milliseconds 200
    }
    Set-NotepadText '128*3'
    Invoke-LongPress 0 0 'direct-math'
    Wait-FileContains (Join-Path $env:LOCALAPPDATA 'Pop\logs\pop.log') '直达结果：计算' 10 | Out-Null
    Save-Screenshot (Join-Path $OutDir 'direct-math-card.png')
    Set-Clipboard -Value 'before'
    Invoke-Key 0x0D   # 回车：复制结果
    Start-Sleep -Milliseconds 300
    $clip = (Get-Clipboard -Raw).Trim()
    if ($clip -ne '384') { throw "回车复制的计算结果是「$clip」" }
    Write-Host '✓ 选中算式直接出结果，回车复制'

    Set-NotepadText '5 km'
    Invoke-LongPress 0 0 'direct-unit'
    Wait-FileContains (Join-Path $env:LOCALAPPDATA 'Pop\logs\pop.log') '直达结果：单位换算' 10 | Out-Null
    Save-Screenshot (Join-Path $OutDir 'direct-unit-card.png')
    Invoke-Key 0x1B
    Set-NotepadText '#FF8800'
    Invoke-LongPress 0 0 'direct-color'
    Wait-FileContains (Join-Path $env:LOCALAPPDATA 'Pop\logs\pop.log') '直达结果：颜色' 10 | Out-Null
    Save-Screenshot (Join-Path $OutDir 'direct-color-card.png')
    Invoke-Key 0x1B
    Write-Host '✓ 带单位的数值、颜色直接出结果'

    # 5a. 翻译：往右下方划（第 2 格「翻译」），卡片里先显示「正在翻译」，译文回来后原地换掉。
    # 翻译服务在外网上，偶尔连不上时只提示，不算失败
    Set-NotepadText 'Good morning, how are you today?'
    Invoke-LongPress 95 55 'translate'
    Wait-FileContains (Join-Path $env:LOCALAPPDATA 'Pop\logs\pop.log') '执行 translate' 10 | Out-Null
    $log = Wait-FileContains (Join-Path $env:LOCALAPPDATA 'Pop\logs\pop.log') '翻译(完成|失败)' 20
    Start-Sleep -Milliseconds 400
    Save-Screenshot (Join-Path $OutDir 'translate-card.png')
    if ($log -match '翻译完成：[^，]+，en → zh-Hans') {
        Write-Host '✓ 翻译：译文显示在卡片里'
    } else {
        $line = ($log -split "`n" | Select-String '翻译失败' | Select-Object -Last 1)
        Write-Host "::warning::翻译服务没有返回译文：$line"
        # 看看必应翻译两条路在这台 runner 上的情况（Edge 的令牌接口、必应翻译网页的接口）
        $ua = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36 Edg/140.0.0.0'
        $edgeStatus = & curl.exe -s -o NUL -m 15 -w '%{http_code}' -A $ua 'https://edge.microsoft.com/translate/auth'
        Write-Host "Edge 翻译令牌接口：${edgeStatus}"
        $old = & curl.exe -s -m 15 -X POST -A $ua -H 'Content-Type: application/json' -d '["Good morning"]' -w ' %{http_code}' 'https://edge.microsoft.com/translate/translatetext?from=&to=zh-Hans&isEnterpriseClient=false'
        Write-Host "Edge 旧的翻译接口：${old}"
        try {
            $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
            $page = Invoke-WebRequest -Uri 'https://www.bing.com/translator' -UserAgent $ua -WebSession $session -TimeoutSec 15
            $ig = [regex]::Match($page.Content, 'IG:"([0-9A-Za-z]+)"').Groups[1].Value
            $iid = [regex]::Match($page.Content, 'data-iid="([^"]+)"').Groups[1].Value
            $abuse = [regex]::Match($page.Content, 'params_AbusePreventionHelper\s*=\s*\[\s*(\d+)\s*,\s*"([^"]+)"')
            $bingHost = $page.BaseResponse.RequestMessage.RequestUri.Host
            $form = @{ fromLang = 'auto-detect'; to = 'zh-Hans'; text = 'Good morning'; token = $abuse.Groups[2].Value; key = $abuse.Groups[1].Value }
            $reply = Invoke-WebRequest -Uri "https://${bingHost}/ttranslatev3?isVertical=1&IG=${ig}&IID=${iid}.1" -Method Post -Body $form -UserAgent $ua -WebSession $session -Headers @{ Referer = "https://${bingHost}/translator" } -SkipHttpErrorCheck -TimeoutSec 15
            $content = [string]$reply.Content
            Write-Host "必应翻译网页接口（${bingHost}，IG=${ig}，IID=${iid}）：$($reply.StatusCode) $($content.Substring(0, [Math]::Min(200, $content.Length)))"
        } catch {
            Write-Host "必应翻译网页接口：$_"
        }
    }
    Invoke-Key 0x1B

    # 5b. 全部功能：往左上方划（第 5 格），列表里搜「base64」，回车执行；卡片上回车复制编码结果
    Set-NotepadText 'hello pop world'
    Invoke-LongPress (-95) (-55) 'all-actions'
    Wait-FileContains (Join-Path $env:LOCALAPPDATA 'Pop\logs\pop.log') '全部功能列表已显示' 10 | Out-Null
    Start-Sleep -Milliseconds 500
    Save-Screenshot (Join-Path $OutDir 'all-actions-shown.png')
    foreach ($vk in 0x42, 0x41, 0x53, 0x45, 0x36, 0x34) { Invoke-Key ([byte]$vk) }   # base64
    Start-Sleep -Milliseconds 400
    Save-Screenshot (Join-Path $OutDir 'all-actions-list.png')
    Invoke-Key 0x0D
    Wait-FileContains (Join-Path $env:LOCALAPPDATA 'Pop\logs\pop.log') '执行 codec（全部功能）' 10 | Out-Null
    Start-Sleep -Milliseconds 500
    Save-Screenshot (Join-Path $OutDir 'all-actions-card.png')
    Set-Clipboard -Value 'before'
    Invoke-Key 0x0D
    Start-Sleep -Milliseconds 300
    $clip = (Get-Clipboard -Raw).Trim()
    if ($clip -ne 'aGVsbG8gcG9wIHdvcmxk') { throw "全部功能 → 编码转换复制到的是「$clip」" }
    Write-Host '✓ 全部功能列表：搜索、执行、复制结果'

    # 5c. 剪贴板历史：复制三段文字，Win+Alt+V 打开历史，搜「second」回车，粘贴到记事本
    Set-NotepadText 'placeholder'
    foreach ($item in 'first item', 'second item', 'third item') {
        Set-Clipboard -Value $item
        Start-Sleep -Milliseconds 500
    }
    Wait-FileContains (Join-Path $env:LOCALAPPDATA 'Pop\logs\pop.log') '(?s)剪贴板历史：记录.*剪贴板历史：记录.*剪贴板历史：记录' 10 | Out-Null
    Set-Foreground $notepad.MainWindowHandle
    Invoke-Key 0x41 -Ctrl
    if ((Get-PopLog) -notmatch '快捷键 Win\+Alt\+V（剪贴板历史） 已注册') { throw "Win+Alt+V 没注册上：`n$(Get-PopLog)" }
    [PopCi.Native]::keybd_event(0x5B, 0, 0, [UIntPtr]::Zero)   # Win
    [PopCi.Native]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)   # Alt
    Invoke-Key 0x56                                            # V
    [PopCi.Native]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
    [PopCi.Native]::keybd_event(0x5B, 0, 2, [UIntPtr]::Zero)
    Wait-FileContains (Join-Path $env:LOCALAPPDATA 'Pop\logs\pop.log') '剪贴板历史已显示' 10 | Out-Null
    Start-Sleep -Milliseconds 500
    Save-Screenshot (Join-Path $OutDir 'clipboard-history.png')
    foreach ($vk in 0x53, 0x45, 0x43, 0x4F, 0x4E, 0x44) { Invoke-Key ([byte]$vk) }   # second
    Start-Sleep -Milliseconds 300
    Save-Screenshot (Join-Path $OutDir 'clipboard-history-search.png')
    Invoke-Key 0x0D
    Wait-FileContains (Join-Path $env:LOCALAPPDATA 'Pop\logs\pop.log') '剪贴板历史：粘贴' 10 | Out-Null
    Start-Sleep -Milliseconds 800
    $text = Get-NotepadText
    if ($text.Trim() -ne 'second item') { throw "从剪贴板历史粘贴后记事本里是「$text」" }
    Write-Host '✓ 剪贴板历史：记录、快捷键打开、搜索、粘贴'

    # 5d. 截图识字：记事本里放大一行字，Win+Alt+O 框选这一行，识别出的文字要对
    Set-NotepadText 'POP OCR TEST 2026'
    Invoke-Key 0x23 -Ctrl                                     # Ctrl+End 取消选中
    for ($i = 0; $i -lt 10; $i++) { Invoke-Key 0xBB -Ctrl }   # Ctrl+= 放大
    Start-Sleep -Milliseconds 500
    $r = New-Object PopCi.Native+RECT
    [PopCi.Native]::GetWindowRect($notepad.MainWindowHandle, [ref]$r) | Out-Null
    Invoke-WinAlt 0x4F                                        # Win+Alt+O
    Wait-FileContains (Join-Path $env:LOCALAPPDATA 'Pop\logs\pop.log') '框选区域：\d+ 个显示器' 10 | Out-Null
    Start-Sleep -Milliseconds 500
    Invoke-LeftDrag ($r.Left + 4) ($r.Top + 52) ($r.Left + 640) ($r.Top + 130)
    Wait-FileContains (Join-Path $env:LOCALAPPDATA 'Pop\logs\pop.log') '识别文字：' 20 | Out-Null
    Start-Sleep -Milliseconds 500
    Save-Screenshot (Join-Path $OutDir 'ocr-card.png')
    $log = Get-PopLog
    if ($log -notmatch '识别文字：识别出 \d+ 个字符，内容「[^」]*POP\s*OCR\s*TEST') { throw "截图识字没识别出来：`n$(($log -split "`n" | Select-String '识别文字|框选|文字识别') -join "`n")" }
    Invoke-Key 0x1B
    Write-Host '✓ 截图识字'

    # 5e. 截图贴图：Win+Alt+P 框选一块区域，贴在最前面
    Invoke-WinAlt 0x50                                        # Win+Alt+P
    Wait-FileContains (Join-Path $env:LOCALAPPDATA 'Pop\logs\pop.log') '(?s)框选区域：\d+ 个显示器.*框选区域：\d+ 个显示器' 10 | Out-Null
    Start-Sleep -Milliseconds 500
    Invoke-LeftDrag ($r.Left + 4) ($r.Top + 52) ($r.Left + 400) ($r.Top + 200)
    Wait-FileContains (Join-Path $env:LOCALAPPDATA 'Pop\logs\pop.log') '贴图：\d+×\d+，现在有 1 张' 10 | Out-Null
    [PopCi.Native]::SetCursorPos($r.Left + 700, $r.Top + 400) | Out-Null
    Start-Sleep -Milliseconds 500
    Save-Screenshot (Join-Path $OutDir 'pin.png')
    # 双击贴图关掉，不挡后面的测试
    [PopCi.Native]::SetCursorPos($r.Left + 150, $r.Top + 120) | Out-Null
    Start-Sleep -Milliseconds 100
    Move-Pointer ($r.Left + 150) ($r.Top + 120) ($r.Left + 150) ($r.Top + 120) 1
    Invoke-LeftDown; Invoke-LeftUp; Start-Sleep -Milliseconds 60; Invoke-LeftDown; Invoke-LeftUp
    Start-Sleep -Milliseconds 300
    for ($i = 0; $i -lt 10; $i++) { Invoke-Key 0xBD -Ctrl }   # Ctrl+- 缩回去
    Invoke-Key 0x30 -Ctrl                                     # Ctrl+0
    Write-Host '✓ 截图贴图'

    # 6. 浏览器：Chrome 和 Edge 里选中网页文字，长按 → 往上划「复制」
    $browsers = @(
        @{ Name = 'chrome'; Paths = @("$env:ProgramFiles\Google\Chrome\Application\chrome.exe", "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe") },
        @{ Name = 'msedge'; Paths = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") }
    )
    $browserText = 'browser text for pop'
    $page = Join-Path $env:RUNNER_TEMP 'pop-browser.html'
    Set-Content -Path $page -Encoding UTF8 -Value "<!doctype html><meta charset=`"utf-8`"><title>PopBrowserTest</title><body style=`"font-size:32px;margin:80px`"><p>$browserText</p></body>"
    foreach ($browser in $browsers) {
        $name = $browser.Name
        $exePath = $browser.Paths | Where-Object { Test-Path $_ } | Select-Object -First 1
        if (-not $exePath) { throw "这台 runner 上没有 $name" }
        $browserProfile = Join-Path $env:RUNNER_TEMP "pop-$name-profile"
        # runner 的虚拟显卡上浏览器偶尔画不出页面，用软件渲染
        Start-Process $exePath -ArgumentList '--no-first-run', '--no-default-browser-check', '--disable-gpu', "--user-data-dir=`"$browserProfile`"", '--new-window', '--window-position=120,60', '--window-size=900,700', "`"$page`""
        $browserWindow = [IntPtr]::Zero
        for ($i = 0; $i -lt 120 -and $browserWindow -eq [IntPtr]::Zero; $i++) {
            Start-Sleep -Milliseconds 500
            $w = Get-Process $name -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like 'PopBrowserTest*' } | Select-Object -First 1
            if ($w) { $browserWindow = $w.MainWindowHandle }
        }
        if ($browserWindow -eq [IntPtr]::Zero) {
            Save-Screenshot (Join-Path $OutDir "$name-missing.png")
            throw "$name 的窗口没有出现"
        }
        Start-Sleep -Seconds 2
        Set-Foreground $browserWindow
        Invoke-Key 0x41 -Ctrl   # Ctrl+A 选中整页文字
        Set-Clipboard -Value 'before'
        Invoke-LongPress 0 (-110) $name -Hwnd $browserWindow
        Wait-FileContains (Join-Path $env:LOCALAPPDATA 'Pop\logs\pop.log') "窗口 Chrome_WidgetWin_1（$name）.*内容「$browserText」" 10 | Out-Null
        Assert-PopAlive $name
        $clip = (Get-Clipboard -Raw).Trim()
        if ($clip -ne $browserText) { throw "$name 里复制到的是「$clip」" }
        Get-Process $name -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
        Write-Host "✓ $name 里读取选中文字并复制"
    }

    # 6b. 再运行一次 Pop.exe：正在运行的 Pop 打开设置窗口
    Start-Process -FilePath $Exe | Out-Null
    Wait-FileContains (Join-Path $env:LOCALAPPDATA 'Pop\logs\pop.log') '设置窗口已打开' 15 | Out-Null
    Start-Sleep -Milliseconds 800
    Save-Screenshot (Join-Path $OutDir 'settings-general.png')
    Invoke-Key 0x1B          # Esc 关闭
    Start-Sleep -Milliseconds 400
    Assert-PopAlive 'settings'
    Write-Host '✓ 再次运行 Pop.exe 打开设置窗口'

    # 7. 深色外观下的圆盘截图（换回普通文字，不然会直接出结果卡片）
    Stop-Process -Id $pop.Id -Force
    Start-Sleep -Seconds 1
    $pop = Start-Pop @{ POP_APPEARANCE = 'dark' }
    Set-NotepadText 'hello pop world'
    Select-AllInNotepad
    Invoke-LongPress 110 (-64) 'dark' -BackToCenter
    Write-Host '✓ 深色外观截图'

    # 整个过程 Pop 不能出错
    $errors = (Get-PopLog) -split "`n" | Where-Object { $_ -match '\[ERROR\]' }
    if ($errors) { throw "Pop 的日志里有错误：`n$($errors -join "`n")" }
    Write-Host '✓ 日志里没有错误'
}
finally {
    $log = Get-PopLog
    $log | Out-File (Join-Path $OutDir 'pop.log') -Encoding utf8
    Write-Host '---- pop.log ----'
    Write-Host $log
    if ($notepad -and -not $notepad.HasExited) { $notepad.Kill() }
    if ($pop -and -not $pop.HasExited) { $pop.Kill() }
    Remove-Item Env:POP_LOG_SELECTION, Env:POP_ANIMATION_SCALE, Env:POP_UPDATE_FEED -ErrorAction SilentlyContinue
}
