# release/

这个目录里只放**签名后的更新清单** `update.json` 和更新日志 `notes.txt`，不放 exe。

## 为什么放在仓库里

`src/BlockLauncher/Branding.cs` 的 `DefaultUpdateSources` 会从本仓库的
`release/update.json` 拉取更新清单（raw.githubusercontent.com、jsDelivr、Gitee
等多个镜像都指向同一个文件）。提交到这里，所有镜像会自动生效，**不需要另外上传一份**。

## 当前签名钥匙

| 项目 | 值 |
|---|---|
| 公钥指纹 | `8dc61dc90fa1f375` |
| 公钥位置 | `src/BlockLauncher/Branding.cs` → `TrustedKeys[0]` |
| 私钥位置 | `E:\mc启动器\keys\private-key.xml`（**仓库外，绝不提交**） |
| 算法 | RSA-3072 / SHA-256 / PKCS#1 v1.5 |

私钥丢了就再也发不了新版，只能让用户手动在 设置 → 更新 → 信任的发布者 里换钥匙。
**请把 `private-key.xml` 至少离线备份两份**（U 盘 / 密码管理器附件都行）。

### 关于上游的钥匙

这个项目源自 OpenLauncher 的开源版本（由社区开发；honghemc.cn 只是原版的收录与下载站，
不是开发方）。`TrustedKeys` 里原本还留着上游那套发行版的初始发布钥匙 `12c461c38f08483a`，
那是为了让**上游自己的**清单能通过验签。本发行版已经把它删掉了，原因有两条：

1. 本发行版的更新只从自己的 GitHub 仓库拉，用不到上游的清单；
2. 那把私钥不在我们手里，留着等于让**任何持有它的人**能给我们用户推"合法"更新。

上游是鼓励他人基于此继续开发的（见 LICENSE），所以这里只信任自己的钥匙。

## 关于程序名（改名时的注意事项）

程序名是 `Block Launcher`（**中间有空格**），生成的文件是 `Block Launcher.exe`，
数据目录也叫 `Block Launcher`。这是因为主人指定了这个写法。

由此带来三条硬约束，改脚本时别踩：

1. 所有路径都要加引号：`--exe "dist\Block Launcher.exe"`，不加会把空格当参数分隔。
2. 上传到 GitHub Releases 时，建议把文件改名成 `BlockLauncher.exe` 再传 ——
   带空格的文件名会被 GitHub 转义成 `Block.Launcher.exe`，清单里的 URL 很容易写错。
   清单 URL 必须写实际能下载到的那个文件名。
3. C# 标识符不能有空格，所以命名空间是 `BlockLauncher`（连写），和 `AppName` 不是一回事。

## 发一版（本机实测可用的命令）

```bat
REM 0) 改版本号：src\BlockLauncher\BlockLauncher.csproj 的 <Version>，
REM    它必须和 --version 完全一致，否则发布工具会拒绝签名。

REM 1) 编译启动器（产物放临时目录，不污染 src/）
dotnet build src\BlockLauncher\BlockLauncher.csproj -c Release -o "%TEMP%\ol-out"
copy "%TEMP%\ol-out\Block Launcher.exe" dist\

REM 2) 编译发布工具
dotnet build tools\Release\BlockLauncher.Release.csproj -c Release -o "%TEMP%\ol-rel"

REM 3) 签名清单
"%TEMP%\ol-rel\ol-release.exe" publish ^
  --exe "dist\Block Launcher.exe" --version 0.2.0 ^
  --key E:\mc启动器\keys\private-key.xml ^
  --pub E:\mc启动器\keys\public-key.xml ^
  --url https://github.com/xiaojiubuchiyao/BlockLauncher/releases/download/v0.2.0/BlockLauncher.exe ^
  --notes release\notes.txt ^
  --out release\update.json

REM 4) 验签（同时核对 exe 的 SHA-256）
"%TEMP%\ol-rel\ol-release.exe" verify --in release\update.json ^
  --pub E:\mc启动器\keys\public-key.xml --exe "dist\Block Launcher.exe"
```

`publish.ps1` 是把上面几步串起来的脚本，参数一样，但它自己会重新编译；
本机 PowerShell 执行策略受限时直接用上面的命令。

之后：把 `dist\Block Launcher.exe` 改名成 `BlockLauncher.exe` 再传到 GitHub Releases
（tag 与版本号一致），把 `release\update.json` 提交进仓库。
镜像（jsDelivr / Gitee / ghfast）会自动跟上。

## 清单里有什么

版本号、`app` 字段（必须是 `Block Launcher`，否则启动器会忽略这份清单）、
exe 的 SHA-256 与大小、下载地址列表、可选的镜像表 / 更新源 / 中文词表，
以及用私钥生成的签名。启动器只接受能通过 `Branding.TrustedKeys` 中公钥验签的清单，
下载完还会再比对一次 SHA-256。

更新日志写在 `release/notes.txt`（每次发版改写它），由 `--notes` 带进清单，
设置 → 更新 里点「检查更新」时会显示出来。

## 自更新与文件名

自更新是**原地替换**：下载到 `<当前 exe 路径>.new`，再把原 exe 改名成 `.old`、新的顶上去。
所以用户机器上 exe 叫什么名字不会因为更新而改变 —— 从旧名字 `OpenLauncher.exe`
升上来的用户，更新后文件仍然叫 `OpenLauncher.exe`，内容是新的。
这不影响任何功能（程序内部一律用 `Paths.ExePath`，不靠文件名判断自己是谁）。

## 安全

- **`private-key.xml` 永远不要提交**（`.gitignore` 里已有 `private-key.xml`）。
- 清单被篡改不影响安全：验签失败会被跳过，用户的启动器不会执行任何内容。
- 换钥匙有正规流程：新清单里用 `--add-key` 带上新公钥、用 `--revoke <指纹>` 吊销旧钥匙，
  老版本拿到后会自我升级信任链。前提是**你手里有旧私钥**；没有就只能让用户手动信任。
