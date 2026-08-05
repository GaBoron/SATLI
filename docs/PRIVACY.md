# 数据与隐私

[返回项目首页](../README.md)

Steam 成就翻译管理器以本地处理为主。扫描、编辑、备份和恢复均在当前 Windows 用户的设备上完成。

## 本地数据

默认数据目录为：

```text
%LOCALAPPDATA%\SteamAchievementTranslationInstaller\
```

其中可能保存：

- 应用设置和窗口位置
- 游戏名称与云端列表缓存
- 安装、编辑和恢复历史
- 编辑草稿、原文件备份与强制恢复归档
- 用户启用的诊断日志

本地编辑只修改选择的目标语言，不会自动把本机 schema 上传到网络。应用本身不会因切换 Microsoft Store、独立安装版渠道而主动清理此目录中的数据。

## 凭据

可选的 Steam Web API Key、GitHub 访问令牌和刷新令牌使用 Windows DPAPI CurrentUser 加密保存，仅当前 Windows 用户能够解密。

在设置中解绑 GitHub 会删除本机保存的 GitHub 凭据。如需撤销服务端授权，请继续打开设置中提供的 GitHub 授权管理页面。

## 网络请求

应用会根据用户使用的功能连接以下服务：

- **翻译目录和译本：** jsDelivr（含 Fastly 域名）、GitHub Raw、StaticDelivr，或用户调整后的来源
- **软件更新：** Microsoft Store 版由 Microsoft Store 管理；独立安装版通过 GitHub 获取更新信息和发布包
- **Steam 游戏库：** 用户主动配置凭据后，通过 Steam Web API 补全账号游戏库
- **游戏名称：** 通过游戏名称数据源补全 App ID 对应的名称
- **账号绑定和问题报告：** 通过 GitHub 授权与 Issue 接口绑定账号或提交云端文件报告

提交文件报告时只发送游戏名、App ID、商店链接、问题类型、用户填写的说明和可选参考来源，不上传本机成就文件。

## 诊断日志

日志不会记录 Steam Web API Key、GitHub 令牌、设备码、完整授权响应或成就文件正文。

日志可以关闭，并可选择普通、详尽或临时 Debug 级别。Debug 日志可能包含本机目录、Steam 路径、App ID、游戏名和文件诊断信息，体积也会明显增大。向他人发送日志前，请先检查并删除不希望分享的内容。

## 文件保护

应用在写入前创建备份。若目标文件之后被 Steam 或其他程序修改，普通恢复会拒绝覆盖；强制恢复也会先归档当前文件，再恢复原备份。

这些保护用于减少误覆盖风险，但不能代替重要数据的额外备份。
