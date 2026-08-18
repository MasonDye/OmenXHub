using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace OmenSuperHub.Services.NetworkBoost {
  /// <summary>
  /// 生成 sing-box 配置：TUN 入站 (172.19.0.1/30) + 三个本地 SOCKS 出站池（2001 有线 / 2002 无线 / 2003 聚合）+ direct。
  /// 防御规则（防环/DNS/UDP）固定强插在前，用户进程分流规则在后。
  /// </summary>
  internal static class SingboxConfigGenerator {
    public const string TunGateway = "172.19.0.1";

    // per-process 限速端口起始（步进 2：socks=N, http=N+1）
    const int LimitPortBase = 2010;

    public static void Write(string path, List<RoutingRule> rules, string hostExe,
      out List<KeyValuePair<int, double>> limitedPorts) {
      limitedPorts = new List<KeyValuePair<int, double>>();

      // DNS：纯 local 解析，不用 fakeip（避免证书校验/IP 直连兼容问题）
      var dnsServers = new List<object> {
        new Dictionary<string, object> { { "type", "local" }, { "tag", "dns-local" } }
      };
      var dns = new Dictionary<string, object> {
        { "servers", dnsServers },
        { "final", "dns-local" }
      };

      var inbounds = new List<object> {
        new Dictionary<string, object> {
          { "type", "tun" }, { "tag", "tun-in" }, { "interface_name", "OmenXHub-Tun" },
          { "address", new List<string> { TunGateway + "/30" } }, { "mtu", 1500 },
          { "auto_route", true }, { "strict_route", false }, { "stack", "system" }
        }
      };

      var outbounds = new List<object> {
        Socks("nic_ethernet", 2001), Socks("nic_wifi", 2002), Socks("aggregation", 2003),
        new Dictionary<string, object> { { "type", "direct" }, { "tag", "direct" } }
      };

      var routeRules = new List<object> {
        // 嗅探协议（用于 DNS 识别）
        new Dictionary<string, object> { { "action", "sniff" }, { "timeout", "300ms" } },
        // 防环：宿主进程自身流量直连
        new Dictionary<string, object> {
          { "process_path", new List<string> { hostExe } }, { "outbound", "direct" } },
        new Dictionary<string, object> {
          { "process_name", new List<string> { "OmenXHub.exe", "sing-box.exe" } }, { "outbound", "direct" } },
        // DNS 劫持
        new Dictionary<string, object> { { "port", new List<int> { 53 } }, { "action", "hijack-dns" } },
        new Dictionary<string, object> { { "protocol", new List<string> { "dns" } }, { "action", "hijack-dns" } },
        // 局域网直连：内网 NAS/打印机/SMB 等不走代理
        new Dictionary<string, object> {
          { "ip_cidr", new List<string> {
            "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16",
            "127.0.0.0/8", "169.254.0.0/16", "224.0.0.0/4", "255.255.255.255/32"
          } }, { "outbound", "direct" } },
        // UDP 直连：SOCKS5 不支持 UDP，让游戏/VoIP/QUIC 走默认路由
        new Dictionary<string, object> {
          { "network", new List<string> { "udp" } }, { "outbound", "direct" } }
      };

      int portCursor = LimitPortBase;
      foreach (var r in rules ?? new List<RoutingRule>()) {
        string name = (r.ProcessName ?? "").Trim();
        if (name.Length == 0) continue;
        string tag;
        if (r.LimitKBps > 0) {
          // per-process 限速：创建独立 SOCKS 出站，由 BoostService 启动带限速器的 ProxyEngine
          tag = "limit_" + portCursor;
          outbounds.Add(Socks(tag, portCursor));
          limitedPorts.Add(new KeyValuePair<int, double>(portCursor, r.LimitKBps));
          portCursor += 2;
        } else {
          tag = string.IsNullOrEmpty(r.Outbound) ? "aggregation" : r.Outbound;
        }
        routeRules.Add(new Dictionary<string, object> {
          { "process_name", new List<string> { name } }, { "outbound", tag } });
      }

      var root = new Dictionary<string, object> {
        { "log", new Dictionary<string, object> { { "level", "warn" }, { "timestamp", true } } },
        { "dns", dns },
        { "inbounds", inbounds },
        { "outbounds", outbounds },
        { "route", new Dictionary<string, object> {
            { "auto_detect_interface", true }, { "default_domain_resolver", "dns-local" },
            { "final", "aggregation" }, { "rules", routeRules } } }
      };

      File.WriteAllText(path, new JavaScriptSerializer().Serialize(root));
    }

    static Dictionary<string, object> Socks(string tag, int port) {
      return new Dictionary<string, object> {
        { "type", "socks" }, { "tag", tag }, { "server", "127.0.0.1" },
        { "server_port", port }, { "version", "5" }
      };
    }
  }
}
