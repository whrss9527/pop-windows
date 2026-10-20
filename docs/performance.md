# 性能检查

本轮优化针对重复图像处理、功能适用性解析和列表重建，不降低图片分辨率或更改结果。

- 取色、框选与标尺：GDI 位图直接复制为冻结的 BGRA 位图，不再为预览执行整屏 PNG 编码和解码。快照与原位图独立，可在原位图修改或释放后使用；覆盖半透明像素和反向扫描线。
- 取色和标尺在预览建立后立即释放原始 GDI 位图，不再一直持有到操作结束；4K 屏幕的该位图像素区为约 31.6 MiB。标尺确认只返回物理坐标，不裁剪、不编码用不到的 PNG。
- 全部功能搜索：每次打开列表创建适用性缓存，同一功能、同一选区只解析一次；启用状态仍逐次筛选，新选区和重新载入的插件重新判断。边缘「更多」只检查自己的功能。
- 列表方向键：保留未改变的行，仅更新原选中行和新选中行。功能、剪贴板和短语列表共用这条路径，避免反复重建图片及控件。

## 自动验证

核心测试覆盖缓存调用次数、新选区和同 ID 插件重新载入的失效行为，以及查询顺序、功能开关的一致性。

Windows CI 在浅色、深色演示中执行 `PerformanceChecks`：

1. 逐字节核对直接转换与 PNG 路径的像素；验证快照不依赖原位图及反向扫描线方向。
2. 对固定的 1920 × 1080、3840 × 2160 像素输入，预热后各运行三次，记录耗时与托管分配中位数。
3. 创建含分组及禁用项的 100 行列表，验证选择跳过禁用项、反向移动和环绕，三次移动只新增六行控件。
4. 继续执行真实取色、自动/手动标尺、列表键盘选择、选择工具条和剪贴板恢复交互。

结果在截图分支 `ci-screenshots/windows-2022`、`ci-screenshots/windows-2025` 的 `ui/light-demo.txt` 和 `ui/dark-demo.txt` 中。先核对分支 README 中的提交号，再查看 `screen-snapshot=ok`、`list-navigation=ok` 和 `performance:` 行。

`png-ms`、`direct-ms` 仅比较相同输入的图像转换，不代表完整工具启动时间；`managed-bytes` 不含 WPF/WIC 的原生内存，不能作为进程总内存。CI 不对耗时设置硬阈值，以免共享运行器负载波动产生误报。

## 在 Windows 单独运行

```powershell
$env:POP_SMOKE_MARKER = Join-Path $PWD 'performance.txt'
Start-Process .\Pop.exe -ArgumentList '--demo-shots', 'performance-shots', '--demo-scenes', 'performance' -Wait
Get-Content $env:POP_SMOKE_MARKER
Remove-Item Env:POP_SMOKE_MARKER
```
