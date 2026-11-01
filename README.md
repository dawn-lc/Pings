# Pings

高效、轻量的实时网络监控工具：对多个目标持续发送 ICMP 探测，记录延迟、抖动与丢包率，
在状态变化或持续丢包时记录日志，并可通过 Webhook / Email 推送告警。

## 特性

- **实时表格**：状态、延迟、抖动、滑窗丢包率、最新警告一屏呈现；运行在终端备用缓冲区中，
  退出后整屏还原，不污染滚动历史
- **自适应布局**：窗口变窄时自动收敛列（依次丢弃日志/延迟/丢包率/目标），变矮时优先显示异常目标，
  并在标题栏提示被隐藏的数量
- **完整的统计数据**：累计发送/接收、滑窗丢包率、累计丢包率、最小/平均/最大 RTT、抖动（EWMA）
- **两类告警**：故障分类变化（超时 / 不可达 / TTL 过期 / 策略拒绝 …）与持续丢包（时长 + 次数双阈值）
- **通知**：Webhook 与 SMTP 邮件，模板占位符统一单遍替换，支持按目标设置冷却时间，避免告警风暴
- **日志可控**：异步单写者写入，按体积自动轮转，保留份数可配
- **适合运维**：`--once` 单轮探测可直接接入脚本 / CI；退出码区分参数、配置、运行期、不可达
- **零依赖**：Native AOT 单文件可执行，无需安装 .NET 运行时
- **跨平台**：Windows 目标带控制台字体/代码页优化；`net10.0` 目标可在 Linux / macOS 运行

## 下载

前往 [Releases](https://github.com/dawn-lc/Pings/releases) 页面下载单个 `Pings.exe`：

- Native AOT 编译，**无需安装 .NET 运行时**
- 已用 UPX 压缩（约 4 MB）
- 兼容未打全累积更新的旧版 Windows 10 / 11（构建时未启用 CET 影子栈）

> [!NOTE]
> 中文 / 日文 / 韩文版 Windows 的默认控制台字体（简中是宋体）把 `±`、`→`、`…` 这类字符画成两格，
> 会让表格在实际终端里比程序计算的宽，出现折行与错位。Pings 启动时会实测一次，若发现是两格，
> 就把窗口字体换成 Consolas（不可用时依次尝试 Lucida Console、Courier New）；已经是一格则不改动。
> 中文显示不受影响，字形由控制台自行回退渲染。

## 快速开始

首次运行若找不到配置文件，程序会询问是否生成默认的 `config.json`。
默认监控 `8.8.8.8` 与 `1.1.1.1`。

```jsonc
{
  "tasks": [
    {
      "name": "网关",
      "ip": "192.168.1.1",
      "timeout": 2000,
      "interval": 1000,
      "maxRecentPackets": 150,
      "significantDelayThreshold": 25,
      "packetLossDuration": 30,
      "packetLossCount": 5
    },
    {
      "name": "Cloudflare DNS",
      "ip": "1.1.1.1"
    }
  ],
  "global": {
    "uiRefreshInterval": 1000,
    "autoConfirmWarningInterval": 0,
    "notificationCooldownSeconds": 60,
    "notifyOnRecovery": true,
    "enableVerboseLogging": false
  },
  "logging": {
    "logFilePath": "Pings.log",
    "maxLogFileSize": 10,
    "maxLogFiles": 5,
    "enableConsoleLog": true
  },
  "notifications": {
    "webhook": {
      "enabled": false,
      "url": "https://example.com/hook",
      "method": "POST",
      "headers": {},
      "authType": "Bearer",
      "authToken": "",
      "content": "{\"name\":\"%#Name#%\",\"ip\":\"%#IP#%\",\"state\":\"%#State#%\",\"delay\":%#Delay#%,\"time\":\"%#CurrentTime#%\"}"
    },
    "email": {
      "enabled": false,
      "smtpServer": "smtp.example.com",
      "port": 587,
      "enableSsl": true,
      "username": "",
      "password": "",
      "from": "pings@example.com",
      "to": ["ops@example.com"]
    }
  }
}
```

### 任务字段

| 字段 | 默认值 | 说明 |
|------|--------|------|
| `name` | 必填 | 任务名称（界面显示） |
| `ip` | 必填 | 目标 IP 或域名 |
| `timeout` | `1000` | 单次 Ping 超时（毫秒） |
| `interval` | `0` | 两次探测间隔（毫秒）；`0` 表示与 `timeout` 相同 |
| `maxRecentPackets` | `255` | 统计滑窗样本数（同时影响丢包率与 RTT 统计） |
| `significantDelayThreshold` | `20` | 延迟较上次成功探测上升超过该值（毫秒）记为「延迟波动」；`0` 关闭 |
| `packetLossDuration` | `30` | 连续丢包持续时长阈值（秒）；`0` 表示不按时长判定 |
| `packetLossCount` | `5` | 连续丢包次数阈值；`0` 表示不按次数判定 |

> 丢包告警规则：两个阈值都配置时 **需同时满足**；只配置一个时按配置的那个判定；
> 都为 `0` 时不触发丢包告警（启动时会给出警告）。

### 全局 / 日志字段

| 字段 | 默认值 | 说明 |
|------|--------|------|
| `global.uiRefreshInterval` | `1000` | 表格刷新间隔（毫秒，200~60000） |
| `global.autoConfirmWarningInterval` | `0` | 每隔多少秒自动确认当前所有警告；`0` 关闭 |
| `global.notificationCooldownSeconds` | `60` | 同一目标的通知冷却时间；`0` 不限 |
| `global.notifyOnRecovery` | `true` | 目标恢复连通时是否也发通知 |
| `global.enableVerboseLogging` | `false` | 输出更多统计日志 |
| `logging.logFilePath` | `Pings.log` | 日志文件路径（相对路径基于配置文件所在目录） |
| `logging.maxLogFileSize` | `10` | 单个日志文件上限（MB） |
| `logging.maxLogFiles` | `5` | 保留的历史日志份数（`Pings.1.log` …） |
| `logging.enableConsoleLog` | `true` | 同时输出到控制台（实时表格运行时会自动抑制，避免破坏界面） |

### 通知模板变量

Webhook 的 `content` 与 `headers` 中可使用 `%#变量#%`，时间类变量支持 `%#CurrentTime:yyyy/MM/dd#%` 格式：

`Name`、`IP`、`State`（中文状态）、`Status`（`IPStatus` 名称）、`Category`、`Delay`、`Jitter`、
`LossRate`（百分比数值，如 `50.0`）、`TotalLossRate`、`Sent`、`Received`、`LastEvent`、`Warning`、`CurrentTime`。

未识别的占位符会原样保留，便于排查模板笔误。

## 使用方法

```bash
# 使用当前目录（或程序目录）的 config.json
Pings.exe

# 指定配置文件
Pings.exe ./configs/office.json
Pings.exe --config ./configs/office.json

# 只探测一轮并输出表格，适合脚本 / CI
Pings.exe --once

# 查看帮助 / 版本
Pings.exe --help
Pings.exe --version
```

### 参数

| 参数 | 说明 |
|------|------|
| `-c`, `--config <路径>` | 指定配置文件（也可直接用位置参数） |
| `--once` | 每个目标只探测一次，输出静态表格后退出 |
| `--no-color` | 关闭彩色输出（ANSI 与备用缓冲区不受影响） |
| `--no-pause` | 出错时不再等待按键 |
| `--verbose` | 出错时输出完整堆栈 |
| `-h`, `--help` | 帮助 |
| `-v`, `--version` | 版本号 |

### 快捷键

| 按键 | 作用 |
|------|------|
| `C` | 确认（消费）当前警告 |
| `Q` | 退出 |

### 退出码

| 退出码 | 含义 |
|--------|------|
| `0` | 正常结束 |
| `1` | 命令行参数错误 |
| `2` | 配置文件缺失或校验失败 |
| `3` | 运行期异常 |
| `4` | `--once` 模式下存在不可达目标 |

## 从旧版升级

1.0.17 起使用 JSON 配置。若只存在旧版 `config.txt`，程序会自动转换：

```text
<名称> <IP或域名> <超时> <丢包率统计范围> <延迟波动阈值> [丢包时长] [丢包次数]
```

旧文件为 GBK 编码（简中 Windows 记事本另存的默认结果）时也能正确读取，
转换结果写入同名 `config.json`，原文件不会被删除。

## 从源码构建

需要 **.NET 10 SDK**。

### Windows Native AOT（发布形态）

**必须在 Windows 上构建**：AOT 不能跨操作系统交叉编译（SDK 硬性检查，且非 Windows
环境缺少 MSVC 链接器与 Windows SDK 导入库）。

```powershell
dotnet publish src\Pings\Pings.csproj -c Release -f net10.0-windows `
  -p:PublishProfile=src\Pings\Properties\PublishProfiles\win-x64-aot.pubxml `
  -p:AppVersion=1.0.22 -p:AppVersionType=Release -o dist\aot
```

`-f` 不能省：本项目是多目标框架，pubxml 里的 `TargetFramework` 在跨目标求值阶段不生效。

UPX 压缩、产物形态自检（必须只有一个文件）都在构建里完成，`tools\upx\upx.exe`
不存在时会自动下载；产物约 **4 MB**。用 `-p:EnableUpx=false` 关闭压缩，
`-p:UpxPath=...` 指定自备的 upx。

### 在 Linux / macOS 上运行

```bash
# 直接跑（使用本机运行时）
dotnet run --project src/Pings/Pings.csproj -f net10.0 -- --once

# 或发布本平台的 AOT（UPX 会自动跳过）
dotnet publish src/Pings/Pings.csproj -c Release -f net10.0 -r linux-x64 -p:PublishAot=true -o dist/linux
```

## 测试

```bash
dotnet test tests/Pings.Tests/Pings.Tests.csproj
```

覆盖配置加载/校验/迁移、统计窗口、告警队列、模板替换、日志轮转、CLI 解析等纯逻辑部分。

## 发布流程

版本号唯一真相源是 git tag，源码里不存在版本常量。tag 支持带发布类型：

```bash
# 正式版
git tag -a v1.0.22 -m "v1.0.22"
git push origin v1.0.22

# 预发布（--version 显示 Pings 1.1.0-Beta，与正式包可区分）
git tag -a v1.1.0-Beta -m "v1.1.0-Beta"
git push origin v1.1.0-Beta
```

推送 tag 后 `.github/workflows/Release.yml` 会解析 tag → 跑测试 → AOT 发布（注入
`AppVersion` / `AppVersionType`）→ 断言单 exe / 无 pdb / 体积 / 版本号 → 回环冒烟 → 创建 Release。

日常提交由 `CI.yml` 做构建、测试与 AOT 冒烟验证（本地开发构建的版本号固定为 `0.0.0-Dev`）。

## 架构与开发说明

见 [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)。

## 许可证

见 [LICENSE.txt](LICENSE.txt)。
