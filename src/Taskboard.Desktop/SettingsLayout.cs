using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Taskboard.Desktop;

internal sealed partial class SettingsSurface
{
    // Move existing controls, retaining their bindings, handlers and options.
    // Only these pages need grouping; the AI cards keep their own progress UI.
    internal static void OrganizeSettingsPage(string name, StackPanel panel)
    {
        var items = panel.Children.Cast<UIElement>().ToArray();
        if (items.Length < 2) return;
        void Group(string title, IEnumerable<UIElement> children)
        {
            var body = new StackPanel();
            if (title.Length > 0) body.Children.Add(Ui.Label(title, 14, Ui.Accent));
            foreach (var child in children) body.Children.Add(child);
            CompactActions(body); panel.Children.Add(Ui.Panel(body));
        }
        if (name == "主设置")
        {
            var startup = Array.FindIndex(items, x => x is CheckBox { Tag: "SettingsAutoStart" });
            var hotkey = startup - 1;
            if (startup < 2) return;
            panel.Children.Clear(); panel.Children.Add(items[0]);
            Group("日常使用", items.Skip(1).Take(hotkey - 1));
            Group("", items.Skip(hotkey).Take(1));
            Group("启动与快捷方式", items.Skip(startup).Take(items.Length - startup - 1));
            panel.Children.Add(items[^1]);
        }
        else if (name == "窗口")
        {
            var resize = Array.FindIndex(items, x => x is CheckBox { Tag: "SettingsResize" });
            if (resize < 3) return;
            panel.Children.Clear(); panel.Children.Add(items[0]);
            Group("", items.Skip(1).Take(1));
            Group("透明度", items.Skip(2).Take(resize - 2));
            Group("尺寸与位置", items.Skip(resize));
        }
        else if (name == "数据与备份" && items.Length == 10)
        {
            panel.Children.Clear(); panel.Children.Add(items[0]);
            Group("任务数据", items.Skip(1).Take(2));
            Group("", items.Skip(4).Take(3));
            Group("配置备份", new[] { items[3], items[7], items[8], items[9] });
        }
        else if (name == "归档")
        {
            // Policy controls stay together, while archive items remain individually accessible.
            panel.Children.Clear(); panel.Children.Add(items[0]);
            Group("归档规则", items.Skip(1).Take(5));
            foreach (var child in items.Skip(6)) panel.Children.Add(child);
        }
        else if (name == "监听")
        {
            var apps = Array.FindIndex(items, x => x is TextBlock { Text: "应用与音频" });
            var history = Array.FindIndex(items, x => x is TextBlock { Text: "近期捕获" });
            if (apps < 1 || history < apps) return;
            panel.Children.Clear(); panel.Children.Add(items[0]);
            Group("监听与捕获", items.Skip(1).Take(apps - 1));
            foreach (var child in items.Skip(apps).Take(history - apps)) panel.Children.Add(child);
            Group("", items.Skip(history));
        }
    }

    private static void CompactActions(Panel panel)
    {
        var children = panel.Children.Cast<UIElement>().ToArray(); panel.Children.Clear();
        WrapPanel? actions = null;
        foreach (var child in children)
        {
            if (child is Button)
            {
                if (actions is null) { actions = new WrapPanel { Margin = new Thickness(0, 3, 0, 3) }; panel.Children.Add(actions); }
                actions.Children.Add(child);
            }
            else { actions = null; panel.Children.Add(child); }
        }
    }
}
