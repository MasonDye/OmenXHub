// SystemTweaks.cs - 通用系统优化开关（注册表级，每项独立可逆）
// 应用 = 写入优化值；恢复 = 写回默认值或删除该项。所有项均可随时手动还原。
using System;
using System.Linq;
using Microsoft.Win32;

namespace OmenSuperHub.Services.SystemOptimization {

  public enum RegHiveKind { CurrentUser, LocalMachine }
  public enum RegViewKind { Default, Registry64, Registry32 }

  public sealed class RegEdit {
    public RegHiveKind Hive;
    public RegViewKind View;
    public string SubKey;
    public string ValueName;
    public RegistryValueKind Kind = RegistryValueKind.DWord;
    public int DWordValue;
    public string StringValue;
    /// <summary>false = 恢复时删除该值；true = 恢复时写 Default*。</summary>
    public bool HasDefault;
    public int DefaultDWord;
    public string DefaultString;
  }

  public enum TweakState { NotApplied, Partial, Applied }

  /// <summary>一项通用优化：由一个或多个注册表修改组成，原子地应用/恢复。</summary>
  public sealed class OptimizationTweak {
    public string Id;
    public bool NeedsRestart;
    public RegEdit[] Edits;
  }

  public static class SystemTweaks {

    /// <summary>全部优化项（Id 全局唯一，UI 与自检依赖此表）。</summary>
    public static readonly OptimizationTweak[] All = {

      new OptimizationTweak {
        Id = "game-dvr",
        NeedsRestart = true,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"System\GameConfigStore", ValueName = "GameDVR_Enabled",
            Kind = RegistryValueKind.DWord, DWordValue = 0, HasDefault = true, DefaultDWord = 1 },
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Software\Microsoft\Windows\CurrentVersion\GameDVR", ValueName = "AppCaptureEnabled",
            Kind = RegistryValueKind.DWord, DWordValue = 0, HasDefault = true, DefaultDWord = 1 }
        }
      },

      new OptimizationTweak {
        Id = "mouse-accel",
        NeedsRestart = false,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Control Panel\Mouse", ValueName = "MouseSpeed",
            Kind = RegistryValueKind.String, StringValue = "0", HasDefault = true, DefaultString = "1" },
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Control Panel\Mouse", ValueName = "MouseThreshold1",
            Kind = RegistryValueKind.String, StringValue = "0", HasDefault = true, DefaultString = "6" },
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Control Panel\Mouse", ValueName = "MouseThreshold2",
            Kind = RegistryValueKind.String, StringValue = "0", HasDefault = true, DefaultString = "10" }
        }
      },

      new OptimizationTweak {
        Id = "fast-startup",
        NeedsRestart = true,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.LocalMachine, View = RegViewKind.Default,
            SubKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", ValueName = "HiberbootEnabled",
            Kind = RegistryValueKind.DWord, DWordValue = 0, HasDefault = true, DefaultDWord = 1 }
        }
      },

      new OptimizationTweak {
        Id = "background-apps",
        NeedsRestart = true,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications",
            ValueName = "GlobalUserDisabled",
            Kind = RegistryValueKind.DWord, DWordValue = 1, HasDefault = true, DefaultDWord = 0 }
        }
      },

      new OptimizationTweak {
        Id = "delivery-optimization",
        NeedsRestart = false,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.LocalMachine, View = RegViewKind.Default,
            SubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config",
            ValueName = "DODownloadMode",
            Kind = RegistryValueKind.DWord, DWordValue = 0, HasDefault = true, DefaultDWord = 1 }
        }
      },

      new OptimizationTweak {
        Id = "no-auto-reboot",
        NeedsRestart = false,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.LocalMachine, View = RegViewKind.Default,
            SubKey = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU",
            ValueName = "NoAutoRebootWithLoggedOnUsers",
            Kind = RegistryValueKind.DWord, DWordValue = 1 }
        }
      },

      new OptimizationTweak {
        Id = "location",
        NeedsRestart = false,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.LocalMachine, View = RegViewKind.Default,
            SubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location",
            ValueName = "Value",
            Kind = RegistryValueKind.String, StringValue = "Deny", HasDefault = true, DefaultString = "Allow" }
        }
      },

      new OptimizationTweak {
        Id = "store-auto-update",
        NeedsRestart = false,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.LocalMachine, View = RegViewKind.Default,
            SubKey = @"SOFTWARE\Policies\Microsoft\WindowsStore", ValueName = "AutoDownload",
            Kind = RegistryValueKind.DWord, DWordValue = 2 }
        }
      },

      // ── 去广告专项 ──

      new OptimizationTweak {
        Id = "ads-personalization",
        NeedsRestart = false,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", ValueName = "Enabled",
            Kind = RegistryValueKind.DWord, DWordValue = 0, HasDefault = true, DefaultDWord = 1 }
        }
      },

      new OptimizationTweak {
        Id = "lock-screen-tips",
        NeedsRestart = false,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
            ValueName = "SubscribedContent-338387Enabled",
            Kind = RegistryValueKind.DWord, DWordValue = 0 },
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
            ValueName = "RotatingLockScreenEnabled",
            Kind = RegistryValueKind.DWord, DWordValue = 0, HasDefault = true, DefaultDWord = 1 }
        }
      },

      new OptimizationTweak {
        Id = "start-menu-suggestions",
        NeedsRestart = true,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
            ValueName = "SubscribedContent-338388Enabled",
            Kind = RegistryValueKind.DWord, DWordValue = 0 },
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
            ValueName = "SubscribedContent-338389Enabled",
            Kind = RegistryValueKind.DWord, DWordValue = 0 },
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
            ValueName = "SubscribedContent-353694Enabled",
            Kind = RegistryValueKind.DWord, DWordValue = 0 },
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
            ValueName = "SubscribedContent-353696Enabled",
            Kind = RegistryValueKind.DWord, DWordValue = 0 },
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            ValueName = "Start_IrisRecommendations",
            Kind = RegistryValueKind.DWord, DWordValue = 0, HasDefault = true, DefaultDWord = 1 }
        }
      },

      new OptimizationTweak {
        Id = "settings-suggestions",
        NeedsRestart = false,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
            ValueName = "SubscribedContent-310093Enabled",
            Kind = RegistryValueKind.DWord, DWordValue = 0 },
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
            ValueName = "SubscribedContent-314563Enabled",
            Kind = RegistryValueKind.DWord, DWordValue = 0 }
        }
      },

      new OptimizationTweak {
        Id = "taskbar-news",
        NeedsRestart = true,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Software\Microsoft\Windows\CurrentVersion\Feeds", ValueName = "ShellFeedsTaskbarViewMode",
            Kind = RegistryValueKind.DWord, DWordValue = 2, HasDefault = true, DefaultDWord = 0 }
        }
      },

      new OptimizationTweak {
        Id = "setup-tips",
        NeedsRestart = false,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
            ValueName = "SubscribedContent-314559Enabled",
            Kind = RegistryValueKind.DWord, DWordValue = 0 },
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
            ValueName = "SubscribedContent-338393Enabled",
            Kind = RegistryValueKind.DWord, DWordValue = 0 },
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
            ValueName = "SubscribedContent-353698Enabled",
            Kind = RegistryValueKind.DWord, DWordValue = 0 }
        }
      },

      new OptimizationTweak {
        Id = "onedrive-banners",
        NeedsRestart = false,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            ValueName = "ShowSyncProviderNotifications",
            Kind = RegistryValueKind.DWord, DWordValue = 0, HasDefault = true, DefaultDWord = 1 }
        }
      },

      new OptimizationTweak {
        Id = "office-ads",
        NeedsRestart = false,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Software\Microsoft\Office\16.0\Common", ValueName = "qmenable",
            Kind = RegistryValueKind.DWord, DWordValue = 0 },
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Software\Microsoft\Office\16.0\Common", ValueName = "TurnOffAdvertising",
            Kind = RegistryValueKind.DWord, DWordValue = 1, HasDefault = true, DefaultDWord = 0 },
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Software\Microsoft\Office\16.0\Common", ValueName = "TurnOffAssistance",
            Kind = RegistryValueKind.DWord, DWordValue = 1, HasDefault = true, DefaultDWord = 0 },
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Software\Microsoft\Office\16.0\Common", ValueName = "TurnOffFileFingerprint",
            Kind = RegistryValueKind.DWord, DWordValue = 1, HasDefault = true, DefaultDWord = 0 }
        }
      },

      // ── 关闭 Windows 更新 ──
      // 服务 Start 值：4=禁用，恢复默认 3=手动(按需)。NoAutoUpdate 策略恢复时删除。
      new OptimizationTweak {
        Id = "disable-windows-update",
        NeedsRestart = true,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.LocalMachine, View = RegViewKind.Default,
            SubKey = @"SYSTEM\CurrentControlSet\Services\wuauserv", ValueName = "Start",
            Kind = RegistryValueKind.DWord, DWordValue = 4, HasDefault = true, DefaultDWord = 3 },
          new RegEdit { Hive = RegHiveKind.LocalMachine, View = RegViewKind.Default,
            SubKey = @"SYSTEM\CurrentControlSet\Services\UsoSvc", ValueName = "Start",
            Kind = RegistryValueKind.DWord, DWordValue = 4, HasDefault = true, DefaultDWord = 3 },
          new RegEdit { Hive = RegHiveKind.LocalMachine, View = RegViewKind.Default,
            SubKey = @"SYSTEM\CurrentControlSet\Services\WaaSMedicSvc", ValueName = "Start",
            Kind = RegistryValueKind.DWord, DWordValue = 4, HasDefault = true, DefaultDWord = 3 },
          new RegEdit { Hive = RegHiveKind.LocalMachine, View = RegViewKind.Default,
            SubKey = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", ValueName = "NoAutoUpdate",
            Kind = RegistryValueKind.DWord, DWordValue = 1 }
        }
      },

      // ── 轻量稳定新增（参考业界实践，单值写注册表、即时生效、可逆）──

      // 性能体感：去掉 Explorer 启动延迟与 Edge 后台常驻、缩短进程失响应超时
      new OptimizationTweak {
        Id = "explorer-startup-delay",
        NeedsRestart = false,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize",
            ValueName = "StartupDelayInMSec",
            Kind = RegistryValueKind.DWord, DWordValue = 0 }
        }
      },
      new OptimizationTweak {
        Id = "edge-startup-boost",
        NeedsRestart = false,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.LocalMachine, View = RegViewKind.Default,
            SubKey = @"SOFTWARE\Policies\Microsoft\Edge", ValueName = "StartupBoostEnabled",
            Kind = RegistryValueKind.DWord, DWordValue = 0 },
          new RegEdit { Hive = RegHiveKind.LocalMachine, View = RegViewKind.Default,
            SubKey = @"SOFTWARE\Policies\Microsoft\Edge", ValueName = "AllowPrelaunch",
            Kind = RegistryValueKind.DWord, DWordValue = 0 },
          new RegEdit { Hive = RegHiveKind.LocalMachine, View = RegViewKind.Default,
            SubKey = @"SOFTWARE\Policies\Microsoft\Edge", ValueName = "BackgroundModeEnabled",
            Kind = RegistryValueKind.DWord, DWordValue = 0 }
        }
      },
      new OptimizationTweak {
        Id = "task-kill-timeout",
        NeedsRestart = false,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Control Panel\Desktop", ValueName = "HungAppTimeout",
            Kind = RegistryValueKind.String, StringValue = "1000", HasDefault = true, DefaultString = "5000" },
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Control Panel\Desktop", ValueName = "WaitToKillAppTimeout",
            Kind = RegistryValueKind.String, StringValue = "2000", HasDefault = true, DefaultString = "5000" },
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Control Panel\Desktop", ValueName = "LowLevelHooksTimeout",
            Kind = RegistryValueKind.String, StringValue = "1000", HasDefault = true, DefaultString = "5000" }
        }
      },
      new OptimizationTweak {
        Id = "service-kill-timeout",
        NeedsRestart = false,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.LocalMachine, View = RegViewKind.Default,
            SubKey = @"SYSTEM\CurrentControlSet\Control", ValueName = "WaitToKillServiceTimeout",
            Kind = RegistryValueKind.String, StringValue = "2000", HasDefault = true, DefaultString = "5000" }
        }
      },

      // 隐私防骚扰：基于诊断数据的个性化体验、反馈通知、SQM/CEIP、应用兼容性遥测
      new OptimizationTweak {
        Id = "tailored-experiences",
        NeedsRestart = false,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Software\Microsoft\Windows\CurrentVersion\Privacy",
            ValueName = "TailoredExperiencesWithDiagnosticDataEnabled",
            Kind = RegistryValueKind.DWord, DWordValue = 0, HasDefault = true, DefaultDWord = 1 }
        }
      },
      new OptimizationTweak {
        Id = "feedback-notifications",
        NeedsRestart = false,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
            ValueName = "DoNotShowFeedbackNotifications",
            Kind = RegistryValueKind.DWord, DWordValue = 1 }
        }
      },
      new OptimizationTweak {
        Id = "ceip-telemetry",
        NeedsRestart = false,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.LocalMachine, View = RegViewKind.Default,
            SubKey = @"SOFTWARE\Microsoft\SQMClient\Windows", ValueName = "CEIPEnable",
            Kind = RegistryValueKind.DWord, DWordValue = 0, HasDefault = true, DefaultDWord = 1 }
        }
      },
      new OptimizationTweak {
        Id = "app-compat-telemetry",
        NeedsRestart = false,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.LocalMachine, View = RegViewKind.Default,
            SubKey = @"SOFTWARE\Policies\Microsoft\Windows\AppCompat", ValueName = "AITEnable",
            Kind = RegistryValueKind.DWord, DWordValue = 0 }
        }
      },
      new OptimizationTweak {
        Id = "webcam-consent",
        NeedsRestart = false,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.LocalMachine, View = RegViewKind.Default,
            SubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam",
            ValueName = "Value",
            Kind = RegistryValueKind.String, StringValue = "Deny", HasDefault = true, DefaultString = "Allow" }
        }
      },

      // Win11 24H2+ AI/Recall 系列策略（单值开关，可逆）
      new OptimizationTweak {
        Id = "recall-snapshots",
        NeedsRestart = false,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.LocalMachine, View = RegViewKind.Default,
            SubKey = @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", ValueName = "TurnOffSavingSnapshots",
            Kind = RegistryValueKind.DWord, DWordValue = 1 }
        }
      },
      new OptimizationTweak {
        Id = "block-recall-enable",
        NeedsRestart = false,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.LocalMachine, View = RegViewKind.Default,
            SubKey = @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", ValueName = "AllowRecallEnablement",
            Kind = RegistryValueKind.DWord, DWordValue = 0, HasDefault = true, DefaultDWord = 1 }
        }
      },
      new OptimizationTweak {
        Id = "ai-data-analysis",
        NeedsRestart = false,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.LocalMachine, View = RegViewKind.Default,
            SubKey = @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", ValueName = "DisableAIDataAnalysis",
            Kind = RegistryValueKind.DWord, DWordValue = 1 }
        }
      },
      new OptimizationTweak {
        Id = "click-to-do",
        NeedsRestart = false,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.LocalMachine, View = RegViewKind.Default,
            SubKey = @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", ValueName = "DisableClickToDo",
            Kind = RegistryValueKind.DWord, DWordValue = 1 }
        }
      },

      // ── 隐私补充（独立的搜索/许可遥测路径）与游戏本性能体感收尾 ──

      new OptimizationTweak {
        Id = "msa-cloud-search",
        NeedsRestart = false,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.CurrentUser, View = RegViewKind.Default,
            SubKey = @"Software\Microsoft\Windows\CurrentVersion\SearchSettings",
            ValueName = "IsMSACloudSearchEnabled",
            Kind = RegistryValueKind.DWord, DWordValue = 0, HasDefault = true, DefaultDWord = 1 }
        }
      },
      new OptimizationTweak {
        Id = "license-telemetry",
        NeedsRestart = false,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.LocalMachine, View = RegViewKind.Default,
            SubKey = @"SOFTWARE\Policies\Microsoft\Windows NT\CurrentVersion\Software Protection Platform",
            ValueName = "NoGenTicket",
            Kind = RegistryValueKind.DWord, DWordValue = 1 }
        }
      },
      new OptimizationTweak {
        Id = "game-responsiveness",
        NeedsRestart = false,
        Edits = new[] {
          new RegEdit { Hive = RegHiveKind.LocalMachine, View = RegViewKind.Default,
            SubKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
            ValueName = "SystemResponsiveness",
            Kind = RegistryValueKind.DWord, DWordValue = 10, HasDefault = true, DefaultDWord = 20 }
        }
      }
    };

    // ── 状态检测 ──

    public static TweakState GetState(OptimizationTweak t) {
      int matched = 0;
      foreach (var e in t.Edits)
        if (IsApplied(e)) matched++;
      if (matched == 0) return TweakState.NotApplied;
      if (matched == t.Edits.Length) return TweakState.Applied;
      return TweakState.Partial;
    }

    /// <summary>应用(enabled=true)或恢复(false)；任一写失败抛异常，调用方决定回滚提示。</summary>
    public static void Apply(OptimizationTweak t, bool enabled) {
      foreach (var e in t.Edits)
        WriteEdit(e, enabled);
    }

    // ── 内部实现 ──

    static bool IsApplied(RegEdit e) {
      try {
        using (var key = Open(e, false)) {
          if (key == null) return false;
          object val = key.GetValue(e.ValueName, null);
          if (val == null) return false;
          return e.Kind == RegistryValueKind.DWord
            ? Convert.ToInt32(val) == e.DWordValue
            : string.Equals(Convert.ToString(val), e.StringValue, StringComparison.OrdinalIgnoreCase);
        }
      } catch {
        return false;
      }
    }

    static void WriteEdit(RegEdit e, bool enabled) {
      using (var key = Open(e, true)) {
        if (key == null) throw new InvalidOperationException("cannot open registry key");
        if (enabled) {
          if (e.Kind == RegistryValueKind.DWord)
            key.SetValue(e.ValueName, e.DWordValue, RegistryValueKind.DWord);
          else
            key.SetValue(e.ValueName, e.StringValue, RegistryValueKind.String);
        } else if (e.HasDefault) {
          if (e.Kind == RegistryValueKind.DWord)
            key.SetValue(e.ValueName, e.DefaultDWord, RegistryValueKind.DWord);
          else
            key.SetValue(e.ValueName, e.DefaultString, RegistryValueKind.String);
        } else {
          key.DeleteValue(e.ValueName, false);
        }
      }
    }

    static RegistryKey Open(RegEdit e, bool writable) {
      RegistryHive hive = e.Hive == RegHiveKind.LocalMachine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
      RegistryView view = e.View == RegViewKind.Registry64 ? RegistryView.Registry64
                        : e.View == RegViewKind.Registry32 ? RegistryView.Registry32
                        : RegistryView.Default;
      var baseKey = RegistryKey.OpenBaseKey(hive, view);
      return writable ? baseKey.CreateSubKey(e.SubKey, true) : baseKey.OpenSubKey(e.SubKey, false);
    }
  }
}
