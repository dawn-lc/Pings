namespace Pings.Properties
{
    /// <summary>
    /// 强类型的本地化字符串入口。所有界面文案都从这里取，代码里不再出现中文字面量。
    /// </summary>
    /// <remarks>
    /// **手工维护**（没有 VS 设计器自动同步）：新增 resx 键后必须在这里补一个属性，
    /// 否则代码里无法强类型引用，只能硬编码字符串。<c>LocalizationTests</c> 会断言
    /// 「中性 resx 的每个键都有对应属性」，漏加会直接测试失败。
    ///
    /// 属性一律走 <see cref="EmbeddedResourceManager" />，**不能**用默认的
    /// <see cref="System.Resources.ResourceManager" />——后者只在卫星程序集里找 culture 资源，
    /// 资源编进主程序集后它只会返回中性（英文）文案。
    ///
    /// 语言切换靠 <c>CultureInfo.CurrentUICulture</c>（由 <c>UiLanguage.Apply</c> 设置），
    /// 因此这些属性每次访问都会按当前 UI 语言取值，不需要重建任何缓存。
    /// </remarks>
    public static class Resources
    {
        private static string Get(string name) => EmbeddedResourceManager.GetString(name, null) ?? name;

        // ── CLI ──
        public static string Usage => Get(nameof(Usage));
        public static string CliMissingOptionValue => Get(nameof(CliMissingOptionValue));
        public static string CliUnknownOption => Get(nameof(CliUnknownOption));
        public static string CliMultipleConfigFiles => Get(nameof(CliMultipleConfigFiles));
        public static string CliInvalidLanguage => Get(nameof(CliInvalidLanguage));
        public static string ExitCodeSuccess => Get(nameof(ExitCodeSuccess));
        public static string ExitCodeInvalidArguments => Get(nameof(ExitCodeInvalidArguments));
        public static string ExitCodeConfiguration => Get(nameof(ExitCodeConfiguration));
        public static string ExitCodeRuntimeError => Get(nameof(ExitCodeRuntimeError));
        public static string ExitCodeUnreachable => Get(nameof(ExitCodeUnreachable));
        public static string ExitCodeUnknown => Get(nameof(ExitCodeUnknown));

        // ── 配置加载 ──
        public static string ConfigFileNotFound => Get(nameof(ConfigFileNotFound));
        public static string ConfigContentEmpty => Get(nameof(ConfigContentEmpty));
        public static string ConfigParseFailed => Get(nameof(ConfigParseFailed));
        public static string ConfigSaveFailed => Get(nameof(ConfigSaveFailed));

        // ── 默认任务名 ──
        public static string DefaultTaskDns => Get(nameof(DefaultTaskDns));
        public static string DefaultTaskBackupDns => Get(nameof(DefaultTaskBackupDns));
        public static string DefaultTaskDoh => Get(nameof(DefaultTaskDoh));
        public static string DefaultTaskDot => Get(nameof(DefaultTaskDot));
        public static string DefaultTaskConnectivity => Get(nameof(DefaultTaskConnectivity));
        public static string DefaultTaskDiagnostics => Get(nameof(DefaultTaskDiagnostics));
        public static string DefaultTaskTrace => Get(nameof(DefaultTaskTrace));

        // ── 配置校验：任务 ──
        public static string ValidationAtLeastOneTask => Get(nameof(ValidationAtLeastOneTask));
        public static string ValidationTaskNameRequired => Get(nameof(ValidationTaskNameRequired));
        public static string ValidationTaskNameDuplicate => Get(nameof(ValidationTaskNameDuplicate));
        public static string ValidationProtocolInvalid => Get(nameof(ValidationProtocolInvalid));
        public static string ValidationEndpointDuplicate => Get(nameof(ValidationEndpointDuplicate));
        public static string ValidationTimeoutOutOfRange => Get(nameof(ValidationTimeoutOutOfRange));
        public static string ValidationIntervalOutOfRange => Get(nameof(ValidationIntervalOutOfRange));
        public static string ValidationIntervalLessThanTimeout => Get(nameof(ValidationIntervalLessThanTimeout));
        public static string ValidationMaxRecentPacketsOutOfRange => Get(nameof(ValidationMaxRecentPacketsOutOfRange));
        public static string ValidationSignificantDelayThresholdOutOfRange => Get(nameof(ValidationSignificantDelayThresholdOutOfRange));
        public static string ValidationPacketLossDurationOutOfRange => Get(nameof(ValidationPacketLossDurationOutOfRange));
        public static string ValidationPacketLossCountInvalid => Get(nameof(ValidationPacketLossCountInvalid));
        public static string ValidationLossFlapWindowOutOfRange => Get(nameof(ValidationLossFlapWindowOutOfRange));
        public static string ValidationLossFlapCountInvalid => Get(nameof(ValidationLossFlapCountInvalid));
        public static string ValidationLossThresholdsBothZero => Get(nameof(ValidationLossThresholdsBothZero));
        public static string ValidationTargetRequired => Get(nameof(ValidationTargetRequired));
        public static string ValidationTargetNotHttpUrl => Get(nameof(ValidationTargetNotHttpUrl));
        public static string ValidationTargetNotHost => Get(nameof(ValidationTargetNotHost));
        public static string ValidationPortRequired => Get(nameof(ValidationPortRequired));
        public static string ValidationPortIgnored => Get(nameof(ValidationPortIgnored));
        public static string ValidationPayloadSizeOutOfRange => Get(nameof(ValidationPayloadSizeOutOfRange));
        public static string ValidationHttpMethodNotAllowed => Get(nameof(ValidationHttpMethodNotAllowed));
        public static string ValidationExpectedStatusOutOfRange => Get(nameof(ValidationExpectedStatusOutOfRange));

        // ── 配置校验：全局 / 日志 / 通知 ──
        public static string ValidationMissingGlobalSection => Get(nameof(ValidationMissingGlobalSection));
        public static string ValidationUiRefreshIntervalOutOfRange => Get(nameof(ValidationUiRefreshIntervalOutOfRange));
        public static string ValidationAutoConfirmIntervalOutOfRange => Get(nameof(ValidationAutoConfirmIntervalOutOfRange));
        public static string ValidationNotificationCooldownOutOfRange => Get(nameof(ValidationNotificationCooldownOutOfRange));
        public static string ValidationLanguageInvalid => Get(nameof(ValidationLanguageInvalid));
        public static string ValidationMissingLoggingSection => Get(nameof(ValidationMissingLoggingSection));
        public static string ValidationLogFilePathRequired => Get(nameof(ValidationLogFilePathRequired));
        public static string ValidationMaxLogFileSizeOutOfRange => Get(nameof(ValidationMaxLogFileSizeOutOfRange));
        public static string ValidationMaxLogFilesOutOfRange => Get(nameof(ValidationMaxLogFilesOutOfRange));
        public static string ValidationMissingNotificationsSection => Get(nameof(ValidationMissingNotificationsSection));
        public static string ValidationWebhookUrlInvalid => Get(nameof(ValidationWebhookUrlInvalid));
        public static string ValidationWebhookMethodRequired => Get(nameof(ValidationWebhookMethodRequired));
        public static string ValidationWebhookEmptyHeaderKey => Get(nameof(ValidationWebhookEmptyHeaderKey));
        public static string ValidationWebhookAuthTokenRequired => Get(nameof(ValidationWebhookAuthTokenRequired));
        public static string ValidationEmailSmtpServerRequired => Get(nameof(ValidationEmailSmtpServerRequired));
        public static string ValidationEmailPortOutOfRange => Get(nameof(ValidationEmailPortOutOfRange));
        public static string ValidationEmailFromInvalid => Get(nameof(ValidationEmailFromInvalid));
        public static string ValidationEmailToRequired => Get(nameof(ValidationEmailToRequired));
        public static string ValidationEmailToInvalid => Get(nameof(ValidationEmailToInvalid));
        public static string ValidationEmailPasswordRequired => Get(nameof(ValidationEmailPasswordRequired));

        // ── 旧配置迁移 ──
        public static string LegacyConfigEmpty => Get(nameof(LegacyConfigEmpty));
        public static string LegacyLineUnparsable => Get(nameof(LegacyLineUnparsable));

        // ── 日志 ──
        public static string LogFileUnavailable => Get(nameof(LogFileUnavailable));
        public static string LogWriterCrashed => Get(nameof(LogWriterCrashed));

        // ── 探测状态 ──
        public static string StatusUnknown => Get(nameof(StatusUnknown));
        public static string StatusSuccess => Get(nameof(StatusSuccess));
        public static string StatusTimeout => Get(nameof(StatusTimeout));
        public static string StatusNoReply => Get(nameof(StatusNoReply));
        public static string StatusUnreachable => Get(nameof(StatusUnreachable));
        public static string StatusPortClosed => Get(nameof(StatusPortClosed));
        public static string StatusDnsFailure => Get(nameof(StatusDnsFailure));
        public static string StatusTlsFailure => Get(nameof(StatusTlsFailure));
        public static string StatusHttpStatusUnexpected => Get(nameof(StatusHttpStatusUnexpected));
        public static string StatusResponseTimeout => Get(nameof(StatusResponseTimeout));
        public static string StatusProtocolError => Get(nameof(StatusProtocolError));
        public static string StatusLocalError => Get(nameof(StatusLocalError));
        public static string StatusFallback => Get(nameof(StatusFallback));

        // ── ICMP 原始状态 ──
        public static string IpStatusBadDestination => Get(nameof(IpStatusBadDestination));
        public static string IpStatusBadHeader => Get(nameof(IpStatusBadHeader));
        public static string IpStatusBadOption => Get(nameof(IpStatusBadOption));
        public static string IpStatusBadRoute => Get(nameof(IpStatusBadRoute));
        public static string IpStatusDestinationHostUnreachable => Get(nameof(IpStatusDestinationHostUnreachable));
        public static string IpStatusDestinationNetworkUnreachable => Get(nameof(IpStatusDestinationNetworkUnreachable));
        public static string IpStatusDestinationPortUnreachable => Get(nameof(IpStatusDestinationPortUnreachable));
        public static string IpStatusDestinationProhibited => Get(nameof(IpStatusDestinationProhibited));
        public static string IpStatusDestinationScopeMismatch => Get(nameof(IpStatusDestinationScopeMismatch));
        public static string IpStatusDestinationUnreachable => Get(nameof(IpStatusDestinationUnreachable));
        public static string IpStatusHardwareError => Get(nameof(IpStatusHardwareError));
        public static string IpStatusIcmpError => Get(nameof(IpStatusIcmpError));
        public static string IpStatusNoResources => Get(nameof(IpStatusNoResources));
        public static string IpStatusPacketTooBig => Get(nameof(IpStatusPacketTooBig));
        public static string IpStatusParameterProblem => Get(nameof(IpStatusParameterProblem));
        public static string IpStatusSourceQuench => Get(nameof(IpStatusSourceQuench));
        public static string IpStatusSuccess => Get(nameof(IpStatusSuccess));
        public static string IpStatusTimedOut => Get(nameof(IpStatusTimedOut));
        public static string IpStatusTimeExceeded => Get(nameof(IpStatusTimeExceeded));
        public static string IpStatusTtlExpired => Get(nameof(IpStatusTtlExpired));
        public static string IpStatusTtlReassemblyTimeExceeded => Get(nameof(IpStatusTtlReassemblyTimeExceeded));
        public static string IpStatusUnknown => Get(nameof(IpStatusUnknown));
        public static string IpStatusUnrecognizedNextHeader => Get(nameof(IpStatusUnrecognizedNextHeader));
        public static string IpStatusFallback => Get(nameof(IpStatusFallback));

        // ── 套接字错误 ──
        public static string SocketTimedOut => Get(nameof(SocketTimedOut));
        public static string SocketConnectionRefused => Get(nameof(SocketConnectionRefused));
        public static string SocketConnectionReset => Get(nameof(SocketConnectionReset));
        public static string SocketConnectionAborted => Get(nameof(SocketConnectionAborted));
        public static string SocketHostNotFound => Get(nameof(SocketHostNotFound));
        public static string SocketNoData => Get(nameof(SocketNoData));
        public static string SocketNetworkUnreachable => Get(nameof(SocketNetworkUnreachable));
        public static string SocketHostUnreachable => Get(nameof(SocketHostUnreachable));
        public static string SocketNetworkDown => Get(nameof(SocketNetworkDown));
        public static string SocketHostDown => Get(nameof(SocketHostDown));
        public static string SocketAccessDenied => Get(nameof(SocketAccessDenied));
        public static string SocketTooManyOpenSockets => Get(nameof(SocketTooManyOpenSockets));
        public static string SocketNoBufferSpaceAvailable => Get(nameof(SocketNoBufferSpaceAvailable));

        // ── 告警状态机 ──
        public static string EventNone => Get(nameof(EventNone));
        public static string AlarmOutageCount => Get(nameof(AlarmOutageCount));
        public static string AlarmOutageSeconds => Get(nameof(AlarmOutageSeconds));
        public static string AlarmOutageConfirmed => Get(nameof(AlarmOutageConfirmed));
        public static string AlarmFlapping => Get(nameof(AlarmFlapping));
        public static string EventDelaySpike => Get(nameof(EventDelaySpike));
        public static string EventSuccess => Get(nameof(EventSuccess));
        public static string EventFailure => Get(nameof(EventFailure));

        // ── 传输层 ──
        public static string ProbeCanceled => Get(nameof(ProbeCanceled));
        public static string HttpTargetInvalid => Get(nameof(HttpTargetInvalid));
        public static string TransportConnectTimeout => Get(nameof(TransportConnectTimeout));
        public static string TransportResponseTimeout => Get(nameof(TransportResponseTimeout));
        public static string TransportNoEcho => Get(nameof(TransportNoEcho));
        public static string TransportEmptyEcho => Get(nameof(TransportEmptyEcho));
        public static string TransportUdpPortUnreachable => Get(nameof(TransportUdpPortUnreachable));

        // ── UDP 负载 ──
        public static string UdpBase64Empty => Get(nameof(UdpBase64Empty));
        public static string UdpBase64Invalid => Get(nameof(UdpBase64Invalid));
        public static string UdpPayloadEmptyAfterDecode => Get(nameof(UdpPayloadEmptyAfterDecode));
        public static string UdpPayloadTooLarge => Get(nameof(UdpPayloadTooLarge));
        public static string UdpFilePrefixNeedsPath => Get(nameof(UdpFilePrefixNeedsPath));
        public static string UdpFileNotFound => Get(nameof(UdpFileNotFound));
        public static string UdpFileTooLarge => Get(nameof(UdpFileTooLarge));
        public static string UdpFileReadFailed => Get(nameof(UdpFileReadFailed));
        public static string UdpFileEmpty => Get(nameof(UdpFileEmpty));

        // ── 监控编排日志 ──
        public static string MonitorWarningAcknowledged => Get(nameof(MonitorWarningAcknowledged));
        public static string MonitorNotificationsPending => Get(nameof(MonitorNotificationsPending));
        public static string MonitorAlarmLogged => Get(nameof(MonitorAlarmLogged));
        public static string MonitorVerboseStatistics => Get(nameof(MonitorVerboseStatistics));
        public static string MonitorNotificationFailed => Get(nameof(MonitorNotificationFailed));

        // ── 通知 ──
        public static string NotificationWebhookSucceeded => Get(nameof(NotificationWebhookSucceeded));
        public static string NotificationWebhookNonSuccess => Get(nameof(NotificationWebhookNonSuccess));
        public static string NotificationWebhookFailed => Get(nameof(NotificationWebhookFailed));
        public static string NotificationEmailSubject => Get(nameof(NotificationEmailSubject));
        public static string NotificationEmailSent => Get(nameof(NotificationEmailSent));
        public static string NotificationEmailAuthFailed => Get(nameof(NotificationEmailAuthFailed));
        public static string NotificationEmailFailed => Get(nameof(NotificationEmailFailed));
        public static string NotificationAlertTitle => Get(nameof(NotificationAlertTitle));
        public static string NotificationLabelTaskName => Get(nameof(NotificationLabelTaskName));
        public static string NotificationLabelEndpoint => Get(nameof(NotificationLabelEndpoint));
        public static string NotificationLabelProtocol => Get(nameof(NotificationLabelProtocol));
        public static string NotificationLabelStatus => Get(nameof(NotificationLabelStatus));
        public static string NotificationLabelDelay => Get(nameof(NotificationLabelDelay));
        public static string NotificationLabelLossRate => Get(nameof(NotificationLabelLossRate));
        public static string NotificationLabelFailureRate => Get(nameof(NotificationLabelFailureRate));
        public static string NotificationLabelTotalLossRate => Get(nameof(NotificationLabelTotalLossRate));
        public static string NotificationLabelLastEvent => Get(nameof(NotificationLabelLastEvent));
        public static string NotificationLabelTimestamp => Get(nameof(NotificationLabelTimestamp));
        public static string NotificationLineFormat => Get(nameof(NotificationLineFormat));
        public static string NotificationLossRateWithWindow => Get(nameof(NotificationLossRateWithWindow));
        public static string NotificationFailureRateWithWindow => Get(nameof(NotificationFailureRateWithWindow));

        // ── 程序入口 ──
        public static string ProgramArgumentError => Get(nameof(ProgramArgumentError));
        public static string ProgramConfigValidationFailed => Get(nameof(ProgramConfigValidationFailed));
        public static string ProgramStartup => Get(nameof(ProgramStartup));
        public static string ProgramShutdown => Get(nameof(ProgramShutdown));
        public static string ProgramNotificationsEnabled => Get(nameof(ProgramNotificationsEnabled));
        public static string ProgramLegacyDetected => Get(nameof(ProgramLegacyDetected));
        public static string ProgramLegacyGenerated => Get(nameof(ProgramLegacyGenerated));
        public static string ProgramCreateDefaultPrompt => Get(nameof(ProgramCreateDefaultPrompt));
        public static string ProgramDefaultCreated => Get(nameof(ProgramDefaultCreated));
        public static string ProgramUnhandledException => Get(nameof(ProgramUnhandledException));
        public static string ProgramError => Get(nameof(ProgramError));
        public static string ProgramPressAnyKey => Get(nameof(ProgramPressAnyKey));

        // ── 界面 ──
        public static string DateTimeFormat => Get(nameof(DateTimeFormat));
        public static string MonitorTitle => Get(nameof(MonitorTitle));
        public static string MonitorColumnName => Get(nameof(MonitorColumnName));
        public static string MonitorColumnTarget => Get(nameof(MonitorColumnTarget));
        public static string MonitorColumnStatus => Get(nameof(MonitorColumnStatus));
        public static string MonitorColumnDelay => Get(nameof(MonitorColumnDelay));
        public static string MonitorColumnLoss => Get(nameof(MonitorColumnLoss));
        public static string MonitorColumnLog => Get(nameof(MonitorColumnLog));
        public static string MonitorNoTasks => Get(nameof(MonitorNoTasks));
        public static string MonitorCaptionAcknowledge => Get(nameof(MonitorCaptionAcknowledge));
        public static string MonitorHiddenTargets => Get(nameof(MonitorHiddenTargets));
        public static string MonitorColumnsTrimmed => Get(nameof(MonitorColumnsTrimmed));
        public static string MonitorDroppedWarnings => Get(nameof(MonitorDroppedWarnings));
    }
}
