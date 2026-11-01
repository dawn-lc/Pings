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
默认配置指向 Cloudflare 的公共测试端点，开箱即可看到四种协议的真实延迟：

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
- `1.1.1.1:443` 建连只有个位数毫秒，而 ICMP 到同一地址有 270ms 左右——Cloudflare 对 ICMP
  的处理在慢路径上。判断线路质量时，TCP / HTTP 的数值比 ICMP 更有参考价值。
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

`Pings.csproj` 是多目标框架（`net10.0` + `net10.0-windows`），新增源文件由默认通配自动纳入，不需要改 csproj；
只有新增**配置模型类型**时必须同步登记到 `Configuration/JsonContext.cs`（AOT 下没有反射兜底）。

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
不存在时会自动下载；UPX `--best` 后产物约 **4 MB**（未压缩约 9 MB）。用 `-p:EnableUpx=false` 关闭压缩，
`-p:UpxPath=...` 指定自备的 upx。压缩是否生效以日志里的 `UPX 压缩（…）` 为准，
也可以用 `-v:n` 重新发布确认。

### 在 Linux / macOS 上运行

```bash
# 直接跑（使用本机运行时）
dotnet run --project src/Pings/Pings.csproj -f net10.0 -- --once

# 或发布本平台的 AOT（UPX 会自动跳过）
dotnet publish src/Pings/Pings.csproj -c Release -f net10.0 -r linux-x64 -p:PublishAot=true -o dist/linux
```

> [!NOTE]
> Linux / macOS 上的 ICMP 探测依赖套接字权限：.NET 使用 ICMP datagram 套接字，
> 要求进程属于 `net.ipv4.ping_group_range` 覆盖的组，否则会报 `本地网络错误`（权限不足）。
> 遇到这种情况，可以授予 `CAP_NET_RAW`（例如 `sudo setcap cap_net_raw+ep` 到宿主进程），
> 或改用 TCP / HTTP 探测——这两种协议不需要额外权限。

### 本地多协议冒烟

发布前建议用一份多协议配置跑一轮 `--once`，确认 AOT 产物在本机真的能探测（下面这份只用到 ICMP / TCP / HTTP，起一个本地 HTTP 服务就能全部通过）：

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
