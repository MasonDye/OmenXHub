// Strings.cs - 多语言字符串资源管理
// 提供简体中文、繁体中文、英文三种语言的UI字符串，支持运行时切换
using System;

namespace OmenSuperHub {
  public enum AppLanguage {
    SimplifiedChinese,
    TraditionalChinese,
    English
  }

  public static class Strings {
    public static event Action OnLanguageChanged;
    public static AppLanguage Current = AppLanguage.SimplifiedChinese;

    private static string T(string zh, string tw, string en) {
      switch (Current) {
        case AppLanguage.TraditionalChinese: return tw;
        case AppLanguage.English: return en;
        default: return zh;
      }
    }

    public static void SetLanguage(AppLanguage lang) {
      Current = lang;
      OnLanguageChanged?.Invoke();
    }

    // Product support
    // Menus
    public static string FanControl => T("风扇控制", "風扇控制", "Fan Control");
    public static string PerfControl => T("性能控制", "效能控制", "Performance");
    public static string PowerStatus => T("电源状态", "電源狀態", "Power Status");
    public static string Help => T("帮助", "說明", "Help");
    public static string Exit => T("退出", "結束", "Exit");
    public static string LanguageMenu => T("语言", "語言", "Language");
	    public static string LangSimplified => T("简体中文", "简体中文", "简体中文");
	    public static string LangTraditional => T("繁體中文", "繁體中文", "繁體中文");
	    public static string LangEnglish => T("English", "English", "English");
	    public static string LangRestartHint => T("💡 切换语言后需重启程序生效", "💡 切換語言後需重啟程式生效", "💡 Restart required after changing language");
    public static string Hint => T("提示", "提示", "Info");
    public static string Error => T("错误", "錯誤", "Error");

    // Presets
    public static string PresetExtreme => T("极致性能", "極致性能", "Extreme Performance");
    public static string PresetGpuPriority => T("GPU优先", "GPU優先", "GPU Priority");
    public static string PresetLightUse => T("轻度使用", "輕度使用", "Light Use");
    public static string RenamePreset => T("重命名", "重新命名", "Rename");
    public static string RenamePresetTitle => T("重命名预设", "重新命名預設", "Rename Preset");
    public static string RenamePresetPrompt => T("请输入新的预设名称：", "請輸入新的預設名稱：", "Please enter new preset name:");
    public static string RenamePresetError => T("预设名称不能为空，且不能与其他预设同名。", "預設名稱不能為空，且不能與其他預設同名。", "Preset name cannot be empty and must be unique.");
    // Fan
    public static string FanSilentMode => T("安静模式", "安靜模式", "Silent Mode");
    public static string FanCoolMode => T("降温模式", "降溫模式", "Cool Mode");
    public static string FanRespRealtime => T("实时", "即時", "Realtime");
    public static string FanRespHigh => T("高", "高", "High");
    public static string FanRespMedium => T("中", "中", "Medium");
    public static string FanRespLow => T("低", "低", "Low");
    public static string FanCustomCurve => T("自定义曲线", "自訂曲線", "Custom Curve");
    public static string FanManualMode => T("手动模式", "手動模式", "Manual Mode");
    public static string FanModePerformance => T("狂暴模式", "狂暴模式", "Performance Mode");
    public static string FanModeDefault => T("平衡模式", "平衡模式", "Default Mode");
    public static string GpuClockReset => T("GPU 频率已重置", "GPU 頻率已重置", "GPU Clock Reset");
    public static string FanLabel => T("风扇: ", "風扇: ", "Fan: ");
    public static string PowerLabel => T("功耗 ", "功耗 ", "Power ");
    public static string UtilizationLabel => T("利用率 ", "使用率 ", "Utilization ");
    public static string ClockLabel => T("频率 ", "频率 ", "Clock ");
    public static string GpuMonitorOff => T("GPU监控已关闭", "GPU監控已關閉", "GPU Monitor Off");
    public static string CpuMonitorOff => T("CPU监控已关闭", "CPU監控已關閉", "CPU Monitor Off");
    public static string FanAutoProtect => T("高温自动保护", "高溫自動保護", "High-Temp Auto-Protect");
    // Clean Creek
    public static string CleanCreekTitle => T("风扇除尘", "風扇除塵", "Fan Dust Removal");
    public static string CleanCreekConfirmMessage => T(
        "即将开始反转除尘。点击确定开始，要停止除尘请选择「取消」。",
        "即將開始反轉除塵。點擊確定開始，要停止除塵請選擇「取消」。",
        "Reverse dust removal will start soon. Click OK to start, or Cancel to stop.");
    // Performance
    public static string GraphicsMode => T("图形模式", "圖形模式", "Graphics Mode");
    public static string GfxDiscreteMode => T("独显直连", "獨顯直連", "Discrete GPU");
    public static string GfxHybridMode => T("混合模式", "混合模式", "Hybrid Mode");
    public static string GfxUMAConfirm => T("仅集成显卡启用，屏蔽独显，该模式下 HDMI 输出将无法工作。确定切换吗?",
        "僅整合顯示啟用，遮蔽獨顯，此模式下 HDMI 輸出將無法運作。確定切換嗎？",
        "Only iGPU will be active. HDMI output will not work in UMA mode. Confirm switch?");
    public static string GfxUMATitle => T("切换到UMA模式", "切換至UMA模式", "Switch to UMA Mode");
    public static string GfxSwitchedTo(string mode) => T(
        $"已切换到{mode}模式，重启生效。", $"已切換至{mode}模式，重啟生效。", $"Switched to {mode} mode. Reboot to apply.");
    public static string SetTppSlider => T("拖动滑块设置功率 (W)", "拖動滑桿設定功率 (W)", "Drag slider to set power (W)");
    public static string NotSet => T("不设置", "不設定", "Not Set");
    public static string Maximum => T("最大", "最大", "Maximum");
    public static string Enable => T("开启", "開啟", "Enable");
    public static string Disable => T("关闭", "關閉", "Disable");
    public static string Normal => T("正常", "正常", "Normal");
    public static string LowPower => T("低功耗", "低功耗", "Low Power");
    public static string Unlimited => T("无限制", "無限制", "Unlimited");
    public static string GpuAppsMenu => T("占用GPU的程序", "佔用GPU的程式", "GPU Processes");
    public static string GpuRestartMenu => T("重启显卡", "重啟顯示卡", "Restart GPU");
    public static string DbUnlockTitle => T("解锁DB", "解鎖DB", "Unlock DB");
    public static string PleaseConnectAC => T("请连接交流电源", "請連接交流電源", "Please connect AC power");
    public static string DriverNotAllow => T("当前驱动版本不满足需求，无法执行此操作。当前驱动版本：",
        "當前驅動版本不滿足需求，無法執行此操作。目前驅動版本：",
        "Driver version does not meet requirements. Current version: ");
    // High Temp Balloon
    // Performance tooltips
    public static string PerfTppTip => T("💡改变Ppab/DB增益点，即 GPU 功率在 CPU 功率低于多少时获得额外的Ppab/DB功耗。",
        "💡改變Ppab/DB增益點，即 GPU 功率在 CPU 功率低於多少時獲得額外的Ppab/DB功耗。",
        "💡 Adjusts the Ppab/DB gain point: the CPU power threshold below which GPU gets additional Ppab/DB power.");
    public static string PerfDbUnlockWarning => T("\n警告：一旦解锁DB，只能通过安装一次显卡驱动恢复到原始状态，确认继续吗？",
        "\n警告：一旦解鎖DB，只能透過安裝一次顯示卡驅動恢復到原始狀態，確認繼續嗎？",
        "\nWarning: Once DB is unlocked, you can only restore to original state by reinstalling graphics driver once. Continue?");
    public static string KeyboardConnectFail => T("键盘连接失败！", "鍵盤連線失敗！", "Keyboard connection failed!");
    public static string DdsInitFail => T(
        "无法初始化 Advanced Optimus 小程序。请确保所有NVIDIA驱动程序均为最新版本，并将BIOS设置菜单中的“图形模式”设置为\"Nvidia Advanced Optimus\"。",
        "無法初始化 Advanced Optimus 小程式。請確認所有 NVIDIA 驅動程式均為最新版本，並在 BIOS 設定中將「圖形模式」設為 \"Nvidia Advanced Optimus\"。",
        "Failed to initialize the Advanced Optimus applet. Make sure all NVIDIA drivers are up to date and set the Graphics Mode to \"Nvidia Advanced Optimus\" in BIOS.");
    public static string HelpTabUpdates => T("更新说明", "更新說明", "Changelog");
    public static string HelpTabFanConfig => T("风扇配置", "風扇配置", "Fan Config");
    public static string HelpTabFanControl => T("风扇控制", "風扇控制", "Fan Control");
    public static string HelpTabPerformance => T("性能控制", "效能控制", "Performance");
    public static string HelpTabOther => T("其他", "其他", "Other");
    public static string HelpTabCredits => T("致谢", "致謝", "Credits");
    public static string HelpBtnGitHub => T("GitHub", "GitHub", "GitHub");
    public static string HelpBtnCheckUpdate => T("检查更新", "檢查更新", "Check Updates");
    public static string HelpBtnIAgree => T("我已阅读", "我已閱讀", "I've Read");
    public static string HelpFanConfigSub => T("风扇配置说明", "風扇配置說明", "Fan Config Guide");
    public static string HelpFanControlSub => T("风扇控制说明", "風扇控制說明", "Fan Control Guide");
    public static string HelpPerformanceSub => T("性能控制说明", "效能控制說明", "Performance Guide");
    public static string HelpOtherSub => T("其他说明", "其他說明", "Other Guide");
    public static string HelpCreditsSub => T("开发者 & 致谢", "開發者 & 致謝", "Developers & Credits");

    // Monitor
    public static string MonitorCpuOn => T("开启CPU监控", "開啟CPU監控", "Enable CPU Monitor");
    public static string MonitorCpuOff => T("关闭CPU监控", "關閉CPU監控", "Disable CPU Monitor");
    public static string MonitorGpuOn => T("开启GPU监控", "開啟GPU監控", "Enable GPU Monitor");
    public static string MonitorGpuOff => T("关闭GPU监控", "關閉GPU監控", "Disable GPU Monitor");
    public static string MonitorFanOn => T("开启风扇监控", "開啟風扇監控", "Enable Fan Monitor");
    public static string MonitorFanOff => T("关闭风扇监控", "關閉風扇監控", "Disable Fan Monitor");
    public static string MonitorMemoryOn => T("开启运存监控", "開啟運存監控", "Enable Memory Monitor");
    public static string MonitorMemoryOff => T("关闭运存监控", "關閉運存監控", "Disable Memory Monitor");
    public static string MonitorNetworkOn => T("开启网速监控", "開啟網速監控", "Enable Network Monitor");
    public static string MonitorNetworkOff => T("关闭网速监控", "關閉網速監控", "Disable Network Monitor");
    public static string MonitorFpsOn => T("开启FPS监控", "開啓FPS監控", "Enable FPS Monitor");
    public static string MonitorFpsOff => T("关闭FPS监控", "關閉FPS監控", "Disable FPS Monitor");
    public static string MonitorRefresh => T("刷新频率", "更新頻率", "Refresh Rate");
    public static string MonitorRefreshHigh => T("高", "高", "High");
    public static string MonitorRefreshLow => T("低", "低", "Low");
    public static string MonitorClosed => T("监控已关闭", "監控已關閉", "Monitor Disabled");
    public static string MonitorPrepareLabel => T("准备中...", "準備中...", "Preparing...");
    public static string MonitorAutoFanWarning => T("当前为自动转速模式，若要关闭监控需切换为其他转速控制模式。",
        "目前為自動轉速模式，若要關閉監控需切換為其他轉速控制模式。",
        "Fan is in auto mode. Switch to another fan control mode before disabling monitoring.");

    // Floating
    public static string FloatingLocLeft => T("左上角", "左上角", "Top Left");
    public static string FloatingLocRight => T("右上角", "右上角", "Top Right");
    public static string FloatingLocFree => T("自由", "自由", "Free");
    public static string FloatingLocTopCenter => T("上方居中", "上方居中", "Top Center");
    public static string FloatLayoutHeading => T("浮窗布局", "浮窗佈局", "Float Layout");
    public static string FloatLayoutRow => T("按行排列", "按行排列", "Horizontal");
    public static string FloatLayoutCol => T("按列排列", "按列排列", "Vertical");
    public static string FormatScreenLabel(int index, string deviceName) => T(
      $"显示器 {index} ({deviceName})",
      $"顯示器 {index} ({deviceName})",
      $"Display {index} ({deviceName})");
    // Omen Key
    public static string OmenKeyNone => T("取消绑定", "取消綁定", "Unbound");
    public static string OmenKeyShowMain => T("显示主界面", "顯示主界面", "Show Main Window");
    public static string OmenKeyCycle => T("循环预设", "循環預設", "Cycle Presets");
    public static string OmenKeyLaunchApp => T("打开应用", "開啟應用程式", "Open App");
    public static string OmenKeyNoAppSelected => T("未选择应用", "未選擇應用", "No App Selected");
    public static string OmenKeyPresetCandidates => T("候选预设", "候選預設", "Preset Candidates");
    // Settings
    public static string IconOriginal => T("原版", "原版", "Default");
    public static string IconCustom => T("自定义图标", "自訂圖示", "Custom Icon");
    public static string IconDynamic => T("动态图标", "動態圖示", "Dynamic Icon");
    // Lighting
    public static string LightingControl => T("灯光控制", "燈光控制", "Lighting Control");
    public static string LightingBrightness => T("亮度", "亮度", "Brightness");
    public static string LightingCustom => T("自定义...", "自訂...", "Custom...");
    public static string LightingAnimation => T("动画效果", "動畫效果", "Animation");
    public static string LightingDirection => T("方向", "方向", "Direction");
    public static string LightingTheme => T("主题", "主題", "Theme");
    public static string LightingAnimNone => T("无", "無", "None");
    public static string LightingAnimColorCycle => T("颜色循环", "顏色循環", "Color Cycle");
    public static string LightingAnimStarlight => T("星光", "星光", "Starlight");
    public static string LightingAnimBreathing => T("呼吸", "呼吸", "Breathing");
    public static string LightingAnimWave => T("波浪", "波浪", "Wave");
    public static string LightingAnimRaindrop => T("雨滴", "雨滴", "Raindrop");
    public static string LightingAnimAudioPulse => T("音频脉冲", "音頻脈衝", "Audio Pulse");
    public static string LightingAnimConfetti => T("五彩纸屑", "五彩紙屑", "Confetti");
    public static string LightingAnimSun => T("太阳", "太陽", "Sun");
    public static string LightingAnimSwipe => T("划过", "劃過", "Swipe");
    public static string LightingSpeedSlow => T("慢", "慢", "Slow");
    public static string LightingSpeedMedium => T("中", "中", "Medium");
    public static string LightingSpeedFast => T("快", "快", "Fast");
    public static string LightingColorRed => T("红色", "紅色", "Red");
    public static string LightingColorGreen => T("绿色", "綠色", "Green");
    public static string LightingColorBlue => T("蓝色", "藍色", "Blue");
    public static string LightingColorWhite => T("白色", "白色", "White");
    public static string LightingColorCyan => T("冰蓝", "冰藍", "Cyan");
    public static string LightingColorMagenta => T("粉色", "粉色", "Pink");
    public static string LightingColorYellow => T("黄色", "黃色", "Yellow");
    // Per-key RGB
    public static string LightingPerKeyTitle => T("单键 RGB（测试功能）", "單鍵 RGB（測試功能）", "Per-Key RGB (Experimental)");
    public static string LightingPerKeyStaticColor => T("静态颜色", "靜態顏色", "Static Color");
    public static string LightingPerKeyAnimation => T("动画效果", "動畫效果", "Animation");
    public static string LightingPerKeyBrightness => T("亮度", "亮度", "Brightness");
    // Dojo specific
    public static string LightingDirLeft => T("左/逆时针", "左/逆時針", "Left/Counterclockwise");
    public static string LightingDirRight => T("右/顺时针", "右/順時針", "Right/Clockwise");
    public static string LightingThemeGalaxy => T("银河", "銀河", "Galaxy");
    public static string LightingThemeVolcano => T("火山", "火山", "Volcano");
    public static string LightingThemeJungle => T("丛林", "叢林", "Jungle");
    public static string LightingThemeOcean => T("海洋", "海洋", "Ocean");
    public static string LightingThemeCustom => T("自定义", "自訂", "Custom");
    public static string LightingLightBar => T("灯条（测试功能）", "燈條（測試功能）", "Light Bar (Experimental)");
    // ponytail: capability-mismatch warnings surfaced by ApplyLightBtn_Click instead of silent drop
    public static string LightingCapabilityAnimBasic => T("当前协议（四分区 Basic）仅支持「星光」「波浪」两种动画，请改用 Dojo 协议或选择「无」", "當前協議（四分割區 Basic）僅支援「星光」「波浪」兩種動畫，請改用 Dojo 協議或選擇「無」", "The Basic 4-Zone protocol only supports Starlight and Wave animations. Switch to Dojo or pick None.");
    public static string LightingCapabilityAnimHpSdk => T("HP SDK 协议仅支持静态颜色，请改用 Dojo 或四分区协议，或将动画设为「无」", "HP SDK 協議僅支援靜態顏色，請改用 Dojo 或四分割區協議，或將動畫設為「無」", "The HP SDK protocol only supports static color. Switch to Dojo or Basic, or set animation to None.");
    // ponytail: PerKey connection / capability warnings surfaced by LightingPage PerKey handlers
    public static string LightingCapabilityPerKeyConnect => T("未检测到单键 RGB 设备，请确认 OMEN 中心服务可用并尝试重新打开本页", "未偵測到單鍵 RGB 設備，請確認 OMEN 中心服務可用並嘗試重新開啟本頁", "Per-key RGB device not detected. Make sure the OMEN Gaming Hub service is running, then reopen this page.");
    // ponytail: PerKey Apply 按钮独有动作 = StorePerKeyToFlash,冷启动保留当前 RGB 设置
    public static string LightingPerKeyFlashSaved => T("已保存到闪存（冷启动生效）", "已儲存到閃存（冷開機生效）", "Saved to flash (applies on cold boot)");
    // ponytail: Dojo firmware accepts brightness > 100 — 128 / 228 are the documented extra-bright presets
    // (see existing LightingBrightnessRangeTip "228开"). Buttons shown only under Dojo protocol.
    public static string LightingBrightnessHigh => T("超亮 128%", "超亮 128%", "Extra Bright 128%");
    public static string LightingBrightnessMax => T("最亮 228%", "最亮 228%", "Max Bright 228%");
    // Keyboard types
    public static string KbTypeNormal => T("普通", "普通", "Normal");
    public static string KbTypeFourZoneWithNumpad => T("四分区带小键盘", "四分割區帶數字鍵", "4-Zone with Numpad");
    public static string KbTypeFourZoneWithoutNumpad => T("四分区无小键盘", "四分割區無數字鍵", "4-Zone without Numpad");
    public static string KbTypeRgbPerKey => T("单键 RGB", "單鍵 RGB", "Per-Key RGB");
    public static string KbTypeOneZoneWithNumpad => T("单分区带小键盘", "單分割區帶數字鍵", "1-Zone with Numpad");
    public static string KbTypeOneZoneWithoutNumpad => T("单分区无小键盘", "單分割區無數字鍵", "1-Zone without Numpad");
    public static string KbTypeUnknown => T("未知或不支持", "未知或不支援", "Unknown/Unsupported");

    // System Info
    public static string SysManufacturer => T("品牌", "品牌", "Manufacturer");
    public static string SysModel => T("型号", "型號", "Model");
    public static string SysBiosVersion => T("BIOS 版本", "BIOS 版本", "BIOS Version");
    public static string SysCpuModel => T("CPU 型号", "CPU 型號", "CPU Model");
    public static string SysGpuList => T("GPU 列表", "GPU 列表", "GPU List");
    public static string SysAdapterPower => T("适配器功率", "適配器功率", "Adapter Power");
    public static string SysCPUTemp => T("CPU 温度", "CPU 溫度", "CPU Temp");
    public static string SysGPUTemp => T("GPU 温度", "GPU 溫度", "GPU Temp");
    public static string SysUnknown => T("未知", "未知", "Unknown");
    public static string SysIRSensor => T("IR传感器", "IR感測器", "IR Sensor");
    public static string SysAmbient => T("环境传感器", "環境感測器", "Ambient Sensor");
    public static string SysPCH => T("PCH传感器", "PCH感測器", "PCH Sensor");
    public static string SysVR => T("VR传感器", "VR感測器", "VR Sensor");
    public static string SysPawnInstalled => T("PawnIO 驱动已安装", "PawnIO 驅動已安裝", "PawnIO Driver Installed");
    public static string SysPawnMissing => T("PawnIO 驱动未安装", "PawnIO 驅動未安裝", "PawnIO Driver Not Installed");
    public static string SysPawnTitle => T("PawnIO 驱动", "PawnIO 驅動", "PawnIO Driver");
    public static string SysKbType => T("键盘灯光类型", "鍵盤燈光類型", "KB Light Type");
    public static string SysModelValidation => T("机型支持情况", "機型支持情況", "Product Validation");
    public static string ValidationGamingProduct => T("完全支持", "完全支持", "Fully supported");
    public static string ValidationUnsupported => T("不支持的机型", "不支援的機型", "Unsupported Product");
    public static string SysBoardProduct => T("主板产品号", "主機板型號", "Board Product");
    public static string SysCpuTjMax => T("CPU温度墙", "CPU溫度上限", "CPU Tjmax");
    public static string SysNvidiaTjMax => T("NVIDIA 温度墙", "NVIDIA 溫度上限", "NVIDIA Tjmax");
    public static string SysNvidiaPowerLimitText(string limitsText) => T(
        $"NVIDIA 功率限制: {limitsText}",
        $"NVIDIA 功率限制: {limitsText}",
        $"NVIDIA Power Limit: {limitsText}");

    // Custom presets
    public static string CustomRename => T("重命名", "重新命名", "Rename");
    // Sidebar
    public static string SidebarDashboard => T("总览", "總覽", "Dashboard");
    public static string SidebarFan => T("风扇", "風扇", "Fan");
    public static string SidebarPerf => T("性能", "效能", "Performance");
    public static string SidebarLighting => T("灯光", "燈光", "Lighting");
    public static string SidebarSettings => T("设置", "設定", "Settings");
    public static string SidebarOther => T("其他", "其他", "Other");

    // Page titles (title bar)
    public static string PageDashboard => T("总览", "總覽", "Dashboard");
    public static string PageFan => T("风扇控制", "風扇控制", "Fan Control");
    public static string PagePerf => T("性能控制", "效能控制", "Performance Control");
    public static string PageLighting => T("灯光", "燈光", "Lighting");
    public static string PageAutomation => T("自动化", "自動化", "Automation");
    public static string PageOther => T("其他设置", "其他設定", "Other Settings");
    public static string PageSettings => T("设置", "設定", "Settings");

    // GPU auto-stop
    public static string MonitorCpuLabel => T("CPU", "CPU", "CPU");
    public static string MonitorGpuLabel => T("GPU", "GPU", "GPU");
    // Power status
    public static string PowerStatusAC => T("交流电源", "交流電源", "AC Power");
    public static string PowerStatusDC => T("电池", "電池", "Battery");

    // Fan page headings & labels
    public static string FanConfigHeading => T("风扇配置", "風扇配置", "Fan Config");
    public static string FanCurveHeading => T("自定义曲线", "自訂曲線", "Custom Curve");
    public static string TempSensitivityHeading => T("温度灵敏度", "溫度靈敏度", "Temp Sensitivity");
    public static string CleanCreekHeading => T("风扇除尘", "風扇除塵", "Fan Dust Removal");
    public static string FanCurveCPULabel => T("CPU 曲线", "CPU 曲線", "CPU Curve");
    public static string FanCurveGPULabel => T("GPU 曲线", "GPU 曲線", "GPU Curve");
    public static string FanCurveTip => T("拖拽控制点调整不同温度下的风扇转速", "拖拽控制點調整不同溫度下的風扇轉速", "Drag points to adjust fan speed at different temperatures");
    public static string FanCurveImport => T("导入", "匯入", "Import");
    public static string FanCurveExport => T("导出", "匯出", "Export");
    public static string FanCurveShare => T("分享", "分享", "Share");
    public static string FanCurveImportTitle => T("导入风扇曲线", "匯入風扇曲線", "Import Fan Curve");
    public static string FanCurveExportTitle => T("导出风扇曲线", "匯出風扇曲線", "Export Fan Curve");
    public static string FanCurveFileFilter => T("风扇曲线文件 (*.json)|*.json|所有文件 (*.*)|*.*", "風扇曲線檔案 (*.json)|*.json|所有檔案 (*.*)|*.*", "Fan Curve Files (*.json)|*.json|All Files (*.*)|*.*");
    public static string FanCurveImportSuccess => T("导入成功: ", "匯入成功: ", "Import success: ");
    public static string FanCurveImportFailed => T("导入失败：文件格式不正确或曲线数据无效", "匯入失敗：檔案格式不正確或曲線資料無效", "Import failed: invalid file format or curve data");
    public static string FanCurveExportSuccess => T("曲线已导出", "曲線已匯出", "Curve exported");
    public static string FanCurveExportFailed => T("导出失败", "匯出失敗", "Export failed");
    public static string FanCurveShareCopied => T("分享码已复制到剪贴板！", "分享碼已複製到剪貼簿！", "Share code copied to clipboard!");
    public static string FanCurveShareGuide => T("将分享码发送给朋友，对方可通过「导入」→粘贴分享码来加载曲线", "將分享碼發送給朋友，對方可透過「匯入」→貼上分享碼來載入曲線", "Send the code to a friend. They can load it via Import → paste share code");
    public static string DustCleanDesc => T("反转风扇清除内部灰尘", "反轉風扇清除內部灰塵", "Reverse fans to clean internal dust");
    public static string CleanCreekStartBtn => T("开始除尘 (30秒)", "開始除塵 (30秒)", "Start Cleaning (30s)");
    public static string AutoFanProtectDesc => T("CPU温度>95°C且固定转速时强制切换为降温曲线", "CPU溫度>95°C且固定轉速時強制切換為降溫曲線", "Forces cool curve when CPU >95°C with fixed fan speed");
    public static string FanSync => T("风扇一致性", "風扇一致性", "Fan Consistency");
    public static string FanSyncDesc => T("所有风扇转速与CPU风扇保持一致", "所有風扇轉速與CPU風扇保持一致", "Keep all fan speeds synchronized with CPU fan");
    public static string FanSmartSettings => T("曲线温度设置", "曲線溫度設置", "Curve Temperature Settings");
    public static string FanSmartEmaAlpha => T("温度平滑系数", "溫度平滑係數", "Temp Smoothing (EMA)");
    public static string FanSmartStepDown => T("降速保护 (RPM/s)", "降速保護 (RPM/s)", "Step-Down Rate (RPM/s)");
    public static string FanSmartHysteresis => T("滞后死区 (°C)", "滯後死區 (°C)", "Hysteresis (°C)");
    public static string FanSmartEmaHint => T("值越小响应越灵敏", "值越小響應越靈敏", "Smaller = smoother but slower");
    public static string FanSmartStepDownHint => T("每秒最多下降 RPM", "每秒最多下降 RPM", "Max RPM drop per second");
    public static string FanSmartHysteresisHint => T("温度变化阈值(°C)", "溫度變化閾值(°C)", "Temperature threshold (°C)");

    // Performance page headings
    public static string DbVersionHeading => T("DB 版本", "DB 版本", "DB Version");
    public static string CpuPowerHeading => T("CPU 功率", "CPU 功率", "CPU Power");
    public static string CpuPowerPL1 => T("PL1", "PL1", "PL1");
    public static string CpuPowerPL2 => T("PL2", "PL2", "PL2");
    public static string GpuClockHeading => T("GPU 频率限制", "GPU 頻率限制", "GPU Clock Limit");
    public static string GpuCoreOverclockHeading => T("GPU 核心超频", "GPU 核心超頻", "GPU Core Overclock");
    public static string GpuMemoryOverclockHeading => T("GPU 显存超频", "GPU 記憶體超頻", "GPU Memory Overclock");
    public static string MaxFrameRateHeading => T("最大帧率", "最大幀率", "Max Frame Rate");
    public static string MaxFrameRateNote => T("注：需要 NVIDIA 显卡支持", "註：需要 NVIDIA 顯示卡支援", "Note: Requires NVIDIA GPU");
    public static string RefreshRateHeading => T("屏幕刷新率", "屏幕刷新率", "Screen Refresh Rate");
    public static string RefreshRateNote => T("注：需要显示器支持", "註：需要顯示器支援", "Note: Requires monitor support");
    public static string ResolutionHeading => T("屏幕分辨率", "螢幕解析度", "Screen Resolution");
    public static string PerfResolutionDesc => T("切换显示器分辨率", "切換顯示器解析度", "Switch display resolution");
    public static string DpiScaleHeading => T("DPI 缩放", "DPI 縮放", "DPI Scale");
    public static string PerfDpiDesc => T("调整系统 DPI 缩放比例", "調整系統 DPI 縮放比例", "Adjust system DPI scale");
    public static string HdrHeading => T("HDR", "HDR", "HDR");
    public static string PerfHdrDesc => T("高动态范围显示", "高動態範圍顯示", "High Dynamic Range");
    public static string TurnOffDisplayHeading => T("关闭显示器", "關閉顯示器", "Turn Off Display");
    public static string PerfTurnOffDisplayDesc => T("关闭屏幕显示", "關閉螢幕顯示", "Turn off screen display");
    public static string TurnOffDisplayBtn => T("关闭", "關閉", "Turn Off");
    public static string PowerPlanHeading => T("电源计划", "電源計劃", "Power Plan");
    public static string PowerModeHeading => T("电源模式", "電源模式", "Power Mode");
    public static string PowerModeEfficiency => T("最佳能效", "最佳能效", "Best Power Efficiency");
    public static string PowerModeBalanced => T("平衡", "平衡", "Balanced");
    public static string PowerModePerformance => T("最佳性能", "最佳性能", "Best Performance");
    public static string HotSwitchHeading => T("热切换", "熱切換", "Hot Switch");
    public static string HotSwitchDesc => T("在集显与独显之间动态切换，无需重启", "在集顯與獨顯之間動態切換，無需重啟", "Dynamically switch between iGPU and dGPU");
    public static string GfxUMALabel => T("UMA 仅集成显卡", "UMA 僅整合顯示卡", "UMA iGPU Only");
    public static string PpabCheckLabel => T("启用 PPAB (Dynamic Boost)", "啟用 PPAB (Dynamic Boost)", "Enable PPAB (Dynamic Boost)");
    public static string DStateHeading => T("dState (GPU 功耗状态)", "dState (GPU 功耗狀態)", "dState (GPU Power State)");
    public static string IccMaxHeading => T("IccMax (CPU 电流限制)", "IccMax (CPU 電流限制)", "IccMax (CPU Current Limit)");
    public static string AcLoadLineHeading => T("AC Load line（负载线校准）", "AC Load line（負載線校準）", "AC Load Line");

    // Combo items
    public static string GpuClockRestore => T("还原", "還原", "Restore Default");
    // Lighting page
    public static string LightingDeviceHeading => T("设备", "設備", "Device");
    public static string LightingKeyboard => T("键盘", "鍵盤", "Keyboard");
    public static string LightingProtocolHeading => T("控制协议", "控制協定", "Protocol");
    public static string LightingZoneColorHeading => T("分区颜色", "分割區顏色", "Zone Color");
    public static string ApplyLightingBtn => T("应用灯光设置", "應用燈光設定", "Apply Lighting");
    public static string LightingSpeedHeading => T("速度", "速度", "Speed");

    // SysInfo / Monitor page headings
    public static string SysInfoHeading => T("系统信息", "系統資訊", "System Information");
    public static string SensorTempsHeading => T("传感器温度", "感測器溫度", "Sensor Temps");
    public static string HwMonitorHeading => T("硬件监控", "硬體監控", "Hardware Monitor");

    // Settings page
    public static string FloatingHeading => T("浮窗显示", "浮窗顯示", "Overlay");
    public static string DisplayHeading => T("显示器选择 (多选)", "顯示器選擇 (多選)", "Monitor Selection");
    public static string FontSizeHeading => T("字体大小", "字體大小", "Font Size");
    public static string PositionHeading => T("位置", "位置", "Position");
    public static string TextOpacityHeading => T("文字透明度", "文字透明度", "Text Opacity");
    public static string OmenKeyHeading => T("Omen 键", "Omen 鍵", "Omen Key");
    public static string TrayIconHeading => T("托盘图标", "托盤圖示", "Tray Icon");
    public static string AutoStartHeading => T("开机自启", "開機自啟", "Autostart");
    public static string AutoStartDesc => T("通过 Task Scheduler 设置开机自启动", "通過 Task Scheduler 設定開機自啟動", "Set autostart via Task Scheduler");
    public static string DataLocalizeHeading => T("数据本地化", "資料本地化", "Data Localize");
    public static string DataLocalizeDesc => T("开启后所有配置仅保存在本地注册表", "開啟後所有設定僅儲存在本地登錄檔", "All config stored locally in registry");
    public static string ThemeHeading => T("主题", "主題", "Theme");
    public static string ThemeSystem => T("跟随系统", "跟隨系統", "System");
    public static string ThemeDark => T("深色", "深色", "Dark");
    public static string ThemeLight => T("亮色", "亮色", "Light");
    public static string DebugLogHeading => T("调试日志", "調試日誌", "Debug Log");
    public static string DebugLogDesc => T("开启后实时记录所有WMI操作到OmenXHub.log", "開啟後即時記錄所有WMI操作到OmenXHub.log", "Log all WMI operations to OmenXHub.log");
    public static string WindowTitle => T("OMEN X Hub 控制面板", "OMEN X Hub 控制面板", "OMEN X Hub Control Panel");
    public static string OmenKeyCustomLabel => T("切换浮窗显示", "切換浮窗顯示", "Toggle Overlay");
    // Error messages
    public static string CleanCreekUnsupported => T("当前设备不支持反转除尘功能", "目前裝置不支援反轉除塵功能", "This device does not support reverse dust removal");

    // Automation
    public static string SidebarAutomation => T("自动化", "自動化", "Automation");
    public static string AutomationHeading => T("自动化控制", "自動化控制", "Automation");
    public static string AutomationQuickActions => T("快捷操作", "快捷操作", "Quick Actions");
    public static string AutomationAddPipeline => T("添加管道", "添加管道", "Add Pipeline");
    public static string AutomationEditPipeline => T("编辑管道", "編輯管道", "Edit Pipeline");
    public static string AutomationTriggerType => T("触发类型", "觸發類型", "Trigger Type");
    public static string AutomationTriggerValue => T("触发值", "觸發值", "Trigger Value");
    public static string AutomationStepType => T("步骤类型", "步驟類型", "Step Type");
    public static string AutomationStepValue => T("步骤值", "步驟值", "Step Value");
    public static string AutomationRefreshRate => T("刷新率 (Hz)", "刷新率 (Hz)", "Refresh Rate (Hz)");
    public static string AutomationPowerPlanGuid => T("电源计划 GUID", "電源計劃 GUID", "Power Plan GUID");
    public static string AutomationMaxFrameRate => T("最大帧率 (FPS)", "最大幀率 (FPS)", "Max Frame Rate (FPS)");
    public static string AutomationCpuPowerValue => T("CPU功率 (W / max)", "CPU功率 (W / max)", "CPU Power (W / max)");
    public static string AutomationDelayMs => T("延迟(毫秒)", "延遲(毫秒)", "Delay (ms)");
    public static string AutomationPipelineName => T("管道名称", "管道名稱", "Pipeline Name");
    public static string AutomationSave => T("保存", "保存", "Save");
    public static string AutomationCancel => T("取消", "取消", "Cancel");
    public static string AutomationAddStep => T("添加步骤", "添加步驟", "Add Step");
    public static string AutomationTriggerProcessStart => T("进程启动", "進程啟動", "Process Start");
    public static string AutomationTriggerProcessStop => T("进程停止", "進程停止", "Process Stop");
    public static string AutomationTriggerPowerAC => T("接入电源", "接入電源", "AC Power On");
    public static string AutomationTriggerPowerDC => T("断开电源", "斷開電源", "AC Power Off");
    public static string AutomationTriggerStartup => T("程序启动", "程式啟動", "App Startup");
    public static string AutomationTriggerResume => T("系统恢复", "系統恢復", "System Resume");
    public static string AutomationTriggerTimeSchedule => T("定时", "定時", "Schedule");
    public static string AutomationTriggerSessionLock => T("锁定电脑", "鎖定電腦", "Session Lock");
    public static string AutomationTriggerSessionUnlock => T("解锁电脑", "解鎖電腦", "Session Unlock");
    public static string AutomationTriggerQuickAction => T("快捷操作", "快捷操作", "Quick Action");
    public static string AutomationStepSetPreset => T("应用预设", "應用預設", "Apply Preset");
    public static string AutomationStepSetRefreshRate => T("设置刷新率", "設定刷新率", "Set Refresh Rate");
    public static string AutomationStepSetPowerPlan => T("设置电源计划", "設定電源計劃", "Set Power Plan");
    public static string AutomationStepSetPowerMode => T("设置电源模式", "設定電源模式", "Set Power Mode");
    public static string AutomationStepSetMaxFrameRate => T("设置最大帧率", "設定最大幀率", "Set Max Frame Rate");
    public static string AutomationStepSetCpuPower => T("设置CPU功率", "設定CPU功率", "Set CPU Power");
    public static string AutomationStepSetFanMode => T("设置风扇模式", "設定風扇模式", "Set Fan Mode");
    public static string AutomationStepRunProgram => T("运行程序", "運行程式", "Run Program");
    public static string AutomationStepDelay => T("延迟", "延遲", "Delay");
    public static string AutomationStepNotification => T("通知", "通知", "Notification");
    public static string AutomationStepSetGpuPower => T("设置GPU功率", "設定GPU功率", "Set GPU Power");
    public static string AutomationStepSetTempSensitivity => T("设置温度灵敏度", "設定溫度靈敏度", "Set Temp Sensitivity");
    public static string AutomationProgramPath => T("程序路径", "程式路徑", "Program Path");
    public static string AutomationMessage => T("消息文本", "消息文本", "Message Text");
    public static string AutomationPreset => T("预设方案", "預設方案", "Preset");
    public static string AutomationPowerModeValue => T("电源模式值", "電源模式值", "Power Mode (0=节能/1=平衡/2=性能)");
    public static string AutomationFanModeValue => T("风扇模式", "風扇模式", "Fan Mode");
    public static string AutomationGpuPowerValue => T("TGP 功率 (W)", "TGP 功率 (W)", "TGP Power (W)");
    public static string AutomationTempSensitivityValue => T("灵敏度 (实时/高/中/低)", "靈敏度 (實時/高/中/低)", "Sensitivity (realtime/high/medium/low)");
    public static string DashboardHeading => T("实时状态", "實時狀態", "Dashboard");
    public static string SaveTooltip => T("保存", "儲存", "Save");
    public static string NewPipelineDefaultName => T("新管道", "新管道", "New Pipeline");
    // EcoQoS / Efficiency Mode
    public static string EcoQosHeading => T("EcoQoS 效率模式", "EcoQoS 效率模式", "EcoQoS Efficiency Mode");
    public static string EcoQosEnable => T("开启 EcoQoS", "開啓 EcoQoS", "Enable EcoQoS");
    public static string EcoQosThrottlePlugged => T("插电时限制所有后台进程", "插電時限制所有後臺進程", "Throttle background processes when plugged in");
    public static string EcoQosWhitelist => T("进程白名单", "進程白名單", "Process Whitelist");
    public static string EcoQosBlacklist => T("进程黑名单", "進程黑名單", "Process Blacklist");
    public static string DriverVersionRange => T("537.42 <= 驱动版本 < 610.47", "537.42 <= 驅動版本 < 610.47", "537.42 <= Driver < 610.47");

    // OSD
    public static string OsdToggleDesc => T("切换预设、风扇模式、电源状态时在屏幕底部显示提示", "切換預設、風扇模式、電源狀態時在螢幕底部顯示提示", "Show notification at screen bottom on preset/fan/power change");
    public static string OsdPositionHeading => T("OSD 位置", "OSD 位置", "OSD Position");
    public static string OsdPosBottomCenter => T("底部居中", "底部居中", "Bottom Center");
    public static string OsdPosTopLeft => T("左上角", "左上角", "Top Left");
    public static string OsdPosTopRight => T("右上角", "右上角", "Top Right");
    public static string OsdPosTopCenter => T("顶部居中", "頂部居中", "Top Center");
    public static string OsdPosBottomLeft => T("左下角", "左下角", "Bottom Left");
    public static string OsdPosBottomRight => T("右下角", "右下角", "Bottom Right");
    public static string TrayHoverPopupHeading => T("悬停浮窗", "懸停浮窗", "Hover Popup");
    public static string TrayHoverPopupDesc => T("鼠标悬停托盘图标时显示硬件信息", "滑鼠懸停托盤圖示時顯示硬體資訊", "Show hardware info when hovering over tray icon");
    public static string CapsLockOn => T("大写锁定：开", "大寫鎖定：開", "Caps Lock: ON");
    public static string CapsLockOff => T("大写锁定：关", "大寫鎖定：關", "Caps Lock: OFF");
    public static string NumLockOn => T("数字锁定：开", "數字鎖定：開", "Num Lock: ON");
    public static string NumLockOff => T("数字锁定：关", "數字鎖定：關", "Num Lock: OFF");

    // Performance page group headers
    public static string PerfGroupCpu => T("CPU 控制", "CPU 控制", "CPU Control");
    public static string PerfGroupGpu => T("GPU 控制", "GPU 控制", "GPU Control");

    // Core Keep
    public static string CoreKeepHeading => T("核心保持", "核心保持", "Core Keep");
    public static string CoreKeepProcessLabel => T("进程:", "進程:", "Process:");
    public static string CoreKeepPriorityLabel => T("优先级:", "優先級:", "Priority:");
    public static string CoreKeepAffinityLabel => T("关联性:", "關聯性:", "Affinity:");
    public static string CoreKeepPriorityIdle => T("空闲", "空閒", "Idle");
    public static string CoreKeepPriorityBelowNormal => T("低于标准", "低於標準", "Below Normal");
    public static string CoreKeepPriorityNormal => T("标准", "標準", "Normal");
    public static string CoreKeepPriorityAboveNormal => T("高于标准", "高於標準", "Above Normal");
    public static string CoreKeepPriorityHigh => T("高", "高", "High");
    public static string CoreKeepPriorityRealtime => T("实时", "實時", "Realtime");
    public static string CoreKeepPriorityUnknown => T("未知", "未知", "Unknown");
    public static string CoreKeepGuardLabel => T("运行中守护", "運行中守護", "Runtime Guard");
    public static string CoreKeepModeLabel => T("核心模式:", "核心模式:", "Core Mode:");
    public static string CoreKeepModeAuto => T("自动", "自動", "Auto");
    public static string CoreKeepModeAll => T("全部核心", "全部核心", "All Cores");
    public static string CoreKeepModeManual => T("手动选择", "手動選擇", "Manual");
    public static string CoreKeepModePerformanceFirst => T("P核优先", "P核優先", "P-core First");
    public static string CoreKeepModeNoSmt => T("关闭超线程", "關閉超執行緒", "No SMT");
    public static string CoreKeepBenchmark => T("核心竞速", "核心競速", "Core Benchmark");
    public static string CoreKeepBenchmarkRunning => T("竞速进行中...", "競速進行中...", "Benchmark running...");
    public static string CoreKeepBenchmarkDone => T("竞速完成", "競速完成", "Benchmark complete");
    public static string CoreKeepBenchmarkResult => T("核心 {0}: 得分={1} 相对={2:F2}", "核心 {0}: 得分={1} 相對={2:F2}", "Core {0}: score={1} rel={2:F2}");
    public static string CoreKeepStatusMatched => T("✓ 已应用", "✓ 已應用", "✓ Applied");
    public static string CoreKeepStatusMismatch => T("✗ 已被修改", "✗ 已被修改", "✗ Modified");
    public static string CoreKeepStatusNotRunning => T("- 进程未运行", "- 進程未運行", "- Not running");
    public static string CoreKeepSaveCurrent => T("捕获更新", "捕獲更新", "Capture");
    public static string CoreKeepTopologyHybrid => T("{0} 核 ({1} P + {2} E)", "{0} 核 ({1} P + {2} E)", "{0} cores ({1} P + {2} E)");
    public static string CoreKeepTopologyDualCcd => T("{0} 核 (CCD0={1} CCD1={2})", "{0} 核 (CCD0={1} CCD1={2})", "{0} cores (CCD0={1} CCD1={2})");
    public static string CoreKeepTopologyNormal => T("{0} 核", "{0} 核", "{0} cores");
    public static string CoreKeepEnforcementLevelLabel => T("强制级别:", "強制級別:", "Enforcement:");
    public static string CoreKeepEnforcementHard => T("软强制", "軟強制", "Soft (Affinity)");
    public static string CoreKeepEnforcementJob => T("硬强制 (Job)", "硬強制 (Job)", "Hard (Job Object)");
    public static string CoreKeepEnforcementLevelHint => T("硬强制可阻止进程自行修改亲和性；部分受保护进程可能无法应用，将自动回退软强制", "硬強制可阻止進程自行修改親和性；部分受保護進程可能無法應用，將自動回退軟強制", "Hard level prevents process from self-modifying affinity; may fall back to Soft for protected processes");
    // ponytail: CoreKeepPage 二级菜单专用字符串
    public static string CoreKeepOpenPage => T("打开核心保持", "打開核心保持", "Open Core Keep");
    public static string PageCoreKeep => T("核心保持", "核心保持", "Core Keep");
    public static string CoreKeepMasterLabel => T("核心保持主开关", "核心保持主開關", "Core Keep Master");
    public static string CoreKeepMasterDesc => T("启用后，进程启动时自动应用规则并定期守护", "啟用後，進程啟動時自動應用規則並定期守護", "When enabled, rules auto-apply on process start and are guarded periodically");
    public static string CoreKeepAddBtn => T("添加", "添加", "Add");
    public static string CoreKeepSaveBtn => T("保存", "保存", "Save");
    public static string CoreKeepDeleteBtn => T("删除", "刪除", "Delete");
    public static string CoreKeepRefreshBtn => T("刷新", "刷新", "Refresh");
    public static string CoreKeepBenchBtn => T("核心竞速", "核心競速", "Benchmark");
    public static string CoreKeepProcInputHint => T("输入进程名 (chrome.exe) 或 PID", "輸入進程名 (chrome.exe) 或 PID", "Enter process name (chrome.exe) or PID");
    public static string CoreKeepGuardIntervalLabel => T("守护间隔 (秒):", "守護間隔 (秒):", "Guard interval (s):");
    // ponytail: 后端支持的完整模式/级别/匹配字符串 — 对齐 CpuAffinity 架构
    public static string CoreKeepModePCores => T("P 核", "P 核", "P-cores");
    public static string CoreKeepModeECores => T("E 核", "E 核", "E-cores");
    public static string CoreKeepModePCoresSmt => T("P 核 (含超线程)", "P 核 (含超執行緒)", "P-cores + SMT");
    public static string CoreKeepModePCoresNoSmt => T("P 核 (无超线程)", "P 核 (無超執行緒)", "P-cores no SMT");
    public static string CoreKeepModeFirstHalf => T("前半核心", "前半核心", "First Half");
    public static string CoreKeepModeSecondHalf => T("后半核心", "後半核心", "Second Half");
    public static string CoreKeepModeCcd0 => T("CCD0", "CCD0", "CCD0");
    public static string CoreKeepModeCcd1 => T("CCD1", "CCD1", "CCD1");
    public static string CoreKeepEnforcementSoft => T("提示 (CPU 集)", "提示 (CPU 集)", "Hint (CPU Sets)");
    public static string CoreKeepEnforcementLocked => T("锁定 (Job 锁定)", "鎖定 (Job 鎖定)", "Locked (Job+Lock)");
    public static string CoreKeepPathFilterLabel => T("路径过滤:", "路徑過濾:", "Path Filter:");
    public static string CoreKeepPathFilterHint => T("可选，如 D:\\Games\\**", "可選，如 D:\\Games\\**", "Optional, e.g. D:\\Games\\**");
    public static string CoreKeepExcludeLabel => T("排除模式:", "排除模式:", "Exclude:");
    public static string CoreKeepExcludeHint => T("逗号分隔，如 updater*,helper*", "逗號分隔，如 updater*,helper*", "Comma-separated, e.g. updater*,helper*");
    public static string CoreKeepRuleConfigHeading => T("规则配置", "規則配置", "Rule Config");
    public static string CoreKeepCoreListLabel => T("选择核心:", "選擇核心:", "Select cores:");
    public static string CoreKeepEmptyList => T("暂无规则，请在上方输入进程名后点击添加", "暫無規則，請在上方輸入進程名後點擊添加", "No rules yet. Enter a process name above and click Add");
    public static string CoreKeepBackToPerf => T("返回性能页", "返回效能頁", "Back to Performance");
    // ponytail: 进程列表 + 拓扑可视化 + 快速操作 — 对齐 CpuAffinityManager
    public static string CoreKeepProcessListHeading => T("进程列表", "進程列表", "Processes");
    public static string CoreKeepProcessListDesc => T("右键进程可快速设置亲和性", "右鍵進程可快速設置親和性", "Right-click a process for quick affinity actions");
    public static string CoreKeepProcessSearch => T("搜索进程...", "搜索進程...", "Search processes...");
    public static string CoreKeepProcessRefresh => T("刷新", "刷新", "Refresh");
    public static string CoreKeepTopologyHeading => T("CPU 拓扑", "CPU 拓撲", "CPU Topology");
    public static string CoreKeepQuickSetPCores => T("设置: P 核", "設置: P 核", "Set: P-Cores");
    public static string CoreKeepQuickSetECores => T("设置: E 核", "設置: E 核", "Set: E-Cores");
    public static string CoreKeepQuickSetAll => T("设置: 全部核心", "設置: 全部核心", "Set: All Cores");
    public static string CoreKeepQuickSetFirstHalf => T("设置: 前半核心", "設置: 前半核心", "Set: First Half");
    public static string CoreKeepQuickSetSecondHalf => T("设置: 后半核心", "設置: 後半核心", "Set: Second Half");
    public static string CoreKeepQuickJobEnforced => T("强制: Job 反篡改", "強制: Job 反篡改", "Enforce: Job (anti-tamper)");
    public static string CoreKeepQuickJobLocked => T("锁定: Job 禁止脱离", "鎖定: Job 禁止脫離", "Lock: Job (no escape)");
    public static string CoreKeepQuickRelax => T("恢复: 全部核心", "恢復: 全部核心", "Relax: All Cores");
    public static string CoreKeepQuickApplyRule => T("应用匹配规则", "應用匹配規則", "Apply Matched Rule");
    public static string CoreKeepBatchHeading => T("批量操作", "批量操作", "Batch Operations");
    public static string CoreKeepBatchDesc => T("对当前筛选结果批量应用模式/级别", "對當前篩選結果批量應用模式/級別", "Apply mode/level to all filtered processes");
    public static string CoreKeepBatchApply => T("批量应用", "批量應用", "Batch Apply");
    public static string CoreKeepBatchRelax => T("批量恢复", "批量恢復", "Batch Relax");
    public static string CoreKeepBatchAddRules => T("添加为规则", "添加為規則", "Add as Rules");
    public static string CoreKeepBatchImpact => T("将影响 {0} 个进程", "將影響 {0} 個進程", "Will affect {0} process(es)");
    public static string CoreKeepBatchDone => T("完成: 成功 {0} / 失败 {1}", "完成: 成功 {0} / 失敗 {1}", "Done: {0} ok / {1} failed");
    public static string CoreKeepBatchSelectAll => T("全选/清空", "全選/清空", "Toggle All");
    public static string CoreKeepBatchNoSelection => T("未勾选时对全部筛选结果生效", "未勾選時對全部篩選結果生效", "Affects all filtered when none checked");
    public static string CoreKeepRuleNameLabel => T("规则名称:", "規則名稱:", "Rule Name:");
    public static string CoreKeepRuleNameHint => T("如: 游戏绑定大核", "如: 遊戲綁定大核", "e.g. Game on P-Cores");
    public static string CoreKeepLockBreakaway => T("锁定子进程 (禁止脱离 Job)", "鎖定子進程 (禁止脫離 Job)", "Lock breakaway (prevent child escape)");
    public static string CoreKeepCustomMaskLabel => T("自定义掩码:", "自定義掩碼:", "Custom Mask:");
    public static string CoreKeepCustomMaskHint => T("十六进制，如 0xFF", "十六進制，如 0xFF", "Hex, e.g. 0xFF");
    public static string CoreKeepRuleEnabled => T("启用", "啟用", "Enabled");
    public static string CoreKeepApplyNow => T("立即应用", "立即應用", "Apply Now");
    // ponytail: CoreKeep 统计概览卡片
    public static string CoreKeepStatProcesses => T("运行进程", "運行進程", "Processes");
    public static string CoreKeepStatRulesActive => T("活跃规则", "活躍規則", "Active Rules");
    public static string CoreKeepStatPCores => T("P 性能核", "P 效能核", "P-Cores");
    public static string CoreKeepStatECores => T("E 能效核", "E 能效核", "E-Cores");
    public static string CoreKeepRuleCardMode => T("模式", "模式", "Mode");
    public static string CoreKeepRuleCardLevel => T("级别", "級別", "Level");
    public static string CoreKeepRuleCardEdit => T("编辑", "編輯", "Edit");

    // Pin tooltip
    // New automation step types
    public static string AutomationStepSetGPUHybridMode => T("休眠独立显卡", "休眠獨立顯卡", "Disable dGPU");
    public static string AutomationStepSetBrightness => T("设置显示器亮度", "設定顯示器亮度", "Set Display Brightness");
    public static string AutomationStepSetMicrophone => T("麦克风静音", "麥克風靜音", "Microphone Mute");
    public static string AutomationStepSetWiFi => T("开关WiFi", "開關WiFi", "Toggle WiFi");
    public static string AutomationStepSetBluetooth => T("开关蓝牙", "開關藍牙", "Toggle Bluetooth");
    public static string AutomationStepPlaySound => T("播放音频", "播放音頻", "Play Sound");

    public static string AutomationGPUHybridModeValue => T("on/off", "on/off", "on/off");
    public static string AutomationBrightnessValue => T("亮度 (0-100)", "亮度 (0-100)", "Brightness (0-100)");
    public static string AutomationMicrophoneValue => T("mute/unmute", "mute/unmute", "mute/unmute");
    public static string AutomationWiFiValue => T("on/off", "on/off", "on/off");
    public static string AutomationBluetoothValue => T("on/off", "on/off", "on/off");
    public static string AutomationPlaySoundValue => T("WAV文件路径", "WAV檔案路徑", "WAV file path");
    public static string AutomationStepRunMacro => T("执行宏", "執行宏", "Run Macro");

    // Macro
    public static string SidebarMacro => T("宏", "巨集", "Macro");
    public static string PageMacro => T("键盘宏", "鍵盤巨集", "Keyboard Macros");
    public static string MacroHeading => T("键盘宏管理", "鍵盤巨集管理", "Keyboard Macro Manager");
    public static string MacroEnabled => T("宏功能", "巨集功能", "Macro");
    public static string MacroMasterDesc => T("全局启用/禁用宏触发键拦截", "全域啟用/禁用巨集觸發鍵攔截", "Enable/disable global macro trigger key interception");
    public static string MacroAddMacro => T("添加宏", "添加巨集", "Add Macro");
    public static string MacroName => T("宏名称:", "巨集名稱:", "Macro Name:");
    public static string MacroTriggerKey => T("触发键:", "觸發鍵:", "Trigger Key:");
    public static string MacroRecord => T("录制", "錄製", "Record");
    public static string MacroPlayTest => T("试播", "試播", "Play");
    public static string MacroRepeatCount => T("重复次数 (1-10):", "重複次數 (1-10):", "Repeat Count (1-10):");
    public static string MacroIgnoreDelays => T("忽略延迟", "忽略延遲", "Ignore Delays");
    public static string MacroInterruptOnOtherKey => T("按键时打断回放", "按鍵時打斷回放", "Interrupt playback on keypress");
    public static string MacroNoMacros => T("暂无宏，点击上方添加", "暫無巨集，點擊上方添加", "No macros yet. Click Add above.");
    public static string MacroConfirmDelete => T("确定删除此宏？", "確定刪除此巨集？", "Delete this macro?");
    public static string MacroConfirmDeleteTitle => T("删除确认", "刪除確認", "Confirm Delete");
    public static string MacroCaptureKey => T("按下任意键...", "按下任意鍵...", "Press any key...");
    public static string MacroClearKey => T("清除", "清除", "Clear");
    public static string MacroEditTitle => T("编辑宏", "編輯巨集", "Edit Macro");
    public static string MacroDelete => T("删除", "刪除", "Delete");
    public static string MacroEventsCount(int count) => T($"{count} 个事件", $"{count} 個事件", $"{count} events");
    public static string MacroTriggerConflict(string name) => T($"触发键已被宏 \"{name}\" 占用", $"觸發鍵已被巨集 \"{name}\" 佔用", $"Trigger key already used by macro \"{name}\"");
    public static string MacroTriggerConflictTitle => T("触发键冲突", "觸發鍵衝突", "Trigger Key Conflict");
    public static string MacroRecordingCardHint => T("录制中…按 ESC 停止", "錄製中…按 ESC 停止", "Recording… press ESC to stop");

    // EcoQoS edit button
    // Custom logo
    public static string CustomLogoHeading => T("自定义主界面 LOGO", "自訂主介面 LOGO", "Custom Main Logo");
    public static string CustomLogoDesc => T("替换左侧导航栏的应用图标", "替換左側導航欄的應用圖示", "Replace the app logo in the left nav bar");
    // Custom background
    public static string CustomBgHeading => T("自定义主界面背景", "自訂主介面背景", "Custom Main Background");
    public static string CustomBgDesc => T("设置主窗口背景图片", "設定主視窗背景圖片", "Set main window background image");
    public static string CustomBgOpacity => T("背景透明度", "背景透明度", "Background Opacity");
    public static string CustomBgBlur => T("高斯模糊", "高斯模糊", "Gaussian Blur");

    // Other page
    public static string NumLockHeading => T("数字键锁定", "數字鍵鎖定", "Num Lock");
    public static string NumLockDesc => T("切换数字小键盘开关状态", "切換數字小鍵盤開關狀態", "Toggle numeric keypad lock");
    public static string CapsLockHeading => T("大写键锁定", "大寫鍵鎖定", "Caps Lock");
    public static string CapsLockDesc => T("切换大写锁定开关状态", "切換大寫鎖定開關狀態", "Toggle caps lock");
    public static string TouchpadLockHeading => T("触摸板锁定", "觸控板鎖定", "Touchpad Lock");
    public static string TouchpadLockDesc => T("禁用或启用触摸板", "禁用或啟用觸控板", "Disable or enable touchpad");
    public static string WinLockHeading => T("Win键锁定", "Win鍵鎖定", "Win Key Lock");
    public static string WinLockDesc => T("禁用或启用 Windows 徽标键（游戏防误触）", "禁用或啟用 Windows 徽標鍵（遊戲防誤觸）", "Disable or enable Windows key (anti-mistouch in games)");

    // New automation trigger types
    public static string AutomationTriggerBatteryAbove => T("电池高于", "電池高於", "Battery Above %");
    public static string AutomationTriggerBatteryBelow => T("电池低于", "電池低於", "Battery Below %");
    public static string AutomationTriggerCpuTempAbove => T("CPU温度高于", "CPU溫度高於", "CPU Temp Above (C)");
    public static string AutomationTriggerGpuTempAbove => T("GPU温度高于", "GPU溫度高於", "GPU Temp Above (C)");
    public static string AutomationTriggerDisplayConnect => T("外接显示器已连接", "外接顯示器已連接", "External Display Connected");
    public static string AutomationTriggerDisplayDisconnect => T("外接显示器已断开", "外接顯示器已斷開", "External Display Disconnected");
    public static string AutomationTriggerHotkey => T("快捷键", "快捷鍵", "Hotkey");
    public static string AutomationHotkeyHint => T("例如 Ctrl+Shift+F12", "例如 Ctrl+Shift+F12", "e.g. Ctrl+Shift+F12");
    public static string AutomationTriggersHeading => T("触发器:", "觸發器:", "Triggers:");
    public static string AutomationAddTrigger => T("添加触发器", "添加觸發器", "Add Trigger");
    public static string AutomationNoTriggers => T("未配置触发器", "未配置觸發器", "No triggers configured");
    public static string AutomationThresholdHint => T("阈值 (例如 80)", "閾值 (例如 80)", "Threshold value (e.g. 80)");
    public static string AutomationTimeHint => T("时间 HH:mm (例如 08:30)", "時間 HH:mm (例如 08:30)", "Time HH:mm (e.g. 08:30)");
    public static string AutomationProcessHint => T("例如 chrome.exe", "例如 chrome.exe", "e.g. chrome.exe");

	    // HWiNFO
	    public static string HWiNFOHeading => T("HWiNFO64 集成", "HWiNFO64 整合", "HWiNFO64 Integration");
	    public static string HWiNFODesc => T("将风扇转速、CPU/GPU温度和功耗共享到 HWiNFO64 自定义传感器", "將風扇轉速、CPU/GPU溫度和功耗共享到 HWiNFO64 自訂感測器", "Share fan speed, CPU/GPU temperature and power to HWiNFO64 custom sensors");
	    public static string HWiNFOReadHeading => T("HWiNFO64 数据源", "HWiNFO64 資料源", "HWiNFO64 Data Source");
	    public static string HWiNFOReadDesc => T("从 HWiNFO64 读取传感器数据（温度/功耗/负载/频率），替代 LibreHardwareMonitor 读数", "從 HWiNFO64 讀取感測器資料（溫度/功耗/負載/頻率），替代 LibreHardwareMonitor 讀數", "Read sensor data (temp/power/load/clock) from HWiNFO64, replacing LibreHardwareMonitor readings");
	
	    // HTTP API
    // Dashboard page
    public static string SysStatusHeading => T("系统状态", "系統狀態", "System Status");
    public static string SysPresetsHeading => T("性能预设", "效能預設", "Performance Presets");
    public static string PerfModeLabel => T("性能模式", "效能模式", "Performance Mode");

    // SysInfo page
    public static string TempDisplayMode => T("温度显示模式", "溫度顯示模式", "Temp Display Mode");
    public static string TempSmoothedShort => T("平滑", "平滑", "Smoothed");
    public static string TempRawShort => T("实时", "即時", "Real-time");
    public static string GpuAppLocate => T("定位文件", "定位文件", "Locate File");
    public static string GpuAppEndTask => T("结束进程", "結束進程", "End Task");
    public static string GpuPrefHeading => T("图形首选项", "圖形首選項", "Graphics Preference");
    public static string GpuPrefAuto => T("让 Windows 决定", "讓Windows決定", "Let Windows Decide");
    public static string GpuPrefPowerSave => T("节能", "節能", "Power Save");
    public static string GpuPrefHighPerf => T("高性能", "高效能", "High Performance");
    public static string GpuRestartConfirmTitle => T("重启 GPU", "重啟GPU", "Restart GPU");
    public static string GpuRestartSuccess => T("GPU 已重启", "GPU 已重啟", "GPU Restarted");
    public static string GpuRestartConfirmMsg => T("确定要重启 GPU 吗？", "確定要重啟GPU嗎？", "Are you sure you want to restart the GPU?");

    // Lighting page
    public static string LightingBrightSpeed => T("亮度与速度", "亮度與速度", "Brightness & Speed");
    public static string LightingZone1 => T("分区 1", "分割區 1", "Zone 1");
    public static string LightingZone2 => T("分区 2", "分割區 2", "Zone 2");
    public static string LightingZone3 => T("分区 3", "分割區 3", "Zone 3");
    public static string LightingZone4 => T("分区 4", "分割區 4", "Zone 4");
    public static string LightingProtoBasic => T("四分区 Basic", "四分割區 Basic", "Basic 4-Zone");
    public static string LightingProtoDojo => T("Dojo 四分区", "Dojo 四分割區", "Dojo 4-Zone");

    // Fan page
    public static string FanSpeedRPM => T("转速控制(RPM)", "轉速控制(RPM)", "Fan Speed (RPM)");
    public static string FanSettings => T("风扇设置", "風扇設定", "Fan Settings");
    public static string TempCelsius => T("温度 (°C)", "溫度 (°C)", "Temperature (°C)");

    // AMD CPU Power Limits (PPT only — TDC/EDC/Tctl removed with advanced tuning)
    public static string AmdCpuPowerHeading => T("CPU 功耗限制 (AMD)", "CPU 功耗限制 (AMD)", "CPU Power Limits (AMD)");
    public static string AmdCpuPowerDesc => T("PPT 桌面 AM5 独立 CPU", "PPT 桌面 AM5 獨立 CPU", "PPT desktop AM5 standalone CPU");

    // Perf page
    public static string PerfAdjustCpuPower => T("调整 CPU 功率限制", "調整 CPU 功率限制", "Adjust CPU Power Limit");
    public static string PerfEcoQosDesc => T("限制未在前台运行的后台进程的CPU性能", "限制未在前臺運行的後臺程序的CPU效能", "Limit CPU performance for background processes");
    public static string PerfCoreKeepDesc => T("持久化 CPU 优先级和关联性，进程启动时自动恢复", "持久化CPU優先級和關聯性，進程啟動時自動恢復", "Persist CPU priority & affinity, auto-restore on process start");
    public static string PerfGfxReboot => T("切换需要重启计算机", "切換需要重啟電腦", "Switching requires reboot");
    public static string PerfMaxFpsDesc => T("限制 GPU 最大帧率", "限制 GPU 最大幀率", "Limit GPU max frame rate");
    public static string PerfRefreshRateDesc => T("切换显示器刷新率", "切換顯示器刷新率", "Switch monitor refresh rate");
    public static string PerfDbUnlockLabel => T("解锁版本", "解鎖版本", "Unlocked Version");
    public static string PerfDbNormalLabel => T("普通版本", "普通版本", "Normal Version");
    public static string PerfPpabDesc => T("建议使用 PPAB (Dynamic Boost) 实现相同效果（如可用）", "建議使用PPAB(Dynamic Boost)實現相同效果(如可用)", "Use PPAB (Dynamic Boost) for same effect if available");

    // Settings page
    public static string SettingsUiHeading => T("界面设置", "界面設定", "UI Settings");
    public static string SettingsSysHeading => T("系统设置", "系統設定", "System Settings");
    public static string SettingsFloatingHeading => T("浮窗设置", "浮窗設定", "Overlay Settings");
    public static string SettingsHardwareHeading => T("硬件设置", "硬體設定", "Hardware Settings");
    // Other page
    public static string OtherHttpApiHeading => T("HTTP API 服务", "HTTP API 服務", "HTTP API Service");
    public static string OtherHttpApiDesc => T("在 localhost:5000 提供硬件状态 API，供外部工具调用", "在 localhost:5000 提供硬體狀態 API，供外部工具調用", "Provides hardware status API at localhost:5000");

    // HTTP API status
    public static string HttpApiRunning => T("运行中", "運行中", "Running");
    public static string HttpApiStopped => T("已停止", "已停止", "Stopped");

    // Power plan names
    // File dialog
    public static string FileDialogSelectApp => T("选择程序", "選擇程式", "Select Application");
    public static string FileDialogExeFilter => T("可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*", "可執行檔(*.exe)|*.exe|所有檔案(*.*)|*.*", "Executable (*.exe)|*.exe|All Files (*.*)|*.*");
    public static string FileDialogSelectLogo => T("选择自定义 LOGO", "選擇自訂LOGO", "Select Custom Logo");
    public static string FileDialogImgFilter => T("图片文件 (*.png;*.gif;*.webp)|*.png;*.gif;*.webp|所有文件 (*.*)|*.*", "圖片檔(*.png;*.gif;*.webp)|*.png;*.gif;*.webp|所有檔案(*.*)|*.*", "Image (*.png;*.gif;*.webp)|*.png;*.gif;*.webp|All Files (*.*)|*.*");

    // GUI buttons / labels
    public static string ButtonOK => T("确定", "確定", "OK");
    public static string ButtonCancel => T("取消", "取消", "Cancel");
    public static string ButtonEdit => T("编辑", "編輯", "Edit");
    public static string ButtonSave => T("保存", "保存", "Save");
    public static string ButtonRefresh => T("刷新", "刷新", "Refresh");
    public static string ButtonDelete => T("删除", "刪除", "Delete");
    public static string ButtonAdd => T("添加", "添加", "Add");
    // Automation page
    public static string AutomationStepCount(int count) => T($"{count} 个步骤", $"{count} 個步驟", $"{count} steps");
    public static string AutomationExecuting => T(" [执行中...]", " [執行中...]", " [Executing...]");

    // ═══ Hetero CPU (AMD dual-CCD hybrid scheduling) ═══
    public static string HeteroCpuHeading => T("异构调度 (AMD双CCD)", "異構調度 (AMD雙CCD)", "Hetero CPU (AMD Dual-CCD)");
    public static string HeteroCpuToggleDesc => T("模拟Intel大小核调度，需重启生效", "模擬Intel大小核調度，需重啟生效", "Simulate Intel hybrid scheduling. Reboot required.");
    public static string HeteroCpuMaskLabel => T("SmallProcessorMask", "SmallProcessorMask", "SmallProcessorMask");
    public static string HeteroCpuMaskDesc => T("小核掩码 (十六进制，如 FFFF0000)", "小核遮罩 (十六進制，如 FFFF0000)", "Small core mask (hex, e.g. FFFF0000)");
    public static string HeteroCpuDetectAuto => T("自动检测拓扑", "自動檢測拓撲", "Auto Detect");
    public static string HeteroCpuDefaultPolicyLabel => T("默认调度策略", "預設調度策略", "Default Policy");
    public static string HeteroCpuDefaultPolicyDesc => T("DefaultDynamicHeteroCpuPolicy", "DefaultDynamicHeteroCpuPolicy", "DefaultDynamicHeteroCpuPolicy");
    public static string HeteroCpuRuntimeLabel => T("预期运行时间 (ms)", "預期運行時間 (ms)", "Expected Runtime (ms)");
    public static string HeteroCpuRuntimeDesc => T("DynamicCpuPolicyExpectedRuntime", "DynamicCpuPolicyExpectedRuntime", "DynamicCpuPolicyExpectedRuntime");
    public static string HeteroCpuImportantPolicyLabel => T("重要任务调度策略", "重要任務調度策略", "Important Task Policy");
    public static string HeteroCpuImportantPolicyDesc => T("DynamicHeteroCpuPolicyImportant", "DynamicHeteroCpuPolicyImportant", "DynamicHeteroCpuPolicyImportant");
    public static string HeteroCpuImportantShortLabel => T("重要短任务调度策略", "重要短任務調度策略", "Important Short Task Policy");
    public static string HeteroCpuImportantShortDesc => T("DynamicHeteroCpuPolicyImportantShort", "DynamicHeteroCpuPolicyImportantShort", "DynamicHeteroCpuPolicyImportantShort");
    public static string HeteroCpuPolicyMaskLabel => T("重要任务判断依据", "重要任務判斷依據", "Policy Mask");
    public static string HeteroCpuPolicyMaskDesc => T("DynamicHeteroCpuPolicyMask", "DynamicHeteroCpuPolicyMask", "DynamicHeteroCpuPolicyMask");
    public static string HeteroCpuPriorityLabel => T("重要任务优先级阈值", "重要任務優先級閾值", "Important Priority Threshold");
    public static string HeteroCpuPriorityDesc => T("DynamicHeteroCpuPolicyImportantPriority", "DynamicHeteroCpuPolicyImportantPriority", "DynamicHeteroCpuPolicyImportantPriority");
    public static string HeteroCpuApplyBtn => T("应用设置", "應用設定", "Apply");
    public static string HeteroCpuRestoreBtn => T("恢复默认", "恢復預設", "Restore Defaults");

    // Hetero CPU policy ComboBox labels
    public static string HeteroPolicyAny => T("0 - 任何核心", "0 - 任何核心", "0 - Any Core");
    public static string HeteroPolicyBig => T("1 - 大核", "1 - 大核", "1 - Big Core");
    public static string HeteroPolicyBigOrIdle => T("2 - 大核或闲置", "2 - 大核或閒置", "2 - Big or Idle");
    public static string HeteroPolicySmall => T("3 - 小核", "3 - 小核", "3 - Small Core");
    public static string HeteroPolicySmallOrIdle => T("4 - 小核或闲置", "4 - 小核或閒置", "4 - Small or Idle");
    public static string HeteroPolicyAuto => T("5 - 自动", "5 - 自動", "5 - Auto");
    public static string HeteroPolicyPreferSmall => T("6 - 偏向小核", "6 - 偏向小核", "6 - Prefer Small");
    public static string HeteroPolicyPreferBig => T("7 - 偏向大核", "7 - 偏向大核", "7 - Prefer Big");

    // Hetero CPU mask ComboBox labels
    public static string HeteroMaskForeground => T("1 - 前台状态", "1 - 前台狀態", "1 - Foreground");
    public static string HeteroMaskPriority => T("2 - 优先级", "2 - 優先級", "2 - Priority");
    public static string HeteroMaskFgPriority => T("3 - 前台+优先级", "3 - 前台+優先級", "3 - Foreground+Priority");
    public static string HeteroMaskRuntime => T("4 - 预期运行时间", "4 - 預期運行時間", "4 - Expected Runtime");
    public static string HeteroMaskFgRuntime => T("5 - 前台+时间", "5 - 前台+時間", "5 - Foreground+Runtime");
    public static string HeteroMaskPriRuntime => T("6 - 优先级+时间", "6 - 優先級+時間", "6 - Priority+Runtime");
    public static string HeteroMaskAll => T("7 - 全部", "7 - 全部", "7 - All");

    // Dialog messages
    public static string HeteroCpuNotDetected => T("未检测到 AMD 双 CCD CPU，请手动填写掩码。", "未檢測到 AMD 雙 CCD CPU，請手動填寫遮罩。", "No AMD dual-CCD CPU detected. Please enter mask manually.");
    public static string HeteroCpuDetectTitle => T("自动检测拓扑", "自動檢測拓撲", "Auto Detect Topology");
    public static string HeteroCpuApplyResult => T("异构调度设置已写入注册表，重启后生效。", "異構調度設定已寫入註冊表，重啟後生效。", "Hetero scheduling settings have been written to registry. Reboot to apply.");
    public static string HeteroCpuApplyTitle => T("应用设置", "應用設定", "Apply");
    public static string HeteroCpuRestoreResult => T("异构调度设置已清除并恢复默认。", "異構調度設定已清除並恢復預設。", "Hetero scheduling settings cleared and restored to defaults.");
    public static string HeteroCpuRestoreTitle => T("恢复默认", "恢復預設", "Restore Defaults");
    public static string HeteroCpuDetectResult => T("已应用检测结果，重启后生效。", "已應用檢測結果，重啟後生效。", "Auto-detect results applied. Reboot to apply.");

    public static string DashboardMemoryLabel => T("运存", "運存", "Memory");
    public static string DashboardStorageLabel => T("储存", "儲存", "Storage");
    public static string DashboardMemoryUsedLabel => T("已用/总计", "已用/總計", "Used/Total");
    public static string DashboardMemoryCleanBtn => T("一键清理", "一鍵清理", "Clean");

    public static string HeteroCpuDetectConfirm(string total, string ccd0, string ccd1, string mask) =>
        T($"检测到双 CCD CPU\n总逻辑处理器: {total}\nCCD0: {ccd0} LP | CCD1: {ccd1} LP\n推荐掩码: {mask}\n\n是否应用此掩码并设置各调度策略?",
          $"檢測到雙 CCD CPU\n總邏輯處理器: {total}\nCCD0: {ccd0} LP | CCD1: {ccd1} LP\n推薦遮罩: {mask}\n\n是否應用此遮罩並設定各調度策略?",
	          $"Dual CCD CPU detected\nTotal logical processors: {total}\nCCD0: {ccd0} LP | CCD1: {ccd1} LP\nSuggested mask: {mask}\n\nApply this mask and default policies?");

	  // ═══ Phase 1: Dashboard / MainWindow / Floating / Tray / Settings hardcoded strings ═══
	  public static string DashboardMemoryVirtualLabel => T("虚拟", "虛擬", "Virtual");
	  public static string DashboardNetworkSpeedLabel => T("网速", "網速", "Network");
	  public static string DashboardFpsLabel => T("FPS", "FPS", "FPS");
	  public static string DashboardHpDriverPage => T("HP 驱动下载 / HP Driver Download", "HP 驅動下載 / HP Driver Download", "HP Driver Download");
	  public static string DashboardHpDriverDesc => T("点击右侧按钮打开 HP 官方驱动页面", "點擊右側按鈕打開 HP 官方驅動頁面", "Click the button to open HP driver page");
	  public static string DashboardMemoryCleaning => T("清理中...", "清理中...", "Cleaning...");
	  public static string DashboardMemoryFreedFormat(string freed) => T(
	    $"已释放 {freed}", $"已釋放 {freed}", $"Freed {freed}");
	  public static string DashboardMemoryNoClean => T("无需清理", "無需清理", "No need to clean");
	  public static string DashboardMemoryCleanFailed(string msg) => T(
	    $"清理失败: {msg}", $"清理失敗: {msg}", $"Clean failed: {msg}");
	  public static string DashboardProcessKilled(string name) => T(
	    $"进程 '{name}' 已终止", $"進程 '{name}' 已終止", $"Process '{name}' terminated");
	  public static string DashboardProcessKillFailed(string name) => T(
	    $"进程 '{name}' 终止失败，PID可能已过期或权限不足",
	    $"進程 '{name}' 終止失敗，PID可能已過期或權限不足",
	    $"Process '{name}' termination failed — PID may be stale or permission insufficient");
	  public static string DashboardProcessKillError(string msg) => T(
	    $"结束进程失败: {msg}", $"結束進程失敗: {msg}", $"Failed to end process: {msg}");

	  public static string MainWindowLogBadge => T("Log", "Log", "Log");
	  public static string MainWindowPinTooltipOn => T("取消顶置", "取消頂置", "Unpin (Cancel Topmost)");
	  public static string MainWindowPinTooltipOff => T("顶置", "頂置", "Always on Top");
	  public static string MainWindowStatusBarFormat(double cpuTemp, double gpuTemp) => T(
	    $"CPU {cpuTemp:F0}°C  GPU {gpuTemp:F0}°C",
	    $"CPU {cpuTemp:F0}°C  GPU {gpuTemp:F0}°C",
	    $"CPU {cpuTemp:F0}°C  GPU {gpuTemp:F0}°C");
	  public static string FloatLabelCpu => T("CPU", "CPU", "CPU");
	  public static string FloatLabelGpu => T("GPU", "GPU", "GPU");
	  public static string FloatLabelMem => T("MEM", "MEM", "MEM");
	  public static string FloatLabelNet => T("NET", "NET", "NET");
	  public static string FloatLabelFps => T("FPS", "FPS", "FPS");
	  public static string FloatLabelFan => T("FAN", "FAN", "FAN");

	  public static string TrayHeader => T("OMEN X HUB", "OMEN X HUB", "OMEN X HUB");

	  public static string SettingsOsdHeading => T("OSD", "OSD", "OSD");
	  public static string SettingsDebugShowAllUi => T("DEBUG: 显示所有UI", "DEBUG: 顯示所有UI", "DEBUG: Show All UI");
	  public static string SettingsDebugShowAllUiDesc => T(
	    "强制展示所有隐藏的功能卡片，即使硬件不支持。仅用于开发调试。",
	    "強制展示所有隱藏的功能卡片，即使硬體不支援。僅用於開發調試。",
	    "Force show all hidden feature cards, even if hardware doesn't support them. For debug only.");

	  // ═══ Phase 2: PerfPage.xaml hardcoded strings ═══
	  public static string PerfPresetLabel => T("预设:", "預設:", "Preset:");
	  public static string PerfPresetCopyRename => T("复制并重命名预设:", "複製並重新命名預設:", "Copy & Rename Preset:");
	  // AMD PPT (TDC/EDC/Tctl removed with advanced tuning)
	  public static string AmdPptLabel => T("PPT (CPU Package Power) (W)", "PPT (CPU Package Power) (W)", "PPT (CPU Package Power) (W)");
	  public static string IccMaxDesc => T(
	    "限制电流峰值。⚠️ 过低降频易死机，过高可能触发保护。",
	    "限制電流峰值。⚠️ 過低降頻易死機，過高可能觸發保護。",
	    "Limits peak current. ⚠️ Too low may throttle; too high may trigger protection.");
	  public static string AcLoadLineDesc => T(
	    "调节CPU的电压响应曲线，通常数值越低越好。",
	    "調節CPU的電壓響應曲線，通常數值越低越好。",
	    "Adjusts CPU voltage response curve. Lower values are usually better.");
	  public static string PowerModeDesc => T(
	    "调节系统电源策略性能倾向。",
	    "調節系統電源策略性能傾向。",
	    "Adjust system power policy performance bias.");
	  public static string PowerPlanDesc => T(
	    "选择 Windows 系统的电源计划，展开可调节处理器高级电源设置。",
	    "選擇 Windows 系統的電源計劃，展開可調節處理器高級電源設置。",
	    "Select a Windows power plan. Expand to adjust advanced processor power settings.");
	  // Power plan sub-labels
	  public static string PwrSourceLabel => T("电源来源", "電源來源", "Power Source");
	  public static string PwrSourceAc => T("交流电源 (AC)", "交流電源 (AC)", "AC Power");
	  public static string PwrSourceDc => T("直流电源 (DC)", "直流電源 (DC)", "DC Power");
	  public static string PwrClassLabel => T("处理器类别", "處理器類別", "Processor Class");
	  public static string PwrClassAll => T("全部处理器", "全部處理器", "All Processors");
	  public static string PwrClassPcore => T("第一类处理器 (P核)", "第一類處理器 (P核)", "Class 1 (P-cores)");
	  public static string EppLabel => T("处理器能源性能首选项策略", "處理器能源性能首選項策略", "Processor Energy Performance Preference");
	  public static string EppHint => T(
	    "预设：极速响应(0)、偏向性能(20)、平衡(50)、偏向省电(80)、极致省电(100)。可自定义输入 0-100。",
	    "預設：極速響應(0)、偏向性能(20)、平衡(50)、偏向省電(80)、極致省電(100)。可自訂輸入 0-100。",
	    "Presets: Instant(0), Perf(20), Balanced(50), PowerSave(80), MaxPowerSave(100). Custom 0-100.");
	  public static string BoostModeLabel => T("处理器性能提升模式", "處理器效能提升模式", "Processor Performance Boost Mode");
	  public static string BoostModeHint => T(
	    "0=禁用(关闭睿频) / 1=已启用 / 2=高性能 / 3=高效率 / 4=高性能高效率 / 5=积极且有保障 / 6=高效积极且有保障",
	    "0=禁用(關閉睿頻) / 1=已啟用 / 2=高效能 / 3=高效率 / 4=高效能高效率 / 5=積極且有保障 / 6=高效積極且有保障",
	    "0=Disabled / 1=Enabled / 2=HighPerf / 3=HighEff / 4=HighPerf+Eff / 5=Aggressive / 6=Eff+Agressive");
	  public static string MaxProcStateLabel => T("最大处理器状态", "最大處理器狀態", "Maximum Processor State");
  public static string MaxProcStateHint => T(
    "预设：100%、99%、95%、90%、85%、80%。可自定义输入 0-100。",
    "預設：100%、99%、95%、90%、85%、80%。可自訂輸入 0-100。",
    "Presets: 100%, 99%, 95%, 90%, 85%, 80%. Custom 0-100.");
  public static string MinProcStateLabel => T("最小处理器状态", "最小處理器狀態", "Minimum Processor State");
  public static string MinProcStateHint => T(
    "预设：5%、10%、20%、50%、80%、100%。可自定义输入 0-100。",
    "預設：5%、10%、20%、50%、80%、100%。可自訂輸入 0-100。",
    "Presets: 5%, 10%, 20%, 50%, 80%, 100%. Custom 0-100.");
	  public static string MaxFreqLabel => T("处理器最大频率", "處理器最大頻率", "Maximum Processor Frequency");
	  public static string MaxFreqHint => T(
	    "0=不限制(自动)。可自定义输入 MHz 数值。",
	    "0=不限制(自動)。可自訂輸入 MHz 數值。",
	    "0=Unlimited (Auto). Custom MHz value.");
	  public static string SmtPolicyLabel => T("SMT 线程启动策略", "SMT 執行緒啟動策略", "SMT Thread Unpark Policy");
	  public static string SmtPolicyHint => T(
	    "0=核心(优先物理核) / 1=每个线程的核心 / 2=循环配置(均衡负载) / 3=顺序",
	    "0=核心(優先物理核) / 1=每個執行緒的核心 / 2=循環配置(均衡負載) / 3=順序",
	    "0=Core(physical first) / 1=Per-thread / 2=Round-robin / 3=Sequential");
	  public static string ButtonApply => T("应用", "應用", "Apply");
	  public static string ButtonView => T("查看", "查看", "View");
	  // GPU overclock
	  public static string GpuCoreOcDesc => T(
	    "调节核心频率，影响性能与功耗。⚠️ 超频存在不稳定风险。",
	    "調節核心頻率，影響效能與功耗。⚠️ 超頻存在不穩定風險。",
	    "Adjust core clock for performance/power. ⚠️ Instability risk when overclocking.");
	  public static string GpuMemOcDesc => T(
	    "调节显存频率，影响高画质流畅度。⚠️ 过度超频可能导致花屏或闪退。",
	    "調節記憶體頻率，影響高畫質流暢度。⚠️ 過度超頻可能導致花屏或閃退。",
	    "Adjust memory clock for high-res smoothness. ⚠️ Excessive OC may cause artifacts.");
		  public static string GfxAdvOptimus => T("NVIDIA Advanced Optimus", "NVIDIA Advanced Optimus", "NVIDIA Advanced Optimus");
	  public static string TgpHardwareLabel => T("TGP / PPAB", "TGP / PPAB", "TGP / PPAB");
	  public static string TgpDesc => T(
	    "调节显卡总功耗及动态功耗分配策略。",
	    "調節顯示卡總功耗及動態功耗分配策略。",
	    "Adjust total GPU power and dynamic power distribution.");
	  // Perf action button tooltips
	  public static string PerfBtnResetDefaultsTip => T("恢复默认预设并清空自定义预设", "恢復預設預設並清空自訂預設", "Reset to defaults and clear custom presets");
	  public static string PerfBtnReloadTip => T("重新加载当前预设的值", "重新載入目前預設的值", "Reload current preset values");
	  public static string PerfBtnDeleteTip => T("删除当前预设", "刪除目前預設", "Delete current preset");
	  public static string PerfBtnSaveTip => T("保存当前设置为新预设并应用", "儲存目前設定為新預設並套用", "Save current as new preset and apply");
	  public static string PerfBtnResetText => T("恢复", "恢復", "Reset");
	  public static string PerfBtnReloadText => T("加载", "載入", "Reload");
	  public static string PerfBtnDeleteText => T("删除", "刪除", "Delete");
	  public static string PerfBtnSaveText => T("保存", "儲存", "Save");

	  // ═══ Phase 3: PerfPage.xaml.cs status messages ═══
	  public static string PerfTgpStatusFormat(bool tgp, bool ppab, int dstate, string tpp) => T(
	    $"TGP={(tgp ? "开" : "关")}, PPAB={(ppab ? "开" : "关")}, dState={(dstate == 2 ? "低功耗" : "标准")}{tpp}",
	    $"TGP={(tgp ? "開" : "關")}, PPAB={(ppab ? "開" : "關")}, dState={(dstate == 2 ? "低功耗" : "標準")}{tpp}",
	    $"TGP={(tgp ? "On" : "Off")}, PPAB={(ppab ? "On" : "Off")}, dState={(dstate == 2 ? "LowPower" : "Standard")}{tpp}");
	  public static string PerfStatusUnavailable => T("不可用", "不可用", "Unavailable");
	  public static string PerfStatusCurrent => T("当前: ", "目前: ", "Current: ");
	  public static string PerfPowerPlanSelectFirst => T("请先选择电源计划", "請先選擇電源計劃", "Please select a power plan first");
	  public static string PerfPowerPlanApplied(string dcac) => T(
	    $"已应用{dcac}设置", $"已應用{dcac}設置", $"Applied {dcac} settings");
	  public static string PerfPowerPlanApplyFailed(string msg) => T(
	    $"应用失败: {msg}", $"應用失敗: {msg}", $"Apply failed: {msg}");
	  // Dialog messages
	  public static string PerfDeleteBuiltinPreset => T("内置预设不可删除。请先切换到自定义预设。", "內建預設不可刪除。請先切換到自訂預設。", "Cannot delete built-in preset. Switch to a custom preset first.");
	  public static string PerfDeleteConfirmMsg(string name) => T(
	    $"确认删除预设 {name}？", $"確認刪除預設 {name}？", $"Delete preset {name}?");
	  public static string PerfDeleteConfirmTitle => T("删除预设", "刪除預設", "Delete Preset");
	  public static string PerfUndoApplyMsg => T("将撤销本次 Apply 操作...", "將撤銷本次 Apply 操作...", "This will undo the last Apply...");
	  public static string PerfUndoApplyTitle => T("撤销应用", "撤銷應用", "Undo Apply");
	  public static string PerfResetDefaultsMsg => T("将恢复到默认性能预设...", "將恢復到預設效能預設...", "Will restore default performance preset...");
	  public static string PerfResetDefaultsTitle => T("恢复默认预设", "恢復預設預設", "Reset Default Preset");

	  // ═══ Phase 4: Misc files ═══
	  public static string FanPresetLabel => T("预设:", "預設:", "Preset:");
	  public static string FanImportTooltip => T("从 JSON 文件或剪贴板分享码导入风扇曲线", "從 JSON 檔案或剪貼簿分享碼導入風扇曲線", "Import fan curve from JSON or clipboard share code");
	  public static string FanExportTooltip => T("将当前风扇曲线保存为 JSON 文件", "將目前風扇曲線儲存為 JSON 檔案", "Save current fan curve to JSON file");
	  public static string FanShareTooltip => T("生成分享码并复制到剪贴板", "生成分享碼並複製到剪貼簿", "Generate share code and copy to clipboard");
	  public static string FanResponseFast => T("0.1 (快)", "0.1 (快)", "0.1 (Fast)");
	  public static string FanResponseMedium => T("0.3 (中)", "0.3 (中)", "0.3 (Medium)");
	  public static string FanResponseSlow => T("0.5 (慢)", "0.5 (慢)", "0.5 (Slow)");
	  public static string FanShareNoData => T("当前无可导出的曲线数据", "目前無可導出的曲線數據", "No curve data to export");
	  public static string FanShareCodeDetected(string code) => T(
	    $"检测到剪贴板中有分享码：\n{code}",
	    $"檢測到剪貼簿中有分享碼：\n{code}",
	    $"Share code detected in clipboard:\n{code}");
	  public static string FanShareNoDataToShare => T("当前无可分享的曲线数据", "目前無可分享的曲線數據", "No curve data to share");
	  public static string FanShareGenerateFail => T("生成分享码失败", "生成分享碼失敗", "Failed to generate share code");
	  public static string FanShareWindowTitle => T("分享码", "分享碼", "Share Code");
	  public static string FanShareCopyInstruction => T("手动复制以下分享码：", "手動複製以下分享碼：", "Manually copy the code below:");
	  public static string FanShareClose => T("关闭", "關閉", "Close");
	  public static string FanShareInvalidCode => T("无效的分享码", "無效的分享碼", "Invalid share code");
	  public static string HelpWindowTitleBar => T("OMEN X Hub", "OMEN X Hub", "OMEN X Hub");
	  public static string HelpCreditsGuiDesign => T("OMEN X Hub GUI设计，功能打磨", "OMEN X Hub GUI設計，功能打磨", "OMEN X Hub GUI design & polishing");
	  public static string HelpCreditsSuperHub => T("OmenSuperHub 提供本项目主要框架及代码", "OmenSuperHub 提供本項目主要框架及程式碼", "OmenSuperHub — core framework & code");
	  public static string HelpCreditsOmenMon => T("OmenMon OmenHwCtl - 本项目的主要灵感来源，提供了交互命令与探索OGH交互的方法。", "OmenMon OmenHwCtl - 本項目的主要靈感來源，提供了交互命令與探索OGH交互的方法。", "OmenMon OmenHwCtl — main inspiration, OGH interaction commands & methods.");
	  public static string HelpCreditsLhm => T("硬件监控核心库支持", "硬體監控核心庫支援", "Hardware monitoring core library support");
	  public static string HelpCreditsWpfUi => T("WPF UI 界面框架，提供 Fluent / Mica 等现代控件", "WPF UI 介面框架，提供 Fluent / Mica 等現代控件", "WPF UI — Fluent / Mica modern control framework");
	  public static string HelpCreditsLlt => T("Lenovo Legion Toolkit — UI 设计与功能实现的参考项目", "Lenovo Legion Toolkit — UI 設計與功能實現的參考專案", "Lenovo Legion Toolkit — reference for UI design & feature implementation");
	  public static string HelpCreditsMemReduct => T("MemReduct — 内存清理思路参考", "MemReduct — 記憶體清理思路參考", "MemReduct — memory cleanup reference");

	  public static string MacroNone => T("(none)", "(none)", "(none)");
	  public static string MacroNewMacro => T("New Macro", "New Macro", "New Macro");
	  
	  public static string FanDragHint => T("可右键创建或删除控制点", "可右鍵創建或刪除控制點", "Right-click to add or delete control points");
	  public static string FanModeChangeHint => T("💡 拖拽调整后需切换一次风扇模式（如切到静音再切回自定义）才能实时生效", "💡 拖拽調整後需切換一次風扇模式（如切到靜音再切回自訂）才能即時生效", "⚠ After dragging, switch fan mode (e.g. to Silent then back to Custom) to apply changes immediately");
	  public static string MacroListHeading => T("宏列表", "巨集列表", "Macro List");
	  // ponytail: Automation missing strings
	  public static string AutoEnableHeading => T("启用自动化", "啟用自動化", "Enable Automation");
	  public static string AutoEnableDesc => T("只有在本程序运行时，自动化才可生效。", "只有在本程式運行時，自動化才可生效。", "Automation only works while this app is running.");
	  public static string AutoNoPipelinesText => T("当前没有自动化脚本，请点击「新建」来新建一项。", "目前沒有自動化腳本，請點擊「新建」來新建一項。", "No automation pipelines yet. Click New to create one.");
	  public static string AutoNoQuickActionsText => T("没有快捷操作，请点击「新建」来新建快捷操作。", "沒有快捷操作，請點擊「新建」來新建快捷操作。", "No quick actions yet. Click New to create one.");
	  public static string AutoAddNew => T("新建", "新建", "New");
	  public static string AutoAddDisabledTooltip => T("需先启用顶部的「启用自动化」开关才能新建", "需先啟用頂部的「啟用自動化」開關才能新建", "Enable \"Enable Automation\" above to create new pipelines");
	  public static string AutoQuickActionsDesc => T("你可以在系统托盘的图标上右键来快速触发这些快捷操作。", "你可以在系統托盤的圖示上按右鍵來快速觸發這些快捷操作。", "Right-click the tray icon to quickly trigger these actions.");
	  
	  // ponytail: PerfPage C-State combo items used in code-behind
	  // Boost mode combo items
	  public static string PerfBoostDisabled => T("已禁用 (关闭睿频)", "已禁用 (關閉睿頻)", "Disabled (No Turbo)");
	  public static string PerfBoostEnabled => T("已启用 (适中)", "已啟用 (適中)", "Enabled (Moderate)");
	  public static string PerfBoostHighPerf => T("高性能 (积极)", "高效能 (積極)", "High Perf (Aggressive)");
	  public static string PerfBoostHighEff => T("高效率", "高效率", "High Efficiency");
	  public static string PerfBoostHighPerfEff => T("高性能高效率", "高效能高效率", "High Perf+Efficiency");
	  public static string PerfBoostAggressive => T("积极且有保障 (满血)", "積極且有保障 (滿血)", "Aggressive (Unleashed)");
	  public static string PerfBoostEffAggressive => T("高效积极且有保障", "高效積極且有保障", "Efficient + Aggressive");
	  // SMT policy combo
	  public static string PerfSmtCore => T("核心 (优先物理核)", "核心 (優先物理核)", "Core (Physical First)");
	  public static string PerfSmtPerThread => T("每个线程的核心", "每個執行緒的核心", "Per-thread Core");
	  public static string PerfSmtRoundRobin => T("循环配置 (均衡负载)", "循環配置 (均衡負載)", "Round-robin (Balanced)");
	  public static string PerfSmtSequential => T("顺序", "順序", "Sequential");
	  // EPP combo items
	  public static string PerfEppInstant => T("极速响应 (0)", "極速響應 (0)", "Instant (0)");
	  public static string PerfEppPerf => T("偏向性能 (20)", "偏向效能 (20)", "Performance (20)");
	  public static string PerfEppBalanced => T("平衡 (50)", "平衡 (50)", "Balanced (50)");
	  public static string PerfEppPowerSave => T("偏向省电 (80)", "偏向省電 (80)", "Power Save (80)");
	  public static string PerfEppMaxSave => T("极致省电 (100)", "極致省電 (100)", "Max Power Save (100)");
	  // Max Freq combo
	  public static string PerfMaxFreqAuto => T("不限制 (自动)", "不限制 (自動)", "Unlimited (Auto)");

	}
}
