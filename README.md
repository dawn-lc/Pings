# Pings

高效、轻量的实时网络监控工具：对多个目标按 ICMP / TCP / UDP / HTTP(S) 持续探测，
记录延迟、抖动、丢包率与故障率，在状态变化或持续中断时记录日志，并可通过 Webhook / Email 推送告警。

## 特性

- **多协议**：ICMP 回显、TCP 建连、UDP 回声、HTTP(S) 状态码，可在同一张表里混排
- **中英双语**：简体中文与英文完整译文，随系统语言自动选择，也可用 `--lang` / `global.language` 固定；
  各语言资源直接编入主程序集，因此翻译**不会**破坏单文件发布（不会有 `zh-CN\` 语言目录）
- **实时表格**：状态、延迟、抖动、滑窗丢包率、最新警告一屏呈现；运行在终端备用缓冲区中，
  退出后整屏还原，不污染滚动历史
- **自适应布局**：窗口变窄时自动收敛列（依次丢弃日志/延迟/丢包率/目标），变矮时优先显示异常目标，
  并在标题栏提示被隐藏的数量
- **完整的统计数据**：累计发送/接收/丢包/故障、滑窗丢包率与故障率、最小/平均/最大/p95 RTT、标准差、抖动（EWMA）
- **三类告警**：故障类别变化、两级中断升级（次数先疑似、时长再确认）、抖动链路（窗口内多次短时中断）；
  延迟波动基于 SRTT / RTTVAR 基线，带预热与连续确认
- **通知**：Webhook 与 SMTP 邮件，模板占位符统一单遍替换，按「任务 + 协议 + 端点」冷却，避免告警风暴
- **日志可控**：异步单写者写入，按体积自动轮转，保留份数可配
- **适合运维**：`--once` 单轮探测可直接接入脚本 / CI（不会提问、不会等待按键）；退出码区分参数、配置、运行期、不可达
- **零依赖**：两种单文件产物任选——Native AOT 原生可执行（最小最快），或 .NET 自包含单文件；都不需要安装 .NET 运行时
- **跨平台**：Windows / Linux / macOS 全面覆盖 x64 与 ARM64，每个平台架构都出上面两种产物

## 下载

前往 [Releases](https://github.com/dawn-lc/Pings/releases) 页面，按你的平台与架构取对应文件。命名固定为 `Pings-<rid>-<variant>[.exe]`：

| RID | 平台 | `net`（推荐） | `aot`（体积最小） |
|-----|------|---------------|-------------------|
| `win-x64` | Windows x64 | `Pings-win-x64-net.exe` | `Pings-win-x64-aot.exe` |
| `win-arm64` | Windows ARM64 | `Pings-win-arm64-net.exe` | `Pings-win-arm64-aot.exe` |
| `linux-x64` | Linux x64 | `Pings-linux-x64-net` | `Pings-linux-x64-aot` |
| `linux-arm64` | Linux ARM64 | `Pings-linux-arm64-net` | `Pings-linux-arm64-aot` |
| `osx-x64` | macOS Intel | `Pings-osx-x64-net` | `Pings-osx-x64-aot` |
| `osx-arm64` | macOS Apple Silicon | `Pings-osx-arm64-net` | `Pings-osx-arm64-aot` |

两个变体怎么选：

- `net`：.NET 自包含单文件。启动快、行为与 `dotnet` 官方运行时完全一致，兼容性最好。
- `aot`：Native AOT 原生单文件，体积最小、启动最快。Windows 与 Linux 上再叠 UPX；macOS 不压（UPX 会破坏代码签名）。

共同点：都是**单个文件**、都**无需安装 .NET 运行时**、都兼容未打全累积更新的旧版 Windows 10 / 11（构建时未启用 CET 影子栈）。

> [!NOTE]
> Linux / macOS 产物下载后需要 `chmod +x`（Release 附件保留了可执行位，但部分解压/下载工具会丢）。macOS 产物未签名，会被 Gatekeeper 拦下，本地运行可用 `xattr -d com.apple.quarantine Pings` 解除隔离；正式分发需要 codesign 加公证。

> [!NOTE]
> 中文 / 日文 / 韩文版 Windows 的默认控制台字体（简中是宋体）把 `±`、`→`、`…` 这类字符画成两格，
> 会让表格在实际终端里比程序计算的宽，出现折行与错位。Pings 启动时会实测一次，若发现是两格，
> 就把窗口字体换成 Consolas（不可用时依次尝试 Lucida Console、Courier New）；已经是一格则不改动。
> 中文显示不受影响，字形由控制台自行回退渲染。

## 快速开始

首次运行若找不到配置文件，程序会询问是否生成默认的 `config.json`。
默认配置指向 Cloudflare 的公共测试端点，开箱即可看到三种协议（ICMP / TCP / HTTP）的真实延迟：

| 任务 | 协议 | 端点 | 验证什么 |
|------|------|------|----------|
| Cloudflare DNS | icmp | `1.1.1.1` | 基础 ICMP 可达性与 RTT |
| Cloudflare 备用 DNS | icmp | `1.0.0.1` | 任意播的第二入口 |
| Cloudflare DoH | tcp | `1.1.1.1:443` | TCP 建连（DoH 端口） |
| Cloudflare DoT | tcp | `1.1.1.1:853` | TCP 建连（DoT 端口） |
| Cloudflare 连通性 | http | `https://cp.cloudflare.com/generate_204` | 端到端 HTTP，期望 204 |
| Cloudflare 诊断页 | http | `https://one.one.one.one/help/` | 官方「Verify connection」页，期望 200 |
| Cloudflare Trace | http | `https://1.1.1.1/cdn-cgi/trace` | `GET` 请求 + 出口线路信息 |

几点说明：

- 这些地址分别对应官方文档的 [IP addresses](https://developers.cloudflare.com/1.1.1.1/ip-addresses/)、
  [DNS over TLS](https://developers.cloudflare.com/1.1.1.1/encryption/dns-over-tls/)（853）、
  [Verify connection](https://developers.cloudflare.com/1.1.1.1/check/)；
  `cp.cloudflare.com/generate_204` 是 Android / ChromeOS 通用的 captive portal 检测端点，
  实测 HEAD / GET、HTTP / HTTPS 都固定返回 204。
- `one.one.one.one/help` **必须带末尾斜杠**，不带会返回 308 重定向。
- `1.1.1.1/cdn-cgi/trace` 不实现 HEAD（返回 302），所以显式配置了 `httpMethod: "GET"`。
- Cloudflare 对 ICMP 的处理在慢路径上，到同一地址的 ICMP 延迟通常明显高于 TCP 建连。判断线路质量时，TCP / HTTP 的数值比 ICMP 更有参考价值。
- 默认配置**不含 UDP 任务**：Cloudflare 没有公开的 UDP 回声服务。实测
  `time.cloudflare.com:123`（[官方 NTP](https://developers.cloudflare.com/time-services/ntp/)）
  与 `1.1.1.1:53` 对探测包都不会回应——它们是协议服务端，不是回声服务。

下面是一份自行扩展的示例（把默认任务换成你自己的目标）：

```jsonc
{
  "tasks": [
    {
      "name": "网关",
      "protocol": "icmp",
      "target": "192.168.1.1",
      "timeout": 2000,
      "interval": 2000,
      "maxRecentPackets": 150,
      "significantDelayThreshold": 25,
      "packetLossDuration": 30,
      "packetLossCount": 5
    },
    {
      "name": "Cloudflare DNS",
      "target": "1.1.1.1"
    },
    {
      "name": "数据库",
      "protocol": "tcp",
      "target": "10.0.0.5",
      "port": 5432
    },
    {
      "name": "自建回声",
      "protocol": "udp",
      "target": "10.0.0.53",
      "port": 5353,
      "udpPayload": "Pings"
    },
    {
      "name": "健康检查",
      "protocol": "http",
      "target": "https://api.example.com/health",
      "httpMethod": "HEAD",
      "expectedStatus": [200, 204]
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
      "content": "{\"name\":\"%#Name#%\",\"endpoint\":\"%#Endpoint#%\",\"state\":\"%#State#%\",\"delay\":%#Delay#%,\"time\":\"%#CurrentTime#%\"}"
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
| `protocol` | `icmp` | `icmp` / `tcp` / `udp` / `http`，大小写不敏感 |
| `target` | 必填 | icmp/tcp/udp 填主机名或 IP；http 填完整 URL |
| `port` | `0` | tcp / udp 必填（1~65535）；其他协议不使用 |
| `timeout` | `1000` | 单次探测超时（毫秒） |
| `interval` | `0` | 两次探测间隔（毫秒）；`0` 表示与 `timeout` 相同 |
| `maxRecentPackets` | `255` | 统计滑窗样本数（同时影响丢包率、故障率与 RTT 统计） |
| `significantDelayThreshold` | `20` | 延迟波动的绝对阈值（毫秒），实际阈值取 `max(该值, 4 × RTTVAR)`；`0` 关闭 |
| `packetLossDuration` | `30` | 连续无响应持续时长阈值（秒）；`0` 表示不按时长判定 |
| `packetLossCount` | `5` | 连续无响应次数阈值；`0` 表示不按次数判定 |
| `lossFlapWindowSeconds` | `300` | 短故障统计窗口（秒）；`0` 关闭抖动链路检测 |
| `lossFlapCount` | `3` | 窗口内短故障达到多少次判定为抖动链路 |
| `payloadSize` | `32` | ICMP 负载字节数 |
| `udpPayload` | `Pings` | UDP 探测负载，支持纯文本 / `base64:` / `file:`，见下节 |
| `httpMethod` | `HEAD` | 仅 `HEAD` / `GET`；HEAD 返回 405 时自动改用 GET |
| `expectedStatus` | `[]` | HTTP 期望状态码；空表示 200~399 |
| `followRedirects` | `false` | HTTP 是否跟随 3xx |
| `reuseConnection` | `false` | HTTP 是否复用连接池；默认每轮新建连接，RTT 才与 ping 可比 |
| `useProxy` | `false` | HTTP 是否使用系统代理 |

> 中断告警规则：两个阈值都配置时是**两级升级**——先按次数给出「疑似」告警（Warning），
> 再按持续时长升级为「确认」告警（Critical）；只配置一个时按该阈值直接确认；
> 都为 `0` 时不触发中断告警（启动时会给出警告）。

### 统计口径

- **丢包率**只统计「完全没有回应」（ICMP / TCP / HTTP 建连超时、UDP 未收到回声）。
- **故障率**统计所有非成功，包含端口拒绝、DNS 失败、TLS 失败、HTTP 状态码异常。
  服务返回 503 说明主机可达，不该算进丢包率——这是两套口径分开的原因。
- RTT 极值 / 均值 / p95 / 标准差只统计窗口内的成功样本；窗口超过 4096 个成功样本时 p95 为等步长抽样的近似值。
- **抖动**为 RFC 3550 风格的 EWMA（增益 1/16），只在连续成功样本之间更新，跨丢包重新起算。

### UDP 的语义边界

UDP 没有握手，**没有回声服务时无法区分「主机不可达」和「服务不响应」**：

- 只有对端真的回包才算成功，此时的 RTT 才是端到端往返；
- 收到 ICMP 端口不可达（connected UDP 会以 `ConnectionReset` / `ConnectionRefused` 暴露）判定为
  「端口关闭」，表示主机可达、服务没在监听；
- 其余情况没有回声只能算「无回应」。需要判定连通性时请改用 TCP 端口探测。

因此 UDP 任务必须指向**会原样回包的自己人**（例如自建 echo 服务）。公共的 NTP、DNS、QUIC
服务端虽然监听 UDP，但只按各自协议回应合法请求——用文本 `udpPayload` 打过去只会得到「无回应」，
这一点已对 Cloudflare 的 `time.cloudflare.com:123` 与 `1.1.1.1:53` 实测确认。

### UDP 负载的三种写法

`udpPayload` 最终会被解析成要发送的字节，按下列顺序判定：

| 写法 | 行为 | 适用场景 |
|------|------|----------|
| `base64:AAECAwT/` | Base64 解码成二进制 | 固定字节序列（私有协议、心跳包），不必依赖外部文件 |
| `file:payload.bin` | 读取文件原始字节 | 二进制报文、需要外部维护或动态替换的负载 |
| `Pings` | 按 UTF-8 文本发送 | 普通文本探测 |

补充规则：

- 不加前缀时，**如果该值指向一个存在的文件**就按文件读，否则当文本。
  也就是说 `payload.bin` 与 `file:payload.bin` 等价，方便直接写路径；
  必须发送这类字面文本时请加 `base64:` 或改个写法。
- 相对路径一律相对**配置文件所在目录**，与 `logging.logFilePath` 一致，不受启动时工作目录影响。
- 负载上限 `65507` 字节（UDP 数据报上限）；文件不存在、Base64 非法、解码为空、超长都会在**启动期校验**报错并阻止运行。
- 负载在启动时读入内存，运行期间不再重读文件——改负载需要重启程序。

```jsonc
{
  "name": "私有协议心跳",
  "protocol": "udp",
  "target": "10.0.0.7",
  "port": 9000,
  "udpPayload": "base64:AAECAwT/"
}
```

### HTTP 的计时口径

默认 `reuseConnection: false` 时，每一轮都重新走 DNS + TCP + TLS，得到的 RTT 与 ICMP / TCP 探测可比。
开启复用后测的是应用层往返，适合观察服务处理时间，两种口径不能横向比较。
默认不跟随重定向（3xx 计为成功但保留状态码），默认不使用系统代理（否则量到的是代理的 RTT）。

### 全局 / 日志字段

| 字段 | 默认值 | 说明 |
|------|--------|------|
| `global.uiRefreshInterval` | `1000` | 表格刷新间隔（毫秒，200~60000） |
| `global.autoConfirmWarningInterval` | `0` | 每隔多少秒自动确认当前所有警告；`0` 关闭 |
| `global.notificationCooldownSeconds` | `60` | 同一目标的通知冷却时间；`0` 不限 |
| `global.notifyOnRecovery` | `true` | 目标恢复连通时是否也发通知 |
| `global.enableVerboseLogging` | `false` | 输出更多统计日志 |
| `global.language` | `"auto"` | 界面语言：`auto`（跟随系统）/ `en` / `zh-CN`；`--lang` 优先于本项 |
| `logging.logFilePath` | `Pings.log` | 日志文件路径（相对路径基于配置文件所在目录） |
| `logging.maxLogFileSize` | `10` | 单个日志文件上限（MB） |
| `logging.maxLogFiles` | `5` | 保留的历史日志份数（`Pings.1.log` …） |
| `logging.enableConsoleLog` | `true` | 同时输出到控制台（实时表格运行时会自动抑制，避免破坏界面） |

### 通知模板变量

Webhook 的 `content` 与 `headers` 中可使用 `%#变量#%`，时间类变量支持 `%#CurrentTime:yyyy/MM/dd#%` 格式：

`Name`、`Target`、`Endpoint`（含协议与端口）、`Protocol`、`State`（当前界面语言下的状态文案）、`Status`（状态枚举名）、
`Category`、`Severity`、`Delay`、`Connect`、`Jitter`、`LossRate`（百分比数值，如 `50.0`）、`TotalLossRate`、
`FailureRate`、`TotalFailureRate`、`Sent`、`Received`、`Lost`、`Failed`、`LastEvent`、`Warning`、`CurrentTime`。

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

# 指定界面语言（默认跟随系统，也可写进配置 global.language）
Pings.exe --lang en
Pings.exe --lang zh-CN

# 查看帮助 / 版本
Pings.exe --help
Pings.exe --version
```

### 界面语言

内置简体中文与英文两套完整译文，默认跟随系统语言（`auto`）；系统语言不在支持范围内时回退到英文。

| 取值 | 说明 |
|------|------|
| `auto` | 跟随系统（默认） |
| `en` | 英文 |
| `zh-CN` | 简体中文。`zh`、`zh-Hans`、`zh-CHS`、`zh-SG`、`zh-TW`、`zh-Hant`、`zh-HK` 等变体都归到这里 |

命令行 `--lang` 优先于配置文件的 `global.language`。

### 参数

| 参数 | 说明 |
|------|------|
| `-c`, `--config <路径>` | 指定配置文件（也可直接用位置参数） |
| `--once` | 每个目标只探测一次，输出静态表格后退出；隐含 `--no-pause`，不会提问也不会等待按键 |
| `--lang <语言>` | 界面语言：`auto` / `en` / `zh-CN`（默认跟随配置，覆盖 `global.language`） |
| `--no-color` | 关闭彩色输出（ANSI 与备用缓冲区不受影响） |
| `--no-pause` | 出错或缺少配置时一律不等待输入 |
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

> [!IMPORTANT]
> 任务字段 `ip` 已更名为 `target`，并新增 `protocol` 等字段。旧版 JSON 配置需要手工改字段名；
> 旧版 `config.txt` 的自动转换不受影响。

1.0.17 起使用 JSON 配置。若只存在旧版 `config.txt`，程序会自动转换：

```text
<名称> <IP或域名> <超时> <丢包率统计范围> <延迟波动阈值> [丢包时长] [丢包次数]
```

旧文件为 GBK 编码（简中 Windows 记事本另存的默认结果）时也能正确读取，
转换结果写入同名 `config.json`，原文件不会被删除。

## 从源码构建

需要 **.NET 10 SDK**。先跑一遍常规构建与测试，确认工具链就绪：

```bash
dotnet build Pings.sln -c Release
dotnet test tests/Pings.Tests/Pings.Tests.csproj -c Release
```

不带 `-r` 时两个目标框架都参与构建：`net10.0`（跨平台）与 `net10.0-windows`（额外启用 Windows 控制台字体/代码页互操作）。它们是**同一份源码编两遍**，不是两份产品。

新增源文件由默认通配自动纳入，不需要改 csproj；只有新增**配置模型类型**时必须同步登记到 `Configuration/JsonContext.cs`（AOT 下没有反射兜底）。

### 发布产物

每种平台架构都出**两个变体**，平台/架构只用 `-r` 一个参数表达，变体只用 `-p:PublishVariant` 一个参数表达。仓库里没有任何按平台区分的发布配置文件，也不打算有。

| 变体 | 形态 |
|------|------|
| `net` | .NET 自包含单文件（自带运行时、内部压缩），免安装，启动快 |
| `aot` | Native AOT 原生单文件，体积最小、启动最快；Windows 与 Linux 上再叠 UPX |

**发当前环境**（当前操作系统 + 当前架构）是零参数：不写 `-r` 时项目自动取 SDK 的 `NETCoreSdkRuntimeIdentifier`。

```bash
dotnet publish src/Pings/Pings.csproj -c Release -p:PublishVariant=net -o dist/net
dotnet publish src/Pings/Pings.csproj -c Release -p:PublishVariant=aot -o dist/aot
```

**发别的平台架构**只是多一个 `-r`，命令其余部分一模一样：

```powershell
# Windows x64 / ARM64
dotnet publish src\Pings\Pings.csproj -c Release -r win-x64   -p:PublishVariant=aot -o dist\win-x64-aot
dotnet publish src\Pings\Pings.csproj -c Release -r win-arm64 -p:PublishVariant=net -o dist\win-arm64-net
```

```bash
# Linux x64 / ARM64
dotnet publish src/Pings/Pings.csproj -c Release -r linux-x64   -p:PublishVariant=aot -o dist/linux-x64-aot
dotnet publish src/Pings/Pings.csproj -c Release -r linux-arm64 -p:PublishVariant=net -o dist/linux-arm64-net

# macOS Intel / Apple Silicon
dotnet publish src/Pings/Pings.csproj -c Release -r osx-x64   -p:PublishVariant=aot -o dist/osx-x64-aot
dotnet publish src/Pings/Pings.csproj -c Release -r osx-arm64 -p:PublishVariant=net -o dist/osx-arm64-net
```

`-f` 不用写。RID 本身就编码了平台与架构，TFM 由项目据此推导（`win-*` → `net10.0-windows`，其余 → `net10.0`）。`-p:PublishVariant` 省略时按 `aot` 处理，所以 `dotnet publish -r win-x64` 直接给出最小体积产物。

其它 SDK 支持的 RID 同样可直接用，不需要改项目文件；体积随目标平台、架构与 .NET 版本变化，以实际发布结果为准。

| RID | 前置依赖 |
|-----|----------|
| `win-x64`、`win-arm64` | MSVC 链接器 + Windows SDK |
| `linux-x64`、`linux-arm64` | `clang`、`zlib1g-dev` |
| `osx-x64`、`osx-arm64` | Xcode Command Line Tools |

上表只是 `aot` 变体的要求。`net` 变体不需要原生工具链，任何平台都能交叉发布（比如在 Linux 上出 `win-x64` 的 `net` 产物）。

AOT **不能跨操作系统交叉编译**：Windows 产物必须在 Windows 上构建，Linux 产物在 Linux 上，macOS 产物在 macOS 上。SDK 有硬性检查，且各平台还需要自己的原生链接器工具链。日常开发与测试不受此限制，任何平台都能 `dotnet build` / `dotnet test`。这也是 CI 与 Release 用「每平台一个 runner」的矩阵、而不是一条命令打包全部的原因。

构建期会自检发布形态：`PublishDir` 里**只能有一个文件**，多出 `.pdb` 会直接报错并说明是哪一步没生效，成功时打印 `发布形态检查通过：…（单文件）`。

UPX 对 **Windows 与 Linux 的 `aot` 变体**生效，macOS 不压：

| 目标 | UPX | 说明 |
|------|-----|------|
| Windows PE | 是 | 格式 `win64/pe`；压缩后的产物能否启动由 CI 的 `--version` 与冒烟验证 |
| Linux ELF | 是 | 格式 `linux/amd64`；Windows 上的 `upx.exe` 也能压同一份 ELF（目标格式由文件头识别），压缩后的产物能否运行同样由 CI 的 Linux 矩阵项验证 |
| macOS Mach-O | 否 | UPX 会重写 Mach-O 映像并破坏代码签名，而 Apple Silicon 上未签名二进制会被内核直接终止（`Killed: 9`），且 UPX 无法重新签名。压完就是「更小但完全跑不起来」；官方 5.3.0 也只发布 Linux 各架构与 win32/win64 资产，没有 macOS 产物 |

UPX 二进制与宿主平台无关，目标格式由文件头自动识别，所以 Windows 上的 `upx.exe` 也能压 ELF。工具解析顺序：`-p:UpxPath` → `tools/upx/upx[.exe]` → `PATH` 里的 `upx`（仅非 Windows 宿主）→ 自动下载（仅 Windows 宿主）。Linux / macOS 上自己装一份即可（`apt install upx-ucl` / `brew install upx`），CI 则在 Linux 矩阵项里固定装 5.3.0 到 `tools/upx/upx`。

用 `-p:EnableUpx=false` 关闭、`-p:UpxVersion=x.y.z` 固定版本、`-p:UpxPath=...` 指定自备的 upx。**确认压缩是否生效看退出码，不要看体积阈值**（阈值会随 .NET / ILCompiler 版本漂移）：

```powershell
tools/upx/upx.exe -l dist/aot/Pings.exe   # 已压缩 → 退出 0；未压缩 → 退出 2（NotPackedException）
```

产物不带版本号信息时会显示 `Pings 0.0.0-Dev`。**版本号的唯一真相源是 git tag**，日常构建不要手工传 `-p:AppVersion`；只有想在本地演练一次发布包时才模拟（见「发布流程 › 推送前本地复现」）。

### 在 Linux / macOS 上运行

```bash
# 直接用本机运行时跑
dotnet run --project src/Pings/Pings.csproj -- --once
```

> [!NOTE]
> Linux 上不使用 root / `CAP_NET_RAW` 时，ICMP 会退化成调用系统的 `ping` 工具（`iputils-ping` 或 BusyBox 版均可），依然可以正常探测，只是不再支持 `payloadSize` 自定义负载。只有连 `ping` 工具都没有时才会报 `本地网络错误`。需要自定义负载时，可以授予 `CAP_NET_RAW`（例如 `sudo setcap cap_net_raw+ep` 到宿主进程），或改用 TCP / HTTP 探测。

### 本地多协议冒烟

发布前建议用一份多协议配置跑一轮 `--once`，确认产物在本机真的能探测（下面这份只用到 ICMP / TCP / HTTP，起一个本地 HTTP 服务就能全部通过）：

```jsonc
{
  "tasks": [
    { "name": "回环", "protocol": "icmp", "target": "127.0.0.1", "timeout": 2000 },
    { "name": "本地 TCP", "protocol": "tcp", "target": "127.0.0.1", "port": 8080 },
    { "name": "健康检查", "protocol": "http", "target": "http://127.0.0.1:8080/health", "expectedStatus": [200] }
  ],
  "logging": { "logFilePath": "smoke.log", "enableConsoleLog": false }
}
```

```powershell
dist\aot\Pings.exe smoke.json --once --no-color
```

`--once` 只要有一个目标非成功就返回 `4`，所以**先把被测服务起起来**再跑；
TCP / UDP 指向没有监听的端口时返回 `4` 属于正确行为（状态列会显示 `连接被拒绝` / `端口不可达`）。

UDP 探测需要目标端真的回包，本地验证可以用任意 UDP 回声服务；没有回声服务时状态是 `无回应`，
这不代表探测本身有问题。

## 测试

```bash
dotnet test tests/Pings.Tests/Pings.Tests.csproj
```

覆盖配置加载/校验/迁移、状态机告警升级、统计窗口、四个协议传输层（本机回环）、告警队列、模板替换、日志轮转、CLI 解析等。

## 发布流程

版本号唯一真相源是 git tag，源码里不存在版本常量。tag 形如 `v<主>.<次>.<修订>`，可带发布类型后缀：

| tag | 解析结果 | `--version` |
|-----|----------|-------------|
| `v1.2.3` | 版本 `1.2.3`，类型 `Release` | `Pings 1.2.3` |
| `v1.2.3-Beta` | 版本 `1.2.3`，类型 `Beta` | `Pings 1.2.3-Beta` |

版本号必须是三段纯数字。段数不对时版本比较与展示都失去意义，工作流会在 `Resolve version from tag` 这一步直接失败，而不是产出一个版本号可疑的包。没带类型后缀即视为 `Release`。

`v` 前缀可省略（`Release.yml` 的 tags 过滤同时匹配 `v[0-9]*` 与 `[0-9]*`），但不带前缀的 tag 容易与普通分支名混淆，建议始终带 `v`。

### 发一个版本

```powershell
# 正式版
git tag -a v1.2.3 -m "v1.2.3"
git push origin v1.2.3

# 预发布
git tag -a v1.2.3-Beta -m "v1.2.3-Beta"
git push origin v1.2.3-Beta
```

`-m` 的文本只进 tag 注释，不参与版本号解析——版本号来自 tag 名本身。注释里要写中文时，PowerShell 5.1 会按 ANSI 传参导致乱码，改用 `-F` 指向一个 UTF-8 无 BOM 的文件（`git tag -a v1.2.3 -F tagmsg.txt`）。

推送 tag 即触发 `.github/workflows/Release.yml`。它自己不写发布步骤，而是调用 `.github/workflows/build-matrix.yml`——**CI 与 Release 共用同一份构建定义**。下面只列有判断意义的步骤：

| 步骤 | 做什么 | 失效能拦住什么 |
|------|--------|----------------|
| `Resolve version from tag` | 剥掉 `v` 前缀，按第一个 `-` 切出「版本 / 类型」，校验三段纯数字 | 版本号格式错误 |
| `Test` | 跑全部单元测试 | 任何测试不通过 |
| `全平台发布`（复用 `build-matrix.yml`） | 12 个矩阵项（6 RID × 2 变体）：Windows 全部跑在 `windows-latest`，Linux / macOS 各用自己的 runner | 任一平台/架构构建失败 |
| `Verify artifact` | 能执行的产物让它自报 `--version` 并与 tag 推导值比对；交叉构建的（`win-arm64`）改为断言 PE 文件头是 ARM64；`aot` 变体再跑 `upx -l` 要求退出 0 | 版本元数据链路断裂、RID 没生效、UPX 静默失效 |
| `Smoke test` | 回环 ICMP 跑 `--once`，要求退出码 0；架构不匹配的矩阵项显式跳过 | 产物在目标平台跑不起来 |
| `Verify asset set` | 断言 12 个附件齐备且没有多余文件 | 某个矩阵项静默失败却照发 |
| `Create GitHub Release` | 12 个附件一次性上传，自动生成发布说明 | 产物形态错误 |

12 个附件命名固定为 `Pings-<rid>-<variant>[.exe]`，例如 `Pings-win-x64-aot.exe`、`Pings-linux-arm64-net`：

| RID | `net` | `aot` |
|-----|-------|-------|
| `win-x64` | `Pings-win-x64-net.exe` | `Pings-win-x64-aot.exe` |
| `win-arm64` | `Pings-win-arm64-net.exe` | `Pings-win-arm64-aot.exe` |
| `linux-x64` | `Pings-linux-x64-net` | `Pings-linux-x64-aot` |
| `linux-arm64` | `Pings-linux-arm64-net` | `Pings-linux-arm64-aot` |
| `osx-x64` | `Pings-osx-x64-net` | `Pings-osx-x64-aot` |
| `osx-arm64` | `Pings-osx-arm64-net` | `Pings-osx-arm64-aot` |

选哪个下载：只想跑起来不想管运行时 → `net`；要最小体积和最快启动 → `aot`（Windows 与 Linux 上叠了 UPX）。

> [!NOTE]
> Release 只由 tag 驱动，不提供手动触发的 `workflow_dispatch` 入口：手动入口只能跑默认分支的代码，而「发出去的产物必须来自被打 tag 的那个提交」是这里唯一不能妥协的事。失败后重跑同一次运行即可，ref 仍是原 tag。

「单 exe / 无 pdb」**不在工作流里断言**，而由 `Pings.csproj` 的 `VerifyPublishOutput` 与 `CopyOutputSymbolsToPublishDirectory=false` 在构建期保证——本地发布受同一约束，不存在「CI 查了、本地没查」的落差。该检查对两个变体都生效（AOT 与自包含单文件都是单文件形态）。

体积也**不设阈值**。阈值随 .NET / ILCompiler 版本漂移，误报比漏报更烦；UPX 是否生效只看 `upx -l` 的退出码，因为未压缩产物照样跑得通，光看「能跑」根本测不出来。

### 日常提交

`CI.yml` 在 push 到 `master` 与 PR 时运行，分两个作业：

**`verify`（单个 runner，与平台无关，跑一次）**

RID → TFM 推导校验（逐平台断言，保证「一条命令发任意平台」成立）→ 编码检查（源码必须 UTF-8 无 BOM，含 `*.resx`）→ restore → build（两个 TFM 全覆盖）→ test。

**`artifacts`（复用 `build-matrix.yml`，12 个矩阵项）**

6 个 RID × 2 个变体，版本号固定 `0.0.0-Dev`，每个矩阵项走一遍「（Linux 先装 UPX）→ 发布 → 产物校验（含 `upx -l`）→ 回环冒烟 → 改名 → 上传」。

Windows 只用一个 runner 标签：`windows-latest`（Windows Server 2025）自带 `VC.Tools.ARM64` 与 Windows SDK，能在 x64 上交叉构建 `win-arm64`。代价是那两项产物没法在 runner 上执行，所以它们的校验换成「PE 文件头必须是 ARM64」——这条比「文件存在」有力得多：RID 一旦没生效、错发了宿主架构的二进制，只有文件头能发现。

测试**不在矩阵里重复跑**：测试与平台无关，`verify` 跑一次就够，12 个矩阵项各跑一遍等于把同一批用例执行 12 次。能执行的矩阵项由冒烟验证「产物真能跑」，不能执行的由文件头 + UPX 验证形态。

矩阵用 `fail-fast: false`：一个平台挂掉不该掩盖其它平台的结果，先看完所有失败再统一修。

CI 与 Release 共用 `env.UPX_VERSION`（定义在 `build-matrix.yml`），否则同一个 tag 两次构建的产物不可重现，「CI 验过的」与「发出去的」也不是同一种构建。

### 推送前本地复现

tag 一推就公开，别拿发布当第一次验证。下面的命令与工作流步骤一一对应，本地全绿再打 tag：

```powershell
dotnet build Pings.sln -c Release -warnaserror                     # verify › Build
dotnet test tests\Pings.Tests\Pings.Tests.csproj -c Release         # verify › Test

# 只发当前环境：不写 -r，两个变体各一条命令
dotnet publish src\Pings\Pings.csproj -c Release `
  -p:PublishVariant=net -p:AppVersion=9.9.9 -p:AppVersionType=Release -o dist\net
dotnet publish src\Pings\Pings.csproj -c Release `
  -p:PublishVariant=aot -p:AppVersion=9.9.9 -p:AppVersionType=Release -o dist\aot

tools\upx\upx.exe -l dist\aot\Pings.exe                             # Verify：必须退出 0
dist\net\Pings.exe --version                                        # 应输出 Pings 9.9.9
dist\aot\Pings.exe --version                                        # 应输出 Pings 9.9.9
dist\aot\Pings.exe smoke.json --once --no-color                     # Smoke test
```

`smoke.json` 用上一节「本地多协议冒烟」里的那份配置；`--once` 只要有一个目标非成功就返回 `4`，所以**先把被测服务起起来**再跑。

`-warnaserror` 不在 CI 里（分析器警告只提示、不拦构建），本地加上能让新增警告不被忽略。

测试的默认界面语言被固定为 `en-US`，与 CI 的英文 runner 保持一致：要断言中文文案的用例必须显式切换文化。这条约定把「本地中文机器绿、CI 红」提前到本机暴露——v1.0.22 首次发布就是栽在这上面。

## 架构与开发说明

见 [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)。

## 许可证

见 [LICENSE.txt](LICENSE.txt)。
