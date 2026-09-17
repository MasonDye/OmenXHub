using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace OverlayDemo.Core;

// 卡片数据模型（名称/数值/颜色/透明度/样式）
public class CardItem : INotifyPropertyChanged
{
    private string _menuName = "";
    private string _menuData = "--";
    private string _menuData2 = "";
    private bool _menuChecked = true;
    private Brush _menuColor = Brushes.White;
    private Brush _menuColorEx = Brushes.White;
    private double _menuTextTransparency = 1.0;
    private double _menuBgTransparency = 1.0;
    private Style _menuStyle;
    private bool _menuEnabled = true;

    public string MenuName { get => _menuName; set => Set(ref _menuName, value); }
    public string MenuData { get => _menuData; set => Set(ref _menuData, value); }
    public string MenuData2 { get => _menuData2; set => Set(ref _menuData2, value); }
    public bool MenuChecked { get => _menuChecked; set => Set(ref _menuChecked, value); }
    public Brush MenuColor { get => _menuColor; set => Set(ref _menuColor, value); }
    public Brush MenuColorEx { get => _menuColorEx; set => Set(ref _menuColorEx, value); }
    public double MenuTextTransparency { get => _menuTextTransparency; set => Set(ref _menuTextTransparency, value); }
    public double MenuBgTransparency { get => _menuBgTransparency; set => Set(ref _menuBgTransparency, value); }
    public Style MenuStyle { get => _menuStyle; set => Set(ref _menuStyle, value); }
    public bool MenuEnabled { get => _menuEnabled; set => Set(ref _menuEnabled, value); }

    public event PropertyChangedEventHandler PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

// 温度等级配色：白(正常)/绿(良好)/橙(警告)/红(危险)
public enum TemperatureState { Normal = 0, Good = 1, Warning = 2, Critical = 3 }

public static class CardLogic
{
    public static readonly Color[] TemperatureColors =
    {
        Color.FromRgb(238, 238, 238),   // Normal
        Color.FromRgb(15, 250, 132),    // Good
        Color.FromRgb(255, 163, 56),    // Warning
        Color.FromRgb(249, 53, 15),     // Critical
    };

    public static TemperatureState FromTemperature(double celsius) => celsius switch
    {
        < 45 => TemperatureState.Good,
        < 65 => TemperatureState.Normal,
        < 80 => TemperatureState.Warning,
        _ => TemperatureState.Critical,
    };

    // 选中(CPU/GPU/RAM/FPS)数为奇数时，第一张选中卡变 336 宽通栏补齐两列；
    // NET 恒为 334 宽双列通栏。items 顺序即显示顺序。
    public static void ApplyCardStyles(IReadOnlyList<CardItem> items, Style basic, Style extended, Style network)
    {
        bool extendedLeft = IsOddCheckedCount(items);
        foreach (var it in items)
        {
            if (it.MenuName == "NET") { it.MenuStyle = network; continue; }
            if (it.MenuChecked && extendedLeft) { extendedLeft = false; it.MenuStyle = extended; }
            else it.MenuStyle = basic;
        }
    }

    // 选中(非 NET)数是否为奇数 — 决定是否需要一张通栏卡补齐两列
    private static bool IsOddCheckedCount(IReadOnlyList<CardItem> items)
    {
        int n = 0;
        foreach (var it in items)
            if (it.MenuName != "NET" && it.MenuChecked) n++;
        return n % 2 == 1;
    }
}
