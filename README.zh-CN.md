# 叠影 · Dieying

[GitHub 仓库](https://github.com/mayday-stacy/Dieying) · [English](README.md) · [使用指南](docs/README.md) · [变更记录](CHANGELOG.md) · [源码提交与发布](RELEASING.md)

本项目从 [Composa v1.4.0](https://github.com/dvdstelt/Composa/releases/tag/v1.4.0) 开始，基线提交为 `86529d3b5355d28ddc28ec3db2e4de1d4d00f63b`。目标是 Windows 优先的日常修图与合成：图层、蒙版、抠图、可编辑文字和接近 Photoshop 的操作方式。

产品名称为“叠影 · Dieying”，机器标识和可执行文件为 `dieying`，目前仍处于独立开发阶段。图像编辑核心来自 Composa，本分支的增量在下方单独列出；关于窗口明确列出来源，不将本分支冒充上游官方版本。`.cmps` 工程格式、源码命名空间和原作者许可保留兼容。公开源码与二进制正式发布是两个阶段；当前提供开发构建和 Windows 便携打包流程，尚无签名安装器。

## 在 Windows 运行

开发需要 Git 和 .NET 10 SDK（版本选择见 `global.json`）。在 PowerShell 中克隆独立仓库后执行：

```powershell
git clone https://github.com/mayday-stacy/Dieying.git
Set-Location Dieying
.\scripts\windows.ps1 -Action Run
```

保留 Git 历史和标签后构建；版本由 MinVer 从最近标签推导，不手改版本号。上游标签表示继承的开发基线，不表示已发布过同号 Dieying 版本。

首次构建需要访问 NuGet，以及下载约 26 MB 的 MODNet 人像模型。三个本地模型均按固定大小和 SHA-256 验证；已有有效文件会复用。程序运行时不下载模型。

其他入口：

```powershell
.\scripts\windows.ps1 -Action Build
.\scripts\windows.ps1 -Action Test
.\scripts\windows.ps1 -Action Models
.\scripts\windows.ps1 -Action Publish -Runtime win-x64 -Zip
```

`Publish` 在 `dist/` 下新建独立目录和可选 ZIP、SHA-256 校验文件，包含 .NET 运行时、图像库、模型与许可文件，不要求使用者安装 .NET。解压后运行 `dieying.exe`。它不安装程序、不注册文件关联，也不替换以前的输出。`win-arm64` 参数可生成 ARM64 包；本机验收仅针对 x64。这里的发布仅指本地打包，不会上传到 GitHub。

默认验证所有模型。仅开发动作允许 `-SkipModels`；缺少 MODNet 时人像模式会退回现有的背景检测，不能把这种运行当成完整人像模型验收。

## 本分支的改动

- 原生 PowerShell 构建、测试、模型准备、启动与便携打包入口。
- 中文等所选字体缺少的字形，按字符使用系统字体回退，测量与绘制共用字体。
- 光标、换行、删除和选区按 Unicode 字形簇处理，避免拆开扩展汉字、组合重音和 emoji 序列；中文段落加入常见标点换行禁则。
- `local` 更新渠道：开发包不会查询、下载或安装上游版本；手动检查会说明从源码重新构建。
- 独立开发数据目录：`Run` 默认使用 `artifacts/local-data/config` 和 `artifacts/local-data/cache`，与已安装的 Composa 配置及恢复文件隔离。
- 独立产品身份：直接启动也使用自己的设置、恢复目录和 AI 控制管道；崩溃恢复通过进程 ID 与启动时间识别活跃实例。
- PNG/JPEG/WebP 统一导出选项：导出尺寸、保持比例、恢复原尺寸；JPEG/WebP 独立质量，JPEG 白色或黑色背景，实际编码的预览和文件大小。导出不改变工程尺寸或已保存状态，后台写盘，关闭等待写完；失败保留原文件并清理临时文件。
- 测试入口逐项目验证 TRX 和实际执行数量。测试进程返回成功但没有执行测试时会明确失败，并保留本次独立日志。
- 中文合成工作流回归：文字输入、图层与蒙版、撤销重做、中文路径保存、重新打开、PNG 像素一致性；产生可编辑的示例工程。

脚本的所有动作与直接 `dotnet build` 均使用 `UpdateChannel=local`。更新兼容测试在测试内部显式注入 GitHub 渠道及模拟环境，不访问 GitHub 或启动安装器；测试构建不是分发包。

可通过进程环境变量 `DIEYING_DATA_DIR` 指定其他**绝对路径**，其中分别建立 `config` 与 `cache`。相对路径会被拒绝；未设置新变量时，仍兼容旧的 `IMAGE_EDITOR_DEV_DATA_DIR`，建议切换到新变量。直接启动默认配置在 `%APPDATA%\dieying`、恢复缓存等在 `%LOCALAPPDATA%\dieying`；Linux 分别使用 XDG 配置和缓存目录下的 `dieying`，macOS 使用 Application Support 和 Caches 下的同名目录。不会读取旧的 `COMPOSA_DATA_DIR`，不迁移或覆盖上游设置；原有 `.cmps` 项目可以手动打开。

默认配置目录下尚无 `settings.json` 时，首次加载会尝试只读使用相邻 `image-editor-dev` 目录中的开发版设置，后续保存写入新的 `dieying` 目录，不修改旧文件。旧开发版的恢复缓存保留在原处，需用旧版恢复并保存工程后再打开；不会迁移或删除这些缓存，也不会读取上游 Composa 的配置。

AI 控制的独立环境变量是 `DIEYING_MCP_PIPE`，Windows 默认管道为 `dieying-mcp`。源码命名空间和已有 MCP 工具/资源协议仍兼容。

上游 Linux 安装包和 Inno Setup 配方保留作参考，其固定安装器 AppId 没有更改；由于这些配方会占用 Composa 的名称和文件关联，本分支已拒绝使用它们打包，CI 仅制作独立 Windows 便携包。公开 Release 自动化保持停用。上传开源源码不需要先完成安装器或签名；未来分发正式二进制时，再配置自己的更新来源、发布流程，以及安装器身份和签名。

## 验收与示例

完整测试：

```powershell
.\scripts\windows.ps1 -Action Test
```

只生成并验证中文示例：

```powershell
dotnet test tests/Composa.App.Tests -c Release -p:UpdateChannel=local --filter FullyQualifiedName~WindowsCompositionWorkflowTests
```

输出：

- `artifacts/acceptance/Windows 中文合成.cmps`：可继续编辑的图层、蒙版与文字。
- `artifacts/acceptance/Windows 中文合成.png`：导出图片。
- `artifacts/screenshots/windows-composition-workflow.png`：无头界面测试截图。

示例图形由测试代码生成，不包含外部摄影素材。无头输入事件可验证文字提交和渲染，**不能替代 Windows 拼音输入法预编辑、候选框和候选定位的人工验收**。真实照片的发丝、透明物体抠图、大尺寸工程性能、数位板和多显示器仍需进一步验收。

## 当前兼容边界

- 原生保存格式为 `.cmps`，与 Mac Compositor 的 `.comp` 不兼容。
- PSD/PSB 仅导入 8-bit RGB；不支持 PSD 导出、CMYK 或 16-bit PSD。部分内容被栅格化、部分效果丢弃，以导入转换报告为准。
- 菜单、工具栏、常用对话框、历史记录支持简体中文和英文。在“帮助 > 语言 / Language”选择语言，重启应用后生效；默认跟随系统。底层库返回的部分技术错误仍保留原文，用户的图层名、文件名、字体名不会自动翻译。
- 画布文字已接入输入法预编辑和候选定位。取消候选保留正文与选区，选词后才写入文字与撤销历史；暂不支持输入法对已提交正文的系统重转换。真实 Windows 拼音已验证选词、取消、连续输入、撤销重做和保存，其他输入法仍需实机覆盖。
- 中文段落加入常见标点换行禁则；光标、删除和选区按 Unicode 字形簇移动，不拆组合重音、肤色修饰和 ZWJ 序列。绘制仍沿用 Skia 字形与字体回退，不承诺完整复杂脚本塑形或彩色组合 emoji。未安装的文字字体会在选项栏提示，并保留工程中记录的原字体名。
- 便携包属于未签名的开发版本，不应当标为已正式发布的完整 Photoshop 替代品。

## 来源与开源

Composa 由 Dennis van der Stelt 开发，基于 Robbie Tilton / Wonder Assembly LLC 的 [Compositor](https://github.com/robbietilton/Compositor) 思路重新实现。保留 [MIT LICENSE](LICENSE) 中所有原作者声明；第三方库、模型和素材的许可见 [THIRD-PARTY-NOTICES.txt](packaging/THIRD-PARTY-NOTICES.txt)。所有再分发包都应携带这些许可，不以本分支名义宣称原作或上游作者背书。

源码提交与便携包验收步骤见 [RELEASING.md](RELEASING.md)。首次提交应保留上游历史与许可、排除个人数据及生成产物，并确认独立仓库的所有者和可见性；正式二进制发布另需完成对应的验收与发布配置。

## 中文版本开发验证

输入法、双语界面和中文海报的自动验收：

```powershell
dotnet test tests/Composa.App.Tests -c Release -p:UpdateChannel=local --filter "FullyQualifiedName~CanvasImeTests|FullyQualifiedName~L10nTests|FullyQualifiedName~ChineseWorkflowTests"
```

`ChineseWorkflowTests` 生成 `artifacts/acceptance/中文海报工作流.cmps`、同名 PNG 和 `artifacts/screenshots/chinese-workflow.png`。真实桌面验收另用副本，避免覆盖自动生成的原始示例。

本地化只作用于显示层：`L10n` 读取 `src/Composa.App/Localization/*.resx` 中的中文词条，内部命令 ID、历史名称、面板设置键和文件格式保持稳定。新的显示文字走 `L10n.Text`，含文件名等变量的说明走 `L10n.Format`；用户内容使用 `Ui.RawLabel` 或 `localize: false`。切换语言不改变数值格式。

如果 Windows Smart App Control 拦截测试程序集，测试日志可能出现“没有可用测试”但进程返回成功；这不等于测试通过。`scripts/windows.ps1 -Action Test` 现在会核验两个项目的 TRX 和实际执行数，这种情况返回失败；日志在每次独立的 `artifacts/windows-tests/<时间-唯一标识>/`。程序及脚本均不修改 Windows 安全设置。测试与验收产物由本机或 CI 生成，不随源码提交；检查本次运行的日志，不能把历史结果当作当前提交的验证。

## Smart App Control 与正式签名

手动关闭路径：**Windows 安全中心 → 应用和浏览器控制 → 智能应用控制设置 → 关闭**。Smart App Control 没有单应用放行选项；关闭会减少一层应用执行保护。[微软最新 FAQ](https://support.microsoft.com/en-us/windows/security/threat-malware-protection/smart-app-control-frequently-asked-questions) 说明，近期 Windows 更新已允许不重装系统而重新开启，具体可用选项以本机更新状态为准。这里没有要求关闭 Defender 实时防护。

分发时应解决签名，而非要求每位用户关闭保护。微软要求受信任签发机构的 RSA 代码签名，程序及加载的 DLL 都需纳入验收；普通自签证书或压缩包 SHA-256 不能替代受信任签名。目前没有项目自己的签名证书，本地包仍未签名。[微软开发者签名说明](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control)。

## 尚待完成的功能和验收

已有图层/蒙版/抠图、剪贴板、拖放、可编辑文字、历史记录、自动恢复、PSD/XCF 导入。这些来自上游并由本分支持续验证。余下工作按用途拆分：

| 范围 | 当前边界 | 下一步 |
| --- | --- | --- |
| 文字与语言 | 中文界面和输入法可用，复杂文字塑形、彩色组合 emoji、其他输入法与多屏 DPI 未完整覆盖 | 完整塑形管线及实机矩阵 |
| 文件兼容 | PSD/PSB 只读 8-bit RGB，不含 PSD 导出、CMYK/16-bit 工作流 | 独立评估兼容格式与色彩管线，不能宣称已补齐 Photoshop 兼容性 |
| 效率工具 | 单图编辑和导出已覆盖；没有完整动作录制、批处理、OCR 图片文字翻译工作流 | 按实际使用场景分别实现；OCR/翻译还需确定本地模型或服务 |
| 大图与输入设备 | 具有尺寸/内存限额；真实发丝抠图、大尺寸工程、数位板仍缺专项验收 | 固定真实素材与压力测试 |
| 二进制发布 | 便携包可用，当前无可信签名或独立安装器；源码可以单独公开 | 配置自己的更新来源、正式发行流程、签名与安装关联 |
