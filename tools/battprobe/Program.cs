using System;
using System.Management;
using System.IO;

class Probe {
  static ManagementObject _bios;
  static StreamWriter _log;
  static ManagementScope _scope;
  static void Log(string s) { Console.WriteLine(s); _log.WriteLine(s); _log.Flush(); }
  static void Init() {
    _scope = new ManagementScope(@"root\wmi");
    _scope.Connect();
    var searcher = new ManagementObjectSearcher(_scope, new ObjectQuery("SELECT * FROM hpqBIntM"));
    foreach (ManagementObject mo in searcher.Get()) { _bios = mo; break; }
    Log("hpqBIntM: " + (_bios != null ? "OK" : "NOT FOUND"));
  }
  static byte[] Send(uint cmdType, byte[] data, int outSize, uint command) {
    if (_bios == null) return null;
    try {
      string methodName = "hpqBIOSInt" + outSize;
      var biosDataIn = new ManagementClass(_scope, new ManagementPath("hpqBDataIn"), null).CreateInstance();
      biosDataIn["Command"] = command;
      biosDataIn["CommandType"] = cmdType;
      biosDataIn["Sign"] = new byte[] { 0x53, 0x45, 0x43, 0x55 };
      if (data != null) { biosDataIn["hpqBData"] = data; biosDataIn["Size"] = (uint)data.Length; }
      else biosDataIn["Size"] = (uint)0;
      var inParams = _bios.GetMethodParameters(methodName);
      inParams["InData"] = biosDataIn;
      var result = _bios.InvokeMethod(methodName, inParams, null);
      var outData = result["OutData"] as ManagementBaseObject;
      var rc = (uint)outData["rwReturnCode"];
      var bytes = outData["Data"] as byte[];
      Log($"  cmd={command} t={cmdType:X2} rc=0x{rc:X} data={BitConverter.ToString(bytes)}");
      return bytes;
    } catch (Exception e) { Log($"  ERR: {e.Message}"); return null; }
  }
  static void Main() {
    string logPath = @"E:\Desktop\OmenXHub-optimized\tools\battprobe\fan2_result.txt";
    _log = new StreamWriter(logPath, false);
    Log("=== Fan type decode ===");
    Init();
    if (_bios == null) { _log.Close(); return; }
    byte[] r = Send(0x2C, new byte[4]{0,0,0,0}, 128, 0x20008);
    if (r != null && r.Length >= 8) {
      Log("=== decoded FanType (OmenHardware.GetFanType logic) ===");
      var types = new System.Collections.Generic.List<int>();
      for (int i = 0; i < 4 && i < r.Length; i++) {
        types.Add(r[i] & 0x0F);
        types.Add((r[i] & 0xF0) >> 4);
      }
      if (types.Count > 0) types.RemoveAt(types.Count - 1);
      for (int i = 0; i < types.Count; i++) {
        Log($"  Fan[{i}] type={types[i]} ({FanName(types[i])})");
      }
      Log("  capabilities bits: " + BitConverter.ToString(r, 8, 4));
    }
    Log("=== done ===");
    _log.Close();
  }
  static string FanName(int t) {
    switch (t) {
      case 0: return "Unsupported";
      case 1: return "CPU";
      case 2: return "GPU";
      case 3: return "Exhaust";
      case 4: return "Pump";
      case 5: return "Intake";
      case 6: return "VRM";
      case 100: return "LightingBoard";
      default: return "?";
    }
  }
}
