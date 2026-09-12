# Android 账号导出器 · 0.4.0 预览版

手机原生 MAUI 入口，协议和数据模型复用此仓库，不需要电脑在线或网页服务。此项目只提供账号读取和数据导出，不是完整 Helper 的所有自动化功能。

**自动化测试、APK 签名校验、真实手机登录是不同的验收项。以当前提交对应的 Actions 为准；无账号测试不能代替真实引继、系统保存和覆盖升级验收。**

## 登录、记住账号、保存数据

1. 安装 ARM64 预览 APK，最低 Android 7.0/API24。
2. 首次输入自己的引继码（用户 ID）和引继密码，备注可不填。点“引继登录并保存账号”，成功后先安全保存登录凭据，再选择区服并读取账号。
3. 下次打开恢复账号选择器，点“读取已保存的账号”；不在启动时自动联网，不需要再次输入仍有效的密码。
4. 玩家、主线、等级链接及成员、角色属性、装备符石、队伍、背包、卡池 **8 类默认全部勾选**。可取消或点击全选，重新启动仍默认全选。
5. 生成精简/易读 JSON 或分文件 ZIP，然后保存到系统选定位置或分享。重复保存、分享不重新联网；修改勾选或格式后需重新生成。

同一账号引继登录更新原条目，最多保存 32 个账号。取消区服选择或读取失败不会自动删除保存的账号。登录凭据失效后需要重新引继登录。

登录账号自动记住；养成文件由用户点“保存文件到手机”保存。内存快照不跨进程保留，重启后生成新文件需重新读取，已经保存到公共目录的文件不受影响。

## 隐私和快照规则

引继复用 `GetClientKey` 的 createUser/getComebackUserData/comebackUser 流程，可能影响其他设备的旧登录凭据，建议先退出游戏。密码原样提交、不删除首尾空格；提交结束后清空输入框，**不持久化引继密码**。

保存的是服务器返回的 ClientKey、账号 ID、备注和必要连接参数，使用 MAUI SecureStorage。交换或保存失败不提前覆盖既有内存账号；本机保存失败时明确提示远端引继可能已经生效，不声称服务器回滚。

核心沿用读取 `list.moonheart.dev` 公共 AuthToken 元数据的步骤，公共元数据请求不附带用户密码或 ClientKey。真实引继、维护和协议兼容性仍需实测。备用配置导入只采用白名单登录字段，不导入任意报告地址、自动任务或本地路径。

养成导出沿用 `mementomori-safe-account-export-v3.1`，不导出密码、ClientKey、Token、Session、原始账号 ID 或游戏 GUID。它仍包含昵称和养成信息，不是完全匿名。

**不在每次生成前自动刷新。** 主动读取建立快照；`accountDataFetchedAtUtc` 与文件的 `generatedAtUtc` 分开。卡池列表在选中时另行读取，可能晚于账号快照；缺失响应/失败与正常空列表明确区分，取消不伪装成成功。

资料缺失时保留真实编号和数量并显示解析状态，不猜名称。缺失符石的等级/攻击型是 null，不是伪造的 0/false；碎片稀有度先通过合成表映射实际装备编号。

系统保存使用 ACTION_CREATE_DOCUMENT，不申请全盘存储权限。FileProvider 只暴露临时导出目录，拒绝目录外、符号链接和未完成文件。关闭系统备份与明文 HTTP，继承的协议控制台和日志提供者关闭。清除本机账号只删除导出器的凭据和临时文件，不删除游戏账号或已经保存/分享的副本。

不要向聊天、GitHub、CI 或普通群聊发送私有配置、引继密码或签名私钥。

## 0.4.0 工具链与验证

Android 使用 .NET 10 / MAUI 10.0.101；共享协议和桌面项目仍为 net9.0。MessagePack 与 Annotations 使用 2.5.302 安全回补，CI 扫描直接和传递依赖；未完成/失败的扫描不能标记为无漏洞。

从 Android 项目目录执行构建，让其中的 global.json 选中 .NET 10：

```powershell
Set-Location MementoMori.Exporter.Android
dotnet workload install maui-android
if ($LASTEXITCODE -ne 0) { throw 'workload installation failed' }
dotnet build -c Debug -f net10.0-android -t:SignAndroidPackage -p:RuntimeIdentifier=android-arm64 -p:AndroidKeyStore=false
if ($LASTEXITCODE -ne 0) { throw 'APK build failed' }
```

仓库根目录的测试使用 .NET 9 SDK，运行 `dotnet test tests/Exporter.Android.Tests/Exporter.Android.Tests.csproj -c Release`。

CI 保留 TRX、SDK 信息、漏洞扫描 JSON、编译日志、APK 签名报告、源码/构建提交与 SHA256。固定 MessagePack wire/实际 DTO 测试只用虚构数据。Windows 打包离线启动检查不登录账号，也不能当作 Android 真机检查。

## 预览签名和正式签名

普通 CI APK 仍为临时开发证书，可能无法覆盖旧预览包。先确认持有自己的引继码/密码或可信私有配置备份，再处理卸载重装；**只卸载导出器，不要卸载游戏本体**。

独立受保护的签名工作流位于 `.github/workflows/exporter-stable-signing.yml`。Release 构建不接触正式私钥，签名步骤校验固定证书指纹并拒绝 debuggable 包。一次性测试密钥仅用于自动化，不是已配置正式密钥的证明。

维护者仍需配置 android-release 环境的审核/分支限制和签名 secrets，并保留独立密钥备份。具体步骤与验收边界见 `tools/release/RELEASE-READINESS.md`。真实引继、重启恢复、断网恢复、系统保存/分享及两次稳定签名包覆盖升级仍需实机验证。

源码沿用根目录 LICENSE；不提交用户真实导出、账号配置或签名密钥。
