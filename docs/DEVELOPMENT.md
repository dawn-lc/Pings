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
│  └─ Properties/
│     ├─ EmbeddedResourceManager.cs  多语言资源装载（语言后缀无 .Culture 段）
│     ├─ Resources.resx              中性资源（英文）
│     ├─ ResourcesZhCn.resx          简体中文
│     └─ Resources.Designer.cs       手写强类型访问器
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
- 超时类文案写成 `连接超时(2000ms)` 而不是 `2000ms 内未完成连接`；
- 换成英文界面后同样不能出现空格，且长度必须压得住（详见「多语言资源约定」）。

套接字错误同理：`SocketException.Message` 是整句英文，必须经 `SocketErrorMapping.Describe`
转成 `连接被拒绝` 这类短文案（随界面语言）再进快照。

## 命令行与退出码

`CliOptions.Parse` 支持位置参数与 `--config` 同时存在时的互斥检查；`--` 之后一律按位置参数处理。
退出码在 `ExitCodes` 中集中定义，README 与 CI 都依赖这些值。

`--once` 用于脚本：每个目标探测一次，输出静态表格；只要有一个目标非成功即返回 `4`。

`--once` **隐含 `--no-pause`**（统一由 `CliOptions.IsNonInteractive` 表达）：脚本场景下既不能在「配置文件不存在，是否创建默认配置」上提问，也不能在结尾「按任意键退出」处停住——两条路径都会把一次笔误变成挂起，而且挂起时没有任何输出，很难定位。

## Native AOT 注意事项

- JSON 必须走源生成（`JsonContext`），不能用反射序列化。
- Windows P/Invoke 使用 `[LibraryImport]`，并且**只在 `net10.0-windows` 目标定义 `WINDOWS` 常量**。
  旧 csproj 对所有 TFM 都定义 `WINDOWS`，会让 Linux 构建出调用 `kernel32.dll` 的程序集。
- `CodePagesEncodingProvider` 会让裁剪器保留代码页数据（体积 +约 0.5MB），
  它是「旧 GBK 配置能正确迁移」的必要代价。

## 版本号

唯一真相源是 **git tag**，源码里不存在版本常量：

```
tag v1.2.3        → AppVersion=1.2.3   AppVersionType=Release  → --version: Pings 1.2.3
tag v1.2.3-Beta   → AppVersion=1.2.3   AppVersionType=Beta     → --version: Pings 1.2.3-Beta
本地构建             → AppVersion=0.0.0   AppVersionType=Dev      → --version: Pings 0.0.0-Dev
```

`v` 前缀可省略（`Release.yml` 的 tags 过滤同时匹配 `v[0-9]*` 与 `[0-9]*`），但不带前缀的 tag 容易与普通分支名混淆，建议始终带 `v`。没带类型后缀即视为 `Release`。

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

### 发布模型：平台/架构与变体都是参数

仓库里**没有**任何按平台区分的发布配置文件，也没有发布脚本。发布会话只由两个参数决定：

| 参数 | 取值 | 省略时 |
|------|------|--------|
| `-r`（RID） | `win-x64` / `win-arm64` / `linux-x64` / `linux-arm64` / `osx-x64` / `osx-arm64` … | 取 SDK 的 `NETCoreSdkRuntimeIdentifier`，即**当前机器** |
| `-p:PublishVariant` | `net` / `aot` | 有 RID 时按 `aot` |

```powershell
# 发当前环境（当前系统 + 当前架构），两个变体各一条命令
dotnet publish src\Pings\Pings.csproj -c Release -p:PublishVariant=net -o dist\net
dotnet publish src\Pings\Pings.csproj -c Release -p:PublishVariant=aot -o dist\aot

# 发别的平台架构：只多一个 -r
dotnet publish src\Pings\Pings.csproj -c Release -r linux-arm64 -p:PublishVariant=net -o dist\linux-arm64-net
```

| 变体 | 实际开关 | `win-x64` 实测 |
|------|----------|----------------|
| `net` | `SelfContained` + `PublishSingleFile` + `EnableCompressionInSingleFile`，`PublishAot=false` | 12.07 MB |
| `aot` | `PublishAot=true`（AOT 本身就是单个原生二进制，不碰单文件开关） | 9.23 MB → UPX 后 3.88 MB |

**为什么用参数而不是 pubxml**：pubxml 是在「TFM 已选定」之后才被导入的，反过来逼着命令行再写一遍 `-f`，还得为每个平台各存一份内容几乎相同的副本——`-r` 是命令行全局属性，求值阶段就可见，一个参数就够，也不存在「改了 csproj 忘了改 profile」的第二份真相源。

**TFM 由 RID 推导**，写在 csproj 顶部：

```xml
<PublishVariant Condition="'$(PublishVariant)' == '' and '$(RuntimeIdentifier)' != ''">aot</PublishVariant>
<RuntimeIdentifier Condition="'$(RuntimeIdentifier)' == '' and '$(PublishVariant)' != ''">$(NETCoreSdkRuntimeIdentifier)</RuntimeIdentifier>
<_IsWindowsRid Condition="'$(RuntimeIdentifier)' != ''">$(RuntimeIdentifier.StartsWith('win'))</_IsWindowsRid>
<TargetFrameworks Condition="'$(RuntimeIdentifier)' == ''">net10.0;net10.0-windows</TargetFrameworks>
<TargetFramework Condition="'$(_IsWindowsRid)' == 'true'">net10.0-windows</TargetFramework>
```

四条约束，改动这里时必须一起守住：

1. **属性函数带字符串参数不能直接写进 `Condition`**。`$(RuntimeIdentifier.StartsWith('win'))` 里单引号会被条件解析器吃掉，报 `MSB4092`；必须先算成 `_IsWindowsRid` 再比较。
2. **必须设置单数 `<TargetFramework>`**。只留 `<TargetFrameworks>`（复数）会让 SDK 进入跨目标模式，`publish` 直接报 `NETSDK1129`。
3. **`<PropertyGroup>` 不能嵌套**。变体差异块是另一个顶层 `PropertyGroup`，塞进上面那个里会静默失效（实测踩过：`-p:PublishVariant=net` 仍然产出 3.88 MB 的 AOT 产物）。
4. **UPX 的「目标是不是 Windows」不能缓存成顶层属性**。不带 `-r` 时 `RuntimeIdentifier` 为空，缓存进属性会让条件永久不成立。判断要写成 target 执行期的内联表达式 `$(RuntimeIdentifier.StartsWith('win'))`，运行到那一刻读到的才是本次发布实际使用的值。

`AppVersion` / `AppVersionType` 默认 `0.0.0` / `Dev`：**版本号唯一真相源是 git tag**，本地构建不需要手工声明。演练发布包时用 `-p:AppVersion=9.9.9 -p:AppVersionType=Release`，此时 `--version` 输出 `Pings 9.9.9`，与正式包格式完全一致。

构建链全部由 MSBuild 承担：工具获取（UPX）、压缩、产物形态自检都是 csproj 里的 target，因此**本地与 CI 跑的是同一份逻辑**，不存在「脚本里做的检查 CI 没做」。

### AOT 不能跨操作系统

`Microsoft.NETCore.Native.Publish.targets` 有硬性检查 `Cross-OS native compilation is not supported`；即便绕过，Linux 上也没有 MSVC 链接器与 Windows SDK 导入库（`kernel32.lib` 等），ILCompiler 仍产不出 PE。

所以每种操作系统必须有**自己的 runner**：这正是 `.github/workflows/build-matrix.yml` 用 12 项矩阵（6 个 RID × 2 个变体）而不是单条命令打包全部的原因。`net` 变体不受此限制，可跨平台发布，但它和 `aot` 共用矩阵，保证同一 RID 的两个产物来自同一份源码、同一套版本号。

注意这里限制的是**操作系统**，不是架构：同一操作系统内可以交叉构建另一种架构。`windows-latest`（Windows Server 2025）镜像自带 `VC.Tools.ARM64` 与 Windows SDK，所以 `win-arm64` 在 x64 runner 上能正常构建（本机没有 ARM64 C++ 工具链时会报 `Platform linker not found`，那是环境缺失，不是设计限制）。因此 Windows 只需要一个 runner 标签。

### 工作流结构

只有两个作业定义，CI 与 Release 共用：

| 文件 | 角色 |
|------|------|
| `build-matrix.yml` | `workflow_call` 可复用工作流，**唯一**的「全平台 × 全变体」定义：发布 → 产物校验 → 回环冒烟 → 改名 → 上传 |
| `CI.yml` | `verify`（编码 / RID→TFM 推导 / build / test，单个 runner）+ `artifacts`（调用 `build-matrix.yml`） |
| `Release.yml` | `version`（解析 tag）+ `test` + `build`（调用 `build-matrix.yml`，传入版本号）+ `publish`（断言 12 个附件齐备 → 创建 Release） |

矩阵项只有三列（`os` / `rid` / `variant`），因为 TFM 与产物名都由 RID 推出来，没有第二条需要维护的信息。实际用到的 runner 只有 5 种：`windows-latest`、`ubuntu-latest`、`ubuntu-24.04-arm`、`macos-15-intel`、`macos-latest`。

几条刻意的选择：

- **`fail-fast: false`**：一个平台失败不该掩盖其它平台的结果，先看完所有失败再统一修。
- **Windows 只用一个 runner 标签**：`windows-latest` 同时负责 `win-x64` 与 `win-arm64`（依靠镜像自带的 ARM64 C++ 工具链交叉构建）。少一种 runner 就少一处环境差异，代价是 ARM64 产物无法在 x64 runner 上执行，验证方式随之降级——见下一条。
- **验证分两种，取决于能否执行**：目标架构与 `RUNNER_ARCH` 一致时，让产物自报 `--version` 并跑回环 `--once`；不一致时（目前只有 `win-arm64`）改为断言 PE 文件头 `Machine` 字段是 `0xAA64`。这条断言不是退而求其次的摆设：RID 没生效、错发了宿主架构二进制时，只有文件头能发现。「跳过」必须显式打印，静默跳过比失败更危险。
- **测试不在矩阵里跑**：测试与平台无关，`verify` 跑一次就够；12 个矩阵项各跑一遍等于把同一批用例执行 12 次。平台相关的正确性由「能执行的跑起来 + 不能执行的验形态」共同覆盖。
- **Release 不给 `workflow_dispatch` 入口**：手动入口只能跑默认分支的代码，而「发出去的产物必须来自被打 tag 的那个提交」不能妥协。失败后重跑同一次运行即可。
- **`Verify asset set` 断言附件集合相等**（不只查缺，也查多）：少了说明某个矩阵项静默失败，多了说明命名规则走散。

「单 exe / 无 pdb」不在工作流里断言，而由 `VerifyPublishOutput` 与 `CopyOutputSymbolsToPublishDirectory=false` 在构建期保证（两个变体都生效）——本地发布受同一约束，不存在「CI 查了、本地没查」的落差。体积也不设阈值（会随 .NET / ILCompiler 版本漂移），UPX 是否生效只看 `upx -l` 的退出码。

### 多协议与 AOT

- 四个传输层只用 BCL 套接字 API（`Ping` / `Socket` / `SocketsHttpHandler`），没有反射与动态代码，可直接被裁剪。
- HTTP 探测自己实现 `ConnectCallback` 只是为了拿到精确的连接耗时；回调参数是强类型的
  `SocketsHttpConnectionContext`，不经过反射路径。
- 新增配置类型必须在 `JsonContext` 登记（源生成）：漏登记时普通构建也能通过，
  但 AOT 产物会在反序列化时失败——这是本项目最容易踩的裁剪坑。
- 冒烟覆盖范围：CI 在**全部 12 个产物**上跑回环 ICMP（不依赖外网、不需要起服务）；
  TCP / UDP / HTTP 的具体行为由单元测试在本机回环端口上验证（见「测试」一节的表格）。
  发布前若改了传输层，建议手工跑一次 README 的「本地多协议冒烟」配置。
- Linux / macOS 上 ICMP 需要 ICMP datagram 套接字权限（`net.ipv4.ping_group_range` 或 `CAP_NET_RAW`），
  拿不到时按 `LocalError` 记录，不会中断探测循环。

### AOT 相关取舍（与参考项目一致）

| 设置 | 作用 |
|------|------|
| `CetCompat=false` | 不打 `/CETCOMPAT`。否则未打全累积更新的旧 Win10/11 会在托管 Main 之前 fail-fast（0x80131506），`try/catch` 捕不到 |
| `CopyOutputSymbolsToPublishDirectory=false` | `DebugType`/`DebugSymbols` 管不到 ILCompiler 的原生符号，这个开关才决定发布目录里有没有几十 MB 的 `.pdb` |
| `InvariantGlobalization=false` | 否则区域性行为被固定为 Invariant（日期格式、GBK 迁移探测都受影响） |

单文件相关开关**不在全局属性里**：`aot` 变体本身就是单个原生 PE，没有 apphost/bundle 概念，所以 `PublishSingleFile` / `EnableCompressionInSingleFile` 只在 `PublishVariant=net` 的条件属性组里打开——写进全局属性等于给 AOT 叠一层无意义的开关。

### UPX

由 csproj 的 `UpxCompressNativeBinary` target 完成，原地压缩 `$(PublishDir)` 里的原生可执行文件；拿不到只警告不失败（体积优化而已）。

**覆盖范围是 Windows PE 与 Linux ELF，不含 macOS**：

| 目标 | 是否压缩 | 依据 |
|------|----------|------|
| Windows PE | 是 | `win-x64`：9,677,824 → 4,065,792 字节（42.01%，`win64/pe`） |
| Linux ELF | 是 | Windows 上的 upx 压同一份 ELF：14,750,709 → 7,103,996 字节（48.16%，`linux/amd64`）；压缩后的 ELF 能否运行由 CI 的 Linux 矩阵项冒烟验证 |
| macOS Mach-O | 否 | UPX 重写 Mach-O 会破坏代码签名，而 Apple Silicon 上未签名二进制会被内核终止（`Killed: 9`），UPX 无法重新签名。官方 5.3.0 也只发布 Linux 各架构与 win32/win64 资产，没有 macOS 产物 |

工具解析顺序：`-p:UpxPath` → `tools/upx/upx[.exe]` → `PATH` 里的 `upx`（仅非 Windows 宿主）→ 自动下载最新 win64 版（仅 Windows 宿主）。

- **为什么非 Windows 宿主不自动下载**：官方 Linux 包是 `tar.xz`，.NET 没有内置 xz 解压，为此引入外部 tar 依赖不划算；这两个平台装一份即可（`apt install upx-ucl` / `brew install upx`）。CI 的 Linux 矩阵项用固定版本装到 `tools/upx/upx` 并写 `.version`，保证压缩工具与其他平台同版本。
- **为什么 UPX 二进制不需要平台匹配**：目标格式由文件头自动识别。Windows 上的 `upx.exe` 照样能压出 `linux/amd64`，所以条件判断的是**目标 RID**，不是宿主。
- **缓存在非 Windows 上不可核版本时按可用处理**：手工放进 `tools/upx/upx` 没有 `.version`，若严格比对固定版本就会被判成「版本不符」而静默跳过压缩。

体积一律按 `1 MB = 1048576` 字节口径（与 PowerShell 的 `1MB`、CI 日志里的 `{0:N2} MB` 一致）：混用十进制会写出「未压缩 9.7 MB → 后 3.9 MB」这种自相矛盾的数字。

五个必须记住的点：

1. **必须挂在 `AfterTargets="CopyNativeBinary"`**。该 target 本身也是 `AfterTargets="Publish"`，
   两个同挂 Publish 的 target 顺序不确定 —— UPX 先跑就会压中间产物，随后被
   CopyNativeBinary 覆盖回未压缩文件（日志有 "Packed 1 file" 但体积不变）。
2. **条件不能引用 `_UpxToolPath`**。MSBuild 在运行 `DependsOnTargets` 之前就求值 target 的
   `Condition`，那时路径变量还是空的；一旦写进 Condition，整个 target 会被跳过（含依赖），
   压缩静默失效（实测踩过：日志里看得见"使用指定的 UPX"，产物仍是未压缩体积）。
   正确做法是把路径判断放到 task 级 `Condition` 上（见 `Pings.csproj`）。
3. **不能把「目标是不是 Windows」缓存成顶层属性**。不带 `-r` 时 `RuntimeIdentifier` 是空值（`dotnet build` 要覆盖两个 TFM），把空值缓存进属性会让条件永久不成立——结果 UPX 的两个 target 都因条件不成立被跳过，产物保持未压缩体积而日志里没有任何提示（端到端会被 CI 的 `upx -l` 断言拦下，本地很容易误以为已压缩）。判断要写成 target 执行期的内联表达式。
4. **确认 UPX 生效用 `upx -l`，不要看体积**：`upx -l <exe>` 对已压缩产物退出 0、对未压缩返回 2（`NotPackedException`）。体积阈值会随 .NET / ILCompiler 版本漂移，而且「压了但没变小」与「压根没压」在阈值下可能给出同一个结论。日志里的 `UPX 压缩（5.3.0）：…` 是构建侧的自述，真正的证据在产物上。
5. **仓库内路径一律用正斜杠**。`RepoRoot` 与 `tools/upx/` 若用反斜杠拼，在 Linux / macOS 上反斜杠是字面量字符而不是分隔符：`UsingTask` 的 `Source` 找不到 `BuildTasks.cs`，UPX 目录也会定位到不存在的名字。MSBuild 在两个平台上都接受 `/`。

`env.UPX_VERSION` 定义在 `build-matrix.yml`，CI 与 Release 共用同一份，否则同一个 tag 两次构建的产物不可重现。

## 编码约定

- 源码 UTF-8（无 BOM），行尾跟随仓库既有风格。
- 类型/成员 PascalCase；私有字段 `_camelCase`；不使用下划线命名。
- 修改配置模型时必须同步更新：`ConfigValidator`、README 字段表、相应测试。

### 排版：一段一行，项内不折行

编辑器已开启自动换行，所以**不要按几十列手工折行**。硬换行会把一句话切成几段，改一个词就要重排整段，diff 里也看不出真实的语义边界。

| 对象 | 规则 |
|------|------|
| 段落（`.cs` 的 `//`、`///`，`.csproj` 的 `<!-- -->`，`.md` 正文） | 一段写一行，换行只用来分段 |
| 列表 | 每项一行，项内不再折行 |
| XML 元素属性 | 一律写在一行：`<EmbeddedResource Update="…" LogicalName="…" />`、`<Target Name="…" AfterTargets="…" Condition="…">`、任务调用的多个属性，哪怕这行很长 |
| 表格、代码块、ASCII 图示 | 保持原有结构，不做合并 |

下面这些换行是**结构**而非硬换行，必须保留：列表项之间、表格行之间、代码块内部、`=====` 分隔线、多段之间的空行、MSBuild 元素的嵌套（`<ParameterGroup>` 里每项一行）。

`.csproj` 里另有一类「必须单行」的注释属于构建硬约束（属性值混进换行会报 `NETSDK1124` / `NETSDK1083`），与排版偏好无关，改动时照注释说明即可。

## 多语言资源约定

界面文案一律通过 `Pings.Properties.Resources.Xxx` 取，**源码里不写用户可见的中文字面量**（XML 文档注释、`ArgumentException` 这类程序员可见的守卫消息除外）。

### 为什么不用标准的 satellite 程序集

标准做法是每种语言生成一个 `zh-CN\Pings.resources.dll` 卫星程序集，但那与「单文件下载」的发布形态冲突：**Native AOT 无法把卫星程序集打进 exe**，产物必然退化成「exe + 若干语言目录」。

因此本项目把各语言的 `.resources` 用 `LogicalName` 直接编进主程序集，由 `Pings.Properties.EmbeddedResourceManager` 自行按 culture 链查找。发布产物始终只有一个 exe（实测 `dist/aot` 只有一个 4 MB 的 `Pings.exe`）。

### 新增一个键

1. 在 `src/Pings/Properties/Resources.resx`（**中性 = 英文**）加 `<data>`；
2. 在 `src/Pings/Properties/ResourcesZhCn.resx` 加同键的中文；
3. 在 `src/Pings/Properties/Resources.Designer.cs` 加一个属性。

`LocalizationTests` 会校验这三处完全对齐：键集相等、占位符集合相等、无空值、Designer 属性覆盖全部键。漏一处就是测试失败，不依赖人工检查。

### 新增一种语言

语言清单的**唯一真相源**是 `Localization/UiLanguage.cs` 的 `SupportedCultures`——`TryResolve`（解析 `--lang` / `global.language`）与帮助文案里的「auto / en / zh-CN」都从它派生，不再各写一份。

1. `SupportedCultures` 里加一项（如 `"ja"`）；
2. 加 `Properties\ResourcesJa.resx`（**文件名不带 `.Culture` 段**）；
3. csproj 里加对应的 `LogicalName`（`Pings.Properties.Resources.ja.resources`）。

`LocalizationTests` 会反向校验：清单里的每一项都必须真能解析、且真能取到资源。漏了第 2 或第 3 步会直接测试失败，而不是运行时静默回退英文。

注意 resx 文件名后缀（`Ja` / `ZhCn`）与 culture 标记（`ja` / `zh-CN`）之间**没有机器可读的映射**（`ZhCn` 对应 `zh-CN` 这层只存在于人的约定里），所以两边都必须是显式清单，不能靠推导。

### 几条硬性约束

- **文件名不带 `.Culture` 段**：写 `ResourcesZhCn.resx`，**不能**写 `Resources.zh-CN.resx`。带合法 culture 名的文件名会被 SDK 的 `AssignCulture` 认成文化资源并自动生成卫星程序集——正是要避免的东西。资源名由 csproj 的 `LogicalName` 指定（`Pings.Properties.Resources.resources` 与 `Pings.Properties.Resources.zh-CN.resources`），与文件名无关。
- **`Resources.Designer.cs` 是手工维护的**，没有 VS 设计器自动同步；并且所有属性都必须走 `EmbeddedResourceManager`，不能用默认的 `ResourceManager`——后者只在卫星程序集里找 culture 资源，资源编进主程序集后会静默只返回英文。
- **`InvariantGlobalization` 必须保持 `false`**（csproj 已设）。设为 `true` 会把 `CurrentUICulture` 永久钉在 Invariant，所有语言资源都会被忽略，且不报错。
- **`NeutralLanguage` 是 `en`**：未受支持的系统语言回退到英文，而不是回退到某个具体译文。
- **占位符语义跨语言必须一致**：`{0}` 在两种语言里指的是同一个参数。「顺序可以为排版而调整，语义不能变」——中文 `窗口 {1}s 内短时中断 {0} 次` 与英文 `Short outages x{0} in {1}s` 是合规的（都是 `{0}`=次数、`{1}`=窗口秒数）。

### 告警/状态文案为什么很短

状态列靠 Spectre 的 `Overflow.Ellipsis` 截断，而**折行只会发生在空格处**：状态文案里一旦出现空格，窄窗口下行高会被撑开，整个活动区域布局错乱。所以 `Status*` 这一组在两种语言里都**不含空格**，且英文长度必须控制在 ~12 字符内（英文比中文宽，长了会被截成 `...` 反而看不出状态）。`LocalizationTests` 用「不含空格 + 英文长度上限」两条断言把这个约束固化下来。

### 语言是怎么选定的

优先级：命令行 `--lang` > 配置 `global.language` > 系统默认。

- `auto` 表示「跟随系统」，即不改动 `CultureInfo.CurrentUICulture`；
- 切换只动 `CurrentUICulture`，**不动** `CurrentCulture`：数字与日期在本项目里一律用显式格式串，继续跟随系统文化，避免「切界面语言把日志时间格式也改了」；
- 简繁与各类 `zh-*` 变体（`zh-Hans`、`zh-CHS`、`zh-SG`、`zh-TW`、`zh-Hant-HK` 等）统一归到 `zh-CN`。不归一的话 `CurrentUICulture` 报的是 `zh-Hans-CN` 之类，找不到对应资源就会**静默退回英文**；繁体用户落到简体也比看英文更可用。

`--lang` 在完整参数解析**之前**就被摘出来应用（`CliOptions.FindLanguage`），否则「未知参数」这类报错只能用默认语言输出。
