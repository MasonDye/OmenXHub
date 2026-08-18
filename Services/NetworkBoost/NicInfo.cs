using System.Collections.Generic;

namespace OmenSuperHub.Services.NetworkBoost {
  /// <summary>一张物理网卡，Down/Up/Connections 由 ProxyEngine 的监控线程每秒刷新。</summary>
  internal class NicInfo {
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public string Ip { get; set; } = "";
    public int IfType { get; set; }
    public bool IsPpp { get; set; }
    public int Metric { get; set; } = -1;
    public double DownMbps { get; set; }
    public double UpMbps { get; set; }
    public int Connections { get; set; }
    public List<string> DnsServers { get; set; } = new List<string>();
  }
}
