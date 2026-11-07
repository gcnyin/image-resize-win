# 图片工具（image-resize-win）

Windows 桌面图片小工具（WinUI 3 + .NET），支持**裁剪**与**按百分比缩放**。

- **裁剪**：拖动四角 / 整体拖动 / 空白处重新框选（带三分网格、白框深描边、选区外压暗）。
- **缩放**：10%~100% 滑杆 + 25%/50%/75% 快捷键，实时预览目标像素尺寸。
- **编辑循环**：裁剪/缩放可在上一步结果上反复进行，满意后保存。
- **恒定压缩率**：目标字节数 = 源每像素字节 × 输出像素数；JPEG/有损 WebP 在 40~97 内二分搜最大质量，PNG/无损 WebP 保持无损直通（50% 缩放 ≈ 原体积 1/4，绝不因重新编码变大）。
- **加载**：自动按 EXIF 摆正方向；最长边超过 4096px 的工作区自动降采样。
- **格式保持**：按文件头魔数嗅探 JPEG/PNG/WebP/HEIF，保存保持原格式；HEIF 源回退 JPEG。
- **桌面化**：文件选择/另存为对话框（默认原格式单选）、拖放打开、命令行 `ImageResize.exe 图片路径` 直接打开、深浅色跟随系统。

## 构建 / 运行

```sh
dotnet build ImageResize.csproj -c Debug -p Platform=x64
bin\x64\Debug\net10.0-windows10.0.19041.0\ImageResize.exe [可选图片路径]
```

- 自包含部署（`WindowsAppSDKSelfContained=true`），无需安装 Windows App SDK 运行时。
- 单工程无解决方案文件，VS 直接打开 `ImageResize.csproj` 选 x64 平台即可。
- `-p Platform=x64` 必需：CLI 直接构建 csproj 时默认 AnyCPU，自包含目标会报架构错误。

## 打包发布

### Release 目录包（自包含，免安装运行时）

```sh
dotnet publish ImageResize.csproj -c Release -p Platform=x64
```

产物：`bin\x64\Release\net10.0-windows10.0.19041.0\publish\`。
把**整个 publish 文件夹**拷给别人即可运行，目标机器无需安装 .NET 或 Windows App SDK 运行时。

### 打包单一 exe 文件

```sh
dotnet publish ImageResize.csproj -c Release -r win-x64 -p Platform=x64 ^
  -p PublishSingleFile=true ^
  -p IncludeNativeLibrariesForSelfExtract=true ^
  -p EnableCompressionInSingleFile=true
```

产物：`bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\publish\ImageResize.exe`（约 96 MB 的单个 exe）。

说明：

- 工程已内置 `EnableMsixTooling=true`——WASDK 2.x 单文件发布内嵌 resources.pri 所必需，缺了会直接报错。
- `IncludeNativeLibrariesForSelfExtract` 把 XAML / SkiaSharp 等原生 DLL 一并打进 exe，首次启动解压到临时目录（略慢），之后启动正常。
- 不加 `EnableCompressionInSingleFile=true` 则体积更大（约 +30 MB）但免去解压、启动更快。
- 旁边的 `*.pdb` 是调试符号，不需要随包分发。
