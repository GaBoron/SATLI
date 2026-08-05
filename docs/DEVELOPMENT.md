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
