# 一键更新的端到端测试：装一个旧版本，用本机的假发布让它更新。
#   校验和不对要拒绝、正常版本要替换 Pop.exe 并重新启动、已是最新时不更新。
param(
    [Parameter(Mandatory)][string]$OldExe,   # 用 -p:Version=0.0.1 编出来的旧版本
    [Parameter(Mandatory)][string]$NewExe,   # 这次提交编出来的版本
    [Parameter(Mandatory)][string]$Version   # 新版本的版本号
)
. "$PSScriptRoot\ci-helpers.ps1"

$work = Join-Path $env:RUNNER_TEMP "pop-update-$([guid]::NewGuid().ToString('N'))"
$installed = Join-Path $work 'installed'
$release = Join-Path $work 'release'
New-Item -ItemType Directory -Force -Path $installed, $release | Out-Null
$installedExe = Join-Path $installed 'Pop.exe'
Copy-Item $OldExe $installedExe
$oldVersion = Get-FileProductVersion $installedExe
Write-Host "旧版本 $oldVersion → 新版本 $Version"

# 假的发布：安装包 + 校验和 + GitHub Releases 接口格式的列表
$packageName = "Pop-$Version-win-x64.zip"
$package = Join-Path $release $packageName
Compress-Archive -Path $NewExe -DestinationPath $package
$hash = (Get-FileHash $package -Algorithm SHA256).Hash.ToLowerInvariant()

function Write-Feed([string]$Name, [string]$Sums) {
    $sumsPath = Join-Path $release "$Name-SHA256SUMS.txt"
    [IO.File]::WriteAllText($sumsPath, $Sums)
    $feed = @(@{
        tag_name = "v$Version"; draft = $false; prerelease = $false; body = '测试'
        assets = @(
            @{ name = $packageName; browser_download_url = ([Uri]$package).AbsoluteUri },
            @{ name = 'SHA256SUMS.txt'; browser_download_url = ([Uri]$sumsPath).AbsoluteUri }
        )
    })
    $feedPath = Join-Path $release "$Name-feed.json"
    ConvertTo-Json -InputObject $feed -Depth 5 | Set-Content -Path $feedPath -Encoding UTF8
    return $feedPath
}

function Invoke-Update([string]$Feed, [string]$Marker) {
    $env:POP_UPDATE_FEED = $Feed
    $env:POP_SMOKE_MARKER = $Marker
    $env:POP_SMOKE_EXIT = '1'
    try {
        $p = Start-Process -FilePath $installedExe -ArgumentList '--update-now' -PassThru
        if (-not $p.WaitForExit(120000)) { $p.Kill(); throw '更新进程没有按时退出' }
    } finally {
        Remove-Item Env:POP_UPDATE_FEED, Env:POP_SMOKE_MARKER, Env:POP_SMOKE_EXIT -ErrorAction SilentlyContinue
    }
}

try {
    # 1. 校验和不对：拒绝，Pop.exe 不变
    $badFeed = Write-Feed 'bad' "$('0' * 64)  $packageName`n"
    $marker = Join-Path $work 'bad.txt'
    Invoke-Update $badFeed $marker
    $text = Wait-FileContains $marker 'update-error=' 10
    Write-Host $text
    if ($text -notmatch '校验和不对') { throw "拒绝的原因不对：$text" }
    if ((Get-FileProductVersion $installedExe) -ne $oldVersion) { throw '校验和不对时 Pop.exe 被替换了' }
    Write-Host '✓ 校验和不对时拒绝更新'

    # 2. 正常更新：替换 Pop.exe，新版本启动并报告自己的版本号
    $goodFeed = Write-Feed 'good' "$hash  $packageName`n"
    $marker = Join-Path $work 'good.txt'
    Invoke-Update $goodFeed $marker
    $text = Wait-FileContains $marker "started version=$([regex]::Escape($Version)) .*updated-from=$([regex]::Escape($oldVersion))" 60
    Write-Host $text
    $actual = Get-FileProductVersion $installedExe
    if ($actual -ne $Version) { throw "更新后 Pop.exe 的版本是 $actual" }
    Write-Host '✓ 更新后替换并重新启动'

    # 3. 已是最新：不更新
    Start-Sleep -Seconds 2
    $marker = Join-Path $work 'latest.txt'
    Invoke-Update $goodFeed $marker
    $text = Wait-FileContains $marker 'no-update' 30
    Write-Host $text
    if (Test-Path "$installedExe.old") { throw '上一次更新留下的 Pop.exe.old 没有被清理' }
    Write-Host '✓ 已是最新时不更新，旧文件已清理'
}
finally {
    Get-PopLog | Write-Host
}
