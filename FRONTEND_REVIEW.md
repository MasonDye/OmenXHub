# OmenXHub 前端优化审查建议

> 审查日期: 2026-08-16 · 范围: Pages/Views/Themes 全部 XAML + 代码内 UI 逻辑
> 方法: 代码走查 + 实机截图分析(灯光页)+ 度量统计

## 总体评价

**底子是好的**: 设计 token 体系(`Themes/Variables.xaml`)、Omen 色系分层(Colors/OmenBrand)、按控件拆分的样式文件(`_ButtonStyles` 等 8 个)、303 处 `DynamicResource` 笔刷引用、灯光页的 VSM 宽窄自适应 —— 这些是可持续演进的骨架。

**主要债务集中在三处**: 字号无体系(13 种字号散落 567 处)、硬编码颜色(55 处,亮色主题雷区)、页面生命周期副作用(Loaded 多次触发模式各页自行防御)。

---

## 度量基线

| 指标 | 数值 | 评价 |
|---|---|---|
| 硬编码十六进制色(Pages+Views) | 55 处 | FloatingWindow 25 处最集中 |
| FontSize 种类 | 13 种(9~28) | 无字号 token,层级混乱 |
| FontSize 使用频次 | 11px×167 / 12px×106 / 13px×95 / 14px×72 | 11-14 四档占 78%,可收敛 |
| DynamicResource 笔刷 | 303 处 | ✅ 主体健康 |
| 主题文件 | Dark/Light 双份 | ⚠️ 未验证 Light 全页回归 |

---

## P0 — 高收益低成本(建议先做)

### 1. 字号 token 化(收敛 13 种 → 5 档)
- **现状**: `FontSize="11/12/13/14/16/18/22/24/28"` 全硬编码,同层级文本大小不一(截图里标签 12、正文 13、小字 11 混排)。
- **建议**: `Variables.xaml` 加 5 档: `FontSizeCaption=11 / FontSizeBody=12(默认) / FontSizeSubtitle=13 / FontSizeTitle=14 / FontSizeHeader=18`,22+ 仅 Dashboard 大数字用。全量 sed 替换 11→Caption、12→Body、13→Subtitle、14→Title。
- **工作量**: 0.5 天(替换+抽查)。**收益**: 全局文本层级立刻统一,后续调字号一处生效。

### 2. 清除 55 处硬编码颜色 — **实测后修正:绝大多数是有意保留**
- **实测结论**(2026-08-16 走查): 55 处中 FloatingWindow 占 25(OSD 悬浮窗恒暗设计,跨主题不变是有意的);其余为 Fluent 状态色(核心保持的 蓝/琥珀/橙/红 严重度)、OMEN 品牌渐变(MainWindow)、灯光预览占位色(运行时被真实颜色覆盖)—— **均为语义/数据可视化色,不应主题化**。
- **已做**: 中性分隔线入 `DividerBrush` token(Variables.xaml)。
- **残留动作**: 无强制项;若后续要做状态色统一,可另建 `StatusInfo/Success/Warning/Danger` 语义色 token 集,非必须。

### 3. Light 主题全页回归
- **现状**: `Colors.Light.xaml` 存在但从未系统性走查;55 处硬编码之外,`_MiscStyles` 菜单阴影 `Color="#80000000"` 在亮色下过重。
- **建议**: 切 Light 截 9 个页面(仪表盘/性能/风扇/灯光/核心保持/网络/路由/其他/设置),逐页比对;补 `DividerBrush`、阴影双色 token。
- **工作量**: 0.5 天(截图走查)+ 修复按需。

---

## P1 — 结构性改进(下个迭代)

### 4. 页面生命周期统一防御
- **现状**: `CachedPageService` 缓存页面导致 Loaded/Unloaded 多次触发,各页自己写防御(DashboardPage.xaml.cs:85、PerfPage:31、FanPage:48 的 ponytail 注释;本会话给 LightingPage 加了 UpdateLayout 修"空白页")。核心保持二级页面与灯光页先后踩过同款坑 —— **还有页面会再踩**。
- **建议**: 排查所有在 Loaded 里做"大量可视树变更/ItemsControl 填充"的页面(OtherPage、SettingsPage、NetworkBoostPage、RoutingRulesPage),统一补 `Loaded 末尾 UpdateLayout()`;长期看抽一个 `PageLifecycleHelper.OnLoadedSafe(page, buildAction)` 收口。
- **工作量**: 排查 2h + 逐页补 1 行。

### 5. 控件宽度规范推广
- **现状**: 灯光页本轮已统一 MinWidth=180,但其他页仍是 120/140/160 混用(SceneCombo 140、LightDevCombo 120、AnimSpeedCombo 140…)。
- **建议**: `Variables.xaml` 加 `ComboMinWidth=180`、`ComboMinWidthCompact=140`(侧栏窄列用),XAML 引用 token。同表单内下拉对齐,视觉整齐度提升明显(灯光页已验证)。

### 6. 空状态与加载态
- **现状**: 场景列表、路由规则、进程列表(CoreKeep)空数据时只有文字提示或什么都不显示;网络页操作无进行中反馈。
- **建议**: 统一空状态组件(图标+一句话+主操作按钮),Wpf.Ui 的 `ui:SymbolIcon` + `InfoBar` 组合即可,不必引入新依赖。

### 7. NavigateToPage 每次导航写盘日志
- **现状**: `MainWindow.NavigateToPage` 每次侧栏跳转 `File.AppendAllText` 两行到 `OmenXHub_ck.log`(临时诊断遗留)。
- **建议**: 删除或挂到 `ConfigService.VerboseLogging` 下。微性能+磁盘卫生。

---

## P2 — 打磨项(有空再做)

8. **动效统一**: 目前仅窗口褪色;卡片/页面切换无过渡。Wpf.Ui 自带 `NavigationTransitionInfo` 类效果,侧栏切换加 150ms 滑入即可,注意与 CachedPageService 兼容(内容复用时过渡不重播,可接受)。
9. **三语截断审计**: 中/繁/英长度差大(如"音频脉冲"vs"Audio Pulse"),窄列下拉与按钮在高 DPI 下可能截断;重点检查设置页 ComboBox 与侧栏项。
10. **DPI 回归**: 100%/125%/150% 各截一轮;重点 FloatingWindow(OSD 悬浮窗)与灯光页 VSM 阈值(1100/480 DIP,缩放无关,理论安全,验证即可)。
11. **对比度无障碍**: `TextFillColorSecondaryBrush`(11px 小字大量使用)在暗底对比度约 4.5:1 边缘;状态文字(如灯光页"未连接")建议升一档笔刷。
12. **滚动体验**: 设置页较长,`_ScrollStyles` 已有样式;建议给根 ScrollViewer 加 `PanningMode` 触屏支持。

---

## 本会话已完成(参考基线)

| 项 | 位置 |
|---|---|
| 灯带动效校准表(显示名=真实动效) | LightingPage.LightBarAnims |
| 单键+灯带合并卡(一张卡一个设备) | PlaceLightBarPanel 运行时搬移 |
| 卡片圆角 token 化(卡 8 / 小控件 4) | _MiscStyles CardControl 隐式样式 |
| 灯光页下拉统一 180 / 键帽间距 / 分区分隔线 / 逐键网格居中对称 | LightingPage.xaml(.cs) |
| 全亮度控件 128/228 直写(四区/灯带/单键) | 三套 BtnHigh 处理器 |
| 褪色卡死看门狗("窗口全透明"bug) | MainWindow.FadeIn |
| 灯光页空白页修复(Loaded+UpdateLayout) | LightingPage Loaded |
| WMI 后端提速(缓存 hpqBIntM)+ 软件渲染动画引擎 | OmenHardware / LightingAnimationService |

## 验证方式建议

UI 无自动化覆盖,建议固定流程: 每轮改动后 **提权 `--selftest`**(逻辑断言)+ **9 页截图比对**(视觉回归人工过一遍,每页 <10s)。截图脚本模式本会话已验证可行(PrintWindow/topmost+CopyFromScreen)。
