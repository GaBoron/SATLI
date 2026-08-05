# Microsoft Store 发布

[返回项目首页](../README.md)

## 发布模型

本项目使用 **MSIX 完全信任桌面应用** 进入 Microsoft Store，而不是 PWA。应用需要扫描本机 Steam 安装、读写游戏成就文件、运行内置 Python 核心，并在写入受保护目录时按需请求管理员权限；这些核心能力不能由浏览器沙箱中的 PWA 完整提供。

MSIX 与现有 Inno Setup 安装程序复用相同的 WinUI 单文件程序和内置 Python 负载。商店包具有 Windows 包身份，并声明 `runFullTrust`；应用检测到包身份后会停止 GitHub 安装器自更新，改由 Microsoft Store 管理软件更新。

## Partner Center 准备

先在 Partner Center 中创建应用并保留名称，然后从“产品标识”页面取得以下三个值：

- Package/Identity/Name
- Package/Identity/Publisher（通常以 `CN=` 开头）
- Package/Properties/PublisherDisplayName

仓库中的默认值只用于本地打包验证，不能代替 Partner Center 分配的正式标识。正式提交时，清单标识必须与 Partner Center 完全一致。

## 构建 Store MSIX

```powershell
.\scripts\build.ps1 `
  -Target StoreMsix `
  -PackageIdentityName '<Partner Center Identity Name>' `
  -PackagePublisher '<Partner Center Publisher>' `
  -PublisherDisplayName '<Partner Center Publisher Display Name>'
```

产物位于 `dist\release\`：

- `SATLInstaller-Store-vMAJOR.MINOR.PATCH.msix`
- `SHA256SUMS.txt`

项目公开版本始终使用三段语义版本。MSIX 清单要求四段版本，因此构建脚本会将 `MAJOR.MINOR.PATCH` 映射为内部包版本 `MAJOR.MINOR.PATCH.0`。

生成的包不带开发者签名，供 Microsoft Store 提交使用；Store 在认证过程中会重新签名。若要在本机旁加载测试，需使用与清单 Publisher 匹配且受测试机信任的测试证书签名，不要把测试私钥或证书提交到仓库。

## 提交前检查

1. 使用正式 Partner Center 标识重新构建。
2. 运行 Python 与 C# 测试，并在未打包和本地签名的 MSIX 环境分别验证启动。
3. 验证 Steam 扫描、文件选择器、安装、恢复、按需 UAC 和单实例激活。
4. 使用 Windows App Certification Kit 检查最终包。
5. 在 Partner Center 填写 `runFullTrust` 使用理由，并完成隐私、年龄分级、商店文案与截图。

Microsoft Store 会管理 MSIX 版本更新；GitHub Releases/Inno Setup 通道仍可继续服务未通过 Store 安装的用户。
