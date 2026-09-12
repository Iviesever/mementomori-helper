# Android 账号导出器 · 0.4.1 预览版

手机原生 MAUI 入口，协议和数据模型复用此仓库，不需要电脑在线或网页服务。此项目只提供账号读取和数据导出，不是完整 Helper 的所有自动化功能。

**自动化测试、APK 签名校验、真实手机登录是不同的验收项。以当前提交对应的 Actions 为准；无账号测试不能代替真实引继、系统保存和覆盖升级验收。**

## 登录、记住账号、保存数据

1. 安装固定证书签名的 ARM64 APK，最低 Android 7.0/API24；不要把 CI 开发测试包当成日常更新。
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

## 0.4.1 工具链与验证

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

## 更新不再依赖反复卸载

从本次修复起，普通 CI **不再上传可供日常安装的临时签名 APK**。Debug 使用独立的 `.dev` 包名和“开发测试”名称；无账号设备测试仍为 `.devicetest`。两者不是正常应用的更新渠道。正常包名只由 Release 路径构建，并通过唯一的受保护固定证书工作流分发。

正常升级必须保持相同包名、相同签名私钥/证书，并使用更大的 versionCode；本机保存账号的存储键不变，不在升级时清除账号。已有临时证书不能凭新代码变成固定证书，旧私钥不存在时无法无损覆盖旧包。

先保留可用旧版，不必为了测试这项修改而立即卸载。维护者在自己的电脑完成一次长期密钥配置，再交付固定签名的第一个版本。首次从旧临时包迁移可能仍需一次重新安装和引继登录；之后同一固定证书的更新才能正常覆盖。不得将签名证书冲突提示改成忽略校验，或将养成数据 JSON 当作登录凭据备份。

维护者入口：`tools/release/SETUP-ANDROID-UPDATES.cmd`。它复用已有本地密钥，只有明确确认才新建；将秘密通过已登录的 GitHub CLI 标准输入上传到已保护的 `android-release`，拒绝覆盖不同的证书指纹。不设置虚假的真机审批、不放宽保护、不自动发布 APK。操作说明见 `tools/release/ANDROID-UPDATES.md`。

设备 CI 构建两个不同版本的测试 APK，先写入虚构账号，再执行 `adb install -r`，由新版实际解密旧 SecureStorage 并读取旧导出文件；中途不卸载、不清数据。这是 Android 模拟器验证，不是用户手机或真实游戏登录验收。

源码沿用根目录 LICENSE；不提交用户真实导出、账号配置或签名密钥。
