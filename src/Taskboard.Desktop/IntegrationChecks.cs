using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal static class IntegrationChecks
{
    internal static async Task Run(BoardController controller)
    {
        var root = controller.Store.DirectoryPath; var checks = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
        var legacy = Path.Combine(root, "legacy-install"); var oldStore = new AtomicStore(Path.Combine(legacy, "data"));
        var oldEngine = new BoardEngine(new BoardState()); var task = oldEngine.NewSession(Lane.Done); task.Working.Title = "迁移隔离样本"; task.Working.Stages[0].Text = "保留全部内容"; oldEngine.Submit(task); await oldStore.SaveAsync(Copies.Of(oldEngine.State));
        var sourceHash = SHA256.HashData(File.ReadAllBytes(oldStore.MainPath));
        var system = Path.Combine(root, "fake-system-profile"); var migrated = DataPaths.Initialize(legacy, system);
        Check(migrated == Path.Combine(system, "data") && AtomicStore.Read(Path.Combine(migrated, "board.json")).Tasks.Single().Title == "迁移隔离样本", "旧便携数据安全复制至系统用户数据路径");
        Check(sourceHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(oldStore.MainPath))), "迁移保留原目录且原数据字节不变");
        Check(DataPaths.Initialize(Path.Combine(root, "updated-install"), system) == migrated, "更换程序目录后仍找到相同系统数据路径");
        var custom = Path.Combine(root, "custom-selection-install"); Directory.CreateDirectory(custom); DataPaths.Save(Path.Combine(custom, "launch-settings.json"), oldStore.DirectoryPath);
        Check(DataPaths.Initialize(custom, Path.Combine(root, "fake-profile-custom")) == oldStore.DirectoryPath, "已选择的旧数据路径保持原样");
        var existing = Path.Combine(root, "fake-profile-existing"); var existingStore = new AtomicStore(Path.Combine(existing, "data")); var existingState = new BoardState(); var existingEngine = new BoardEngine(existingState); var already = existingEngine.NewSession(Lane.Running); already.Working.Title = "系统原任务"; existingEngine.Submit(already); await existingStore.SaveAsync(existingState);
        DataPaths.Initialize(legacy, existing); Check(existingStore.Load().Tasks.Single().Title == "系统原任务", "系统目录已有数据时不覆盖或擅自混合旧便携数据");
        var itemSession = controller.Engine.NewSession(Lane.Done); itemSession.Working.Title = "界面归档样本"; controller.Engine.AddStage(itemSession); itemSession.Working.Stages[1].Text = "阶段详情"; var item = controller.Engine.Submit(itemSession);
        var card = new TaskCard { Controller = controller, DataContext = item }; Layout(card, 300, 200);
        Check(card.ContextMenu!.Items.OfType<MenuItem>().Any(x => Equals(x.Header, "归档")), "已完成卡片右键具有归档入口");
        controller.ArchiveTask(item.Id); Check(controller.Engine.State.Archives.Count == 1 && controller.Engine.State.Tasks.Count == 0, "界面归档移出正式列表并形成历史");
        var settings = new SettingsSurface(controller); settings.SelectSection("归档"); Layout(settings.Root, 760, 680);
        Check(SettingsSurface.Sections.Contains("归档") && Descendants<TaskCard>(settings.Root).Single().ArchivedPreview, "设置归档侧栏显示历史任务卡片");
        var historicalCard = Descendants<TaskCard>(settings.Root).Single();
        Check(historicalCard.ContextMenu!.Items.OfType<MenuItem>().Any(x => Equals(x.Header, "取消归档")) && !historicalCard.ContextMenu.Items.OfType<MenuItem>().Any(x => x.Header?.ToString()?.StartsWith("删除") == true), "历史卡片菜单取消归档且不混入普通删除");
        Descendants<Button>(settings.Root).Single(x => Equals(x.Content, "取消归档")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(controller.Engine.State.Archives.Count == 0 && controller.Engine.State.Tasks.Single().Stages.Count == 2, "设置取消归档恢复全部阶段");
        settings.SelectSection("数据与备份"); Layout(settings.Root, 760, 680);
        Check(Descendants<Button>(settings.Root).Any(x => Equals(x.Content, "切换配置路径")) && Descendants<Button>(settings.Root).Any(x => Equals(x.Content, "切换数据路径")), "任务和配置路径均有可编辑入口");
        var configuration = Path.Combine(root, "alternate-config"); await controller.ChangeConfigurationDirectory(configuration);
        Check(controller.Store.ConfigurationDirectory == configuration && File.Exists(Path.Combine(configuration, "settings.json")) && controller.Engine.State.Tasks.Single().Id == item.Id, "切换配置目录保存设置且保留当前任务身份");
        var data = Path.Combine(root, "alternate-data"); await controller.ChangeDataDirectory(data);
        Check(controller.Store.DirectoryPath == data && controller.Store.ConfigurationDirectory == configuration && controller.Store.Load().Tasks.Single().Id == item.Id, "任务目录切换后独立配置路径保持且重读任务完整");
        Check(DataPaths.ReadSelection(Path.Combine(root, "launch-settings.json")) == data, "独立数据入口持久保存");
        controller.Engine.Move(item.Id, Lane.Paused, null); controller.Undo(); controller.Redo();
        Check(controller.Engine.State.Tasks.Single().Lane == Lane.Paused, "控制器恢复操作重放撤销的移动");
        var before = controller.VisibleColumns.Select(x => x.Position()).ToArray(); controller.BeginWorkspaceResize(); controller.ResizeWorkspace(20, 10);
        var range = controller.MaximumRange; var area = controller.WorkArea(controller.Ball);
        File.WriteAllText(Path.Combine(root, "center-diagnostic.json"), JsonSerializer.Serialize(new { range = range.ToString(), area = area.ToString(), columns = controller.VisibleColumns.Select(x => x.Position()), controller.Smoke }, new JsonSerializerOptions { WriteIndented = true }));
        Check(Math.Abs(range.X + range.Width / 2 - (-10000 + area.Width / 2)) < 1 && Math.Abs(range.Y + range.Height / 2 - (-10000 + area.Height / 2)) < 1, "整组等比缩放时最大范围固定在工作区中央");
        var ratios = controller.VisibleColumns.Select((x, i) => x.Width / before[i].Width).ToArray();
        Check(ratios.Max() - ratios.Min() < .01, "居中缩放仍保持所有列表同比例"); controller.EndWorkspaceResize();
        settings.Destroy(false); await controller.Flush();
        File.WriteAllText(Path.Combine(root, "integration-report.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks, desktopShown = false, mouseMoved = false, systemProfile = "injected isolated fixture", realRegistryChanged = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static void Layout(FrameworkElement element, double width, double height)
    { element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout(); }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    { if (root is T found) yield return found; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child; }
}
