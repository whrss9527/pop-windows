"""读取 .NET 8 单文件清单，按包内实际存储字节统计体积（不解压、不修改程序）。"""

import argparse
import collections
import json
import struct
import zlib
from pathlib import Path


SIGNATURE = bytes.fromhex("8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae")


def measure(path):
    data = path.read_bytes()
    marker = data.index(SIGNATURE)
    if marker < 8:
        raise ValueError("单文件宿主标记无效")
    position = struct.unpack_from("<Q", data, marker - 8)[0]

    def read(fmt):
        nonlocal position
        values = struct.unpack_from(fmt, data, position)
        position += struct.calcsize(fmt)
        return values[0] if len(values) == 1 else values

    def text():
        nonlocal position
        size = 0
        for shift in range(0, 35, 7):
            value = read("<B")
            size |= (value & 127) << shift
            if value < 128:
                break
        else:
            raise ValueError("清单字符串长度无效")
        if position + size > len(data):
            raise ValueError("清单字符串超出文件边界")
        value = data[position:position + size].decode("utf-8")
        position += size
        return value

    major, minor, count = read("<III")
    if (major, minor) != (6, 0) or count > 10000:
        raise ValueError("仅支持 .NET 8 使用的单文件清单版本 6.0")
    text()  # 包 ID
    read("<QQQQQ")  # deps、runtimeconfig 的位置和兼容标志
    entries = []
    dependencies = {}
    for _ in range(count):
        offset, size, compressed = read("<QQQ")
        read("<B")
        name = text()
        stored = compressed or size
        if offset + stored > len(data):
            raise ValueError(f"包内文件超出边界：{name}")
        entries.append(dict(name=name, raw_bytes=size, stored_bytes=stored))
        if name.endswith(".deps.json"):
            content = data[offset:offset + stored]
            if compressed:
                content = zlib.decompress(content, -15)
            dependencies = json.loads(content)

    runtime_files = {}
    # 分类依据程序自带的依赖清单，不依赖本机 SDK、NuGet 缓存或程序集名称推测。
    for target in dependencies.get("targets", {}).values():
        for library, assets in target.items():
            if library.startswith("runtimepack.Microsoft.NETCore.App.Runtime."):
                group = ".NET 运行时"
            elif library.startswith("runtimepack.Microsoft.WindowsDesktop.App.Runtime."):
                group = "WPF 桌面运行时"
            else:
                continue
            for kind in ("runtime", "native", "resources"):
                for name in assets.get(kind, {}):
                    runtime_files[name.rsplit("/", 1)[-1]] = group

    groups = collections.Counter()
    for entry in entries:
        name = entry["name"]
        if name in ("Pop.dll", "Pop.Core.dll"):
            group = "Pop（含字体）"
        elif name in ("Microsoft.Windows.SDK.NET.dll", "WinRT.Runtime.dll"):
            group = "Windows OCR 接口"
        elif name.startswith("Wpf.Ui"):
            group = "WPF-UI 控件与图标"
        elif name in ("Jint.dll", "Acornima.dll"):
            group = "JavaScript 插件"
        elif "sqlite" in name.lower():
            group = "SQLite"
        else:
            group = runtime_files.get(name, "其他依赖与配置")
        groups[group] += entry["stored_bytes"]
    groups["单文件宿主、清单与对齐"] = len(data) - sum(e["stored_bytes"] for e in entries)
    return dict(file=str(path), bytes=len(data), groups=dict(groups), entries=entries)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("executables", nargs="+", type=Path)
    parser.add_argument("--json", type=Path, help="保存完整清单与分组数据")
    args = parser.parse_args()
    results = [measure(path) for path in args.executables]
    for result in results:
        print(f"{result['file']}: {result['bytes']:,} 字节")
        for name, size in sorted(result["groups"].items(), key=lambda pair: -pair[1]):
            print(f"  {size / 1_000_000:6.2f} MB  {name}")
    if args.json:
        args.json.write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
