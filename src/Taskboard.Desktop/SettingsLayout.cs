using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Taskboard.Desktop;

internal sealed partial class SettingsSurface
{
    // Group the existing controls without rebuilding their bindings or handlers.
    internal static void OrganizeSettingsPage(string name, StackPanel panel)
    {
        var items = panel.Children.Cast<UIElement>().ToArray();
        if (items.Length == 0) return;
        var descriptions = new Dictionary<string, string>
        {
            ["主设置"] = "日常操作、快捷键和启动方式。",
            ["窗口"] = "调整列表显示、透明度和窗口位置。",
            ["监听"] = "为指定应用设置统一的通知提示音。",
            ["归档"] = "管理自动归档规则，找回已归档的任务。",
            ["数据与备份"] = "备份程序配置，管理任务与配置的保存位置。",
            ["AI平台接入情况"] = "连接已有聊天，让任务状态与 AI 工作进度对应。",
            ["飞书接入情况"] = "查看飞书连接与同步的开放情况。"
        };
        var title = items[0] is TextBlock oldTitle && name == "归档" ? oldTitle.Text : SectionCaption(name);
        void Header()
        {
            var heading = new StackPanel { Margin = new Thickness(0, 4, 0, 16), Tag = "SettingsPageHeading" };
            var caption = Ui.Label(title, 20); caption.FontWeight = FontWeights.SemiBold; heading.Children.Add(caption);
            heading.Children.Add(Ui.Label(descriptions.GetValueOrDefault(name, ""), 12, Ui.Muted)); panel.Children.Add(heading);
        }
        void Group(string title, IEnumerable<UIElement> children)
        {
            var body = new StackPanel();
            if (title.Length > 0) { var caption = Ui.Label(title, 14); caption.FontWeight = FontWeights.SemiBold; caption.Margin = new Thickness(0, 0, 0, 10); body.Children.Add(caption); }
            foreach (var child in children) body.Children.Add(child);
            CompactActions(body); panel.Children.Add(SettingsCard(body));
        }
        if (name == "主设置")
        {
            var startup = Array.FindIndex(items, x => x is CheckBox { Tag: "SettingsAutoStart" });
            var hotkey = startup - 1;
            if (startup < 2) return;
            panel.Children.Clear(); Header();
            Group("日常使用", items.Skip(1).Take(hotkey - 1));
            Group("", items.Skip(hotkey).Take(1));
            var startupItems = items.Skip(startup).Take(items.Length - startup - 1).ToArray();
            Group("启动与快捷方式", startupItems.Take(2));
            if (startupItems.Length > 2)
            {
                var repair = new StackPanel(); foreach (var child in startupItems.Skip(2)) repair.Children.Add(child);
                CompactActions(repair); panel.Children.Add(SettingsDisclosure("启动诊断与修复", repair));
            }
            panel.Children.Add(items[^1]);
        }
        else if (name == "窗口")
        {
            var resize = Array.FindIndex(items, x => x is CheckBox { Tag: "SettingsResize" });
            if (resize < 3) return;
            panel.Children.Clear(); Header();
            Group("", items.Skip(1).Take(1));
            Group("透明度", items.Skip(2).Take(resize - 2));
            Group("尺寸与位置", items.Skip(resize));
        }
        else if (name == "数据与备份" && items.Length == 10)
        {
            panel.Children.Clear(); Header();
            Group("备份与恢复", new[] { items[3], items[7], items[8] });
            var locations = new StackPanel(); locations.Children.Add(Ui.Label("任务与草稿", 13));
            foreach (var child in items.Skip(1).Take(2)) locations.Children.Add(child);
            foreach (var child in items.Skip(4).Take(3)) locations.Children.Add(child);
            CompactActions(locations); panel.Children.Add(SettingsDisclosure("存储位置", locations));
            panel.Children.Add(items[9]);
        }
        else if (name == "归档")
        {
            // Policy controls stay together, while archive items remain individually accessible.
            panel.Children.Clear(); Header();
            Group("归档规则", items.Skip(1).Take(5));
            foreach (var child in items.Skip(6)) panel.Children.Add(child);
        }
        else if (name == "监听")
        {
            var apps = Array.FindIndex(items, x => x is TextBlock { Text: "应用与音频" });
            var history = Array.FindIndex(items, x => x is TextBlock { Text: "近期捕获" });
            if (apps < 1 || history < apps) return;
            panel.Children.Clear(); Header();
            Group("播放与音量", items.Skip(1).Take(apps - 1));
            foreach (var child in items.Skip(apps).Take(history - apps)) panel.Children.Add(child);
            Group("", items.Skip(history));
        }
        else
        {
            panel.Children.Clear(); Header();
            foreach (var child in items.Skip(1)) panel.Children.Add(child);
        }
    }

    private static Border SettingsCard(UIElement child) => new()
    {
        Child = child, Background = Ui.Card, BorderBrush = Ui.Line, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 14)
    };

    private static Expander SettingsDisclosure(string caption, UIElement child) => new()
    {
        Header = caption, Content = child, Foreground = Ui.Muted, FontSize = 12,
        Margin = new Thickness(0, 10, 0, 12), Padding = new Thickness(0, 8, 0, 0), IsExpanded = false
    };

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
