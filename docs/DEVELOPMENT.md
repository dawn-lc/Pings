# 开发文档

面向维护者的技术说明。使用方式见 [README](../README.md)。

## 目录结构

```text
Pings/
├─ src/Pings/
│  ├─ Program.cs                入口：参数 → 配置 → 装配服务 → 运行模式
│  ├─ Cli/
│  │  └─ CliOptions.cs          参数解析、用法文本、退出码约定
│  ├─ Configuration/
│  │  ├─ AppConfig.cs           JSON 模型（任务 / 全局 / 日志 / 通知）
│  │  ├─ JsonContext.cs         源生成 JSON 上下文（AOT 必需）
│  │  ├─ ConfigLoader.cs        加载、原子保存、默认配置
│  │  ├─ ConfigValidator.cs     启动期区间校验（错误 / 警告两级）
│  │  └─ LegacyConfigConverter.cs   旧 config.txt 迁移（含 GBK 编码探测）
│  ├─ Monitoring/
│  │  ├─ ProbeProtocol.cs       协议枚举与配置名解析
│  │  ├─ ProbeStatus.cs         结果状态 / 故障类别 / 严重程度 / 派生事实
│  │  ├─ ProbeResult.cs         单次探测结果
│  │  ├─ ProbeTaskOptions.cs    运行期不可变任务参数（含协议专属字段）
│  │  ├─ ProbeTask.cs           探测循环：统计 + 状态机 + 事件
│  │  ├─ ProbeStateMachine.cs   告警判定（纯逻辑，注入 TimeProvider）
│  │  ├─ ProbeStatistics.cs     滑窗统计（丢包率 / 故障率 / RTT 分布 / 抖动）
│  │  ├─ ProbeTaskSnapshot.cs   只读快照（界面唯一输入）
│  │  ├─ UdpPayloadResolver.cs  udpPayload 解析（文本 / base64: / file:）
│  │  ├─ WarningQueue.cs        有界线程安全告警队列（相邻去重）
│  │  ├─ ProbeMonitor.cs        编排：日志 / 告警 / 通知，不接触 UI
│  │  └─ Transports/
│  │     ├─ IProbeTransport.cs      探测 I/O 抽象
│  │     ├─ ProbeTransportFactory.cs
│  │     ├─ IcmpProbeTransport.cs
│  │     ├─ TcpProbeTransport.cs
│  │     ├─ UdpProbeTransport.cs
│  │     ├─ HttpProbeTransport.cs
│  │     └─ SocketErrorMapping.cs   SocketError → ProbeStatus / 中文描述
│  ├─ Notifications/
│  │  ├─ NotificationService.cs Webhook + SMTP（尽力而为，失败只记日志）
│  │  └─ TemplateRenderer.cs    单遍模板渲染
│  ├─ Diagnostics/
│  │  ├─ AppInfo.cs             版本 / 仓库信息（读程序集元数据）
│  │  ├─ Logging.cs             异步单写者日志
│  │  └─ RotatingLogFile.cs     按体积轮转
│  ├─ Ui/
│  │  ├─ MonitorView.cs         表格渲染（Live / 一次性，纯 Spectre）
│  │  └─ MonitorLayout.cs       列/行自适应决策（纯函数，便于测试）
│  ├─ Interop/
│  │  └─ WindowsConsole.cs      Windows 控制台 P/Invoke（仅 WINDOWS 目标编译）
│  └─ Properties/PublishProfiles/   Native AOT 发布配置
├─ tests/Pings.Tests/           xUnit 测试（不参与发布）
├─ build/
│  └─ BuildTasks.cs             构建期 MSBuild 内联任务（自动获取 UPX）
└─ docs/
```

`build/` 与 `docs/` 留在仓库根：前者是构建基础设施（RoslynCodeTaskFactory 直接读文件，不在任何项目的编译范围内），后者不随程序发布。主项目位于 `src/Pings/` 后，不再需要用 `DefaultItemExcludes` 手动排除 `Pings.Tests/**` 与 `build/**`。

依赖方向：`Program` → 各模块；`Monitoring` 与 `Notifications` 之间只通过
`ProbeTaskSnapshot` 传递数据；`Ui` 只依赖 `Monitoring` 的快照。任何模块都不反向依赖 `Program`。

协议差异全部收敛在 `Monitoring/Transports/`：`ProbeTask` 只看得到 `ProbeResult`，
`IPStatus` 不会渗出 ICMP 实现。

## 线程模型

这是本项目最需要守住的不变量：

| 角色 | 线程 | 职责 |
|------|------|------|
| 探测循环 | 每任务一个 Task | 按协议探测、更新统计与状态机、触发事件 |
| 事件处理 | 探测循环线程 | 写日志、入告警队列、派发通知（不碰 UI） |
| 渲染循环 | 前台 `MonitorView` | 每个刷新周期读取快照，重建整张表 |
| 按键处理 | 前台循环 | 读取 C/Q，调用监控器的确认方法 |
| 日志写入 | 单个后台 Task | 从通道取行写文件并轮转 |
| 通知发送 | 独立 Task（受控） | HTTP / SMTP，失败只记日志 |

由此得到两条硬规则：

1. **界面只读快照**。`MonitorView.BuildTable()` 只调用 `ProbeMonitor.Snapshots()`，
   不订阅任务事件、不共享 `Table` 实例。旧实现从后台线程直接改 Spectre 表格，
   与渲染线程并发写入，属于数据竞争（表现为乱码、错行或偶发异常）。
2. **事件只描述「发生了什么」**。事件里带的是不可变快照，订阅方不需要回读任务字段。

## 探测与统计语义

- 单次失败（超时、不可达、端口关闭、DNS / TLS 失败、状态码异常）都计入累计 `Sent`，
  但只有**完全没有回应**（`Timeout` / `NoReply`）才算丢包，其余计入故障率。
- `maxRecentPackets` 决定滑窗长度；滑窗满后覆盖最旧样本，用于界面丢包率与故障率。
- 累计丢包率使用全部样本（`Lost / Sent`），不会因窗口滚动而回落。
- 窗口统计在 `Snapshot()` 时单遍扫描完成，不做增量聚合——窗口回绕时扣减聚合量容易漂移，
  而单遍扫描的开销（窗口上限 10 万、刷新频率 1Hz）完全可以接受。
- RTT 极值 / 均值 / p95 / 标准差只统计窗口内成功样本；
  成功样本超过 4096 时 p95 按固定步长抽样，是近似值。
- 抖动使用 RFC 3550 风格的 EWMA（增益 1/16），只在连续成功样本间更新，跨丢包重新起算。
- `LastRttMs` 跨丢包保留（最近一次成功 RTT）；快照里的 `DelayMs` 只表示「本次是否成功」。

## 告警语义

- 状态事件只在**故障类别**变化时产生。同一类别内的 `Timeout ↔ NoReply` 只更新快照与日志，
  不重复入队、不重复通知。
- 中断升级（`ProbeStateMachine.EscalateOutage`）：
  - 同时配置次数与时长 → 达到次数先发 Warning（疑似），达到时长再发 Critical（确认）；
  - 只配置一个阈值 → 达到即 Critical；
  - 两个都为 0 → 不告警（配置校验会给出警告）；
  - 每个 episode 每个级别只触发一次，成功回应后重置。
- 已确认的长故障**不计入**短故障；未确认就恢复的 episode 计入短故障窗口，
  窗口内达到 `lossFlapCount` 判定为抖动链路（同一窗口只告警一次）。
- 延迟波动用 RFC 6298 的 SRTT / RTTVAR 双 EWMA：
  - 预热 5 个成功样本后才开始判定；
  - 阈值 `max(significantDelayThreshold, 4 × RTTVAR)`，连续 2 个超限样本才确认；
  - 回落到半个阈值以内才解除（施密特回差）；
  - **超限样本不参与基线更新**：否则一次尖峰会把 RTTVAR 撑大，紧随其后的同样高的样本
    立刻变得「正常」，连续确认永远凑不满；
  - 恢复当次不判波动（基线已过期，报出来的「升高」没有意义）。
- 时长全部用 `TimeProvider.GetTimestamp()` 单调时钟计算，系统校时 / 夏令时不会让 episode 时长跳变。
- `LastEvent` 按严重程度保留：Critical > Warning > Info，低优先级不得覆盖高优先级。
- 告警队列相邻重复消息合并为一条并计数（`请求超时 11:02:15 ×3`），容量溢出时累计 `Dropped`，
  表格 caption 会提示被丢弃的告警数。

## 配置校验

`ConfigValidator` 把所有「能提前发现」的问题放在启动时，并按协议分支校验：

- 错误（阻止启动）：缺少任务、未知 protocol、目标非法、tcp/udp 缺少端口、
  http 的 target 不是绝对 http/https 地址、httpMethod 不在白名单、expectedStatus 越界、
  udpPayload 解析失败（文件不存在 / Base64 非法 / 解码为空 / 超过 65507 字节）、
  数值越界、启用通知但必填项缺失等；
- 警告（继续运行）：名称重复、同一端点重复（同主机不同协议/端口不算重复）、
  icmp 配置了 port、`interval < timeout`、丢包双阈值都为 0 等。

### udpPayload 的解析

`UdpPayloadResolver` 是唯一真相源，校验与运行期共用它，所以不会出现
「启动时说可以、跑起来却不行」。判定顺序：

1. `base64:<数据>` → 解码成二进制；
2. `file:<路径>` → 读文件原始字节；
3. 其他值 → **若该值指向存在的文件**则按文件读，否则按 UTF-8 文本。

第 3 条是为了让用户直接把路径写进 `udpPayload`；代价是「同名文件恰好存在」时会被当成路径，
需要发送这种字面文本时加 `base64:` 即可。

相对路径以 **配置文件所在目录** 为基准（`Program` 把该目录同时传给
`ConfigValidator.Validate` 与 `ProbeTaskOptions.FromJson`），与 `logging.logFilePath` 保持一致。
负载在启动时一次性读入 `ProbeTaskOptions.UdpPayloadBytes`，运行期不再重读文件。

`ConfigLoader.SaveConfig` 采用「写临时文件 → 原子替换」，避免写一半的文件被当成有效配置。

## 日志

- 相对路径的 `logging.logFilePath` 会在启动时拼接配置文件所在目录，
  避免双击启动（工作目录变化）把日志写到意想不到的位置。
- 所有调用方只做 `Channel.TryWrite`，不阻塞探测线程。
- 单一后台任务落盘，避免多个线程同时写 `StreamWriter`。
- `RotatingLogFile` 在写入前按 1MB 阈值判断体积，轮转顺序为
  `Pings.log` → `Pings.1.log` → … → 删除最旧。
- 退出时 `Dispose` 会关闭通道并最多等待 5 秒把队列写完。
- 实时表格运行期间 `SuppressConsole = true`，控制台日志不会破坏 Live 布局。

## 通知

- 模板渲染改为单遍扫描：不会出现「替换结果里又含占位符」导致的循环替换，
  也不存在旧实现的 `seenStates` 集合上限问题。
- 同一任务（`Name|Protocol|Endpoint`）在 `notificationCooldownSeconds` 内最多通知一次；
  `0` 表示不限制；Critical 级别的告警不受冷却限制（中断确认必须送达），但仍会刷新时间戳。
- 冷却字典超过 1024 条时清理一天前的记录，避免长期运行无界增长。
- 通知任务被登记并在 `ProbeMonitor.StopAsync` 等待（最多 10 秒），
  避免进程退出时丢掉在途请求。
- Webhook 默认 `Content-Type: application/json`，可通过 `headers` 覆盖。
- SMTP 使用 `StartTlsWhenAvailable`（`enableSsl` 为真时），连接/发送超时 20 秒。

## 告警文案为什么很短

告警队列里的文本形如 `请求超时 11:02:15`，**不带完整日期**。原因不是省事而是排版：
Spectre 的 `Overflow.Ellipsis` 只截断“超长单词”，遇到空格仍会折行，
一行变三行会把表格撑高，越出窗口后就开始滚动/错位。完整时间戳由表格标题（日期）
与日志文件负责，界面只需要能分辨“什么时候发生的”。

同一条约束也适用于**状态列**：`ProbeResult.Detail` 会被直接渲染成状态文本，因此：

- HTTP 只用 `HTTP 404`，**不带英文原因短语**（`HTTP 404 File not found` 带空格会被折行）；
- 超时类文案写成 `连接超时(2000ms)` 而不是 `2000ms 内未完成连接`。

套接字错误同理：`SocketException.Message` 是整句英文，必须经 `SocketErrorMapping.Describe`
转成 `连接被拒绝` 这类短中文再进快照。

## 命令行与退出码

`CliOptions.Parse` 支持位置参数与 `--config` 同时存在时的互斥检查；`--` 之后一律按位置参数处理。
退出码在 `ExitCodes` 中集中定义，README 与 CI 都依赖这些值。

`--once` 用于脚本：每个目标探测一次，输出静态表格；只要有一个目标非成功即返回 `4`。

## Native AOT 注意事项

- JSON 必须走源生成（`JsonContext`），不能用反射序列化。
- Windows P/Invoke 使用 `[LibraryImport]`，并且**只在 `net10.0-windows` 目标定义 `WINDOWS` 常量**。
  旧 csproj 对所有 TFM 都定义 `WINDOWS`，会让 Linux 构建出调用 `kernel32.dll` 的程序集。
- `CodePagesEncodingProvider` 会让裁剪器保留代码页数据（体积 +约 0.5MB），
  它是「旧 GBK 配置能正确迁移」的必要代价。

## 版本号

唯一真相源是 **git tag**，源码里不存在版本常量：

```
tag v1.0.22        → AppVersion=1.0.22  AppVersionType=Release  → --version: Pings 1.0.22
tag 1.1.0-Beta     → AppVersion=1.1.0   AppVersionType=Beta     → --version: Pings 1.1.0-Beta
本地构建             → AppVersion=0.0.0   AppVersionType=Dev      → --version: Pings 0.0.0-Dev
```

链路：工作流解析 tag → `-p:AppVersion` / `-p:AppVersionType` → csproj 写入
`AssemblyMetadata` → `AppInfo` 反射读回。

几个刻意的决定：

| 决定 | 理由 |
|------|------|
| 读自定义 `AssemblyMetadata`，**不读** `AssemblyInformationalVersion` | 后者由 SDK 生成，会追加源码哈希（实测 `1.2.3+c6e9e86…`），解析它等于把「版本正确」寄托在 SDK 内部行为上；csproj 里写 `<InformationalVersion>` 也拦不住 |
| 版本号与发布类型分开 | 单个 tag 同时表达「发什么版本」与「发到哪个渠道」；否则 `1.1.0-Beta` 会被截成 `1.1.0`，beta 包与正式包在 `--version`、日志、窗口标题里完全无法分辨（这是旧实现的实际缺陷） |
| 用 `typeof(AppInfo).Assembly` 而非 `Assembly.GetEntryAssembly()` | 后者在单元测试里拿到 testhost，读到的是无关程序集的元数据 |
| `IsDevelopment` 依据显式标记 `VersionType == "Dev"` | 不能依据「版本号是不是 0」：元数据意外缺失时会把发布包误判成开发包 |

守护：`VersionMetadataTests` 断言元数据确实注入、三段格式、类型默认值，以及版本文本规则与
工作流断言一致；CI 额外断言 AOT 产物的 `--version` 为 `Pings 0.0.0-Dev`（覆盖裁剪后的真实路径）。

## 显示层（Ui）

全部基于 Spectre.Console，没有自写的 ANSI 序列或字符宽度计算：

| 能力 | 用法 |
|------|------|
| 备用缓冲区 | `AnsiConsole.AlternateScreen(...)`：在独立画布上绘制，退出（含异常）时整屏还原，滚动历史与提示符不受影响 |
| 终端尺寸 | `AnsiConsole.Profile.Width` / `Profile.Height`（Spectre 自己探测） |
| 列数自适应 | `MonitorLayout.ColumnsFor(width)`：宽度不足时依次丢弃日志/延迟/丢包率/目标，始终保留名称+状态 |
| 行数自适应 | `MonitorLayout.DataRowCapacity(height)` + `MonitorLayout.Select(...)`：超高时优先保留异常目标，并在 caption 里提示隐藏数量 |
| 溢出处理 | 单元格统一 `Overflow.Ellipsis`：超长文本截断并显示 `…`，不让表格被撑高 |
| 终端能力守卫 | `AnsiConsole.Profile.Capabilities.AlternateBuffer`（0.57+ 提供） |

当前使用 **Spectre.Console 0.57.2**（0.57 起新增依赖 `Spectre.Console.Ansi`，会自动一并还原）。

### Spectre 版本敏感点（全部实测）

| 版本 | 配置 | 行为 |
|------|------|------|
| 0.54.0 | `TableColumn.NoWrap = true` 且列宽之和 > 终端宽度 | **死循环**（进程卡死） |
| 0.54.0 | `Overflow.Crop` / `Ellipsis` + 中文 | 抛 `ArgumentOutOfRangeException`（`Segment.SplitOverflow` 按字符数切片） |
| 0.57.2 | 上述两项 | ✅ 均已修复 |
| 0.57.2 | `Overflow.Fold` + 列数多 + 窗口窄 | ❌ **会挂死**（回归；0.54 下只是行数膨胀） |

结论：升级到 0.57.2 后必须用 `Ellipsis`/`Crop`，**绝不要显式或隐式退回 `Overflow.Fold`**。
旧版本只能靠 `Fold` 苟住（0.54 的 Crop 会崩），升级后用 `Ellipsis` 同时解决了
「不崩」与「表格不被撑高」两件事——这正是之前用 `Fold` 时表格高度随窗口变窄而膨胀的原因。

补充两点容易误解的行为：

- `TableColumn.NoWrap` 只影响**列宽测算**，并不能阻止空格式换行（如 `171ms ±2.3ms`），
  不要把“不折行”寄托在它身上；真正掌控行高的是单元格的 `Overflow` 策略。
- `AlternateScreen` 在终端不支持时是 **抛 `NotSupportedException`** 而非降级，
  所以调用前必须先用 `Capabilities.AlternateBuffer` 守卫。`--no-color` 只设
  `Capabilities.ColorSystem = NoColors`，**不能**关 `Capabilities.Ansi`，
  否则备用缓冲区/清屏/光标控制一并失效。

### 控制台字体与「歧义宽度」字符（Windows）

Windows 控制台的双击启动曾出现「标题重复、表格错位、说明行插进数据行」，
根因不是布局算错，而是**终端与 Spectre 对「歧义宽度」字符的格子数判断不一致**：

- Spectre.Console 按 Unicode East Asian Width 计算列宽，把 Ambiguous 类字符（`± → … · °`）算作 **1 格**；
- 东亚语言的 Windows 默认控制台字体（简体中文是新宋体 8x16）把它们画成 **2 格**。

于是 Spectre 认为「正好铺满一行」的数据行，在屏幕上多出 1~2 格并折行；备用缓冲区里
`LiveDisplay` 按自己算的高度上移光标，位移与实际不符，画面就逐帧错位——标题每隔一行出现一次，
说明行被挤进数据行之间。这与窗口宽度、`Expand`、`Overflow` 策略都无关（纯 ASCII 内容或
`Expand = false` 时不复现，可用来快速确认是不是这个原因）。

处置分两步，缺一不可：

| 步骤 | 位置 | 原因 |
|------|------|------|
| 启动时先检查一次 | `Program.InitializeConsole` → `WindowsConsole.EnsureNarrowAmbiguousWidth()` | `--once` 静态表格与首帧之前就需要正确字体 |
| **进入备用缓冲区之后**再检查一次 | `MonitorView` 的 `prepareAlternateScreen` 回调 | conhost 的备用缓冲区有独立的一份字体，切换缓冲区会把窗口字体改回默认 CJK 字体。实测：先设 Consolas 再进备用缓冲区，进去后字体又变回新宋体、`±` 重新变两格 |

程序无法改变「字形宽度」，只能换字体，所以逻辑是「写一个 `±` 看光标前进几格」实测，
已经是一格就不动（不覆盖用户调好的字体）。候选只取微软官方 Windows 字体清单中
**主表**（而非按需语言包 FOD）的等宽字体，保证不随系统语言变化：

| 字体 | `± → … · °` | 中文 | 制表符 | 备注 |
|------|-------------|------|--------|------|
| Consolas | 1 ✅ | 2 ✅ | 1 ✅ | 首选，Win10/11 均预装 |
| Lucida Console | 1 ✅ | 2 ✅ | 1 ✅ | 备选 |
| Courier New | 1 ✅ | 2 ✅ | 1 ✅ | 备选（衬线，观感较差） |
| MS Gothic | 2 ❌ | 2 ✅ | 1 ✅ | 歧义宽按两格，不可用 |
| 新宋体（NSimSun） | 2 ❌ | 2 ✅ | 1 ✅ | 中文 Windows 默认字体，正是问题来源 |
| Segoe UI / Verdana | 2 ❌ | 2 ✅ | 1 ✅ | 比例字体，仅字宽测试列为参考 |
| Cascadia Mono / Code | 1 ✅ | 2 ✅ | 1 ✅ | 合格，但仅 Windows 11 随系统提供，故不作首选 |

拉丁字体没有 CJK 字形，中文由 conhost 自行回退渲染（`SystemLink` 里 Consolas 并没有条目，
但实测中文显示正常，不会变成方块），所以换字体不会牺牲中文显示。

同一套宽度问题也会影响**要不要用歧义字符**：日志里的 `延迟波动 190ms → 262ms`、
延迟列的 `±`、caption 的分隔符 `·` 都在其中。既然字体已保证为窄宽度，这些字符可以保留。

## 测试

`Pings.Tests` 的默认用例不依赖外网；TCP / UDP / HTTP 传输层全部跑在本机回环上，
ICMP 只做纯逻辑校验（真实发包留给 CI 的 `--once` 冒烟）。

| 文件 | 守护内容 |
|------|----------|
| `ConfigLoaderTests` | 往返一致、BOM、原子保存、非法 JSON |
| `ConfigValidatorTests` | 区间校验、协议分支校验、重复项警告、通知必填项 |
| `LegacyConfigConverterTests` | 旧格式字段映射、注释/空行、默认值 |
| `ProbeStatisticsTests` | 滑窗丢包率、故障率、RTT 极值 / p95 / 标准差、抖动重算 |
| `ProbeStateMachineTests` | 两级中断升级、短故障、去重、SRTT 波动确认与回差 |
| `ProbeTaskTests` | 快照更新、事件、异常降级、传输层释放 |
| `ProbeStatusTests` | 故障分类、丢包 / 故障 / 中断口径、协议名解析 |
| `ProbeTaskOptionsTests` | 主机 / URL 校验、间隔推导、JSON 往返、端点文本 |
| `TcpProbeTransportTests` | 可连接 / 关闭端口 |
| `UdpProbeTransportTests` | 回声成功 / 端口不可达 / 发出去的确实是解析后的字节 |
| `UdpPayloadResolverTests` | 文本 / base64 / 文件三种来源、相对路径基准、各种失败分支 |
| `HttpProbeTransportTests` | 2xx、状态码异常、响应超时、HEAD→GET 回退 |
| `WarningQueueTests` | FIFO、相邻去重、容量溢出计数、并发入队 |
| `TemplateRendererTests` | 占位符、时间格式、未命中保留、自引用不死循环 |
| `RotatingLogFileTests` | 追加、轮转、保留份数、Dispose 后不写 |
| `CliOptionsTests` | 位置参数/命名参数、错误分支、退出码文案 |

CI 额外做 AOT 冒烟：`--version` / `--help` / `--once`（回环 ICMP，回环地址不依赖外网）。
冒烟配置使用 `protocol` + `target` 字段（旧字段名 `ip` 会在启动期校验失败，返回退出码 2）。

## 构建与发布

```bash
# 本地构建 + 测试
dotnet build Pings.sln -c Release
dotnet test tests/Pings.Tests/Pings.Tests.csproj -c Release
```

### Windows Native AOT

```powershell
dotnet publish src/Pings/Pings.csproj -c Release -f net10.0-windows `
  -p:PublishProfile=src/Pings/Properties/PublishProfiles/win-x64-aot.pubxml `
  -p:AppVersion=1.0.22 -p:AppVersionType=Release -o dist/aot
```

`-f` 不能省：本项目是多目标框架，pubxml 里的 `TargetFramework` 在跨目标求值阶段不生效
（会报 `NETSDK1129`）。

构建链全部由 MSBuild 承担，仓库里没有发布脚本：工具获取（UPX）、压缩、产物形态自检
都是 csproj 里的 target，因此**本地与 CI 跑的是同一份逻辑**，不存在「脚本里做的检查 CI 没做」。

**Windows AOT 必须在 Windows 上构建**：`Microsoft.NETCore.Native.Publish.targets` 有硬性检查
`Cross-OS native compilation is not supported`，且非 Windows 环境没有 MSVC 链接器与
Windows SDK 导入库（`kernel32.lib` 等）。所以：本地在 Windows 上执行上面的命令，
CI 用 `windows-latest`。非 Windows 平台可以发布自己的 AOT（`-r linux-x64`），UPX 会自动跳过。

发布由 tag 驱动（`Release.yml`）：tag 解析出「版本 + 类型」→ 校验格式 → 跑测试 →
AOT 发布（注入 `AppVersion` / `AppVersionType`）→ 断言单 exe / 无 pdb / 体积 / 版本号 →
回环冒烟 → 创建 Release。

### 多协议与 AOT

- 四个传输层只用 BCL 套接字 API（`Ping` / `Socket` / `SocketsHttpHandler`），没有反射与动态代码，可直接被裁剪。
- HTTP 探测自己实现 `ConnectCallback` 只是为了拿到精确的连接耗时；回调参数是强类型的
  `SocketsHttpConnectionContext`，不经过反射路径。
- 新增配置类型必须在 `JsonContext` 登记（源生成）：漏登记时普通构建也能通过，
  但 AOT 产物会在反序列化时失败——这是本项目最容易踩的裁剪坑。
- 冒烟覆盖范围：CI 只在 AOT 产物上跑回环 ICMP（不依赖外网、不需要起服务）；
  TCP / UDP / HTTP 的具体行为由单元测试在本机回环端口上验证（见「测试」一节的表格）。
  发布前若改了传输层，建议手工跑一次 README 的「本地多协议冒烟」配置。
- Linux / macOS 上 ICMP 需要 ICMP datagram 套接字权限（`net.ipv4.ping_group_range` 或 `CAP_NET_RAW`），
  拿不到时按 `LocalError` 记录，不会中断探测循环。

### AOT 相关取舍（与参考项目一致）

| 设置 | 作用 |
|------|------|
| `CetCompat=false` | 不打 `/CETCOMPAT`。否则未打全累积更新的旧 Win10/11 会在托管 Main 之前 fail-fast（0x80131506），`try/catch` 捕不到 |
| `CopyOutputSymbolsToPublishDirectory=false` | `DebugType`/`DebugSymbols` 管不到 ILCompiler 的原生符号，这个开关才决定发布目录里有没有几十 MB 的 `.pdb` |
| `PublishSingleFile=false` | AOT 产物本身就是单个原生 PE，没有 apphost/bundle 概念 |
| `EnableCompressionInSingleFile=false` | 同上，只对单文件形态有意义 |
| `InvariantGlobalization=false` | 否则区域性行为被固定为 Invariant（日期格式、GBK 迁移探测都受影响） |

### UPX

由 csproj 的 `UpxCompressNativeBinary` target 完成，原地压缩 `$(PublishDir)` 里的 exe；
解析顺序 `-p:UpxPath` → `tools/upx/upx.exe` → 自动下载最新 win64 版。
拿不到只警告不失败（体积优化而已）。

实测：未压缩 9.1 MB（含四种协议的传输层），UPX `--best` 后 3.8 MB。

四个必须记住的点：

1. **必须挂在 `AfterTargets="CopyNativeBinary"`**。该 target 本身也是 `AfterTargets="Publish"`，
   两个同挂 Publish 的 target 顺序不确定 —— UPX 先跑就会压中间产物，随后被
   CopyNativeBinary 覆盖回未压缩文件（日志有 "Packed 1 file" 但体积不变）。
2. **条件不能引用 `_UpxToolPath`**。MSBuild 在运行 `DependsOnTargets` 之前就求值 target 的
   `Condition`，那时路径变量还是空的；一旦写进 Condition，整个 target 会被跳过（含依赖），
   压缩静默失效（实测踩过：日志里看得见"使用指定的 UPX"，产物仍是未压缩体积）。
   正确做法是把路径判断放到 task 级 `Condition` 上（见 `Pings.csproj`）。
3. **不能把「目标是不是 Windows」缓存成顶层属性**。`RuntimeIdentifier` 由 pubxml 在文件末尾
   导入后才生效，顶层属性求值早于它，读到的永远是空值——结果 UPX 的两个 target 都因条件
   不成立被跳过，产物保持 9.1 MB 而日志里没有任何提示（CI 的体积断言会拦下，本地很容易误以为
   已压缩）。判断要写成 target 执行期的内联表达式 `$(RuntimeIdentifier.StartsWith('win'))`。
4. **UPX 生效与否看日志**：正常会打印 `UPX 压缩（5.3.0）：…`。只跑 `dotnet publish`
   默认 verbosity 看不到，用 `-v:n` 确认；或者直接看产物体积（3.8 MB 对 9.1 MB）。

CI 与 Release 的 `env.UPX_VERSION` 必须一致并固定，否则同一个 tag 两次构建的产物不可重现。

## 编码约定

- 源码 UTF-8（无 BOM），行尾跟随仓库既有风格。
- 类型/成员 PascalCase；私有字段 `_camelCase`；不使用下划线命名。
- 面向用户的中文文本直接写在资源所在位置（当前不做多语言；
  若要加语言，参考「资源编进主程序集 + 自定义 ResourceManager」的 AOT 方案）。
- 修改配置模型时必须同步更新：`ConfigValidator`、README 字段表、相应测试。
