// HelpWindow.xaml.cs - 帮助/关于对话框
// 显示更新日志、功能指南、致谢信息、GitHub 链接和更新检查
using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace OmenSuperHub.Views {
  public partial class HelpWindow : Wpf.Ui.Controls.FluentWindow {
    private static HelpWindow _instance;

    public HelpWindow() {
      InitializeComponent();

      // Version info
      var version = Assembly.GetExecutingAssembly().GetName().Version;
      VersionText.Text = $"Version {version.Major}.{version.Minor}.{version.Build}";

      // Load help content
      LoadContent();
    }

    public static void ShowInstance() {
      Application.Current?.Dispatcher.Invoke(() => {
        if (_instance == null || !_instance.IsLoaded) {
          _instance = new HelpWindow();
        }
        _instance.Show();
        _instance.Activate();
      });
    }

    protected override void OnClosed(EventArgs e) {
      base.OnClosed(e);
      _instance = null;
    }

    private void BtnGitHub_Click(object sender, RoutedEventArgs e) {
        Process.Start("https://github.com/breadeding/OmenSuperHub")?.Dispose();
    }

    private void BtnUpdate_Click(object sender, RoutedEventArgs e) {
        Process.Start("https://github.com/breadeding/OmenSuperHub/releases")?.Dispose();
    }

    private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e) {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri))?.Dispose();
      e.Handled = true;
    }

    private void LoadContent() {
      // ── Tab 1: 更新说明 / Changelog ──────────────────────────────
      UpdateNotesText.Text =
          "更新说明 / Changelog\n\n" +
          "v4.x 重构更新\n" +
          "- Windows 11 Settings 风格 UI 重构：所有页面统一 CardControl + (*,Auto) 双列布局\n" +
          "- 仪表板 (Dashboard)：8 指标网格 + 颜色编码进度条 + 预设下拉（内置/自定义 ←→ 保存/重命名）\n" +
          "- 性能 (PerfPage)：GPU模式 / DB版本 / dState 三选框 + EcoQoS / CoreKeep 开关 + 可折叠展开 8 节\n" +
          "- 风扇 (FanPage)：风扇模式 / 灵敏度 / 曲线三选框 + AutoFanProtect 开关\n" +
          "- 灯光 (LightingPage)：设备 / 协议 / 动画速度 / 4区颜色 / 动画 选框 + 亮度/速度滑块\n" +
          "- 设置 (SettingsPage)：17+ 卡片（Omen键复合 / 自定义背景/LOGO / 浮窗 / 主题 / 语言 / 托盘 / 开机自启…）\n" +
          "- 自动化 (AutomationPage)：总开关 + 流水线列表 + 快捷操作区 + 16种触发 / 23种步骤\n" +
          "- 其它 (OtherPage)：6 个 ToggleSwitch（智能充电 / NumLock / CapsLock / 触摸板 / HWiNFO / HTTP API）\n" +
          "- 系统信息 (SysInfoPage)：5 选框（CPU/GPU/风扇/刷新/温度显示）+ 传感器列表 + GPU进程\n" +
          "- 宏 (MacroPage)：录制 / 回放 / 快捷键绑定 + 事件序列编辑器\n" +
          "- PipelineEditorWindow：4 模块重构 — 卡片选择器 / 触发步骤卡 + ToggleSwitch / 可展开体 / 统一 Margin=20\n" +
          "- 预设管理全面重写 (PresetManager)：固定预设永不持久化，自定义预设保存/恢复\n" +
          "- 全局鼠标滚轮支持 + 侧栏滚动条隐藏 + 托盘快捷操作重构\n" +
          "- OmenKey 轮询跨页面同步 + OSD 即时提示\n" +
          "- 性能优化：GC.Collect 移除 / Thread.Sleep → Task.Delay / ConfigService.Save 单字段写入\n" +
          "- LibreComputer.Open 后台线程 / QueryHardware 800ms 缓存 / 定时器频率降低\n" +
          "- SystemEvents 事件泄漏修复 / WMI ManagementScope 静态缓存\n" +
          "- 自定义主界面背景：图片选择 + 透明度滑块 + 高斯模糊开关\n" +
          "- MacroPage 修复：Cancel 不再创建空宏\n\n" +
          "v3.0 (OmenSuperHub Merge)\n" +
          "- 合并 OmenSuperHub 全部新功能\n" +
          "- 多语言支持（简体中文/繁體中文/English）\n" +
          "- 预设配置文件（极致性能/GPU优先/轻度使用/自定义）\n" +
          "- 灯光控制（键盘4区/WMI Dojo协议）\n" +
          "- GPU进程管理 / IccMax / AC Load Line / TPP / dState\n" +
          "- 图形模式切换（独显直连/混合模式）\n" +
          "- 数据本地化 / 日志系统 / 风扇控制（3风扇/逆转除尘）\n\n" +
          "v2.0\n" +
          "- UI 全面重构：全新黑色极简主题 + 致谢页面\n\n" +
          "v1.x\n" +
          "- WPF 迁移、GPU频率限制、动态托盘、DB解锁、浮窗、Omen键";

      // ── Tab 2: 风扇配置 / Fan Config ─────────────────────────────
      FanConfigHelp.Text =
          "风扇配置提供两种基础模式及自定义曲线：\n\n" +
          "【安静模式 (Silent)】以 BIOS 默认风扇曲线的 80% 运行，适合日常办公和轻度使用。\n\n" +
          "【降温模式 (Cool)】以 BIOS 默认风扇曲线的 100% 运行，适合游戏和高负载场景。\n\n" +
          "【灵敏度的选择】控制温度响应速度：\"实时\"立即响应；\"高\"轻微平滑（默认）；\"中\"和\"低\"减少风扇变速频率。\n\n" +
          "【自定义风扇曲线】\n" +
          "通过拖拽控制点绘制自定义风扇曲线，支持 CPU / GPU 分别设定。\n\n" +
          "【高温自动保护 (AutoFanProtect)】\n" +
           "启用后 CPU 温度超过 95°C 且风扇处于固定转速时，强制切换为降温曲线保护硬件。\n\n" +
          "【风扇除尘 (Fan Clean)】\n" +
          "反转风扇 30 秒清除内部灰尘。";

      // ── Tab 3: 风扇控制 / Fan Control ────────────────────────────
      FanControlHelp.Text =
          "风扇控制模式通过顶部的 ComboBox 切换：\n\n" +
          "【自动 (Auto)】根据风扇配置（安静/降温/自定义曲线）自动调节转速。" +
          "程序读取 CPU 和 GPU 温度插值计算目标转速。\n\n" +
          "【最大 (Max)】风扇全速运转。\n\n" +
          "【固定 RPM (Fixed)】锁定在指定转速（1600~6400 RPM）。\n\n" +
          "注意：\n" +
          "- 自动模式下取 CPU 和 GPU 对应转速的最大值\n" +
          "- 关闭 GPU 监控后仅根据 CPU 温度调节\n" +
          "- 风扇控制需要管理员权限";

      // ── Tab 4: 性能控制 / Performance ────────────────────────────
      PerformanceHelp.Text =
          "性能控制说明：\n\n" +
          "【性能预设】一键切换整机性能调优方案：\n" +
          "- 极致性能 (Extreme)：PL1=PL2=254W，适合极限游戏/跑分\n" +
          "- GPU优先 (GpuPriority)：PL1=PL2=45W，倾斜功耗给 GPU\n" +
          "- 轻度使用 (LightUse)：PL1=PL2=25W，延长续航\n" +
          "- 自定义分组 (Custom1-3)：切换时自动保存/恢复绑定的参数\n" +
          "仅 CPU 功率 / 电源计划 / GPU 频率上限 / TGP+PPAB / dState 跟随预设绑定；监控、灯光、宏、音频等独立保存。\n\n" +
          "【CPU 控制（可展开）】\n" +
          "- CPU 功率：PL1+PL2 限制，10W~254W\n" +
          "- IccMax：CPU 电流限制（安培）\n" +
          "- AC Load Line：负载线校准级别\n" +
          "- 电源模式：Windows 电源模式（最佳效率 / 平衡 / 最佳性能）\n" +
          "- 电源计划：切换 Windows 电源计划\n" +
          "- EcoQoS：后台进程自动应用效率模式\n" +
          "- Core Keep：持久化指定进程的 CPU 优先级和关联性\n\n" +
          "【GPU 控制（可展开）】\n" +
          "- TGP / PPAB：总图形功耗 + 动态加速开关与值\n" +
          "- dState：GPU 功耗状态（正常 / 低功耗）\n" +
          "- GPU 频率限制：使用 nvidia-smi 锁定频率上限（0=无限制）\n" +
          "- GPU 监控模式（独显直连 / 混合）\n" +
          "- DB 版本：替换驱动 VBIOS 以解锁更高 GPU 功耗\n\n" +
          "【显示（可展开）】\n" +
          "- 屏幕刷新率：设置显示器刷新率\n" +
          "- 最大帧率：通过 nvidia-smi 限制游戏帧率上限\n\n" +
          "GPU 核心 / 显存超频（需 DB 解锁后可用）";

      // ── Tab 5: 其他功能 / Other Features ─────────────────────────
      OtherHelp.Text =
          "各页面功能介绍：\n\n" +
          "═══ 仪表板 (Dashboard) ═══\n" +
          "实时显示 CPU 温度 / 使用率 / 频率 / 功率 + GPU 温度 / 使用率 / 频率 / 功率 / 显存占用 + 风扇转速 + 内存占用 + 网速。" +
          "颜色编码进度条（绿→黄→红）直观反映负载状态。\n" +
          "顶部预设组合框一键切换方案，内置预设（Extreme/GpuPriority/LightUse）不可删除，自定义预设支持重命名。\n\n" +
          "═══ 性能 (Performance) ═══\n" +
          "CPU 功率限制、IccMax、AC Load Line、电源模式/计划、EcoQoS、Core Keep。" +
          "GPU 模式、DB 版本、dState、TGP/PPAB、频率限制。" +
          "显示刷新率、最大帧率、GPU 超频。\n" +
          "每个区域均可通过右侧展开按钮 (ChevronDown) 折叠/展开。\n\n" +
          "═══ 风扇 (Fan) ═══\n" +
          "风扇模式（自动/最大/固定 RPM）、温度灵敏度（实时/高/中/低）、" +
          "曲线选择（安静/降温/自定义）、AutoFanProtect 开关、风扇除尘。\n\n" +
          "═══ 灯光 (Lighting) ═══\n" +
          "支持键盘 / 灯条（实验性）设备。协议：四分区 Basic / Dojo 四分区。\n" +
          "动画：无 / 颜色循环 / 星光 / 呼吸 / 波浪 / 雨滴 / 音频脉冲 / 五彩纸屑 / 太阳 / 划过。\n" +
          "4 区单独颜色设定（红/绿/蓝/白/冰蓝/粉/黄）+ 亮度与速度滑块。\n\n" +
          "═══ 设置 (Settings) ═══\n" +
          "浮窗：开关 / 字体大小 / 位置 / 显示器选择 / 背景透明度 / 文字透明度\n" +
          "硬件：Omen 键（5 种行为 + 循环预设候选勾选 / 应用路径）\n" +
          "界面：OSD 开关 / 锁定键提示 / 托盘图标（原版/自定义/动态）/ 开机自启 / 自定义 LOGO\n" +
          "系统：主题（跟随系统/深色/亮色）/ 语言 / 自定义背景（图片+透明度+高斯模糊）/ 数据本地化 / 调试日志\n\n" +
          "═══ 自动化 (Automation) ═══\n" +
          "总开关启用/停用所有自动化管道。\n" +
          "流水线：绑定触发条件 → 执行一系列步骤。16 种触发：\n" +
          "  进程启动/停止、程序启动、系统恢复、锁定/解锁电脑、\n" +
          "  接入/断开电源、外接显示器连接/断开、定时、电池高于/低于 %、\n" +
          "  CPU/GPU 温度高于、快捷操作\n" +
          "23 种步骤：应用预设 / 刷新率 / 电源计划 / 电源模式 / 最大帧率 /\n" +
          "  CPU功率 / GPU功率 / TPP / IccMax / AC Load Line / 风扇模式 / 风扇曲线 /\n" +
          "  温度灵敏度 / 休眠独显 / 显示器亮度 / 麦克风静音 / WiFi / 蓝牙 /\n" +
          "  播放音频 / 运行程序 / 延迟 / 通知 / 执行宏\n" +
          "快捷操作：右击流水线设为快捷操作后，在托盘菜单或页面按钮一键触发。\n\n" +
          "═══ 其它 (Other) ═══\n" +
          "智能充电：充至 80% 停止（需 BIOS 支持 Adaptive Battery Optimizer）\n" +
          "数字锁定 / 大写锁定 / 触摸板锁定 — 一键 ToggleSwitch\n" +
          "HWiNFO64 集成：共享温度/风扇/功耗到 HWiNFO64\n" +
          "HTTP API 服务：localhost:5000 提供硬件状态 REST API\n\n" +
          "═══ 系统信息 (SysInfo) ═══\n" +
          "CPU/GPU/风扇/刷新率/温度显示方式 5 个选框。" +
          "传感器实时温度列表（CPU / GPU / IR / 环境 / PCH / VR）。" +
          "PawnIO 驱动状态、GPU 进程管理（定位文件 / 切换图形首选项）。" +
          "支持平滑/实时温度显示模式，高/低刷新间隔。\n\n" +
          "═══ 宏 (Macro) ═══\n" +
          "录制键盘操作序列，支持回放、设置触发快捷键、启用/停用。" +
          "事件序列编辑器支持插入/删除/调整延迟。";
    }
  }
}
