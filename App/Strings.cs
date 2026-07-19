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
    public static string ProductUnsupported => T(
        "您的设备不是 HP 产品，本程序可能无法正常工作。是否继续？",
        "您的設備不是 HP 產品，本程式可能無法正常工作。是否繼續？",
        "Your device is not an HP product. This program may not function correctly. Continue anyway?");
    public static string ProductUnsupportedHP => T(
        "您的 HP 机型非惠普游戏笔记本，可能无法正常使用。是否继续？",
        "您的 HP 機型非惠普遊戲筆記本，可能無法正常使用。是否繼續？",
        "Your HP model is not an HP gaming laptop. Some features may not work properly. Continue anyway?");
    public static string ProductOldOmen => T(
        "您的设备属于旧款 OMEN 产品，部分功能可能无法使用。是否继续运行程序？",
        "您的設備屬於舊款 OMEN 產品，部分功能可能無法使用。是否繼續執行程式？",
        "Your device is an older OMEN product. Some features may not be available. Do you want to continue?");
    public static string ProductUnsupportedWarning => T(
        "部分功能本机型可能不适配请注意！",
        "部分功能本機型可能不適請注意！",
        "Some features may not be compatible with this model. Please note!");

    // Menus
    public static string FanConfig => T("风扇配置", "風扇配置", "Fan Config");
    public static string FanControl => T("风扇控制", "風扇控制", "Fan Control");
    public static string PerfControl => T("性能控制", "效能控制", "Performance");
    public static string PowerStatus => T("电源状态", "電源狀態", "Power Status");
    public static string HwMonitor => T("硬件监控", "硬體監控", "HW Monitor");
    public static string FloatingBar => T("浮窗显示", "浮窗顯示", "Overlay");
    public static string OmenKeyMenu => T("Omen键", "Omen鍵", "Omen Key");
    public static string OtherSettings => T("其他设置", "其他設定", "Settings");
    public static string Help => T("帮助", "說明", "Help");
    public static string Exit => T("退出", "結束", "Exit");
    public static string LanguageMenu => T("语言", "語言", "Language");
	    public static string LangSimplified => T("简体中文", "简体中文", "简体中文");
	    public static string LangTraditional => T("繁體中文", "繁體中文", "繁體中文");
	    public static string LangEnglish => T("English", "English", "English");
	    public static string LangRestartHint => T("💡 切换语言后需重启程序生效", "💡 切換語言後需重啟程式生效", "💡 Restart required after changing language");
    public static string Hint => T("提示", "提示", "Info");
    public static string Warning => T("警告", "警告", "Warning");
    public static string Error => T("错误", "錯誤", "Error");

    // Presets
    public static string PresetsMenu => T("预设", "預設", "Presets");
    public static string PresetExtreme => T("极致性能", "極致性能", "Extreme Performance");
    public static string PresetGpuPriority => T("GPU优先", "GPU優先", "GPU Priority");
    public static string PresetLightUse => T("轻度使用", "輕度使用", "Light Use");
    public static string PresetCustom1 => T("自定义预设1", "自定義預設1", "Custom 1");
    public static string PresetCustom2 => T("自定义预设2", "自定義預設2", "Custom 2");
    public static string PresetCustom3 => T("自定义预设3", "自定義預設3", "Custom 3");
    public static string RenamePreset => T("重命名", "重新命名", "Rename");
    public static string RenamePresetTitle => T("重命名预设", "重新命名預設", "Rename Preset");
    public static string RenamePresetPrompt => T("请输入新的预设名称：", "請輸入新的預設名稱：", "Please enter new preset name:");
    public static string RenamePresetError => T("预设名称不能为空，且不能与其他预设同名。", "預設名稱不能為空，且不能與其他預設同名。", "Preset name cannot be empty and must be unique.");
    public static string PresetNote => T(
        "💡预设包括除DB版本之外的风扇配置、风扇控制、性能控制选项。",
        "💡預設包含DB版本以外的風扇配置、風扇控制、效能控制選項。",
        "💡 Presets include fan config, fan control, and performance control options—excluding the DB version.");
    public static string PresetInternalNote => T(
        "💡只有自定义预设能永久保存设置并额外包括硬件监控配置，内置预设的改动会在下一次切换预设时丢失！",
        "💡只有自訂預設能永久儲存設定並額外包含硬體監控配置，內建預設的變更會在下次切換預設時遺失！",
        "💡 Only custom presets permanently save settings and include hardware monitoring configurations; changes made to built-in presets will be lost the next time you switch presets.");
    public static string PresetExtremeTooltip => T(
        "完全释放性能，甚至可以尝试继续调高CPU功率。",
        "完全釋放效能，甚至可以嘗試繼續調高CPU功率。",
        "Unleash full performance—you can even try further increasing the CPU power.");
    public static string PresetGpuPriorityTooltip => T(
        "散热不足的情况下优先保证GPU性能，适当降低CPU功耗。",
        "散熱不足的情況下優先確保GPU效能，適當降低CPU功耗。",
        "In scenarios with insufficient cooling, priority is given to maintaining GPU performance while appropriately reducing CPU power consumption.");
    public static string PresetLightUseTooltip => T(
        "降低整体功耗，适合需要安静的场景。",
        "降低整體功耗，適合需要安靜的場景。",
        "Reduces overall power consumption, making it suitable for environments requiring quiet operation.");

    // Fan
    public static string FanSilentMode => T("安静模式", "安靜模式", "Silent Mode");
    public static string FanCoolMode => T("降温模式", "降溫模式", "Cool Mode");
    public static string FanResponseSpeed => T("风扇响应速度", "風扇響應速度", "Fan Response Speed");
    public static string FanRespRealtime => T("实时", "即時", "Realtime");
    public static string FanRespHigh => T("高", "高", "High");
    public static string FanRespMedium => T("中", "中", "Medium");
    public static string FanRespLow => T("低", "低", "Low");
    public static string FanAuto => T("自动", "自動", "Auto");
    public static string FanMax => T("最大风扇", "最大風扇", "Max Fan");
    public static string FanCustomCurve => T("智能自定义曲线", "智能自訂曲線", "Smart Custom Curve");
    public static string FanManualMode => T("手动模式", "手動模式", "Manual Mode");
    public static string FanSmartMode => T("智能风扇", "智能風扇", "Smart Fan");
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
    public static string FanAutoProtectNote => T(
        "💡开启后，若CPU温度过高且风扇处于固定转速且低于80%最大转速，OSH将自动切换为降温模式和自动风扇控制。",
        "💡開啟後，若CPU溫度過高且風扇處於固定轉速且低於80%最大轉速，OSH將自動切換為降溫模式和自動風扇控制。",
        "💡 When enabled, OSH automatically switches to Cool mode and Auto fan control if CPU temperature is too high and fans are running at a fixed low speed.");
    public static string FanAutoProtectOn => T("开启保护", "開啟保護", "Enable Protection");
    public static string FanAutoProtectOff => T("关闭保护", "關閉保護", "Disable Protection");
    public static string FanSilentTooltip => T(
        "安静模式下风扇转速较低，对应 silent.txt 文件，可以通过修改文件改变转速曲线。",
        "安靜模式下風扇轉速較低，對應 silent.txt 檔案，可以透過修改檔案改變轉速曲線。",
        "Silent mode uses lower fan speeds. Edit silent.txt to customize the fan curve.");
    public static string FanCoolTooltip => T(
        "降温模式下风扇转速较高，对应 cool.txt 文件，可以通过修改文件改变转速曲线。",
        "降溫模式下風扇轉速較高，對應 cool.txt 檔案，可以透過修改檔案改變轉速曲線。",
        "Cool mode uses higher fan speeds. Edit cool.txt to customize the fan curve.");
    public static string SetFanSpeedSlider => T("拖动滑块设置转速 (RPM)", "拖動滑桿設定轉速 (RPM)", "Drag slider to set speed (RPM)");
    public static string CurrentSliderValueTemp => T("滑块值：{0}", "滑桿值：{0}", "Slider Value: {0}");

    // Clean Creek
    public static string CleanCreekMenuItem => T("反转除尘", "反轉除塵", "Clean Creek");
    public static string CleanCreekLegacyMenuItem => T("反转除尘（旧版）", "反轉除塵（舊版）", "Clean Creek (Legacy)");
    public static string CleanCreekTitle => T("风扇除尘", "風扇除塵", "Fan Dust Removal");
    public static string CleanCreekConfirmMessage => T(
        "即将开始反转除尘。点击确定开始，要停止除尘请选择「取消」。",
        "即將開始反轉除塵。點擊確定開始，要停止除塵請選擇「取消」。",
        "Reverse dust removal will start soon. Click OK to start, or Cancel to stop.");
    public static string CleanCreekProgressMessageTemplate => T(
        "清洁进行中，剩余 {0} 秒...",
        "清潔進行中，剩餘 {0} 秒...",
        "Cleaning in progress, {0} seconds remaining...");
    public static string CleanCreekStopButton => T("停止", "停止", "Stop");

    // Performance
    public static string HotSwitch => T("热切换", "熱切換", "Hot Switch");
    public static string GraphicsMode => T("图形模式", "圖形模式", "Graphics Mode");
    public static string GfxDiscreteMode => T("独显直连", "獨顯直連", "Discrete GPU");
    public static string GfxHybridMode => T("混合模式", "混合模式", "Hybrid Mode");
    public static string GfxOnlyInternal => T("💡仅部分机型支持在此修改图形模式（需重启），若不支持可在BIOS设置修改。",
        "💡僅部分機型支援在此修改圖形模式（需重啟），若不支援可在BIOS設定修改。",
        "💡 Only some models support switching graphics mode here (requires reboot). Use BIOS otherwise.");
    public static string GfxUMAConfirm => T("仅集成显卡启用，屏蔽独显，该模式下 HDMI 输出将无法工作。确定切换吗?",
        "僅整合顯示啟用，遮蔽獨顯，此模式下 HDMI 輸出將無法運作。確定切換嗎？",
        "Only iGPU will be active. HDMI output will not work in UMA mode. Confirm switch?");
    public static string GfxUMATitle => T("切换到UMA模式", "切換至UMA模式", "Switch to UMA Mode");
    public static string GfxSwitchedTo(string mode) => T(
        $"已切换到{mode}模式，重启生效。", $"已切換至{mode}模式，重啟生效。", $"Switched to {mode} mode. Reboot to apply.");
    public static string GfxUnsupported => T("该机器不支持系统内冷切！",
        "此機型不支援系統內冷切！", "This model does not support in-OS graphics switching!");
    public static string CpuPowerMenu => T("CPU功率", "CPU功率", "CPU Power");
    public static string SetCpuPowerSlider => T("拖动滑块设置功率 (W)", "拖動滑桿設定功率 (W)", "Drag slider to set power (W)");
    public static string SetTppSlider => T("拖动滑块设置功率 (W)", "拖動滑桿設定功率 (W)", "Drag slider to set power (W)");
    public static string SetGpuClockSlider => T("拖动滑块设置频率 (MHz)", "拖動滑桿設定頻率 (MHz)", "Drag slider to set clock (MHz)");
    public static string SetMaxFrameRateSlider => T("拖动滑块设置最大帧率", "拖動滑桿設定最大幀率", "Drag slider to set max frame rate");
    public static string NotSet => T("不设置", "不設定", "Not Set");
    public static string Maximum => T("最大", "最大", "Maximum");
    public static string Enable => T("开启", "開啟", "Enable");
    public static string Disable => T("关闭", "關閉", "Disable");
    public static string Normal => T("正常", "正常", "Normal");
    public static string LowPower => T("低功耗", "低功耗", "Low Power");
    public static string Unlimited => T("无限制", "無限制", "Unlimited");
    public static string IccMaxMenu => T("IccMax", "IccMax", "IccMax");
    public static string AcLoadLineMenu => T("AC Load Line", "AC Load Line", "AC Load Line");
    public static string PpabPowerMenu => T("PPab条件(Tpp)", "PPab條件(Tpp)", "PPab (Tpp)");
    public static string DStateSubMenu => T("dState", "dState", "dState");
    public static string DbVersionMenu => T("DB版本", "DB版本", "DB Version");
    public static string DbNormal => T("普通版本", "普通版本", "Normal");
    public static string DbUnlocked => T("解锁版本", "解鎖版本", "Unlocked");
    public static string GpuClockMenu => T("GPU频率限制", "GPU頻率限制", "GPU Clock Limit");
    public static string GpuCoreOverclock => T("GPU核心超频", "GPU核心超頻", "GPU Core Overclock");
    public static string GpuMemoryOverclock => T("GPU显存超频", "GPU記憶體超頻", "GPU Memory Overclock");
    public static string SetGpuCoreOverclockSlider => T("拖动滑块设置GPU核心超频 (MHz)", "拖動滑桿設定GPU核心超頻 (MHz)", "Drag slider to set GPU core overclock (MHz)");
    public static string SetGpuMemoryOverclockSlider => T("拖动滑块设置GPU显存超频 (MHz)", "拖動滑桿設定GPU記憶體超頻 (MHz)", "Drag slider to set GPU memory overclock (MHz)");
    public static string MaxFrameRateMenu => T("最大帧率", "最大幀率", "Max Frame Rate");
    public static string GpuPowerControlMenu => T("GPU功率控制", "GPU功率控制", "GPU Power Control");
    public static string GpuAppsMenu => T("占用GPU的程序", "佔用GPU的程式", "GPU Processes");
    public static string GpuAppsNone => T("无", "無", "None");
    public static string GpuRestartMenu => T("重启显卡", "重啟顯示卡", "Restart GPU");
    public static string GpuRestartTooltip => T("通过重启独立 GPU 减少不必要的占用 GPU 情况。",
        "透過重啟獨立 GPU 減少不必要的 GPU 佔用情況。",
        "Restart the discrete GPU to reduce unnecessary usage.");
    public static string GpuRestartConfirm => T(
        "可能会导致应用崩溃，请尽可能通过手动关闭占用进程来解除独立显卡占用，建议只在混合模式下操作。确定重启显卡吗?",
        "可能導致應用程式崩潰，建議先手動關閉佔用程式，且只在混合模式下操作。確定重啟顯示卡嗎？",
        "This may crash running applications. Close GPU processes manually if possible. Proceed?");
    public static string GpuRestartTitle => T("重启显卡", "重啟顯示卡", "Restart GPU");
    public static string GpuCloseConfirm(string name) => T(
        $"是否关闭进程 {name}?", $"是否關閉程序 {name}？", $"Close process {name}?");
    public static string GpuCloseTitle => T("关闭确认", "關閉確認", "Confirm Close");
    public static string GpuCloseError(string msg) => T(
        $"关闭进程失败: {msg}", $"關閉程序失敗: {msg}", $"Failed to close process: {msg}");
    public static string DbUnlockTitle => T("解锁DB", "解鎖DB", "Unlock DB");
    public static string PleaseConnectAC => T("请连接交流电源", "請連接交流電源", "Please connect AC power");
    public static string DbUnlockCpuHighWarning => T("请在CPU低负载下解锁",
        "請在CPU低負載下解鎖", "Please unlock under low CPU load.");
    public static string DbUnlockFailed(float w) => T(
        $"功耗异常，解锁失败，请重新尝试！\n当前显卡功耗限制为：{w:F2} W ！",
        $"功耗異常，解鎖失敗，請重新嘗試！\n當前顯示卡功耗限制為：{w:F2} W！",
        $"Power limit anomaly. Unlock failed. Current GPU power limit: {w:F2} W. Please retry.");
    public static string DbUnlockSuccessNoAutoStart => T(
        "解锁成功！但当前未设置开机自启，解锁后若重启电脑会导致功耗异常，需要重新解锁！",
        "解鎖成功！但目前未設定開機自啟，重啟電腦後功耗將恢復限制，需重新解鎖！",
        "Unlock successful! However, autostart is not enabled. Rebooting will reset the power limit and require re-unlocking.");
    public static string DbNo50Series => T("不支持英伟达50系及以后的显卡解锁DB！",
        "不支援 NVIDIA 50 系列及以後的顯示卡解鎖 DB！",
        "Unlocking DB is not supported for NVIDIA 50 series and later GPUs!");
    public static string DriverNotAllow => T("当前驱动版本不满足需求，无法执行此操作。当前驱动版本：",
        "當前驅動版本不滿足需求，無法執行此操作。目前驅動版本：",
        "Driver version does not meet requirements. Current version: ");
    public static string DriverNotFound => T("未检测到NVIDIA驱动版本。",
        "未檢測到NVIDIA驅動版本。",
        "NVIDIA driver version not found.");
    public static string CheckDriverFailed => T("无法检查NVIDIA驱动版本。",
        "無法檢查NVIDIA驅動版本。",
        "Failed to check NVIDIA driver version.");
    public static string DeviceNotFound => T("未找到NVIDIA显卡设备。",
        "未找到NVIDIA顯示卡設備。",
        "NVIDIA GPU device not found.");

    // High Temp Balloon
    public static string HighTempBalloonTitle => T("温度过高警告", "溫度過高警告", "High Temperature Warning");
    public static string HighTempBalloonText(int limit, float temp) => T(
        $"检测到CPU温度高于{limit - 5}℃ ({temp:F1}℃)，且风扇处于固定转速状态，OSH已自动切换为降温模式并将风扇控制切换为自动模式。",
        $"偵測到CPU溫度高於{limit - 5}℃ ({temp:F1}℃)，且風扇處於固定轉速狀態，OSH已自動切換至降溫模式並將風扇控制改為自動。",
        $"CPU temperature exceeded {limit - 5}°C ({temp:F1}°C) with a fixed fan speed. OSH has switched to Cool mode and Auto fan control.");

    // Performance tooltips
    public static string PerfCpuPowerTip => T("💡可分别调节PL1与PL2，点击下拉展开双滑块。直接选预设值则PL1=PL2。",
        "💡可分別調節PL1與PL2，點擊下拉展開雙滑塊。直接選預設值則PL1=PL2。",
        "💡PL1 and PL2 can be set independently. Expand the dropdown for dual sliders. Preset values set PL1=PL2.");
    public static string PerfTgpTip => T("💡关闭可降低GPU最大功耗。",
        "💡關閉可降低GPU最大功耗。", "💡 Disable to reduce GPU max power.");
    public static string PerfPpabTip => T("💡关闭可降低GPU最大功耗。",
        "💡關閉可降低GPU最大功耗。", "💡 Disable to reduce GPU max power.");
    public static string PerfTppTip => T("💡改变Ppab/DB增益点，即 GPU 功率在 CPU 功率低于多少时获得额外的Ppab/DB功耗。",
        "💡改變Ppab/DB增益點，即 GPU 功率在 CPU 功率低於多少時獲得額外的Ppab/DB功耗。",
        "💡 Adjusts the Ppab/DB gain point: the CPU power threshold below which GPU gets additional Ppab/DB power.");
    public static string PerfDStateTip => T("💡选择低功耗将把GPU功率限制在一个较低水平。",
        "💡選擇低功耗將把GPU功率限制在一個較低水平。",
        "💡 Low power mode restricts GPU power to a lower level.");
    public static string PerfDbTip => T("💡你的设备支持Ppab条件更改，请优先选择增大Ppab条件中的功率而不是更改DB版本，两者效果相同。",
        "💡你的設備支援Ppab條件更改，請優先選擇增大Ppab條件中的功率而不是更改DB版本，兩者效果相同。",
        "💡 Your device supports Ppab condition adjustment. Prefer increasing Ppab condition power over changing DB version — same effect.");
    public static string PerfDbUnlockWarning => T("\n警告：一旦解锁DB，只能通过安装一次显卡驱动恢复到原始状态，确认继续吗？",
        "\n警告：一旦解鎖DB，只能透過安裝一次顯示卡驅動恢復到原始狀態，確認繼續嗎？",
        "\nWarning: Once DB is unlocked, you can only restore to original state by reinstalling graphics driver once. Continue?");
    public static string PerfDbUnlockTooltip => T("解锁DB可以在CPU功率较高时避免GPU功率降低。",
        "解鎖DB可以在CPU功率較高時避免GPU功率降低。",
        "Unlocking DB prevents GPU power reduction when CPU power is high.");
    public static string PerfDbNormalTooltip => T("该选项可以重新恢复系统分配功耗的状态。",
        "該選項可以重新恢復系統分配功耗的狀態。",
        "This option restores the system's default power allocation.");
    public static string RestartGPUFailed => T("重启显卡失败。",
        "重啟顯示卡失敗。",
        "Failed to restart GPU.");
    public static string NoCustomIcon => T("不存在自定义图标custom.ico",
        "找不到自訂圖示 custom.ico", "Custom icon file custom.ico not found.");
    public static string KeyboardConnectFail => T("键盘连接失败！", "鍵盤連線失敗！", "Keyboard connection failed!");
    public static string CrashMessage => T("OXH出现意外错误，详细信息请查看日志文件。",
        "OXH出現意外錯誤，詳細資訊請查看日誌檔案。",
        "OXH encountered an unexpected error. Check log file for details.");
    public static string DdsInitFail => T(
        "无法初始化 Advanced Optimus 小程序。请确保所有NVIDIA驱动程序均为最新版本，并将BIOS设置菜单中的“图形模式”设置为\"Nvidia Advanced Optimus\"。",
        "無法初始化 Advanced Optimus 小程式。請確認所有 NVIDIA 驅動程式均為最新版本，並在 BIOS 設定中將「圖形模式」設為 \"Nvidia Advanced Optimus\"。",
        "Failed to initialize the Advanced Optimus applet. Make sure all NVIDIA drivers are up to date and set the Graphics Mode to \"Nvidia Advanced Optimus\" in BIOS.");
    public static string HelpWindowTitle => T("OXH 帮助", "OXH 說明", "OXH Help");
    public static string HelpTabUpdates => T("更新说明", "更新說明", "Changelog");
    public static string HelpTabFanConfig => T("风扇配置", "風扇配置", "Fan Config");
    public static string HelpTabFanControl => T("风扇控制", "風扇控制", "Fan Control");
    public static string HelpTabPerformance => T("性能控制", "效能控制", "Performance");
    public static string HelpTabOther => T("其他", "其他", "Other");
    public static string HelpTabCredits => T("致谢", "致謝", "Credits");
    public static string HelpBtnGitHub => T("GitHub", "GitHub", "GitHub");
    public static string HelpBtnCheckUpdate => T("检查更新", "檢查更新", "Check Updates");
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
    public static string TempDisplay => T("温度显示", "溫度顯示", "Temp Display");
    public static string TempSmoothed => T("平滑值", "平滑值", "Smoothed");
    public static string TempRaw => T("原始值", "原始值", "Raw");
    public static string MonitorClosed => T("监控已关闭", "監控已關閉", "Monitor Disabled");
    public static string MonitorPrepareLabel => T("准备中...", "準備中...", "Preparing...");
    public static string MonitorAutoFanWarning => T("当前为自动转速模式，若要关闭监控需切换为其他转速控制模式。",
        "目前為自動轉速模式，若要關閉監控需切換為其他轉速控制模式。",
        "Fan is in auto mode. Switch to another fan control mode before disabling monitoring.");

    // Floating
    public static string FloatingShow => T("显示浮窗", "顯示浮窗", "Show Overlay");
    public static string FloatingHide => T("关闭浮窗", "關閉浮窗", "Hide Overlay");
    public static string FloatingLocLeft => T("左上角", "左上角", "Top Left");
    public static string FloatingLocRight => T("右上角", "右上角", "Top Right");
    public static string FloatingLocFree => T("自由", "自由", "Free");
    public static string FloatingLocTopCenter => T("上方居中", "上方居中", "Top Center");
    public static string FloatLayoutHeading => T("浮窗布局", "浮窗佈局", "Float Layout");
    public static string FloatLayoutRow => T("按行排列", "按行排列", "Horizontal");
    public static string FloatLayoutCol => T("按列排列", "按列排列", "Vertical");
    public static string FloatingScreen => T("显示器选择", "顯示器選擇", "Display");
    public static string FloatingScreenPrimary => T("主屏幕", "主螢幕", "Primary");
    public static string FormatScreenLabel(int index, string deviceName) => T(
      $"显示器 {index} ({deviceName})",
      $"顯示器 {index} ({deviceName})",
      $"Display {index} ({deviceName})");
    public static string FontSize24 => T("24号", "24號", "Size 24 font");
    public static string FontSize36 => T("36号", "36號", "Size 36 font");
    public static string FontSize48 => T("48号", "48號", "Size 48 font");
    public static string SetTextSizeSlider => T("拖动滑块设置字号", "拖動滑塊設置字號", "Drag the slider to set font size");

    // Omen Key
    public static string OmenKeyDefault => T("默认", "預設", "Default");
    public static string OmenKeyToggle => T("切换浮窗显示", "切換浮窗顯示", "Toggle Overlay");
    public static string OmenKeyNone => T("取消绑定", "取消綁定", "Unbound");
    public static string OmenKeyShowMain => T("显示主界面", "顯示主界面", "Show Main Window");
    public static string OmenKeyCycle => T("循环预设", "循環預設", "Cycle Presets");
    public static string OmenKeyLaunchApp => T("打开应用", "開啟應用程式", "Open App");
    public static string OmenKeySelectApp => T("选择应用", "選擇應用程式", "Select App");
    public static string OmenKeyNoAppSelected => T("未选择应用", "未選擇應用", "No App Selected");
    public static string OmenKeyPresetCandidates => T("候选预设", "候選預設", "Preset Candidates");
    public static string OmenKeyAppLaunchFailed(string msg) => T(
        $"打开应用失败：{msg}", $"開啟應用程式失敗：{msg}", $"Failed to launch app: {msg}");

    // Settings
    public static string IconMenu => T("图标", "圖示", "Icon");
    public static string IconOriginal => T("原版", "原版", "Default");
    public static string IconCustom => T("自定义图标", "自訂圖示", "Custom Icon");
    public static string IconDynamic => T("动态图标", "動態圖示", "Dynamic Icon");
    public static string DataLocalize => T("数据本地化", "資料本地化", "Data Localize");
    public static string AutoStart => T("开机自启", "開機自啟", "Autostart");

    // Lighting
    public static string LightingControl => T("灯光控制", "燈光控制", "Lighting Control");
    public static string LightingOn => T("开", "開", "On");
    public static string LightingOff => T("关", "關", "Off");
    public static string LightingBrightness => T("亮度", "亮度", "Brightness");
    public static string LightingStaticColor => T("静态颜色", "靜態顏色", "Static Color");
    public static string LightingAllZones => T("全局颜色", "全局顏色", "All Zones");
    public static string LightingZone => T("分区", "分割區", "Zone");
    public static string LightingCustom => T("自定义...", "自訂...", "Custom...");
    public static string LightingAnimation => T("动画效果", "動畫效果", "Animation");
    public static string LightingEffect => T("效果", "效果", "Effect");
    public static string LightingSpeed => T("速度", "速度", "Speed");
    public static string LightingDirection => T("方向", "方向", "Direction");
    public static string LightingTheme => T("主题", "主題", "Theme");
    public static string LightingWmiProtocol => T("WMI 协议", "WMI 協議", "WMI Protocol");
    public static string LightingProtocolBasic => T("四分区", "四分割區", "Basic 4-Zone");
    public static string LightingProtocolDojo => T("Dojo四分区", "Dojo四分割區", "Dojo 4-Zone");
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
    public static string LightingFourZoneKeyboard => T("四分区/单分区键盘", "四分割區/單分割區鍵盤", "4-Zone/1-Zone Keyboard");
    public static string LightingLightBar => T("灯条（测试功能）", "燈條（測試功能）", "Light Bar (Experimental)");
    // ponytail: capability-mismatch warnings surfaced by ApplyLightBtn_Click instead of silent drop
    public static string LightingCapabilityAnimBasic => T("当前协议（四分区 Basic）仅支持「星光」「波浪」两种动画，请改用 Dojo 协议或选择「无」", "當前協議（四分割區 Basic）僅支援「星光」「波浪」兩種動畫，請改用 Dojo 協議或選擇「無」", "The Basic 4-Zone protocol only supports Starlight and Wave animations. Switch to Dojo or pick None.");
    public static string LightingCapabilityAnimHpSdk => T("HP SDK 协议仅支持静态颜色，请改用 Dojo 或四分区协议，或将动画设为「无」", "HP SDK 協議僅支援靜態顏色，請改用 Dojo 或四分割區協議，或將動畫設為「無」", "The HP SDK protocol only supports static color. Switch to Dojo or Basic, or set animation to None.");
    public static string LightingBrightnessRangeTip => T("💡 亮度范围可能为0~100，也可能为100关228开",
        "💡 亮度範圍可能為0~100，也可能為100關228開",
        "💡 Brightness range may be 0-100, or 100=off, 228=on");

    // Keyboard types
    public static string KbTypeNormal => T("普通", "普通", "Normal");
    public static string KbTypeFourZoneWithNumpad => T("四分区带小键盘", "四分割區帶數字鍵", "4-Zone with Numpad");
    public static string KbTypeFourZoneWithoutNumpad => T("四分区无小键盘", "四分割區無數字鍵", "4-Zone without Numpad");
    public static string KbTypeRgbPerKey => T("单键 RGB", "單鍵 RGB", "Per-Key RGB");
    public static string KbTypeOneZoneWithNumpad => T("单分区带小键盘", "單分割區帶數字鍵", "1-Zone with Numpad");
    public static string KbTypeOneZoneWithoutNumpad => T("单分区无小键盘", "單分割區無數字鍵", "1-Zone without Numpad");
    public static string KbTypeUnknown => T("未知或不支持", "未知或不支援", "Unknown/Unsupported");

    // System Info
    public static string SysInfoTitle => T("总览", "總覽", "Overview");
    public static string SysManufacturer => T("品牌", "品牌", "Manufacturer");
    public static string SysModel => T("型号", "型號", "Model");
    public static string SysBiosVersion => T("BIOS 版本", "BIOS 版本", "BIOS Version");
    public static string SysCpuModel => T("CPU 型号", "CPU 型號", "CPU Model");
    public static string SysCpuCores => T("CPU 核心", "CPU 核心", "CPU Cores");
    public static string SysGpuList => T("GPU 列表", "GPU 列表", "GPU List");
    public static string SysAdapterPower => T("适配器功率", "適配器功率", "Adapter Power");
    public static string SysSensorTemps => T("传感器温度", "感測器溫度", "Sensor Temperatures");
    public static string SysCPUTemp => T("CPU 温度", "CPU 溫度", "CPU Temp");
    public static string SysGPUTemp => T("GPU 温度", "GPU 溫度", "GPU Temp");
    public static string SysUnknown => T("未知", "未知", "Unknown");
    public static string SysNotAvailable => T("不可用", "不可用", "N/A");
    public static string SysRefresh => T("刷新", "重新整理", "Refresh");
    public static string SysPresets => T("预设配置", "預設配置", "Preset Config");
    public static string SysSaveAsPreset => T("保存当前设置为自定义预设", "儲存目前設定為自訂預設", "Save Current as Custom Preset");
    public static string SysPresetSaved => T("已保存到预设：", "已儲存到預設：", "Saved to preset: ");
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
    public static string ValidationOldOmenProduct => T("旧 OMEN 機型", "舊 OMEN 機型", "Old Omen Product");
    public static string ValidationUnsupportedHPProduct => T("不支持的 HP 机型", "不支援的 HP 機型", "Unsupported HP Product");
    public static string ValidationUnsupported => T("不支持的机型", "不支援的機型", "Unsupported Product");
    public static string SysBoardProduct => T("主板产品号", "主機板型號", "Board Product");
    public static string SysCpuTjMax => T("CPU温度墙", "CPU溫度上限", "CPU Tjmax");
    public static string SysNvidiaTjMax => T("NVIDIA 温度墙", "NVIDIA 溫度上限", "NVIDIA Tjmax");
    public static string SysNvidiaPower => T("NVIDIA 功率限制", "NVIDIA 功率限制", "NVIDIA Power Limit");
    public static string SysNvidiaPowerLimitText(string limitsText) => T(
        $"NVIDIA 功率限制: {limitsText}",
        $"NVIDIA 功率限制: {limitsText}",
        $"NVIDIA Power Limit: {limitsText}");

    // Custom presets
    public static string CustomPresets => T("自定义预设", "自訂預設", "Custom Presets");
    public static string CustomSaveCurrent => T("保存当前", "儲存目前", "Save Current");
    public static string CustomRename => T("重命名", "重新命名", "Rename");
    public static string CustomApply => T("应用", "套用", "Apply");

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
    public static string GpuAutoStopTitle => T("状态更改提示", "狀態更改提示", "Status Change");
    public static string GpuAutoStopText => T(
        "检测到显卡进入低功耗状态，OSH已停止监控GPU以节约能源。",
        "偵測到顯示卡進入低功耗狀態，OSH已停止監控GPU以節約能源。",
        "GPU entered low-power state. OSH has stopped monitoring GPU to save energy.");
    public static string GpuAutoStartText => T(
        "检测到显卡连接到显示器，OSH已开始监控GPU。",
        "偵測到顯示卡連接到顯示器，OSH已開始監控GPU。",
        "GPU is now connected to a display. OSH has started monitoring GPU.");
    public static string MonitorCpuLabel => T("CPU", "CPU", "CPU");
    public static string MonitorGpuLabel => T("GPU", "GPU", "GPU");
    public static string MonitorFanLabel => T("风扇", "風扇", "Fan");
    public static string AcLoadLineBalloonTitle => T("AC Load Line 提示", "AC Load Line 提示", "AC Load Line Hint");
    public static string AcLoadLineBalloonText(int maxSupported, int current) => T(
        $"当前设备支持的最大 AC Load Line 为 {180 - 10 * maxSupported}，将尝试设置 {180 - 10 * current}。",
        $"目前裝置支援的最大 AC Load Line 為 {180 - 10 * maxSupported}，將嘗試設定 {180 - 10 * current}。",
        $"The maximum supported AC Load Line for this device is {180 - 10 * maxSupported}. Attempting to set {180 - 10 * current}.");
    public static string RestartGPUSuccess => T("重启显卡成功！", "重啟顯示卡成功！", "Restart GPU successful!");

    // Power status
    public static string PowerStatusAC => T("交流电源", "交流電源", "AC Power");
    public static string PowerStatusDC => T("电池", "電池", "Battery");

    // Fan page headings & labels
    public static string FanConfigHeading => T("风扇配置", "風扇配置", "Fan Config");
    public static string FanCurveHeading => T("智能自定义风扇曲线", "智能自訂風扇曲線", "Smart Custom Fan Curve");
    public static string TempSensitivityHeading => T("温度灵敏度", "溫度靈敏度", "Temp Sensitivity");
    public static string FanSpeedControlHeading => T("转速控制", "轉速控制", "Fan Speed Control");
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
    public static string FanCurveImportFromClipboard => T("从剪贴板导入分享码", "從剪貼簿匯入分享碼", "Import from clipboard share code");
    public static string DustCleanDesc => T("反转风扇清除内部灰尘", "反轉風扇清除內部灰塵", "Reverse fans to clean internal dust");
    public static string CleanCreekStartBtn => T("开始除尘 (30秒)", "開始除塵 (30秒)", "Start Cleaning (30s)");
    public static string AutoFanProtectDesc => T("CPU温度>95°C且固定转速时强制切换为降温曲线", "CPU溫度>95°C且固定轉速時強制切換為降溫曲線", "Forces cool curve when CPU >95°C with fixed fan speed");
    public static string FanSync => T("风扇一致性", "風扇一致性", "Fan Consistency");
    public static string FanSyncDesc => T("所有风扇转速与CPU风扇保持一致", "所有風扇轉速與CPU風扇保持一致", "Keep all fan speeds synchronized with CPU fan");
    public static string FanSmartSettings => T("智能风扇设置", "智能風扇設置", "Smart Curve Settings");
    public static string FanSmartEmaAlpha => T("温度平滑系数", "溫度平滑係數", "Temp Smoothing (EMA)");
    public static string FanSmartEmaAlphaDesc => T("新温度读数的权重，越低越平滑但响应越慢", "新溫度讀數的權重，越低越平滑但響應越慢", "Weight of new temp readings. Lower = smoother but slower");
    public static string FanSmartStepDown => T("降速保护 (RPM/s)", "降速保護 (RPM/s)", "Step-Down Rate (RPM/s)");
    public static string FanSmartStepDownDesc => T("风扇降温时的最大RPM下降速率", "風扇降溫時的最大RPM下降速率", "Max RPM drop per second while cooling down");
    public static string FanSmartHysteresis => T("滞后死区 (°C)", "滯後死區 (°C)", "Hysteresis (°C)");
    public static string FanSmartHysteresisDesc => T("温度变化阈值，低于此值不触发调速", "溫度變化閾值，低於此值不觸發調速", "Min temp change to trigger recalculation");
    public static string FanSmartBalanced => T("均衡", "均衡", "Balanced");
    public static string FanSmartQuiet => T("静音", "靜音", "Silent");
    public static string FanSmartPerformance => T("高性能", "高性能", "Performance");
    public static string FanSmartEmaHint => T("值越小响应越灵敏", "值越小響應越靈敏", "Smaller = smoother but slower");
    public static string FanSmartStepDownHint => T("每秒最多下降 RPM", "每秒最多下降 RPM", "Max RPM drop per second");
    public static string FanSmartHysteresisHint => T("温度变化阈值(°C)", "溫度變化閾值(°C)", "Temperature threshold (°C)");

    // Performance page headings
    public static string DbVersionHeading => T("DB 版本", "DB 版本", "DB Version");
    public static string DbVersionPpabHint => T("建议使用 PPAB (Dynamic Boost) 实现相同效果（如可用）", "建議使用 PPAB (Dynamic Boost) 實現相同效果（如可用）", "Use PPAB (Dynamic Boost) for the same effect if available");
    public static string CpuPowerHeading => T("CPU 功率", "CPU 功率", "CPU Power");
    public static string CpuPowerPL1 => T("PL1", "PL1", "PL1");
    public static string CpuPowerPL2 => T("PL2", "PL2", "PL2");
    public static string SetCpuPowerPL1Slider => T("拖动滑块设置PL1功率 (W)", "拖動滑桿設定PL1功率 (W)", "Drag slider to set PL1 power (W)");
    public static string SetCpuPowerPL2Slider => T("拖动滑块设置PL2功率 (W)", "拖動滑桿設定PL2功率 (W)", "Drag slider to set PL2 power (W)");
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
    public static string ErrRefreshRateRange => T("请输入 30-360 之间的值", "請輸入 30-360 之間的值", "Please enter a value between 30-360");
    public static string PowerPlanHeading => T("电源计划", "電源計劃", "Power Plan");
    public static string PowerModeHeading => T("电源模式", "電源模式", "Power Mode");
    public static string PowerModeEfficiency => T("最佳能效", "最佳能效", "Best Power Efficiency");
    public static string PowerModeBalanced => T("平衡", "平衡", "Balanced");
    public static string PowerModePerformance => T("最佳性能", "最佳性能", "Best Performance");
    public static string HotSwitchHeading => T("热切换", "熱切換", "Hot Switch");
    public static string HotSwitchDesc => T("在集显与独显之间动态切换，无需重启", "在集顯與獨顯之間動態切換，無需重啟", "Dynamically switch between iGPU and dGPU");
    public static string GfxModeHeading => T("图形模式", "圖形模式", "Graphics Mode");
    public static string GfxRestartDesc => T("切换需要重启计算机", "切換需要重啟電腦", "Switching requires a reboot");
    public static string GfxUMALabel => T("UMA 仅集成显卡", "UMA 僅整合顯示卡", "UMA iGPU Only");
    public static string PpabCheckLabel => T("启用 PPAB (Dynamic Boost)", "啟用 PPAB (Dynamic Boost)", "Enable PPAB (Dynamic Boost)");
    public static string DStateHeading => T("dState (GPU 功耗状态)", "dState (GPU 功耗狀態)", "dState (GPU Power State)");
    public static string IccMaxHeading => T("IccMax (CPU 电流限制)", "IccMax (CPU 電流限制)", "IccMax (CPU Current Limit)");
    public static string AcLoadLineHeading => T("AC Load line（负载线校准）", "AC Load line（負載線校準）", "AC Load Line");

    // Combo items
    public static string GpuClockRestore => T("还原", "還原", "Restore Default");
    public static string TppDisable => T("关闭", "關閉", "Disable");

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
    public static string PawnDriveHeading => T("PawnIO 驱动", "PawnIO 驅動", "PawnIO Driver");
    public static string HwMonitorHeading => T("硬件监控", "硬體監控", "Hardware Monitor");

    // Settings page
    public static string FloatingHeading => T("浮窗显示", "浮窗顯示", "Overlay");
    public static string DisplayHeading => T("显示器选择 (多选)", "顯示器選擇 (多選)", "Monitor Selection");
    public static string FontSizeHeading => T("字体大小", "字體大小", "Font Size");
    public static string PositionHeading => T("位置", "位置", "Position");
    public static string BgOpacityHeading => T("背景透明度", "背景透明度", "Background Opacity");
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
    public static string GpuProcessesHeading => T("占用 GPU 的程序", "佔用 GPU 的程式", "GPU Processes");
    public static string NoGpuProcesses => T("无 GPU 占用程序", "無 GPU 佔用程式", "No GPU processes");
    public static string BrowseBtn => T("浏览...", "瀏覽...", "Browse...");
    public static string WindowTitle => T("OMEN X Hub 控制面板", "OMEN X Hub 控制面板", "OMEN X Hub Control Panel");
    public static string OmenKeyCustomLabel => T("切换浮窗显示", "切換浮窗顯示", "Toggle Overlay");
    public static string OmenKeyUnbound => T("取消绑定", "取消綁定", "Unbound");

    // Error messages
    public static string ErrCpuPowerRange => T("请输入 10-254 之间的数值", "請輸入 10-254 之間的數值", "Enter a value between 10-254");
    public static string ErrGpuClockRange => T("请输入 0-2500 之间的数值", "請輸入 0-2500 之間的數值", "Enter a value between 0-2500");
    public static string ErrFpsNonNegative => T("请输入非负整数", "請輸入非負整數", "Enter a non-negative integer");
    public static string ErrFpsNotSupported => T("仅支持: 不限,30,60,90,120,144,165,240,300,360,480,1000", "僅支援: 不限,30,60,90,120,144,165,240,300,360,480,1000", "Supported: Unlimited,30,60,90,120,144,165,240,300,360,480,1000");
    public static string ErrTppRange => T("请输入 0-255 之间的数值", "請輸入 0-255 之間的數值", "Enter a value between 0-255");
    public static string ErrIccMaxRange => T("请输入 0 或 160-255 之间的数值", "請輸入 0 或 160-255 之間的數值", "Enter 0 or a value between 160-255");
    public static string ErrCpuPowerWmi => T("CPU功率限制设置失败！WMI调用无响应。", "CPU功率限制設定失敗！WMI呼叫無回應。", "CPU power limit failed! WMI call unresponsive.");
    public static string CleanCreekUnsupported => T("当前设备不支持反转除尘功能", "目前裝置不支援反轉除塵功能", "This device does not support reverse dust removal");

    // Automation
    public static string SidebarAutomation => T("自动化", "自動化", "Automation");
    public static string AutomationHeading => T("自动化控制", "自動化控制", "Automation");
    public static string AutomationPipelines => T("自动化管道", "自動化管道", "Pipelines");
    public static string AutomationQuickActions => T("快捷操作", "快捷操作", "Quick Actions");
    public static string AutomationAddPipeline => T("添加管道", "添加管道", "Add Pipeline");
    public static string AutomationEditPipeline => T("编辑管道", "編輯管道", "Edit Pipeline");
    public static string AutomationDeletePipeline => T("删除", "刪除", "Delete");
    public static string AutomationRunNow => T("立即运行", "立即運行", "Run Now");
    public static string AutomationEnabled => T("已启用", "已啟用", "Enabled");
    public static string AutomationDisabled => T("已禁用", "已禁用", "Disabled");
    public static string AutomationTriggerType => T("触发类型", "觸發類型", "Trigger Type");
    public static string AutomationTriggerValue => T("触发值", "觸發值", "Trigger Value");
    public static string AutomationStepType => T("步骤类型", "步驟類型", "Step Type");
    public static string AutomationStepValue => T("步骤值", "步驟值", "Step Value");
    public static string AutomationRefreshRate => T("刷新率 (Hz)", "刷新率 (Hz)", "Refresh Rate (Hz)");
    public static string AutomationPowerPlanGuid => T("电源计划 GUID", "電源計劃 GUID", "Power Plan GUID");
    public static string AutomationMaxFrameRate => T("最大帧率 (FPS)", "最大幀率 (FPS)", "Max Frame Rate (FPS)");
    public static string AutomationCpuPowerValue => T("CPU功率 (W / max)", "CPU功率 (W / max)", "CPU Power (W / max)");
    public static string AutomationDelayMs => T("延迟(毫秒)", "延遲(毫秒)", "Delay (ms)");
    public static string AutomationNoPipelines => T("暂无自动化管道", "暫無自動化管道", "No pipelines yet");
    public static string AutomationNoQuickActions => T("暂无快捷操作", "暫無快捷操作", "No quick actions yet");
    public static string AutomationPipelineName => T("管道名称", "管道名稱", "Pipeline Name");
    public static string AutomationSave => T("保存", "保存", "Save");
    public static string AutomationCancel => T("取消", "取消", "Cancel");
    public static string AutomationAddStep => T("添加步骤", "添加步驟", "Add Step");
    public static string AutomationDeleteStep => T("删除步骤", "刪除步驟", "Delete Step");
    public static string AutomationConfirmDelete => T("确定删除此管道？", "確定刪除此管道？", "Delete this pipeline?");
    public static string AutomationConfirmDeleteTitle => T("删除确认", "刪除確認", "Confirm Delete");
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
    public static string AutomationStepSetIccMax => T("设置IccMax", "設定IccMax", "Set IccMax");
    public static string AutomationStepSetAcLoadLine => T("设置AC Load Line", "設定AC Load Line", "Set AC Load Line");
    public static string AutomationStepSetTpp => T("设置TPP", "設定TPP", "Set TPP");
    public static string AutomationStepSetGpuPower => T("设置GPU功率", "設定GPU功率", "Set GPU Power");
    public static string AutomationStepSetTempSensitivity => T("设置温度灵敏度", "設定溫度靈敏度", "Set Temp Sensitivity");
    public static string AutomationStepSetFanCurve => T("设置风扇曲线", "設定風扇曲線", "Set Fan Curve");
    public static string AutomationProcessName => T("进程名称", "進程名稱", "Process Name");
    public static string AutomationProgramPath => T("程序路径", "程式路徑", "Program Path");
    public static string AutomationTime => T("时间 (HH:mm)", "時間 (HH:mm)", "Time (HH:mm)");
    public static string AutomationMessage => T("消息文本", "消息文本", "Message Text");
    public static string AutomationPreset => T("预设方案", "預設方案", "Preset");
    public static string AutomationPowerModeValue => T("电源模式值", "電源模式值", "Power Mode (0=节能/1=平衡/2=性能)");
    public static string AutomationFanModeValue => T("风扇模式", "風扇模式", "Fan Mode");
    public static string AutomationIccMaxValue => T("电流 (A)", "電流 (A)", "Current (A)");
    public static string AutomationAcLoadLineValue => T("电阻 (mΩ)", "電阻 (mΩ)", "Resistance (mΩ)");
    public static string AutomationTppValue => T("功率 (W)", "功率 (W)", "Power (W)");
    public static string AutomationGpuPowerValue => T("TGP 功率 (W)", "TGP 功率 (W)", "TGP Power (W)");
    public static string AutomationTempSensitivityValue => T("灵敏度 (实时/高/中/低)", "靈敏度 (實時/高/中/低)", "Sensitivity (realtime/high/medium/low)");
    public static string AutomationFanCurveValue => T("曲线文件名", "曲線文件名", "Curve file name");
    public static string AutomationBrowse => T("浏览...", "瀏覽...", "Browse...");
    public static string PipelineSaved => T("管道已保存", "管道已保存", "Pipeline saved");
    public static string PipelineDeleted => T("管道已删除", "管道已删除", "Pipeline deleted");
    public static string DashboardHeading => T("实时状态", "實時狀態", "Dashboard");
    public static string RenameTooltip => T("重命名", "重新命名", "Rename");
    public static string SaveTooltip => T("保存", "儲存", "Save");
    public static string NewPipelineDefaultName => T("新管道", "新管道", "New Pipeline");
    // EcoQoS / Efficiency Mode
    public static string EcoQosHeading => T("EcoQoS 效率模式", "EcoQoS 效率模式", "EcoQoS Efficiency Mode");
    public static string EcoQosEnable => T("开启 EcoQoS", "開啓 EcoQoS", "Enable EcoQoS");
    public static string EcoQosThrottlePlugged => T("插电时限制所有后台进程", "插電時限制所有後臺進程", "Throttle background processes when plugged in");
    public static string EcoQosWhitelist => T("进程白名单", "進程白名單", "Process Whitelist");
    public static string EcoQosBlacklist => T("进程黑名单", "進程黑名單", "Process Blacklist");
    public static string EcoQosWhitelistPlaceholder => T("每行一个进程名，白名单中的进程不会被限制", "每行一個進程名，白名單中的進程不會被限制", "One process per line. Whitelisted processes won't be throttled.");
    public static string EcoQosBlacklistPlaceholder => T("每行一个进程名，黑名单中的进程始终被限制", "每行一個進程名，黑名單中的進程始終被限制", "One process per line. Blacklisted processes will always be throttled.");
    public static string DriverVersionRange => T("537.42 <= 驱动版本 < 610.47", "537.42 <= 驅動版本 < 610.47", "537.42 <= Driver < 610.47");

    // OSD
    public static string OsdToggle => T("切换预设时显示 OSD 提示", "切換預設時顯示 OSD 提示", "Show OSD hint on preset switch");
    public static string OsdToggleDesc => T("切换预设、风扇模式、电源状态时在屏幕底部显示提示", "切換預設、風扇模式、電源狀態時在螢幕底部顯示提示", "Show notification at screen bottom on preset/fan/power change");
    public static string LockKeysToggle => T("Caps Lock / Num Lock 开关提示", "Caps Lock / Num Lock 開關提示", "Show Caps Lock / Num Lock OSD");
    public static string CapsLockOn => T("大写锁定：开", "大寫鎖定：開", "Caps Lock: ON");
    public static string CapsLockOff => T("大写锁定：关", "大寫鎖定：關", "Caps Lock: OFF");
    public static string NumLockOn => T("数字锁定：开", "數字鎖定：開", "Num Lock: ON");
    public static string NumLockOff => T("数字锁定：关", "數字鎖定：關", "Num Lock: OFF");

    // Performance page group headers
    public static string PerfGroupCpu => T("CPU 控制", "CPU 控制", "CPU Control");
    public static string PerfGroupGpu => T("GPU 控制", "GPU 控制", "GPU Control");

    // Core Keep
    public static string CoreKeepHeading => T("核心保持", "核心保持", "Core Keep");
    public static string CoreKeepDesc => T("持久化 CPU 优先级和关联性，进程启动时自动恢复", "持久化 CPU 優先級和關聯性，進程啟動時自動恢復", "Persist CPU priority & affinity, auto-restore on process start");
    public static string CoreKeepProcessLabel => T("进程:", "進程:", "Process:");
    public static string CoreKeepPriorityLabel => T("优先级:", "優先級:", "Priority:");
    public static string CoreKeepAffinityLabel => T("关联性:", "關聯性:", "Affinity:");
    public static string CoreKeepRefresh => T("刷新", "刷新", "Refresh");
    public static string CoreKeepDelete => T("删除", "刪除", "Delete");
    public static string CoreKeepAdd => T("添加", "添加", "Add");
    public static string CoreKeepStatusExists => T("已存在", "已存在", "Already exists");
    public static string CoreKeepStatusNotFound => T("未找到进程或无法读取", "未找到進程或無法讀取", "Process not found or inaccessible");
    public static string CoreKeepStatusUncaptured => T("未捕获", "未捕獲", "Uncaptured");
    public static string CoreKeepStatusCapturedAt => T("捕获于 ", "捕獲於 ", "Captured at ");
    public static string CoreKeepPriorityIdle => T("空闲", "空閒", "Idle");
    public static string CoreKeepPriorityBelowNormal => T("低于标准", "低於標準", "Below Normal");
    public static string CoreKeepPriorityNormal => T("标准", "標準", "Normal");
    public static string CoreKeepPriorityAboveNormal => T("高于标准", "高於標準", "Above Normal");
    public static string CoreKeepPriorityHigh => T("高", "高", "High");
    public static string CoreKeepPriorityRealtime => T("实时", "實時", "Realtime");
    public static string CoreKeepPriorityUnknown => T("未知", "未知", "Unknown");
    public static string CoreKeepGuardLabel => T("运行中守护", "運行中守護", "Runtime Guard");
    public static string CoreKeepGuardDesc => T("周期检查并重新应用 CPU 亲和性设置", "週期檢查並重新應用 CPU 親和性設置", "Periodically check and re-apply CPU affinity");
    public static string CoreKeepGuardInterval => T("检查间隔(秒)", "檢查間隔(秒)", "Check Interval (s)");
    public static string CoreKeepModeLabel => T("核心模式:", "核心模式:", "Core Mode:");
    public static string CoreKeepModeAuto => T("自动", "自動", "Auto");
    public static string CoreKeepModeAll => T("全部核心", "全部核心", "All Cores");
    public static string CoreKeepModePerformance => T("性能核优先", "性能核優先", "P-Cores First");
    public static string CoreKeepModeEfficiency => T("能效核优先", "能效核優先", "E-Cores First");
    public static string CoreKeepModeManual => T("手动选择", "手動選擇", "Manual");
    public static string CoreKeepBenchmark => T("核心竞速", "核心競速", "Core Benchmark");
    public static string CoreKeepBenchmarkRunning => T("竞速进行中...", "競速進行中...", "Benchmark running...");
    public static string CoreKeepBenchmarkDone => T("竞速完成", "競速完成", "Benchmark complete");
    public static string CoreKeepBenchmarkResult => T("核心 {0}: 得分={1} 相对={2:F2}", "核心 {0}: 得分={1} 相對={2:F2}", "Core {0}: score={1} rel={2:F2}");
    public static string CoreKeepStatusMatched => T("✓ 已应用", "✓ 已應用", "✓ Applied");
    public static string CoreKeepStatusMismatch => T("✗ 已被修改", "✗ 已被修改", "✗ Modified");
    public static string CoreKeepStatusNotRunning => T("- 进程未运行", "- 進程未運行", "- Not running");
    public static string CoreKeepSaveCurrent => T("捕获更新", "捕獲更新", "Capture");
    public static string CoreKeepTopologyLabel => T("核心拓扑:", "核心拓撲:", "Core Topology:");
    public static string CoreKeepTopologyHybrid => T("{0} 核 ({1} P + {2} E)", "{0} 核 ({1} P + {2} E)", "{0} cores ({1} P + {2} E)");
    public static string CoreKeepTopologyDualCcd => T("{0} 核 (CCD0={1} CCD1={2})", "{0} 核 (CCD0={1} CCD1={2})", "{0} cores (CCD0={1} CCD1={2})");
    public static string CoreKeepTopologyNormal => T("{0} 核", "{0} 核", "{0} cores");

    // Pin tooltip
    public static string PinTooltip => T("窗口置顶", "視窗置頂", "Always on Top");

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
    public static string MacroStopRecord => T("停止录制", "停止錄製", "Stop Recording");
    public static string MacroPlayTest => T("试播", "試播", "Play");
    public static string MacroRecording => T("录制中", "錄製中", "Recording");
    public static string MacroRecordHint => T("正在录制中，按 ESC 停止录制。", "正在錄製中，按 ESC 停止錄製。", "Recording in progress. Press ESC to stop.");
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

    // EcoQoS edit button
    public static string EcoQosEdit => T("编辑", "編輯", "Edit");

    // Custom logo
    public static string CustomLogoHeading => T("自定义主界面 LOGO", "自訂主介面 LOGO", "Custom Main Logo");
    public static string CustomLogoDesc => T("替换左侧导航栏的应用图标", "替換左側導航欄的應用圖示", "Replace the app logo in the left nav bar");
    public static string CustomLogoSelectBtn => T("选择图片...", "選擇圖片...", "Select Image...");
    public static string CustomLogoResetBtn => T("重置为默认", "重設為預設", "Reset to Default");

    // Custom background
    public static string CustomBgHeading => T("自定义主界面背景", "自訂主介面背景", "Custom Main Background");
    public static string CustomBgDesc => T("设置主窗口背景图片", "設定主視窗背景圖片", "Set main window background image");
    public static string CustomBgSelectBtn => T("选择图片...", "選擇圖片...", "Select Image...");
    public static string CustomBgResetBtn => T("重置为默认", "重設為預設", "Reset to Default");
    public static string CustomBgOpacity => T("背景透明度", "背景透明度", "Background Opacity");
    public static string CustomBgBlur => T("高斯模糊", "高斯模糊", "Gaussian Blur");

    // Other page
    public static string BatteryChargeHeading => T("智能充电", "智慧充電", "Smart Charge");
    public static string BatteryChargeDesc => T("电池充至 80% 即停止，延长电池寿命", "電池充至 80% 即停止，延長電池壽命", "Stop charging at 80% to extend battery life");
    public static string BatteryChargeHint => T("当前设备不支持，请打开 myHP 应用设置", "目前裝置不支援，請打開 myHP 應用程式設定", "Not supported on this device. Please open myHP.");
    public static string NumLockHeading => T("数字键锁定", "數字鍵鎖定", "Num Lock");
    public static string NumLockDesc => T("切换数字小键盘开关状态", "切換數字小鍵盤開關狀態", "Toggle numeric keypad lock");
    public static string CapsLockHeading => T("大写键锁定", "大寫鍵鎖定", "Caps Lock");
    public static string CapsLockDesc => T("切换大写锁定开关状态", "切換大寫鎖定開關狀態", "Toggle caps lock");
    public static string TouchpadLockHeading => T("触摸板锁定", "觸控板鎖定", "Touchpad Lock");
    public static string TouchpadLockDesc => T("禁用或启用触摸板", "禁用或啟用觸控板", "Disable or enable touchpad");

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
    public static string HttpApiHeading => T("HTTP API", "HTTP API", "HTTP API");
    public static string HttpApiDesc => T("启动本地 HTTP 服务器以通过外部程序控制硬件", "啟動本地 HTTP 伺服器以通過外部程式控制硬體", "Start local HTTP server for external program control");
    public static string CoreKeepMasterToggle => T("启用 CoreKeep", "啟用 CoreKeep", "Enable CoreKeep");
    public static string CoreKeepMasterDesc => T("自动恢复已捕捉进程的 CPU 优先级和关联性", "自動恢復已捕捉進程的 CPU 優先級和關聯性", "Auto-restore CPU priority & affinity for captured processes");

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

    // ═══ Advanced CPU Tuning cards ═══
    public static string PerfGroupCpuAdv => T("CPU 高级调校", "CPU 高級調校", "Advanced CPU Tuning");
    public static string PerfGroupGpuAdv => T("GPU 高级调校", "GPU 高級調校", "Advanced GPU Tuning");
    // ponytail: only 2 added here; the rest live in the later block near PerfAdlxConnected.
    public static string AdvNeedAdmin => T("此操作需要管理员权限", "此操作需要管理員權限", "Administrator privileges required");
    public static string AdvInstallPawnIoGuide => T("查看 PawnIO(UXTU) 安装说明", "查看 PawnIO(UXTU) 安裝說明", "View PawnIO (UXTU) install guide");
    // ponytail: advanced-tuning silent-failure surface — handlers used to
    // `_ = Service.SetXxx(...)` and write a fixed "✓" / "SMU 已连接" status
    // regardless of return value. These strings replace that lie.
    public static string AdvWriteFail => T("写入失败 / 驱动未就绪", "寫入失敗 / 驅動未就緒", "Write failed / driver not ready");
    public static string AdvDriverNotReady => T("驱动未就绪", "驅動未就緒", "Driver not ready");
    public static string AdvInstallOmenDriver => T("安装内核驱动", "安裝內核驅動", "Install kernel driver");
    public static string AdvDriverInstallOk => T("驱动已就绪", "驅動已就緒", "Driver ready");
    public static string AdvDriverInstallFail => T("驱动安装失败，请以管理员运行并检查日志", "驅動安裝失敗，請以管理員執行並檢查日誌", "Driver install failed. Run as administrator and check logs.");
    // PBO Scalar (AMD)
    public static string PboScalarHeading => T("PBO Scalar (AMD)", "PBO Scalar (AMD)", "PBO Scalar (AMD)");
    public static string PboScalarDesc => T("调节 Precision Boost Overdrive 缩放倍数", "調節 Precision Boost Overdrive 縮放倍數", "Adjust Precision Boost Overdrive scalar multiplier");
    // Curve Optimiser (AMD)
    public static string CoHeading => T("Curve Optimiser (AMD)", "Curve Optimiser (AMD)", "Curve Optimiser (AMD)");
    public static string CoDesc => T("全核电压曲线偏移", "全核電壓曲線偏移", "All-core voltage curve offset");
    public static string CoAllCoreLabel => T("全核偏移 (mV)", "全核偏移 (mV)", "All-core Offset (mV)");
    public static string CoIGpuLabel => T("iGPU 偏移", "iGPU 偏移", "iGPU Offset");
    public static string CoPerCoreLabel => T("每核心偏移 (mV)", "每核心偏移 (mV)", "Per-core Offset (mV)");
    // Per-Core Curve Optimiser (AMD)
    public static string Ccd1CoHeading => T("CCD1 单核调校 (AMD)", "CCD1 單核調校 (AMD)", "CCD1 Per-Core CO (AMD)");
    public static string Ccd1CoDesc => T("Core 0~11 每核心电压曲线偏移", "Core 0~11 每核心電壓曲線偏移", "Core 0~11 per-core voltage curve offset");
    public static string Ccd2CoHeading => T("CCD2 单核调校 (AMD)", "CCD2 單核調校 (AMD)", "CCD2 Per-Core CO (AMD)");
    public static string Ccd2CoDesc => T("Core 12~23 每核心电压曲线偏移。仅双 CCD 可见。", "Core 12~23 每核心電壓曲線偏移。僅雙 CCD 可見。", "Core 12~23 per-core CO. Dual-CCD only.");
    // AMD CPU Power Limits (AM5 desktop)
    public static string AmdCpuPowerHeading => T("CPU 功耗限制 (AMD)", "CPU 功耗限制 (AMD)", "CPU Power Limits (AMD)");
    public static string AmdCpuPowerDesc => T("PPT/TDC/EDC 桌面 AM5 独立 CPU", "PPT/TDC/EDC 桌面 AM5 獨立 CPU", "PPT/TDC/EDC desktop AM5 standalone CPU");
    // AMD CPU Temperature
    public static string CpuTempHeading => T("CPU 温度限制 (AMD)", "CPU 溫度限制 (AMD)", "CPU Temp Limit (AMD)");
    public static string CpuTempDesc => T("Tctl 硬降频温度阈值", "Tctl 硬降頻溫度閾值", "Tctl hard throttle temperature threshold");
    // CCD Affinity (AMD)
    public static string CcdAffinityHeading => T("CCD 亲和性 (AMD)", "CCD 親和性 (AMD)", "CCD Affinity (AMD)");
    public static string CcdAffinityDesc => T("双 CCD 调度优化，模拟大小核", "雙 CCD 調度優化，模擬大小核", "Dual-CCD scheduling, simulated hybrid");
    // FIVR Undervolt (Intel)
    public static string FivrHeading => T("FIVR 降压 (Intel)", "FIVR 降壓 (Intel)", "FIVR Undervolt (Intel)");
    public static string FivrDesc => T("调节 CPU 核心/缓存/核显/SA 电压偏移", "調節 CPU 核心/快取/內顯/SA 電壓偏移", "Adjust core/cache/iGPU/SA voltage offsets");
    public static string FivrCoreLabel => T("核心偏移 (mV)", "核心偏移 (mV)", "Core Offset (mV)");
    public static string FivrCacheLabel => T("缓存偏移 (mV)", "快取偏移 (mV)", "Cache Offset (mV)");
    public static string FivrIgpuLabel => T("核显偏移 (mV)", "內顯偏移 (mV)", "iGPU Offset (mV)");
    public static string FivrSaLabel => T("SA 偏移 (mV)", "SA 偏移 (mV)", "SA Offset (mV)");
    // Clock Ratio (Intel)
    public static string ClockRatioHeading => T("时钟比例 (Intel)", "時鐘比例 (Intel)", "Clock Ratio (Intel)");
    public static string ClockRatioDesc => T("调节 CPU 倍频", "調節 CPU 倍頻", "Adjust CPU multiplier");
    // Power Balance (Intel)
    public static string PowerBalanceHeading => T("Power Balance (Intel)", "Power Balance (Intel)", "Power Balance (Intel)");
    public static string PowerBalanceDesc => T("CPU/GPU 功率分配比例", "CPU/GPU 功率分配比例", "CPU/GPU power distribution");
    // NVIDIA Voltage Curve
    public static string NvVoltCurveHeading => T("NVIDIA 电压曲线", "NVIDIA 電壓曲線", "NVIDIA Voltage Curve");
    public static string NvVoltCurveDesc => T("电压-频率曲线调节（降压超频）", "電壓-頻率曲線調節（降壓超頻）", "Voltage-frequency curve tuning (undervolt)");
    public static string NvVoltCurveOffsetLabel => T("电压偏移 (mV)", "電壓偏移 (mV)", "Voltage Offset (mV)");
    public static string NvVoltCurveNote => T("负值为降压，慎用大幅度正值。实时生效。", "負值為降壓，慎用大幅度正值。即時生效。", "Negative = undervolt. Large positive values may cause instability.");
    // ADLX Radeon (AMD GPU)
    public static string AdlxHeading => T("Radeon 设置 (AMD GPU)", "Radeon 設定 (AMD GPU)", "Radeon Settings (AMD GPU)");
    public static string AdlxDesc => T("Anti-Lag / RSR / Boost / Image Sharpening", "Anti-Lag / RSR / Boost / Image Sharpening", "Anti-Lag / RSR / Boost / Image Sharpening");
    // RTSS Frame Limit
    public static string RtssHeading => T("RTSS 帧率限制", "RTSS 幀率限制", "RTSS Frame Limit");
    public static string RtssDesc => T("通过 RivaTuner 精确锁定帧率", "通過 RivaTuner 精確鎖定幀率", "Precise frame rate limiter via RivaTuner");
    // AutoOC Adaptive Undervolt
    public static string AutoOcHeading => T("AutoOC 自适应降压", "AutoOC 自適應降壓", "AutoOC Adaptive Undervolt");
    public static string AutoOcDesc => T("自动稳定性测试的动态电压优化", "自動穩定性測試的動態電壓優化", "Dynamic stability-tested voltage optimisation");
    // Status messages
    public static string HwNotSupported => T("当前硬件不支持此功能", "目前硬體不支援此功能", "Not supported on this hardware");
    public static string HwNotDetected => T("未检测到对应硬件", "未檢測到對應硬體", "Required hardware not detected");
    public static string DriverWriteNotAvail => T("需要 PawnIO 内核写入权限", "需要 PawnIO 內核寫入權限", "Requires PawnIO kernel write access");
    public static string FeaturePartialImpl => T("此功能为预览版，部分调节可能无效", "此功能為預覽版，部分調節可能無效", "Preview: some adjustments may not apply");

    // Perf page
    public static string PerfAdjustCpuPower => T("调整 CPU 功率限制", "調整 CPU 功率限制", "Adjust CPU Power Limit");
    public static string PerfEcoQosDesc => T("限制未在前台运行的后台进程的CPU性能", "限制未在前臺運行的後臺程序的CPU效能", "Limit CPU performance for background processes");
    public static string PerfCoreKeepDesc => T("持久化 CPU 优先级和关联性，进程启动时自动恢复", "持久化CPU優先級和關聯性，進程啟動時自動恢復", "Persist CPU priority & affinity, auto-restore on process start");
    public static string PerfGfxReboot => T("切换需要重启计算机", "切換需要重啟電腦", "Switching requires reboot");
    public static string PerfHotSwitchDesc => T("在集显与独显之间动态切换，无需重启", "在集顯與獨顯之間動態切換，無需重啟", "Dynamically switch between iGPU and dGPU");
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
    public static string AccentColorHeading => T("强调色", "強調色", "Accent Color");
    public static string AccentColorSystem => T("跟随系统", "跟隨系統", "System");
    public static string AccentColorCustom => T("自定义", "自訂", "Custom");
    public static string AccentColorPreview => T("预览", "預覽", "Preview");

    // Other page
    public static string OtherHttpApiHeading => T("HTTP API 服务", "HTTP API 服務", "HTTP API Service");
    public static string OtherHttpApiDesc => T("在 localhost:5000 提供硬件状态 API，供外部工具调用", "在 localhost:5000 提供硬體狀態 API，供外部工具調用", "Provides hardware status API at localhost:5000");

    // Battery charge
    public static string BatteryChargeTitle => T("电池充电", "電池充電", "Battery Charging");
    public static string BatteryChargeMyHpHint => T("请在 myHP 中关闭电池养护模式后再试", "請在myHP中關閉電池養護模式後再試", "Please disable battery care mode in myHP first");

    // HTTP API status
    public static string HttpApiRunning => T("运行中", "運行中", "Running");
    public static string HttpApiStopped => T("已停止", "已停止", "Stopped");

    // Power plan names
    public static string PowerPlanBalanced => T("平衡", "平衡", "Balanced");
    public static string PowerPlanHighPerf => T("高性能", "高效能", "High Performance");
    public static string PowerPlanPowerSave => T("节能", "節能", "Power Saver");
    public static string PowerPlanUltimatePerf => T("卓越性能", "卓越效能", "Ultimate Performance");

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
    public static string ButtonBrowse => T("浏览...", "瀏覽...", "Browse...");
    public static string LabelName => T("名称:", "名稱:", "Name:");
    public static string LabelProcess => T("进程:", "進程:", "Process:");
    public static string LabelPriority => T("优先级:", "優先級:", "Priority:");
    public static string LabelAffinity => T("关联性:", "關聯性:", "Affinity:");
    public static string LabelSteps => T("步骤:", "步驟:", "Steps:");
    public static string LabelTriggers => T("触发器:", "觸發器:", "Triggers:");

    // Automation page
    public static string AutomationStepCount(int count) => T($"{count} 个步骤", $"{count} 個步驟", $"{count} steps");
    public static string AutomationExecuting => T(" [执行中...]", " [執行中...]", " [Executing...]");

    // ═══ Hetero CPU (AMD dual-CCD hybrid scheduling) ═══
    public static string HeteroCpuHeading => T("异构调度 (AMD双CCD)", "異構調度 (AMD雙CCD)", "Hetero CPU (AMD Dual-CCD)");
    public static string HeteroCpuToggleLabel => T("启用异构调度", "啟用異構調度", "Enable Hetero Scheduling");
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
	  public static string MainWindowAdvancedUnlocked => T(
	    "高级调校已解锁！性能页将显示 CPU/GPU 进阶选项。",
	    "高級調校已解鎖！效能頁將顯示 CPU/GPU 進階選項。",
	    "Advanced tuning unlocked! CPU/GPU advanced options will appear on Performance page.");
	  public static string MainWindowAdvancedHidden => T(
	    "高级调校已隐藏。再次点击 logo 5 次可重新解锁。",
	    "高級調校已隱藏。再次點擊 logo 5 次可重新解鎖。",
	    "Advanced tuning hidden. Click the logo 5 more times to re-unlock.");

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
	  // AMD PPT/TDC/EDC
	  public static string AmdPptLabel => T("PPT (CPU Package Power) (W)", "PPT (CPU Package Power) (W)", "PPT (CPU Package Power) (W)");
	  public static string AmdTdcLabel => T("TDC (VRM 持续电流) (A)", "TDC (VRM 持續電流) (A)", "TDC (VRM Continuous Current) (A)");
	  public static string AmdEdcLabel => T("EDC (VRM 峰值电流) (A)", "EDC (VRM 峰值電流) (A)", "EDC (VRM Peak Current) (A)");
	  public static string AmdTctlLabel => T("CPU Tctl 硬降频温度 (°C)", "CPU Tctl 硬降頻溫度 (°C)", "CPU Tctl Hard Throttle Temp (°C)");
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
		  public static string PerfHotSwitchCardDesc => T(
	    "免重启切换混合/独显模式。", "免重啟切換混合/獨顯模式。",
	    "Switch hybrid/discrete GPU without reboot.");
	  public static string TgpHardwareLabel => T("TGP / PPAB", "TGP / PPAB", "TGP / PPAB");
	  public static string TgpDesc => T(
	    "调节显卡总功耗及动态功耗分配策略。",
	    "調節顯示卡總功耗及動態功耗分配策略。",
	    "Adjust total GPU power and dynamic power distribution.");
	  // Advanced CPU cards
	  public static string AdvCpuBias => T("CPU 偏向", "CPU 偏向", "CPU Bias");
	  public static string AdvTurboBoost => T("睿频开关 (MSR 0x1A0)", "睿頻開關 (MSR 0x1A0)", "Turbo Boost (MSR 0x1A0)");
	  public static string AdvTurboBoostDesc => T(
	    "直写 MSR 0x1A0 bit 38，关闭/启用 Intel Turbo Boost。",
	    "直寫 MSR 0x1A0 bit 38，關閉/啟用 Intel Turbo Boost。",
	    "Write MSR 0x1A0 bit 38 to disable/enable Intel Turbo Boost.");
	  public static string AdvProchot => T("过温保护偏移 (MSR 0x1A2)", "過溫保護偏移 (MSR 0x1A2)", "PROCHOT Offset (MSR 0x1A2)");
	  public static string AdvProchotDesc => T(
	    "调整CPU降频温度阈值。0=最高上限，数值越大越早降频。",
	    "調整CPU降頻溫度閾值。0=最高上限，數值越大越早降頻。",
	    "Adjust CPU throttle temp threshold. 0=highest limit, higher=earlier throttle.");
	  public static string AdvProchotOffset => T("PROCHOT 偏移 (°C)", "PROCHOT 偏移 (°C)", "PROCHOT Offset (°C)");
	  public static string AdvHwp => T("HWP 能效偏好 (MSR 0x774)", "HWP 能效偏好 (MSR 0x774)", "HWP Energy Efficiency (MSR 0x774)");
	  public static string AdvHwpDesc => T(
	    "调节Intel Speed Shift能效/性能偏向。0=最高性能, 255=最高能效。",
	    "調節Intel Speed Shift能效/性能偏向。0=最高性能, 255=最高能效。",
	    "Adjust Intel Speed Shift energy/perf bias. 0=max perf, 255=max efficient.");
	  public static string AdvCState => T("C-State 限制 (MSR 0xE2)", "C-State 限制 (MSR 0xE2)", "C-State Limit (MSR 0xE2)");
	  public static string AdvCStateDesc => T(
	    "最大C-State深度。数字越小延迟越低，功耗越高。",
	    "最大C-State深度。數字越小延遲越低，功耗越高。",
	    "Max C-State depth. Lower = lower latency, higher power.");
	  public static string AdvApuPower => T("APU 功耗调教 (SMU)", "APU 功耗調教 (SMU)", "APU Power Tuning (SMU)");
	  public static string AdvApuPowerDesc => T(
	    "STAPM 持续功耗 · Fast 峰值 · Slow 平均 · 持续时间窗口。参考 RyzenAdj/UXTU。",
	    "STAPM 持續功耗 · Fast 峰值 · Slow 平均 · 持續時間窗口。參考 RyzenAdj/UXTU。",
	    "STAPM sustained power · Fast peak · Slow avg · duration window. See RyzenAdj/UXTU.");
	  public static string AdvStapmLabel => T("STAPM 持续功耗 (W)", "STAPM 持續功耗 (W)", "STAPM Sustained (W)");
	  public static string AdvFastPptLabel => T("Fast PPT 峰值功耗 (W)", "Fast PPT 峰值功耗 (W)", "Fast PPT Peak (W)");
	  public static string AdvSlowPptLabel => T("Slow PPT 平均功耗 (W)", "Slow PPT 平均功耗 (W)", "Slow PPT Average (W)");
	  public static string AdvStapmDuration => T("STAPM 持续时间 (秒)", "STAPM 持續時間 (秒)", "STAPM Duration (s)");
	  public static string AdvSlowPptDuration => T("Slow PPT 持续时间 (秒)", "Slow PPT 持續時間 (秒)", "Slow PPT Duration (s)");
	  public static string AdvVrmCurrent => T("VRM 电流限制 (SMU)", "VRM 電流限制 (SMU)", "VRM Current Limit (SMU)");
	  public static string AdvVrmCurrentDesc => T(
	    "CPU/SoC VRM 持续电流 (TDC) 与峰值电流 (EDC)。",
	    "CPU/SoC VRM 持續電流 (TDC) 與峰值電流 (EDC)。",
	    "CPU/SoC VRM continuous (TDC) and peak (EDC) current.");
	  public static string AdvCpuTdcLabel => T("CPU TDC 持续电流 (A)", "CPU TDC 持續電流 (A)", "CPU TDC (A)");
	  public static string AdvSocTdcLabel => T("SoC TDC 持续电流 (A)", "SoC TDC 持續電流 (A)", "SoC TDC (A)");
	  public static string AdvCpuEdcLabel => T("CPU EDC 峰值电流 (A)", "CPU EDC 峰值電流 (A)", "CPU EDC (A)");
	  public static string AdvSocEdcLabel => T("SoC EDC 峰值电流 (A)", "SoC EDC 峰值電流 (A)", "SoC EDC (A)");
	  public static string AdvTempLimit => T("温度限制 (SMU)", "溫度限制 (SMU)", "Temp Limit (SMU)");
	  public static string AdvTempLimitDesc => T("Tctl 热阈值 · 皮肤温度限制。", "Tctl 熱閾值 · 皮膚溫度限制。", "Tctl thermal threshold · skin temp limit.");
	  public static string AdvTctlLimit => T("Tctl 温度上限 (°C)", "Tctl 溫度上限 (°C)", "Tctl Max (°C)");
	  public static string AdvSkinTemp => T("APU 皮肤温度 (°C)", "APU 皮膚溫度 (°C)", "APU Skin Temp (°C)");
	  public static string AdvDgpuSkinTemp => T("dGPU 皮肤温度 (°C)", "dGPU 皮膚溫度 (°C)", "dGPU Skin Temp (°C)");
	  // Advanced GPU cards
	  public static string AdvGpuTuning => T("NVIDIA GPU Tuning", "NVIDIA GPU Tuning", "NVIDIA GPU Tuning");
	  public static string AdvGpuTuningDesc => T(
	    "核心/显存超频 · 功耗墙 · 频率锁 · 电压曲线",
	    "核心/記憶體超頻 · 功耗牆 · 頻率鎖 · 電壓曲線",
	    "Core/memory OC · power limit · clock lock · V-F curve");
	  public static string AdvGpuPowerLimit => T("GPU 功耗墙 (W)", "GPU 功耗牆 (W)", "GPU Power Limit (W)");
	  public static string AdvVfCurveHeading => T("V-F 曲线编辑器", "V-F 曲線編輯器", "V-F Curve Editor");
	  public static string AdvVfCurveRead => T("读取曲线", "讀取曲線", "Read Curve");
	  public static string AdvVfCurveReadTip => T("从 GPU 读取当前 V-F 曲线", "從 GPU 讀取目前 V-F 曲線", "Read current V-F curve from GPU");
	  public static string AdvVfCurveApply => T("应用曲线", "應用曲線", "Apply Curve");
	  public static string AdvVfCurveApplyTip => T("将编辑后的曲线写入 GPU（需管理员权限）", "將編輯後的曲線寫入 GPU（需管理員權限）", "Write edited curve to GPU (admin required)");
	  public static string AdvVfCurveReset => T("恢复默认", "恢復預設", "Reset Defaults");
	  public static string AdvVfCurveResetTip => T("清除所有 V-F 偏移，恢复出厂曲线", "清除所有 V-F 偏移，恢復出廠曲線", "Clear all V-F offsets, restore factory curve");
	  public static string AdvRsr => T("RSR", "RSR", "RSR");
	  public static string AdvRsrSharpness => T("锐度:", "銳度:", "Sharpness:");
	  public static string AdvIgpuPowerWall => T("iGPU 功耗墙 (MSR 0x621)", "iGPU 功耗牆 (MSR 0x621)", "iGPU Power Limit (MSR 0x621)");
	  public static string AdvIgpuPowerWallDesc => T(
	    "直写GT VR功率限制，解锁iGPU功耗上限。",
	    "直寫GT VR功率限制，解鎖iGPU功耗上限。",
	    "Write GT VR power limit to unlock iGPU power ceiling.");
	  public static string AdvIgpuMaxRatio => T("iGPU 最大倍频 (MSR 0x1A2)", "iGPU 最大倍頻 (MSR 0x1A2)", "iGPU Max Ratio (MSR 0x1A2)");
	  public static string AdvIgpuMaxRatioDesc => T(
	    "调节iGPU最高运行频率。8=800MHz, 60=6000MHz。⚠️ 过高可能死机。",
	    "調節iGPU最高運行頻率。8=800MHz, 60=6000MHz。⚠️ 過高可能死機。",
	    "Set iGPU max clock. 8=800MHz, 60=6000MHz. ⚠️ Too high may crash.");
	  public static string AdvIgpuClockOverride => T("iGPU 时钟覆盖 (SMU)", "iGPU 時鐘覆蓋 (SMU)", "iGPU Clock Override (SMU)");
	  public static string AdvIgpuClockOverrideDesc => T(
	    "强制设定 iGPU 运行频率 (gfx-clk)。0=自动。",
	    "強制設定 iGPU 運行頻率 (gfx-clk)。0=自動。",
	    "Force iGPU frequency (gfx-clk). 0=Auto.");
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
	  public static string PerfSmuUnavailable => T(
	    "SMU 服务不可用 — TDC/EDC 调节需安装 PawnIO 驱动",
	    "SMU 服務不可用 — TDC/EDC 調節需安装 PawnIO 驅動",
	    "SMU service unavailable — TDC/EDC adjustment requires PawnIO driver");

	  // ponytail: advanced-tuning failure strings (AdvWriteFail / AdvDriverNotReady /
	  // AdvInstallOmenDriver / AdvDriverInstallOk / AdvDriverInstallFail /
	  // AdvNeedAdmin / AdvInstallPawnIoGuide) are defined up in the
	  // "Advanced CPU Tuning cards" block. This block keeps one source of truth.
	  public static string PerfAdlxConnected => T(
	    "ADLX 已连接 - RSR/Anti-Lag/Enhanced Sync/Boost/Image Sharpening",
	    "ADLX 已連接 - RSR/Anti-Lag/Enhanced Sync/Boost/Image Sharpening",
	    "ADLX Connected - RSR/Anti-Lag/Enhanced Sync/Boost/Image Sharpening");
	  public static string PerfAutoOcEnabled => T("AutoOC 已启用 (SDK)", "AutoOC 已啟用 (SDK)", "AutoOC Enabled (SDK)");
	  public static string PerfAutoOcDisabled => T("已禁用", "已禁用", "Disabled");
	  public static string PerfStatusUnavailable => T("不可用", "不可用", "Unavailable");
	  public static string PerfStatusCurrent => T("当前: ", "目前: ", "Current: ");
	  public static string PerfPowerPlanSelectFirst => T("请先选择电源计划", "請先選擇電源計劃", "Please select a power plan first");
	  public static string PerfPowerPlanApplied(string dcac) => T(
	    $"已应用{dcac}设置", $"已應用{dcac}設置", $"Applied {dcac} settings");
	  public static string PerfPowerPlanApplyFailed(string msg) => T(
	    $"应用失败: {msg}", $"應用失敗: {msg}", $"Apply failed: {msg}");
	  // V-F Curve status
	  public static string PerfVfReading => T("正在读取 V-F 曲线...", "正在讀取 V-F 曲線...", "Reading V-F curve...");
	  public static string PerfVfReadDone(int count) => T(
	    $"已加载 {count} 个曲线点 — 拖拽点调频、Ctrl+点击锁平高频段",
	    $"已載入 {count} 個曲線點 — 拖拽點調頻、Ctrl+點擊鎖平高頻段",
	    $"Loaded {count} curve points — drag to adjust freq, Ctrl+click to flatten high end");
	  public static string PerfVfReadFail => T("无法读取 V-F 曲线，请确认 NVIDIA 驱动已加载", "無法讀取 V-F 曲線，請確認 NVIDIA 驅動已載入", "Cannot read V-F curve. Ensure NVIDIA driver is loaded");
	  public static string PerfVfEdited(int count) => T(
	    $"已编辑 {count} 个点 — 点击 [应用曲线] 写入 GPU",
	    $"已編輯 {count} 個點 — 點擊 [應用曲線] 寫入 GPU",
	    $"Edited {count} points — click [Apply Curve] to write GPU");
	  public static string PerfVfNoChanges => T("曲线无修改", "曲線無修改", "No changes");
	  public static string PerfVfReadFirst => T("请先读取 V-F 曲线", "請先讀取 V-F 曲線", "Read V-F curve first");
	  public static string PerfVfWriting => T("正在写入 V-F 曲线...", "正在寫入 V-F 曲線...", "Writing V-F curve...");
	  public static string PerfVfWriteDone(int wrote, int matched, int total) => T(
	    $"应用成功 — 写入 {wrote} 点，回读验证 {matched}/{total} 点匹配",
	    $"應用成功 — 寫入 {wrote} 點，回讀驗證 {matched}/{total} 點匹配",
	    $"Applied — wrote {wrote} points, readback verified {matched}/{total} points");
	  public static string PerfVfWritePartial(int wrote, int matched) => T(
	    $"写入 {wrote} 点但回读验证仅 {matched} 点匹配 — GPU 可能不支持 V-F 曲线编辑（OEM 锁）",
	    $"寫入 {wrote} 點但回讀驗證僅 {matched} 點匹配 — GPU 可能不支援 V-F 曲線編輯（OEM 鎖）",
	    $"Wrote {wrote} points but only {matched} verified — GPU may not support V-F editing (OEM lock)");
	  public static string PerfVfWriteFail => T("V-F 曲线写入失败，请检查 NVIDIA 驱动", "V-F 曲線寫入失敗，請檢查 NVIDIA 驅動", "V-F curve write failed. Check NVIDIA driver");
	  public static string PerfVfRestoring => T("正在恢复默认 V-F 曲线...", "正在恢復預設 V-F 曲線...", "Restoring default V-F curve...");
	  public static string PerfVfRestoreDone => T("已恢复默认 V-F 曲线（所有偏移归零）", "已恢復預設 V-F 曲線（所有偏移歸零）", "Restored default V-F curve (all offsets zeroed)");
	  public static string PerfVfRestoreFail => T("恢复默认失败，请检查 NVIDIA 驱动", "恢復預設失敗，請檢查 NVIDIA 驅動", "Restore failed. Check NVIDIA driver");
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
	  public static string FanCurveHint => T("拖拽控制点可进一步微调", "拖拽控制點可進一步微調", "Drag control points to fine-tune");

	  public static string HelpWindowTitleBar => T("OMEN X Hub", "OMEN X Hub", "OMEN X Hub");
	  public static string HelpCreditsGuiDesign => T("OMEN X Hub GUI设计，功能打磨", "OMEN X Hub GUI設計，功能打磨", "OMEN X Hub GUI design & polishing");
	  public static string HelpCreditsSuperHub => T("OmenSuperHub 提供本项目主要框架及代码", "OmenSuperHub 提供本項目主要框架及程式碼", "OmenSuperHub — core framework & code");
	  public static string HelpCreditsOmenMon => T("OmenMon OmenHwCtl - 本项目的主要灵感来源，提供了交互命令与探索OGH交互的方法。", "OmenMon OmenHwCtl - 本項目的主要靈感來源，提供了交互命令與探索OGH交互的方法。", "OmenMon OmenHwCtl — main inspiration, OGH interaction commands & methods.");
	  public static string HelpCreditsLhm => T("硬件监控核心库支持", "硬體監控核心庫支援", "Hardware monitoring core library support");

	  public static string MacroNone => T("(none)", "(none)", "(none)");
	  public static string MacroNewMacro => T("New Macro", "New Macro", "New Macro");
	  
	  public static string PipelineEditorPowerModeEco => T("节能 (0)", "節能 (0)", "Eco (0)");
	  public static string PipelineEditorPowerModeBalanced => T("平衡 (1)", "平衡 (1)", "Balanced (1)");
	  public static string PipelineEditorPowerModePerf => T("性能 (2)", "效能 (2)", "Performance (2)");
	  public static string PipelineEditorFanSilent => T("安静模式", "安靜模式", "Silent Mode");
	  public static string PipelineEditorFanCool => T("降温模式", "降溫模式", "Cool Mode");
	  public static string PipelineEditorFanCustom => T("导入自定义曲线", "導入自訂曲線", "Import Custom Curve");
	  public static string PipelineEditorFanManual => T("手动模式", "手動模式", "Manual Mode");
	  public static string PipelineEditorGpuOff => T("关闭独显", "關閉獨顯", "Disable dGPU");
	  public static string PipelineEditorGpuOn => T("开启独显", "開啟獨顯", "Enable dGPU");
	  public static string PipelineEditorMicMute => T("静音", "靜音", "Mute");
	  public static string PipelineEditorMicUnmute => T("取消静音", "取消靜音", "Unmute");
	  public static string PipelineEditorToggleOn => T("开启", "開啟", "On");
	  public static string PipelineEditorToggleOff => T("关闭", "關閉", "Off");
	  public static string PipelineEditorDgpuMax => T("CTGP开+DB开 (max)", "CTGP開+DB開 (max)", "CTGP On+DB On (max)");
	  public static string PipelineEditorDgpuMed => T("CTGP开+DB关 (med)", "CTGP開+DB關 (med)", "CTGP On+DB Off (med)");
	  public static string PipelineEditorDgpuMin => T("CTGP关+DB关 (min)", "CTGP關+DB關 (min)", "CTGP Off+DB Off (min)");
	  public static string PipelineEditorRecord => T("录制", "錄製", "Record");
	  public static string PipelineEditorRecording => T("点击录制...", "點擊錄製...", "Click to record...");
	  public static string PipelineEditorPressKey => T("按下快捷键...", "按下快捷鍵...", "Press hotkey...");
	  public static string PipelineEditorBrowse => T("浏览...", "瀏覽...", "Browse...");
	  public static string PipelineEditorSelectApp => T("选择程序", "選擇程式", "Select Application");
	  public static string PipelineEditorExeFilter => T("可执行文件|*.exe|所有文件|*.*", "可執行檔|*.exe|所有檔案|*.*", "Executable|*.exe|All Files|*.*");
	  public static string PipelineEditorFanCurveFilter => T("风扇曲线 JSON|*.json|所有文件|*.*", "風扇曲線 JSON|*.json|所有檔案|*.*", "Fan Curve JSON|*.json|All Files|*.*");
	  public static string PipelineEditorFanCurveInvalid => T("无效的风扇曲线文件", "無效的風扇曲線檔案", "Invalid fan curve file");
	  public static string PipelineEditorFanCurveImportFail => T("读取文件失败", "讀取檔案失敗", "Failed to read file");
	  public static string PipelineEditorImportFailTitle => T("导入失败", "導入失敗", "Import Failed");
	  public static string PipelineEditorWavFilter => T("WAV 文件|*.wav|所有文件|*.*", "WAV 檔案|*.wav|所有檔案|*.*", "WAV Files|*.wav|All Files|*.*");
	  public static string PipelineEditorExeBatFilter => T("可执行文件|*.exe;*.bat;*.cmd|所有文件|*.*", "可執行檔|*.exe;*.bat;*.cmd|所有檔案|*.*", "Executable|*.exe;*.bat;*.cmd|All Files|*.*");
	  public static string PipelineEditorD2Format => T("D2", "D2", "D2");

	  public static string OsdWindowSmartLabel => T("smart", "smart", "smart");
	  public static string FanDragHint => T("可右键创建或删除控制点", "可右鍵創建或刪除控制點", "Right-click to add or delete control points");
	  public static string FanModeChangeHint => T("💡 拖拽调整后需切换一次风扇模式（如切到静音再切回自定义）才能实时生效", "💡 拖拽調整後需切換一次風扇模式（如切到靜音再切回自訂）才能即時生效", "⚠ After dragging, switch fan mode (e.g. to Silent then back to Custom) to apply changes immediately");
	  public static string MacroListHeading => T("宏列表", "巨集列表", "Macro List");
	  // ponytail: Automation missing strings
	  public static string AutoEnableHeading => T("启用自动化", "啟用自動化", "Enable Automation");
	  public static string AutoEnableDesc => T("只有在本程序运行时，自动化才可生效。", "只有在本程式運行時，自動化才可生效。", "Automation only works while this app is running.");
	  public static string AutoNoPipelinesText => T("当前没有自动化脚本，请点击「新建」来新建一项。", "目前沒有自動化腳本，請點擊「新建」來新建一項。", "No automation pipelines yet. Click New to create one.");
	  public static string AutoNoQuickActionsText => T("没有快捷操作，请点击「新建」来新建快捷操作。", "沒有快捷操作，請點擊「新建」來新建快捷操作。", "No quick actions yet. Click New to create one.");
	  public static string AutoAddNew => T("新建", "新建", "New");
	  public static string AutoQuickActionsDesc => T("你可以在系统托盘的图标上右键来快速触发这些快捷操作。", "你可以在系統托盤的圖示上按右鍵來快速觸發這些快捷操作。", "Right-click the tray icon to quickly trigger these actions.");
	  
	  // ponytail: PerfPage C-State combo items used in code-behind
	  public static string PerfCStateNone => T("无限制", "無限制", "Unlimited");
	  public static string PerfCState1 => T("C1", "C1", "C1");
	  public static string PerfCState2 => T("C2", "C2", "C2");
	  public static string PerfCState3 => T("C3", "C3", "C3");
	  public static string PerfCState4 => T("C4", "C4", "C4");
	  public static string PerfCState5 => T("C5", "C5", "C5");
	  public static string PerfCState6 => T("C6", "C6", "C6");
	  public static string PerfCState7 => T("C7", "C7", "C7");
	  public static string PerfCState8 => T("C8", "C8", "C8");
	  public static string PerfCState9 => T("C9", "C9", "C9");
	  public static string PerfCState10 => T("C10", "C10", "C10");
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
