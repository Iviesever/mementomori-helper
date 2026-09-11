# Android 账号导出器 · 0.1.0 预览版

这是新增的手机独立入口，不是连接电脑 localhost 的网页壳。界面使用原生 MAUI 控件，协议和数据模型复用此仓库；没有 Kestrel、BAT、PowerShell 子进程或电脑常驻服务。

**当前提交提供源码、测试和 APK 构建工作流。只有工作流成功并出现 Signed APK 后才有可安装产物；源码静态检查不等于 APK 编译成功，也不等于真机登录测试通过。**

## 使用流程

安装 `MementoMori-Exporter-android-arm64-preview.apk` 后：

1. 将已有私有登录配置 `appsettings.user.json` 通过自己可信的本地方式传到手机，点“导入登录配置”。它不是导出的 `mementomori-account-*.json`。Windows 现有配置位于 `%LOCALAPPDATA%\MementoMoriExporter\appsettings.user.json`。
2. 选择账号，点“读取账号”，再选择区服。页面会显示当前读取阶段。首次下载基础资料可能较慢；以后会复用手机自己的基础数据缓存。
3. 勾选玩家、主线、等级链接、角色、装备符石、队伍、背包或卡池。默认单个精简 JSON；需要分文件时选择 ZIP。
4. 点“导出并分享”，使用 Android 系统分享面板。分享面板允许选择支持接收文件的 App；能否直接接收由目标 App 决定。也可以回到目标 App 的附件选择界面选择已保存文件。

导出完成后页面保留，可以再次导出或重新分享上一个文件。App 不会替你操作游戏、自动抽卡、领奖或推进战斗。

## 配置与隐私

- **不要把私有配置上传到 GitHub、PR、Actions、ChatGPT 或普通群聊。** 本 App 不要求将登录密码交给第三方服务；初版仅导入已有登录配置，没有新建账号/引继密码交换流程。
- 只读取白名单登录字段。导入文件中的自动任务、BattleLog 上传、任意报告地址和本地路径均不采用；登录地址限定为 HTTPS 的游戏认证域名。
- 配置持久化使用 MAUI SecureStorage，运行中的可写配置仅在内存里。不会写出明文 `appsettings.user.json` 到 App 工作目录。
- Manifest 关闭系统备份与设备迁移，并禁止明文 HTTP。共享 FileProvider 仅允许 `cache/sharing-root/`，不暴露整个私有目录。
- 导出文件使用明确的字段投影，扫描禁止字段名，不输出原始 UserId、PlayerId、游戏 GUID、ClientKey 或 Session。昵称与游戏养成数据仍属于个人数据，不等于完全匿名。
- 原核心错误日志可能包含请求正文，本 App 禁用这些控制台输出和日志提供者，界面只显示固定进度与异常类型。
- **传到手机公共目录里的原始配置文件不会被 App 自动删除。** 导入后请自行删除传输副本。清除本机配置会删除本 App 的安全存储内容和临时导出文件，不会删除游戏账号。

## 快照语义（保留为未来事项）

本版不实现“每次导出前自动刷新账号”。主动点“读取账号”时建立快照；同一会话重复导出使用该快照。界面和 JSON `source.accountDataFetchedAtUtc` 标示读取时间，`generatedAtUtc` 只是文件生成时间。勾选卡池时会额外读取卡池列表；它的时点可能不同于角色/背包快照。

游戏端变更后需要主动再点“读取账号”。并发登录和会话失效需要真机验证；读取前建议先完成游戏内操作。本版不承诺后台常驻或跨进程保留已读取的快照。

## 构建与产物

新增工作流 `.github/workflows/android-exporter.yml`，名称 **Android account exporter (preview)**。对 Android 功能分支 push 或相关 PR 运行，不依赖原仓库发布签名 secrets，也不修改原 Windows 发布流程。

工作流顺序：无账号测试 → 安装 MAUI Android 工具链 → 安装对应 Android SDK 依赖 → 生成带开发签名的 ARM64 APK → 上传 APK 和 SHA256。APK 下载入口在成功运行的 **Artifacts**，名称 `mementomori-exporter-android-arm64-preview`。

这是 **开发签名预览包**，不是应用商店正式发布。干净 CI 环境可能生成不同的开发签名，后续 APK 未必能覆盖旧包；可能需要卸载旧预览包并重新导入配置。正式分发前需要维护者保管稳定的签名密钥，不应把密钥提交到仓库。

本地构建（已安装 .NET 9 SDK 和 Android 工具链的机器）：

```powershell
& {
    $ErrorActionPreference = 'Stop'
    dotnet workload install maui-android
    if ($LASTEXITCODE -ne 0) { throw 'workload installation failed' }
    dotnet test tests/Exporter.Android.Tests/Exporter.Android.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'tests failed' }
    dotnet build MementoMori.Exporter.Android/MementoMori.Exporter.Android.csproj -c Debug -f net9.0-android -t:SignAndroidPackage -p:RuntimeIdentifier=android-arm64 -p:AndroidKeyStore=false
    if ($LASTEXITCODE -ne 0) { throw 'APK build failed' }
}
```

输出在项目 `bin/Debug/` 子目录下的 `*-Signed.apk`，不是原有完整 Helper 的 APK。目标 Android 7.0/API24 及以上，ARM64。

## 验证范围与后续

测试覆盖：配置白名单/错误输入/地址约束/大小上限/重复账号、敏感属性递归拒绝、正常文本不误判、ZIP 清单与等级链接成员、内存可写配置。它们使用虚构账号值，不联网登录。

还必须实测：APK 编译安装、首次读取与第二次启动、区服选择、中文角色字段、分享面板与目标 App、清除配置、断网恢复。导出字段投影以 Windows v3.1 为基线，当前保持 Android 独立文件，避免更改已可用的 Windows 导出器；未来可在测试覆盖下提取共享模块。

本预览版复用仓库 .NET 9 / MAUI 9 工具链和已有协议依赖，**没有顺带升级或解决原有 MessagePack 等依赖漏洞警告**。发布正式版前仍需依赖安全审查、稳定签名与真机验收。

代码沿用仓库根目录 LICENSE；未复制用户登录配置、游戏资源、签名密钥或字体文件。
