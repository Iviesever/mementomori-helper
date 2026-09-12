# Android 0.4.1：固定签名与保留账号更新

## 用户安装规则

只有 `Exporter protected stable signing` 工作流产出的 `mementomori-exporter-android-arm64-stable-signed` 是日常安装渠道。普通 CI 不再上传临时签名 APK；Debug 的 `.dev` 包名和独立 `.devicetest` 包名只用于开发/自动验证。改名、改文件名、增加应用版本号，都不能让两个不相同的签名证书相互覆盖。

已经安装的旧导出器可以先继续使用。不要为本 PR 卸载它。固定签名实际配置好、固定签名 APK 已交付后，再处理一次旧临时签名包的迁移。没有旧私钥时，新证书无法更新该旧包；需要保留自己的引继资料后重新登录。导出的养成 JSON 不包含登录凭据，不能用来恢复登录。

迁移后，更新保持正常包名 `io.github.iviesever.mementomori.exporter` 和同一证书，versionCode 使用唯一工作流的递增序列 `100000 + github.run_number`。不要删除/重建该工作流并重置序号，不在不同签名入口间交替发包，不在升级时清理 SecureStorage。密码输入和账号保存/导出逻辑未改动。

## 一次性配置（仓库维护者本机）

需要 Windows PowerShell、JDK 的 `keytool`、GitHub CLI `gh`。先在自己的电脑完成 `gh auth login`，使用有此仓库环境管理和 secrets 写权限的账号；不要把 token 或密码粘贴到聊天。

先在仓库 Settings → Environments 建立 `android-release`，设置可信 required reviewer，并将部署分支明确限制到 `master` 和/或 `feature/android-account-exporter`。必须用自定义分支限制，不能留为任意分支。脚本会读取并核对这些保护，不会代你放宽它们。

运行 `SETUP-ANDROID-UPDATES.cmd`。首次需要确认 CREATE，输入至少 12 字符的签名密码（由密码管理器保管），把生成的密钥文件做独立加密备份，最后确认 UPLOAD。默认密钥位于：

```text
%LOCALAPPDATA%\MementoMoriExporterSigning\exporter.p12
```

这个密码是你自己的应用签名密码，**不是游戏引继密码**。私钥、签名密码、备份均不得进入 Git 仓库、聊天、Actions artifacts 或公共缓存。脚本会生成长期 RSA 私钥，并使用 PKCS12 保护；脚本不自动保存明文密码。读取密码时的明文仅在进程内用于 keytool 环境变量和 gh 标准输入，finally 清理引用与环境变量，不能视为对进程内存取证的防护。

本机接管配置可先用 `New-AndroidSigningPassword.ps1 -PasswordFile <私有目录>/password.dpapi` 打开两次确认的遮蔽密码窗口，再向初始化工具传入同一路径的 `-PasswordFile` 和独立 `-BackupDirectory`。密码文件使用 Windows 当前用户 DPAPI 加密，目录只允许当前用户和 SYSTEM 访问；密码不进入命令行。初始化工具会在生成密钥前检查仓库/环境已有身份与分支保护，并验证备份文件哈希和实际私钥操作，拒绝覆盖不同的备份。

`password.dpapi` 只适合这台 Windows 的当前账号恢复，不能当作跨电脑密码备份。请在自己的密码管理器中单独保留输入的密码。`exporter.p12` 是有密码保护的可移植密钥，`exporter.cer` 是公开证书。另一分区的备份仍有整机损坏风险，后续应再复制到自己的离机介质；不要上传普通 Release 附件。

重跑会复用同一文件，绝不自动覆盖已有密钥。丢失本地文件时，优先恢复备份，不能重新生成一把当作同一签名。已有其他路径的有效密钥可以直接复用：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Initialize-AndroidSigning.ps1 -KeyPath "D:\PrivateKeys\exporter.jks" -KeyAlias exporter -UploadToGitHub
```

脚本核验私钥密码和公开证书指纹，再检查环境保护。遇到仓库内既存的不同证书指纹，或已有 Android secrets 却没有身份指纹，会拒绝修改，避免无意间换签名。指纹相同允许重传修复中断的初始化。脚本通过 gh 标准输入上传 secrets；设置公开指纹后才上传各项 secrets，部分失败时签名工作流仍会拒绝缺失/错误输入，可用原密钥重试。

## 发布流程与边界

受保护工作流在审核通过的 master/feature 分支上手动运行。Release 编译与私钥签名分离，签名任务需 `DEVICE_VALIDATED_SOURCE_SHA` 精确源码批准。初始化工具**不设置此批准、不触发工作流、不合并 PR、不发布 Release**。真实验收后再由维护者设置它；不能把模拟器通过当成物理手机/真实游戏验收。

首次手机验收：将工作流审阅合入默认分支后，手动运行 `Android account exporter (preview)`，只在需要时勾选 `physical_acceptance_apk`。它输出明确标记 PHONE-ACCEPTANCE-ONLY 的独立 `.dev` ARM64 包与精确源码记录，不访问长期密钥、不替代日常应用、不证明正常包覆盖升级。普通 push/PR 仍不分发临时签名 APK。用该源码完成真实手机功能验收后再记录批准；日常包首次安装及同证书升级另行验收。

本 fork 的旧 `Publish dev Build` 已改为必须手动勾选 `publish_release` 才对外发布，避免合入 Android 工作流时顺带生成未经批准的公开桌面 Release 或 Docker 镜像；上游仓库的原有自动行为保持。

日常发包使用同一份持久 secrets 和公开指纹。密钥缺失、证书不符、包名不符、调试包输入时必须停止，不能回退随机调试证书。首次固定签名包需核对正常包名、证书指纹、非调试属性和版本号；下一版本在不卸载前版、不清数据的情况下覆盖安装，确认账号仍能读取。

## 回归验证

设备 CI 构建两个不同 APK，versionCode 为 1001 和 1002，证书必须相同且 v2 校验通过。先安装 1001，验证重启恢复，再保存虚构账号及导出文件；使用 `adb install -r` 安装 1002，由新版解密旧安全存储并读取旧导出文件。两次安装之间没有 uninstall、pm clear 或伪造恢复数据。测试会拒绝“同一个 APK 重装”或运行的版本仍是旧版。

证据包含两包哈希、证书指纹、升级前后版本、源码提交、模拟器系统信息，以及明确的 `physicalDevice=false` / `realGameLogin=false`。这证明受测 Android 模拟器上的更新流程，不是长期 secrets 已配置或真实手机已验收的证明。

## 依据

- Android 应用签名与更新：https://developer.android.com/studio/publish/app-signing
- Android 版本代码：https://developer.android.com/studio/publish/versioning
- GitHub CLI 加密并设置 secret：https://cli.github.com/manual/gh_secret_set

来源核对日期：2026-09-12。
