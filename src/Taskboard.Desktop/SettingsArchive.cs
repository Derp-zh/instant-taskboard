using System;
using System.Linq;
using System.Windows.Controls;

namespace Taskboard.Desktop;

internal sealed partial class SettingsSurface
{
    internal void RefreshArchive() { if (CurrentSection == "归档") RefreshSection(); }
    private StackPanel ArchiveSettings()
    {
        var panel = new StackPanel(); panel.Children.Add(Ui.Label("归档 · " + controller.Engine.State.Archives.Count + "/20", 18));
        panel.Children.Add(Ui.Label("保留最近20条。新归档会永久替换最早一条；删除任务不产生归档。取消归档恢复原列表与全部阶段。", 12, Ui.Muted));
        if (controller.Engine.State.Archives.Count == 0) panel.Children.Add(Ui.Label("暂无归档任务", 13, Ui.Muted));
        foreach (var archived in controller.Engine.State.Archives.AsEnumerable().Reverse())
        {
            var entry = new StackPanel(); entry.Children.Add(Ui.Label(archived.ArchivedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + " · " + archived.OriginalColumn.Name, 11, Ui.Muted));
            entry.Children.Add(new TaskCard { ArchivedPreview = true, Controller = controller, DataContext = archived.Item });
            entry.Children.Add(Ui.Button("取消归档", () => { try { controller.UnarchiveTask(archived.Item.Id); } catch (Exception e) { Ui.Error(e.Message); } }));
            panel.Children.Add(entry);
        }
        return panel;
    }
}
