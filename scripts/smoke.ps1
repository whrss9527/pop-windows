# 启动一次 Release 版 Pop.exe，确认能正常启动并装上输入钩子，然后退出
param([Parameter(Mandatory)][string]$Exe)
. "$PSScriptRoot\ci-helpers.ps1"

$marker = Join-Path $env:RUNNER_TEMP "pop-smoke-$([guid]::NewGuid().ToString('N')).txt"
$env:POP_SMOKE_MARKER = $marker
$env:POP_SMOKE_EXIT = '1'
try {
    $p = Start-Process -FilePath $Exe -ArgumentList '--silent' -PassThru
    $text = Wait-FileContains $marker 'started' 60
    Write-Host $text
    if ($text -notmatch 'hooks=ok') { throw "输入钩子没装上：$text" }
    if (-not $p.WaitForExit(20000)) { $p.Kill(); throw 'Pop 没有按时退出' }
    Write-Host '启动测试通过'
} finally {
    Remove-Item Env:POP_SMOKE_MARKER, Env:POP_SMOKE_EXIT -ErrorAction SilentlyContinue
}
