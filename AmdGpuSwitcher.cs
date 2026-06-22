using System;
using System.Linq;
using System.Reflection;

namespace OmenSuperHub {
  public static class AmdGpuSwitcher {
    public enum LocalADLSmartMuxEnableState {
      ADL_MUXCONTROL_DISABLED = 0,
      ADL_MUXCONTROL_ENABLED = 1
    }

    private static object GetSAGHelper() {
      Assembly commonAssembly = AppDomain.CurrentDomain.GetAssemblies()
          .First(a => a.GetName().Name == "HP.Omen.Core.Common");
      Type sagHelpType = commonAssembly.GetType(
          "HP.Omen.Core.Common.Utilities.SmartAccessGraphicsHelp.SmartAccessGraphicsHelp");
      PropertyInfo sagHelperProp = sagHelpType.GetProperty("SAGHelper",
          BindingFlags.Public | BindingFlags.Static);
      return sagHelperProp.GetValue(null);
    }

    public static bool IsSupported() {
      if (OmenHardware.HasNvidiaGpu() || !OmenHardware.HasAmdDiscreteGpu())
        return false;
      object helper = GetSAGHelper();
      if (helper == null) return false;
      PropertyInfo supportProp = helper.GetType().GetProperty(
          "SmartAccessGraphicsSupport",
          BindingFlags.Public | BindingFlags.Instance);
      return (bool)supportProp.GetValue(helper);
    }

    public static LocalADLSmartMuxEnableState GetMode() {
      object helper = GetSAGHelper();
      if (helper == null) return LocalADLSmartMuxEnableState.ADL_MUXCONTROL_DISABLED;
      PropertyInfo modeProp = helper.GetType().GetProperty(
          "SmartAccessGraphicsMode",
          BindingFlags.Public | BindingFlags.Instance);
      int modeValue = (int)modeProp.GetValue(helper);
      return (LocalADLSmartMuxEnableState)modeValue;
    }

    public static void SetMode(LocalADLSmartMuxEnableState mode) {
      object helper = GetSAGHelper();
      if (helper == null) return;
      Assembly commonAssembly = AppDomain.CurrentDomain.GetAssemblies()
          .First(a => a.GetName().Name == "HP.Omen.Core.Common");
      Type stateEnum = commonAssembly.GetType(
          "HP.Omen.Core.Common.Utilities.SmartAccessGraphicsHelp.ADLSmartMuxEnableState");
      object modeValue = Enum.ToObject(stateEnum, (int)mode);
      MethodInfo setMethod = helper.GetType().GetMethod(
          "SetSmartAccessGraphicsMode",
          BindingFlags.Public | BindingFlags.Instance);
      setMethod.Invoke(helper, new[] { modeValue });
    }
  }
}
