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

foreach ($appearance in 'light', 'dark') {
    $marker = Join-Path $env:RUNNER_TEMP "pop-demo-$appearance.txt"
    Remove-Item $marker -ErrorAction SilentlyContinue
    $env:POP_SMOKE_MARKER = $marker
    $env:POP_APPEARANCE = $appearance
    try {
        $p = Start-Process -FilePath $Exe -ArgumentList '--demo-shots', "`"$OutDir`"" -PassThru
        if (-not $p.WaitForExit(240000)) {
            $p.Kill()
            throw "演示模式（${appearance}）四分钟内没有结束"
        }
    } finally {
        Remove-Item Env:POP_SMOKE_MARKER, Env:POP_APPEARANCE -ErrorAction SilentlyContinue
    }
    $lines = @(Get-Content $marker -ErrorAction SilentlyContinue)
    Write-Host ($lines -join "`n")
    if (-not ($lines -match 'demo-done')) { throw "演示模式（${appearance}）没有跑完，退出码 $($p.ExitCode)" }
    $errors = @($lines | Where-Object { $_ -match 'demo-error|crash=' })
    if ($errors.Count -gt 0) { throw "演示模式（${appearance}）出错：`n$($errors -join "`n")" }
}
$shots = @(Get-ChildItem $OutDir -Filter '*.png')
Write-Host "一共 $($shots.Count) 张截图"
