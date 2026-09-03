using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using OverlayDemo.Core;

namespace OverlayDemo;

// AGENTS.md：非平凡逻辑留一个可运行检查。dotnet run --project OverlayDemo -- --selftest
public static class SelfTest
{
    public static bool Run()
    {
        int pass = 0, fail = 0;
        void Check(bool ok, string name)
        {
            if (ok) pass++;
            else { fail++; Console.WriteLine("FAIL: " + name); }
        }

        // 1. 通栏卡逻辑：选中(非NET)数为奇数 → 第一张选中卡 Extended；偶数 → 全 Basic；NET 恒 Network
        var basic = new Style(typeof(ContentControl));
        var ext = new Style(typeof(ContentControl));
        var net = new Style(typeof(ContentControl));
        string[] names = { "CPU", "GPU", "RAM", "FPS", "NET" };

        void VerifyStyles(string label, bool[] visible)
        {
            var items = new List<CardItem>();
            for (int i = 0; i < 5; i++)
                items.Add(new CardItem { MenuName = names[i], MenuChecked = visible[i] });
            CardLogic.ApplyCardStyles(items, basic, ext, net);

            int oddCount = 0, firstChecked = -1;
            for (int i = 0; i < 4; i++)
            {
                if (!visible[i]) continue;
                oddCount++;
                if (firstChecked < 0) firstChecked = i;
            }
            bool hasExtended = oddCount % 2 == 1;
            for (int i = 0; i < 5; i++)
            {
                Style want = i == 4 ? net
                    : (visible[i] && hasExtended && i == firstChecked) ? ext : basic;
                Check(ReferenceEquals(items[i].MenuStyle, want), $"{label} {names[i]}");
            }
        }

        VerifyStyles("all4", new[] { true, true, true, true, true });
        VerifyStyles("odd3", new[] { true, false, true, true, true });
        VerifyStyles("onlyFps", new[] { false, false, false, true, true });
        VerifyStyles("none", new[] { false, false, false, false, true });

        // 2. 温度等级阈值
        Check(CardLogic.FromTemperature(44.9) == TemperatureState.Good, "temp 44.9 -> Good");
        Check(CardLogic.FromTemperature(45) == TemperatureState.Normal, "temp 45 -> Normal");
        Check(CardLogic.FromTemperature(65) == TemperatureState.Warning, "temp 65 -> Warning");
        Check(CardLogic.FromTemperature(80) == TemperatureState.Critical, "temp 80 -> Critical");

        // 3. 透明度映射公式 (v*0.9+10)/100
        var cfg = new AppConfig { BackgroundTransparency = 0, TextTransparency = 100 };
        Check(Math.Abs(cfg.BackgroundOpacity - 0.10) < 1e-9, "opacity slider 0 -> 0.10");
        Check(Math.Abs(cfg.TextOpacity - 1.0) < 1e-9, "opacity slider 100 -> 1.00");

        // 4. 配置 XamlWriter 往返（含 per-widget 状态）
        cfg.HotkeyIndex = 2; cfg.State(0).X = 123.5;
        cfg.State(1).Launched = true; cfg.State(1).X = 77.5;
        var round = (AppConfig)System.Windows.Markup.XamlReader.Parse(System.Windows.Markup.XamlWriter.Save(cfg));
        Check(round.HotkeyIndex == 2 && Math.Abs(round.State(1).X - 77.5) < 1e-9 && round.State(1).Launched, "config roundtrip");

        // 5. 老配置迁移 + 全新配置默认
        var legacy = (AppConfig)System.Windows.Markup.XamlReader.Parse(
            "<AppConfig WidgetLaunched=\"True\" xmlns=\"clr-namespace:OverlayDemo.Core;assembly=OverlayDemo\" />");
        legacy.EnsureDefaults(false);
        Check(legacy.State(0).Launched, "legacy migrate launched");
        var legacyOff = (AppConfig)System.Windows.Markup.XamlReader.Parse(
            "<AppConfig WidgetLaunched=\"False\" xmlns=\"clr-namespace:OverlayDemo.Core;assembly=OverlayDemo\" />");
        legacyOff.EnsureDefaults(false);
        Check(!legacyOff.State(0).Launched, "legacy keep off");
        var freshCfg = new AppConfig();
        freshCfg.EnsureDefaults(true);
        Check(freshCfg.State(0).Launched, "fresh default launched");

        Console.WriteLine($"selftest: {pass} passed, {fail} failed");
        return fail == 0;
    }
}
