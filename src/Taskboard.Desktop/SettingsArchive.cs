using System;
using System.Linq;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;

namespace Taskboard.Desktop;

internal sealed partial class SettingsSurface
{
    private bool archiveAllLoaded;
    internal void RefreshArchive() { if (CurrentSection == "归档") RefreshSection(); }
    private StackPanel ArchiveSettings()
    {
        var total = controller.Engine.State.Archives.Count;
        var panel = new StackPanel(); panel.Children.Add(Ui.Label("归档 · 共" + total + "条", 18));
        var preferences = controller.Engine.State.Preferences;
        var automatic = new CheckBox { Content = "已完成任务超过指定天数后自动归档", IsChecked = preferences.AutoArchiveCompleted, Tag = "AutoArchiveCompleted", Margin = new Thickness(0, 8, 0, 8) };
        automatic.Click += async (_, _) => { preferences.AutoArchiveCompleted = automatic.IsChecked == true; controller.Engine.Touch(); await controller.CheckAutomaticArchives(true); };
        panel.Children.Add(automatic);
        var daysRow = new StackPanel { Orientation = Orientation.Horizontal }; daysRow.Children.Add(Ui.Label("自动归档天数", 12));
        var days = Ui.Input(preferences.AutoArchiveAfterDays.ToString()); days.Width = 90; days.Tag = "AutoArchiveDays"; days.Margin = new Thickness(10, 0, 0, 8); daysRow.Children.Add(days); panel.Children.Add(daysRow);
        var error = Ui.Label("", 11, Ui.Brush("#F28F98")); panel.Children.Add(error);
        async void ApplyDays()
        {
            if (!int.TryParse(days.Text, out var value) || value is < 1 or > 36500) { error.Text = "请输入1至36500的整数天数。"; return; }
            error.Text = ""; preferences.AutoArchiveAfterDays = value; controller.Engine.Touch(); await controller.CheckAutomaticArchives(true);
        }
        days.LostFocus += (_, _) => ApplyDays(); days.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyDays(); e.Handled = true; } };
        var purge = new CheckBox { Content = "自动删除较早归档，只保留最近40条", IsChecked = preferences.AutoDeleteArchives, Tag = "AutoDeleteArchives", Margin = new Thickness(0, 6, 0, 8) };
        purge.Click += async (_, _) =>
        {
            var enabled = purge.IsChecked == true;
            if (enabled && controller.Engine.State.Archives.Count > 40 && !await Ui.Confirm("开启后将永久删除较早的" + (controller.Engine.State.Archives.Count - 40) + "条归档，只保留最近40条。", "开启并删除")) { purge.IsChecked = false; return; }
            preferences.AutoDeleteArchives = enabled; controller.Engine.TrimArchiveHistory(); controller.Engine.Touch(); RefreshArchive();
        };
        panel.Children.Add(purge);
        panel.Children.Add(Ui.Label("完成时间从进入已完成列表起计算，修改内容不重置。正在编辑的任务暂不自动归档。默认先显示最近20条；关闭自动删除时保留全部归档。", 12, Ui.Muted));
        if (controller.Engine.State.Archives.Count == 0) panel.Children.Add(Ui.Label("暂无归档任务", 13, Ui.Muted));
        foreach (var archived in controller.Engine.State.Archives.AsEnumerable().Reverse().Take(archiveAllLoaded ? int.MaxValue : 20))
        {
            var entry = new StackPanel(); entry.Children.Add(Ui.Label(archived.ArchivedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + " · " + archived.OriginalColumn.Name, 11, Ui.Muted));
            entry.Children.Add(new TaskCard { ArchivedPreview = true, Controller = controller, DataContext = archived.Item });
            entry.Children.Add(Ui.Button("取消归档", () => { try { controller.UnarchiveTask(archived.Item.Id); } catch (Exception e) { Ui.Error(e.Message); } }));
            panel.Children.Add(entry);
        }
        if (!archiveAllLoaded && total > 20) { var more = Ui.Button("加载更多（显示全部" + total + "条）", () => { archiveAllLoaded = true; RefreshArchive(); }); more.Tag = "LoadAllArchives"; panel.Children.Add(more); }
        return panel;
    }
}
