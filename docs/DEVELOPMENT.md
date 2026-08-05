# 🛠️ 开发与构建

[返回项目首页](../README.md)

## 🧱 技术结构

- `src/Satl.Gui/`：C#、WinUI 3 图形界面
- `src/satl/`：Python 核心逻辑与命令行接口
- `tests/Satl.Gui.Tests/`：GUI 与服务层测试
- `tests/`：Python 核心测试
- `installer/`：Inno Setup 安装程序配置
- `scripts/build.ps1`：完整发布构建脚本

## 📋 开发环境

- Windows 10/11 x64
- Python 3.13.0 或更高的兼容版本
- .NET SDK 10.0.0
- WinUI 3 工具链
- Inno Setup 6.0.0（仅完整发布构建需要）

## 🧪 安装与测试

```powershell
python -m venv .venv
.\.venv\Scripts\pip.exe install -e ".[dev]"
.\.venv\Scripts\python.exe -m pytest -q
dotnet test .\tests\Satl.Gui.Tests\Satl.Gui.Tests.csproj -c Release
```

## 📦 构建发布包

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

构建脚本会发布自包含的 WinUI 应用、打包 Python 运行环境、生成 Inno Setup 安装程序，并计算 SHA-256。最终文件位于 `dist\release\`。

### 🔏 公开发布的代码签名

Windows 的“已验证的发布者”来自 Authenticode 证书的签名主体与受信任证书链，不能通过 `AppPublisher`、程序集公司名或自签名证书为其他用户启用。正式发布必须使用可链接到 Windows 受信任根的代码签名证书；私钥应由硬件令牌或云 HSM 保护。

把签名提供商公开的证书同步到构建账户的 `CurrentUser\My` 或 `LocalMachine\My` 证书存储后，设置证书 SHA-1 指纹并构建：

```powershell
$env:SATL_SIGNING_CERTIFICATE_SHA1 = "PUBLIC_CERTIFICATE_THUMBPRINT"
$env:SATL_TIMESTAMP_URL = "http://timestamp.digicert.com" # 可省略
./scripts/build.ps1 -RequireCodeSigning
```

构建流程会依次签署并验证：

1. 安装后的 `SATLInstaller.exe`
2. Inno Setup 安装程序
3. Inno Setup 卸载程序

`-RequireCodeSigning` 会拒绝无证书、自签名证书、无私钥证书、过期证书或无法链接到 Windows 受信任根的证书。未指定该开关时仍可生成仅供本地测试的未签名构建；也可显式提供自签名证书测试签名流程，但不得发布该产物。

GitHub Actions 的 tag 构建固定使用 `-RequireCodeSigning`，并从仓库变量 `SATL_SIGNING_CERTIFICATE_SHA1` 读取证书指纹。签名服务还必须先在 runner 中提供证书和私钥访问能力。符合条件的开源项目可申请 [SignPath Foundation](https://signpath.org/)；其他选择包括公共 CA 的硬件令牌或云 HSM 签名服务。签名身份获批并接入前，正式 tag 构建会按预期失败，而不是产生未签名发布包。

## 🧭 单独运行项目

调试 Python CLI：

```powershell
.\.venv\Scripts\python.exe -m satl --help
```

构建 WinUI 项目：

```powershell
dotnet build .\src\Satl.Gui\Satl.Gui.csproj -c Debug -p:Platform=x64
```

图形界面依赖随发布包内置的 Python 核心。对 CLI 协议、参数或输出结构的修改，应同步验证 Python 与 C# 两侧测试。
