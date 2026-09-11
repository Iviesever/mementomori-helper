# Android 账号导出器 · 0.2.0 预览版

这是手机独立入口，不是连接电脑 localhost 的网页壳。界面使用原生 MAUI 控件，协议和数据模型复用此仓库；没有 Kestrel、BAT、PowerShell 子进程或电脑常驻服务。现有 Windows 导出器和主分支发布流程不变。

**APK 编译成功、无账号测试通过、真机登录成功是不同的验收项。以当前提交对应的 Actions 结果为准；不要将上一提交的成功结果视为本提交已经通过。**

## 使用流程

1. 安装 `MementoMori-Exporter-android-arm64-preview.apk`。目标 Android 7.0/API24 及以上、ARM64。
2. 通过自己可信的本地方式传入已有私有 `appsettings.user.json`，点“导入登录配置”。它不是 `mementomori-account-*.json` 游戏数据导出文件。Windows 现有配置位于 `%LOCALAPPDATA%\MementoMoriExporter\appsettings.user.json`。
3. 选择账号，点“读取账号”，再选择区服。首次下载基础资料可能较慢；以后复用手机的数据缓存。完成后显示区服、昵称、Rank、角色数量和账号读取时间。
4. 勾选玩家、主线、等级链接、角色、装备符石、队伍、背包或卡池。默认精简 JSON，也可选择 ZIP。
5. 点“生成文件”，核对已生成文件的格式、大小和内容。修改勾选项或格式后需要重新生成。
6. 点“保存文件到手机”，通过 Android 系统文件选择器选择保存位置；或点“分享已生成的文件”，使用系统分享面板。保存和分享均不重新联网读取账号。取消保存不会删除临时文件，可以重试或改为分享。

保存到公共目录后，可在聊天 App 的附件选择器中选择该文件。目标 App 是否支持直接接收分享，以目标 App 为准；打开分享面板不等于已发送成功。

## 0.2.0 的改动

- 分离生成、保存、分享，增加系统 `ACTION_CREATE_DOCUMENT` 保存入口；只获得用户所选文档的访问权，不增加存储权限，不申请整个目录权限。也不持久化该文档的访问授权。
- 文件复制在后台线程执行，避免文档提供器阻塞界面；取消或窗口被系统销毁时释放等待状态。写入失败时尽力删除新建的不完整文档；部分文档提供器可能不允许删除，请自行检查目标目录。
- 生成到私有临时目录，完整写入后才发布为可分享文件。保存和分享只接受导出目录下的 JSON/ZIP，不接受登录配置、目录外路径、符号链接或未完成文件。
- 切换账号、重新读取或重新生成时清除旧文件指示；生成时在服务层再次校验账号身份。账号读取使用新的协议会话，保留已导入账号与本机基础资料缓存。
- 安全存储损坏或读取失败时不自动删除配置；没有成功载入账号也能点清除并重新导入。配置解析失败不写入安全存储，写入失败不返回替换后的账号。
- APK 附带 `BUILD-INFO.json`，记录源码提交、实际构建提交、工作流编号与 SHA256，便于核对安装的是哪一版。

## 配置与隐私

**不要把私有配置上传到 GitHub、PR、Actions、ChatGPT 或普通群聊。** 初版仍只导入已有登录配置，没有新建账号或引继密码交换流程，也不会要求把密码交给第三方服务。

只导入白名单登录字段。导入文件中的自动任务、BattleLog 上传、任意报告地址和本地路径均不采用；登录地址限定为 HTTPS 游戏认证域名。持久化使用 MAUI SecureStorage，运行中的可写配置只在内存；不向 App 工作目录写出明文 `appsettings.user.json`。

Manifest 关闭系统备份与设备迁移，并禁止明文 HTTP。FileProvider 仅允许 `cache/sharing-root/`，不暴露整个私有目录。生成的文件保留昵称与养成数据，不是完全匿名；字段投影与敏感属性检查不输出原始 UserId、PlayerId、游戏 GUID、ClientKey 或 Session。仍应只分享给可信对象。

继承的核心错误日志可能包含请求正文，本 App 禁用控制台输出和日志提供者；界面只显示固定提示和异常类型，不显示远端错误正文、凭据或保存地址。

导入后请自行删除手机公共目录里的原始配置传输副本。“清除本机登录配置和临时文件”删除本 App 安全存储内容和临时导出文件，不删除游戏账号，也不会撤回或删除已经保存、上传或分享出去的副本。即使没有导入账号，这个清除入口也可用。

## 快照语义

按当前既定范围，**不实现每次导出前自动刷新账号**。主动点“读取账号”时建立快照；同一会话重复生成使用该快照。界面与 JSON `source.accountDataFetchedAtUtc` 表示读取时间，`generatedAtUtc` 只是生成时间。勾选卡池时额外读取卡池列表，它的时点可能不同。

游戏内变更后需要主动再点“读取账号”。切换账号后不能把旧账号文件作为新账号文件保存或分享。读取失败或取消区服选择后不能生成快照。App 不自动抽卡、领奖或推进战斗，不承诺后台常驻或跨进程保留快照。

## 构建与产物

工作流：`.github/workflows/android-exporter.yml`，名称 **Android account exporter (preview)**。在 Android 功能分支、续作分支或相关 PR 上运行，不依赖原 Windows 发布签名 secrets。

顺序：配置/导出/存储测试 → MAUI Android 工具链 → Android SDK 依赖 → 自包含 ARM64 开发签名 APK → 上传 APK、SHA256 与构建信息。成功运行的 Artifacts 中，产物名为 `mementomori-exporter-android-arm64-preview`；测试报告名为 `android-exporter-test-results`。

这是**开发签名预览包**，不是应用商店正式发布。干净 CI 环境可能使用不同开发签名，后续包未必能覆盖旧包，可能需要卸载重装并重新导入配置。卸载前请自行确认持有可信的私有配置备份，不要把签名密钥或账号配置提交到仓库。

本地构建（.NET 9 SDK、Java 与 Android 工具链）：

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

输出在项目 `bin/Debug/` 子目录下的 `*-Signed.apk`，不是原有完整 Helper 的 APK。

## 验证边界与真机验收

无账号测试使用虚构凭据，覆盖配置白名单/地址约束/大小上限/重复账号、敏感属性拒绝、ZIP 契约、内存配置，以及安全存储失败恢复、文件原子发布、复制、过期清理、目录隔离、重复生成。它们不登录游戏，不覆盖 Android 系统文件选择器或真实 SecureStorage 实现。

真机仍需依次确认：首次启动和第二次启动；导入与重新导入；账号与区服切换；断网后恢复；中文角色字段；JSON/ZIP 生成；保存到本地目录后从聊天附件选择器打开；取消保存后再次保存；分享后回到 App；清除配置与临时文件。配置读取恢复使用的是受控测试替身，不代表已经在所有厂商系统上验证密钥库故障恢复。进程被系统终止时不保留读取或保存操作，应重新读取和生成。

本预览版沿用仓库 .NET 9 / MAUI 9 及协议依赖，**没有顺带升级或解决已有 MessagePack 等依赖漏洞警告**。正式发布前仍需依赖安全审查、稳定签名与真机验收。源码沿用根目录 LICENSE，未复制用户配置、游戏资源、签名密钥或字体。
