using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace OmenSuperHub.Services.NetworkBoost {
  /// <summary>扫描可用物理网卡（以太网/PPP/WLAN），只返回已连接且有可路由 IPv4 的网卡。</summary>
  internal static class NicScanner {
    static readonly NetworkInterfaceType[] AllowedTypes = {
      NetworkInterfaceType.Ethernet, NetworkInterfaceType.Ppp, NetworkInterfaceType.Wireless80211
    };

    public static List<NicInfo> Scan() {
      var list = new List<NicInfo>();
      NetworkInterface[] nifs;
      try { nifs = NetworkInterface.GetAllNetworkInterfaces(); }
      catch { return list; }
      foreach (var nif in nifs) {
        try {
          if (nif.OperationalStatus != OperationalStatus.Up) continue;
          if (!AllowedTypes.Contains(nif.NetworkInterfaceType)) continue;
          var props = nif.GetIPProperties();
          var v4props = props.GetIPv4Properties();
          if (v4props == null) continue;
          string ip = props.UnicastAddresses
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && IsRoutable(a.Address))
            .Select(a => a.Address.ToString()).FirstOrDefault();
          if (ip == null) continue;
          list.Add(new NicInfo {
            Index = v4props.Index,
            Name = nif.Name,
            Ip = ip,
            IfType = (int)nif.NetworkInterfaceType,
            IsPpp = nif.NetworkInterfaceType == NetworkInterfaceType.Ppp,
            DnsServers = props.DnsAddresses
              .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
              .Select(a => a.ToString()).ToList()
          });
        } catch { }
      }
      return list;
    }

    static bool IsRoutable(IPAddress a) {
      byte[] b = a.GetAddressBytes();
      if (b[0] == 127 || b[0] == 0) return false;          // 回环 / 未指定
      if (b[0] == 169 && b[1] == 254) return false;        // APIPA
      return true;
    }
  }
}
