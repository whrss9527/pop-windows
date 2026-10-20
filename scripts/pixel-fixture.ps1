# 独立 STA 进程持续绘制测试像素，避免父脚本等待时阻塞窗口消息循环。
param(
    [Parameter(Mandatory)][ValidateSet('picker', 'ruler')][string]$Mode,
    [Parameter(Mandatory)][string]$Marker
)
. "$PSScriptRoot\ci-helpers.ps1"
Add-Type -AssemblyName System.Windows.Forms
$form = [Windows.Forms.Form]::new()
$image = [Drawing.Bitmap]::new(200, 120)
try {
    $form.Text = 'Pop CI 像素夹具'
    $form.FormBorderStyle = [Windows.Forms.FormBorderStyle]::None
    $form.AutoScaleMode = [Windows.Forms.AutoScaleMode]::None
    $form.StartPosition = [Windows.Forms.FormStartPosition]::Manual
    $form.Location = [Drawing.Point]::new(350, 250)
    $form.ClientSize = [Drawing.Size]::new(200, 120)
    $form.TopMost = $true
    $form.ShowInTaskbar = $false
    if ($Mode -eq 'picker') {
        for ($x = 0; $x -lt 200; $x++) {
            for ($y = 0; $y -lt 120; $y++) { $image.SetPixel($x, $y, [Drawing.Color]::FromArgb($x, $y, 50)) }
        }
    } else {
        $g = [Drawing.Graphics]::FromImage($image)
        try {
            $g.Clear([Drawing.Color]::FromArgb(40, 50, 60))
            $g.FillRectangle([Drawing.Brushes]::White, 50, 30, 80, 60)
        } finally { $g.Dispose() }
    }
    $form.BackgroundImage = $image
    $form.Add_Shown({ $form.Refresh(); [IO.File]::WriteAllText($Marker, 'ready') })
    $form.ShowDialog() | Out-Null
} finally { $form.Dispose(); $image.Dispose() }
