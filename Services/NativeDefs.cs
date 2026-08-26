// NativeDefs.cs - 共享的 P/Invoke 定义 (电源模式 overlay GUID)
// ponytail: GUID 原先在 4 处重复定义; PowerSetActive*/DEVMODE 等函数与结构
// 已统一归口到 Pages/NativeMethods.cs (NativeMethods_Display/Power/Proc)。
// 注意类名用 PowerOverlay 避免与 System.Windows.Forms.PowerModes enum 冲突。
using System;

namespace OmenSuperHub.Services {
  /// <summary>
  /// Windows Power Mode overlay GUIDs (shared across services).
  /// </summary>
  internal static class PowerOverlay {
    // 节能模式 (Power Mode = 0)
    public static readonly Guid BestPowerEfficiency = Guid.Parse("961cc777-2547-4f9d-8174-7d86181b8a7a");
    // 性能模式 (Power Mode = 2)
    public static readonly Guid BestPerformance = Guid.Parse("ded574b5-45a0-4f42-8737-46345c09c238");
  }
}

