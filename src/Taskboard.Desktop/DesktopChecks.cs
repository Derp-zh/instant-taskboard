using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal static class DesktopChecks
{
    private static readonly List<string> checks = new();
    private static string? progressPath;
    private static void Assert(bool value, string text) { if (!value) throw new InvalidOperationException("UI验证失败：" + text); checks.Add(text); if (progressPath is not null) File.WriteAllText(progressPath, JsonSerializer.Serialize(checks)); }
    public static async Task Run(BoardController controller)
    {
        controller.SuppressCollapse = true;
        var testDirectory = controller.Store.DirectoryPath;
        progressPath = Path.Combine(testDirectory, "ui-progress.json");
        var output = Path.Combine(testDirectory, "ui-artifacts"); Directory.CreateDirectory(output);
        var initial = controller.Engine.State.Tasks.Count;
        if (initial == 0)
        {
            for (var i = 0; i < 300; i++)
            {
                var session = controller.Engine.NewSession((Lane)(i % 6)); session.Working.Title = "示例任务 " + i;
                for (var stage = 0; stage < 4; stage++)
                {
                    if (stage > 0) controller.Engine.AddStage(session);
                    session.Working.Stages[stage].Text = "在桌面整理需求，记录下一步要做的事。第" + stage + "阶段 / " + i;
                }
                session.Working.CurrentStageId = session.Working.Stages[1].Id;
                session.Working.AiIds.Add(controller.Engine.State.AiOptions[i % 3].Id);
                session.Working.PriorityId = controller.Engine.State.PriorityOptions[i % 3].Id;
                controller.Engine.Submit(session);
            }
            for (var i = 0; i < 10; i++) { var draft = controller.Engine.NewSession(Lane.Refining); draft.Working.Title = "草稿 " + i; draft.Visible = false; controller.Engine.StageSession(draft); }
        }
        var watch = Stopwatch.StartNew(); controller.SetExpanded(true); await Task.Delay(200); await Idle(); watch.Stop();
        var layoutMs = watch.Elapsed.TotalMilliseconds;
        Assert(controller.Columns.Count == 6 && controller.Columns.All(x => x.IsVisible && !x.ShowInTaskbar), "六个独立窗口可见且不进入任务栏");
        foreach (var lane in controller.Columns) { lane.UpdateLayout(); Capture(lane.Root, Path.Combine(output, "lane-" + (int)lane.Lane + ".png")); }
        var target = controller.Engine.State.Tasks[0]; var oldTitle = target.Title;
        var editor = controller.EditTask(target.Id); await Idle();
        Assert(editor.RowDefinitions[0].Height.Value == 72 && editor.Host?.WindowLayer == 1 && controller.Columns.All(x => x.WindowLayer == 0), "编辑移动区72且编辑层级固定高于六列");
        var input = Descendants<TextBox>(editor.Root).First(); var focusSucceeded = input.Focus();
        Native.PostMessage(editor.Host!.ContentHandle, 0x0102, new IntPtr('测'), IntPtr.Zero); await Idle();
        File.WriteAllText(Path.Combine(testDirectory, "keyboard-diagnostic.json"), JsonSerializer.Serialize(new { focusSucceeded, input.IsKeyboardFocused,
            active = editor.Host.IsActive, focusedType = System.Windows.Input.Keyboard.FocusedElement?.GetType().Name,
            win32Focus = Native.GetFocus().ToString(), child = editor.Host.ContentHandle.ToString(), parent = new WindowInteropHelper(editor.Host).Handle.ToString(),
            input.Text, actualSource = (PresentationSource.FromVisual(input) as HwndSource)?.Handle.ToString() }));
        Assert(input.Text.Contains('测'), "透明内容表面接收原生字符输入，焦点可落到真实输入框");
        Assert(input.InputHitTest(new Point(2, input.ActualHeight / 2)) is not null && input.InputHitTest(new Point(input.ActualWidth - 2, input.ActualHeight / 2)) is not null,
            "输入框左右圆弧内部都可命中");
        Assert(input.InputHitTest(new Point(.1, .1)) is null && input.InputHitTest(new Point(input.ActualWidth - .1, .1)) is null,
            "输入框圆弧之外的矩形角不触发输入");
        var aiSelect = Descendants<Button>(editor.Root).First(x => x.Content is TextBlock label && label.Text.StartsWith("codex"));
        Invoke(aiSelect); await Idle();
        Assert(aiSelect.ContextMenu?.IsOpen == true, "点击AI选择框打开选择菜单");
        Invoke(aiSelect); await Idle();
        Assert(aiSelect.ContextMenu?.IsOpen == false, "再次点击AI选择框关闭菜单");
        input.Text = "隐藏后恢复的输入"; await Idle();
        Assert(controller.Engine.State.Tasks[0].Title == oldTitle, "实际输入不会立即修改正式任务");
        controller.SetExpanded(false); await Task.Delay(140); controller.SetExpanded(true); await Task.Delay(140);
        Assert(editor.IsVisible && input.Text == "隐藏后恢复的输入" && controller.Engine.State.Tasks[0].Title == oldTitle, "收起展开保留编辑窗口与未提交内容");
        Capture(editor.Root, Path.Combine(output, "editor.png"));
        var close = Descendants<Button>(editor.Root).First(x => Equals(x.Content, "×")); Invoke(close); await Until(() => !controller.Editors.Contains(editor));
        Assert(controller.Engine.State.Tasks[0].Title == oldTitle && !controller.Engine.State.Sessions.Any(x => x.TaskId == target.Id), "点击真实X放弃修改并关闭编辑");
        editor = controller.EditTask(target.Id); await Idle(); Descendants<TextBox>(editor.Root).First().Text = "UI实际保存";
        var save = Descendants<Button>(editor.Root).First(x => Equals(x.Content, "保存任务")); Invoke(save); await Until(() => !controller.Editors.Contains(editor));
        Assert(controller.Engine.State.Tasks[0].Title == "UI实际保存", "点击真实保存按钮提交任务并关闭编辑");
        controller.Undo(); Assert(controller.Engine.State.Tasks[0].Title == oldTitle, "UI提交可撤销字段修改");
        var draftEditor = controller.NewTask(Lane.Running); await Idle(); Descendants<TextBox>(draftEditor.Root).First().Text = "UI持久草稿";
        Invoke(Descendants<Button>(draftEditor.Root).First(x => Equals(x.Content, "×"))); await Until(() => !controller.Editors.Contains(draftEditor)); await Idle();
        Assert(controller.Engine.State.Sessions.Any(x => x.TaskId is null && x.Working.Title == "UI持久草稿") && !controller.Engine.Search("UI持久草稿").Any(), "真实新建X暂存且不参与任务搜索");
        Assert(!Application.Current.Windows.OfType<DraftStackSurface>().Any(x => x.IsVisible), "全部新建窗口关闭后没有宿主或草稿列表残留");
        var single = controller.NewTask(Lane.Running); await Idle();
        Capture(single.Root, Path.Combine(output, "single-stage.png"));
        Assert(!Descendants<TextBox>(single.Root).Any(x => Equals(x.ToolTip, "阶段名称")), "仅阶段0时阶段名胶囊隐藏且不可编辑");
        Invoke(Descendants<Button>(single.Root).First(x => x.Content?.ToString()?.Contains("新阶段") == true)); await Idle();
        var names = Descendants<TextBox>(single.Root).Where(x => Equals(x.ToolTip, "阶段名称")).ToArray();
        Assert(names.Length == 2 && names[0].Text == "阶段0", "新增阶段后阶段0和阶段1名称胶囊均出现");
        names[1].Text = "独立修改阶段";
        Assert(single.Session.Working.Stages[1].Name == "独立修改阶段", "框内阶段胶囊仍可独立编辑");
        Capture(single.Root, Path.Combine(output, "unified-stage-input.png"));
        var reminder = Descendants<CheckBox>(single.Root).Single(x => Equals(x.Tag, "ReminderToggle")); reminder.IsChecked = true;
        Descendants<ComboBox>(single.Root).First(x => x.Items.Count == 3 && Equals(x.Items[0], "一次")).SelectedIndex = 0;
        var date = Descendants<DatePicker>(single.Root).Single(); date.IsDropDownOpen = true; await Idle();
        var datePopup = (System.Windows.Controls.Primitives.Popup)date.Template.FindName("PART_Popup", date);
        if (datePopup.Child is FrameworkElement calendar) Capture(calendar, Path.Combine(output, "calendar.png"));
        date.SelectedDate = DateTime.Today.AddDays(2); date.IsDropDownOpen = false;
        Assert(single.Session.Working.Reminder.Enabled && single.Session.Working.Reminder.NextLocal.Date == DateTime.Today.AddDays(2), "圆角日期选择器仍可修改一次性提醒日期");
        Descendants<TextBox>(single.Root).First().Text = "堆叠前一份";
        var second = controller.NewTask(Lane.Running); await Idle(); Descendants<TextBox>(second.Root).First().Text = "堆叠第二份";
        var third = controller.NewTask(Lane.Running); await Idle(); Descendants<TextBox>(third.Root).First().Text = "堆叠第三份";
        var bounds = controller.LastDraftBounds; var area = controller.WorkArea(controller.Ball);
        Assert(bounds.Length >= 3 && Math.Abs(bounds[1].Left - bounds[0].Left - bounds[0].Width / 5) < .01, "旧新建窗口右移五分之一宽度");
        Assert(Math.Abs(bounds[0].Left + bounds[0].Width / 2 - area.Left - area.Width / 2) < .01 && Math.Abs(bounds[0].Top + bounds[0].Height / 2 - area.Top - area.Height / 2) < .01, "新建前台窗口在工作区中央");
        Assert(Application.Current.Windows.OfType<DraftStackSurface>().Single().IsVisible && Application.Current.Windows.OfType<DraftStackSurface>().Count() == 1, "多份草稿只有一个真实宿主，虚线入口为窗口内部控件");
        Assert(ReferenceEquals(single.Host, second.Host) && ReferenceEquals(second.Host, third.Host), "三份草稿和切换区域共用同一个真实窗口");
        Capture(single.Host!.Root, Path.Combine(output, "single-window-stack.png"));
        single.Activate(); await Idle();
        Assert(single.IsActive && controller.Editors.Count == 3, "旧窗口可以切换到前台且不重复创建");
        foreach (var draft in new[] { single, second, third }) { Invoke(Descendants<Button>(draft.Root).First(x => Equals(x.Content, "×"))); await Until(() => !controller.Editors.Contains(draft)); }
        await Idle();
        Assert(!Application.Current.Windows.OfType<DraftStackSurface>().Single().IsVisible, "关闭多份新建后宿主和虚线入口完全消失");
        Assert(Application.Current.Windows.OfType<DraftStackSurface>().Single().DraftCount == 0, "全部草稿关闭后释放堆叠绘制和实时背景区域");
        var sharedDraftCount = controller.Engine.State.Sessions.Count(x => x.TaskId is null && !x.Inline && x.Working.HasContent);
        var resumed = controller.OpenNewEntry(Lane.Running); await Idle();
        Assert(controller.Editors.Count(x => x.Session.TaskId is null) == sharedDraftCount && resumed.Session.Working.HasContent, "列末新建入口恢复所有列表的非空共享暂存草稿");
        foreach (var draft in controller.Editors.ToArray()) { Invoke(Descendants<Button>(draft.Root).First(x => Equals(x.Content, "×"))); await Until(() => !controller.Editors.Contains(draft)); }
        await Idle();
        for (var i = 0; i < 20; i++) { controller.Toggle(); await Task.Delay(12); }
        controller.SetExpanded(false); await Task.Delay(160);
        File.WriteAllText(Path.Combine(testDirectory,"rapid-toggle-state.json"),JsonSerializer.Serialize(new { controller.Expanded, columns=controller.Columns.Select(x=>new{x.Title,x.IsVisible,rootVisible=x.Root.IsVisible}),editors=controller.Editors.Select(x=>new{x.IsVisible,host=x.Host?.IsVisible,x.Session.Visible}) }));
        Assert(controller.Columns.All(x => !x.IsVisible) && controller.Editors.All(x => !x.IsVisible), "快速切换20次后无列表或编辑残留");
        controller.SetExpanded(true); await Task.Delay(150);
        Assert(controller.Columns.All(x => x.IsVisible && x.IsHitTestVisible), "快速切换后恢复输入命中");
        var position = controller.Columns[2].Position(); var neighbor = controller.Columns[3].Position();
        controller.Columns[2].Width += 15; controller.SavePosition("lane-2", controller.Columns[2]);
        Assert(controller.Columns[3].Width == neighbor.Width, "调整单列不会挤压邻列");
        controller.Columns[2].Place(position);
        var originalColumns = controller.Columns.Select(x => x.Position()).ToArray();
        controller.BeginWorkspaceResize(); var cursor = System.Windows.Forms.Cursor.Position;
        var resizePoint = new System.Drawing.Point(cursor.X + 60, cursor.Y - 25); controller.ResizeWorkspaceAtPointer(resizePoint);
        var resizedColumns = controller.Columns.Select(x => x.Position()).ToArray(); controller.ResizeWorkspaceAtPointer(resizePoint);
        Assert(controller.Columns.Select((x, i) => Math.Abs(x.Width - resizedColumns[i].Width) < .01 && Math.Abs(x.Height - resizedColumns[i].Height) < .01).All(x => x), "鼠标位置不变时整组缩放不会因图案移动再次偏移");
        var ratios = resizedColumns.Select((x, i) => x.Width / originalColumns[i].Width).ToArray();
        Assert(ratios.Max() - ratios.Min() < .005 && resizedColumns.Select((x, i) => Math.Abs(x.Height / originalColumns[i].Height - ratios[i]) < .005).All(x => x), "整组缩放的六列宽高比例相同（允许设备像素取整）");
        controller.EndWorkspaceResize(); for (var i = 0; i < controller.Columns.Count; i++) controller.Columns[i].Place(originalColumns[i]);
        watch.Restart(); var matches = controller.Engine.Search("记录下一步").ToArray(); watch.Stop();
        Assert(matches.Length == 300 && watch.Elapsed.TotalMilliseconds < 200, "300任务搜索全部阶段低于200ms");
        var searchInput = Descendants<TextBox>(controller.Ball.Root).Single(); searchInput.Text = "记录下一步";
        await Task.Delay(150); await Idle();
        searchInput.ApplyTemplate(); searchInput.UpdateLayout();
        var searchHost = (ScrollViewer)searchInput.Template.FindName("PART_ContentHost", searchInput);
        File.WriteAllText(Path.Combine(testDirectory, "search-layout.json"), JsonSerializer.Serialize(new {
            searchInput.ActualWidth, searchInput.ActualHeight, padding = searchInput.Padding.ToString(), hostWidth = searchHost.ActualWidth,
            hostHeight = searchHost.ActualHeight, searchHost.ViewportHeight, searchHost.ExtentHeight,
            children = Descendants<FrameworkElement>(searchHost).Select(x => new { type = x.GetType().Name, x.ActualWidth, x.ActualHeight, visibility = x.Visibility.ToString() })
        }, new JsonSerializerOptions { WriteIndented = true }));
        Capture(searchInput, Path.Combine(output, "search-input.png"));
        Assert(searchHost.ViewportHeight >= searchInput.FontSize && searchInput.GetRectFromCharacterIndex(0).Height > 0,
            "搜索文字区域高度足够且光标具有可绘制区域");
        var searchBitmap = new RenderTargetBitmap((int)searchInput.ActualWidth, (int)searchInput.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        searchBitmap.Render(searchInput); var pixels = new byte[searchBitmap.PixelWidth * searchBitmap.PixelHeight * 4]; searchBitmap.CopyPixels(pixels, searchBitmap.PixelWidth * 4, 0);
        var visibleGlyphs = 0;
        for (var y = 5; y < searchBitmap.PixelHeight - 5; y++) for (var x = 24; x < searchBitmap.PixelWidth - 24; x++)
        { var p = (y * searchBitmap.PixelWidth + x) * 4; if (pixels[p] > 210 && pixels[p + 1] > 210 && pixels[p + 2] > 210) visibleGlyphs++; }
        Assert(visibleGlyphs > 20, "搜索框实际渲染可见文字像素，不能只验证输入属性");
        searchInput.Focus(); searchInput.CaretIndex = searchInput.Text.Length;
        Native.PostMessage(controller.Ball.ContentHandle, 0x0102, new IntPtr('测'), IntPtr.Zero); await Idle();
        Assert(searchInput.Text == "记录下一步测" && searchInput.IsKeyboardFocused, "搜索框接收原生中文输入且保留真实键盘焦点");
        searchInput.Text = "记录下一步"; await Task.Delay(150); await Idle();
        Assert(Descendants<System.Windows.Controls.Primitives.ScrollBar>(searchInput).All(x => x.Visibility != Visibility.Visible), "搜索框不会显示青色空滑块，长文本仍可用光标滚动");
        Assert(Descendants<Button>(controller.Ball.Root).All(x => x.Cursor == System.Windows.Input.Cursors.Arrow), "搜索组件按钮悬停使用普通箭头");
        var resultsWindow = Application.Current.Windows.OfType<SurfaceWindow>().First(x => x.GetType() == typeof(SurfaceWindow) && Descendants<ListBox>(x.Root).Any());
        var resultsList = Descendants<ListBox>(resultsWindow.Root).Single();
        Assert(resultsWindow.IsVisible && resultsList.Items.Count == 300, "真实搜索控件展示300项匹配结果");
        resultsList.SelectedIndex = 0; await Idle();
        Assert(!resultsWindow.IsVisible && searchInput.Text == "记录下一步", "选中搜索结果隐藏结果并保留搜索词");
        Invoke(Descendants<Button>(controller.Ball.Root).First(x => Equals(x.Content, "×"))); await Task.Delay(120);
        Assert(searchInput.Text.Length == 0 && !resultsWindow.IsVisible, "搜索X清空输入和结果");
        var dragSource = controller.Engine.State.Tasks.First(x => x.Lane == Lane.Refining);
        var sourceCard = controller.Columns[(int)Lane.Refining].Locate(dragSource.Id)!; await Idle();
        Assert(Math.Abs(sourceCard.Padding.Top - 6.6) < .01 && Math.Abs(sourceCard.Margin.Bottom - 8) < .01 &&
            Descendants<TextBlock>(sourceCard).All(x => Math.Abs(x.Margin.Top - 2.4) < .01), "卡片内部空白缩到3/5，卡片之间间距保持8");
        var originalOrder = controller.Engine.State.Tasks.Select(x => (x.Id, x.Lane)).ToArray();
        var destination = controller.Columns[(int)Lane.Action];
        var firstDestination = controller.Engine.State.Tasks.First(x => x.Lane == Lane.Action).Id;
        using (var drag = new CardDragSession(controller, sourceCard, dragSource, new Point(8, 8)))
        {
            controller.CardDrag = drag; drag.Preview(destination, firstDestination); await Idle();
            Capture(destination.Root, Path.Combine(output, "drag-preview.png"));
            Assert(Descendants<System.Windows.Shapes.Rectangle>(destination.Root).Any(x => x.StrokeDashArray.Count == 2 && x.Stroke == Ui.Accent), "拖动目标有虚线边框卡片占位");
            var updates = drag.PreviewUpdates; for (var repeat = 0; repeat < 100; repeat++) drag.Preview(destination, firstDestination);
            Assert(drag.PreviewUpdates == updates, "相同落点100次移动不重建或重排卡片预览");
            Assert(controller.Engine.State.Tasks.Select(x => (x.Id, x.Lane)).SequenceEqual(originalOrder), "拖动预览不修改任务顺序及状态");
            drag.Preview(destination, null); await Idle();
            Assert(drag.PreviewUpdates == updates + 1, "落点改变时才更新插入预览");
            File.WriteAllText(Path.Combine(testDirectory, "drag-performance.json"), JsonSerializer.Serialize(new { drag.SnapshotMs, drag.MaxMoveMs, drag.MaxPreviewMs, drag.PositionUpdates, drag.PreviewUpdates }));
        }
        controller.CardDrag = null; await Idle();
        Assert(controller.Columns[(int)Lane.Refining].Locate(dragSource.Id) is not null && controller.Engine.State.Tasks.Select(x => (x.Id, x.Lane)).SequenceEqual(originalOrder), "取消拖动恢复源卡片且不产生任务修改");
        Assert(controller.Columns.All(x => !Descendants<System.Windows.Shapes.Rectangle>(x.Root).Any(r => r.StrokeDashArray.Count == 2 && r.Stroke == Ui.Accent)), "结束拖动后所有列无落点预览残留");
        controller.Settings(); await Idle();
        var settings = Application.Current.Windows.OfType<SettingsSurface>().First(); Capture(settings.Root, Path.Combine(output, "settings.png"));
        Assert(SettingsSurface.Sections.Length == 7 && SettingsSurface.Sections.Contains("监听") && SettingsSurface.Sections.Contains("归档") && Descendants<Button>(settings.Root).All(x => !Equals(x.Content, "AI 选项") && !Equals(x.Content, "优先级选项")), "设置有七分区含监听和归档且不再管理字段选项");
        settings.SelectSection("窗口"); await Idle(); var transparency = Descendants<Slider>(settings.Root).Single(x => Equals(x.ToolTip, "多列表窗口透明度")); transparency.Value = 60;
        Assert(controller.Engine.State.Preferences.GlassTransparency == 60 && SurfaceWindow.Transparency == 60, "设置滑块实时将透明度更新为60%");
        var ballSlider = Descendants<Slider>(settings.Root).Single(x => Equals(x.ToolTip, "悬浮球透明度")); ballSlider.Value = 65;
        Assert(controller.Engine.State.Preferences.BallTransparency == 65 && controller.Ball.GlassTransparency == 65 && SurfaceWindow.Transparency == 60, "球透明度独立调节，不改变普通窗口"); ballSlider.Value = 40;
        transparency.Value = 20; settings.SelectSection("数据与备份"); await Idle(); Capture(settings.Root, Path.Combine(output, "settings-data.png"));
        settings.SelectSection("AI平台接入情况"); await Idle(); Capture(settings.Root, Path.Combine(output, "settings-platforms.png"));
        var scrollbars = Descendants<System.Windows.Controls.Primitives.ScrollBar>(settings.Root).Where(x => x.Orientation == Orientation.Vertical).ToArray();
        Assert(scrollbars.Length > 0 && scrollbars.All(x => x.Width == 6), "设置滚动条固定6单位细宽");
        controller.SetExpanded(false); await WaitWindow(settings, visible: false);
        Assert(!settings.IsVisible, "点击球收起时设置窗口随组隐藏");
        controller.SetExpanded(true); await WaitWindow(settings, visible: true);
        Assert(settings.IsVisible, "重新展开恢复原来的设置窗口");
        settings.Hide(); await WaitWindow(settings, visible: false);
        controller.SetExpanded(false); await Task.Delay(140); controller.SetExpanded(true); await Task.Delay(140);
        Assert(!settings.IsVisible, "主动关闭的设置窗口不会在重新展开时复活");
        for (var i = 0; i < 100; i++) { var e = controller.EditTask(target.Id); e.Session.Working.Title = "循环" + i; controller.Engine.Discard(e.Session); controller.RemoveEditor(e); e.Destroy(); }
        Assert(controller.Editors.Count == 0, "编辑开关100次无窗口列表增长");
        var inspiration = controller.Columns[0];
        inspiration.Activate(); await Idle();
        foreach (var column in controller.Columns) column.RevealNewEntry();
        Assert(controller.Columns.All(x => x.NewEntryIsLast), "六列新建入口与正式任务处于同一滚动列表末尾");
        Invoke(Descendants<Button>(inspiration.Root).First(x => Equals(x.Content, "＋ 新建任务"))); await Idle();
        Assert(controller.Editors.Count == 0, "灵感新建在列内录入且不打开编辑窗口");
        Assert(Descendants<Button>(inspiration.Root).Any(x => Equals(x.Content, "✓") && x.Width == 20 && x.Height == 20), "灵感结束录入按钮为详情内圆形✓");
        var inlineInputs = Descendants<TextBox>(inspiration.Root).ToArray(); inlineInputs[1].Text = "UI灵感直接输入";
        await Task.Delay(750);
        Assert(controller.Engine.State.Tasks.Count == 301 && controller.Engine.Search("UI灵感直接输入").Single().Lane == Lane.Inspiration, "灵感非空自动建立正式任务");
        var inspirationId = controller.Engine.Search("UI灵感直接输入").Single().Id;
        var currentInlineInputs = Descendants<TextBox>(inspiration.Root).ToArray();
        File.WriteAllText(Path.Combine(testDirectory, "inline-focus-report.json"), JsonSerializer.Serialize(new { inputsAfterSave = currentInlineInputs.Length, inspiration.IsActive, controller.Expanded,
            focusedType = System.Windows.Input.Keyboard.FocusedElement?.GetType().Name, foreground = Native.GetForegroundWindow().ToString() }));
        Assert(currentInlineInputs.Length >= 2, "灵感自动保存后输入区保持打开，插入卡片不会误触发离开编辑");
        currentInlineInputs[1].Text = "UI灵感最后一个字符";
        controller.SetExpanded(false); await Task.Delay(150);
        Assert(controller.Engine.State.Tasks.First(x => x.Id == inspirationId).Stages[0].Text == "UI灵感最后一个字符", "收起前立即提交灵感最后输入");
        controller.Engine.Delete(inspirationId);
        await controller.Flush(); var disk = controller.Store.Load();
        Assert(disk.Tasks.Count == 300 && disk.Sessions.Any(x => x.Working.Title == "UI持久草稿"), "实际UI数据落盘且恢复有效");
        var configuration = ConfigurationBackup.Capture(controller.Engine.State, controller.Store.DirectoryPath);
        var tasksBefore = JsonSerializer.Serialize(controller.Engine.State.Tasks); var sessionsBefore = JsonSerializer.Serialize(controller.Engine.State.Sessions);
        configuration.Preferences.GlassTransparency = 45; await controller.ImportConfiguration(configuration);
        Assert(JsonSerializer.Serialize(controller.Engine.State.Tasks) == tasksBefore && JsonSerializer.Serialize(controller.Engine.State.Sessions) == sessionsBefore && SurfaceWindow.Transparency == 45, "真实配置恢复仅更新设置，不修改任务和草稿");
        controller.SetGlassTransparency(20); await controller.Flush();
        var inspirationDefinition = controller.Engine.State.Preferences.Columns.First(x=>x.BuiltinLane==Lane.Inspiration);
        inspirationDefinition.Visible=false; controller.ColumnsChanged();
        Assert(controller.VisibleColumns.Length==5 && !controller.Columns[0].IsVisible,"隐藏灵感后显示数量为5且窗口关闭");
        inspirationDefinition.Visible=true; var actionDefinition=controller.Engine.State.Preferences.Columns.First(x=>x.BuiltinLane==Lane.Action); actionDefinition.Name="等我处理"; controller.ColumnsChanged();
        Assert(controller.Columns.First(x=>x.Definition==actionDefinition).Title=="等我处理","改名同步独立窗口并保留待操作身份");
        var addedDefinition=new BoardColumn { Name="自定义窗口" }; controller.Engine.State.Preferences.Columns.Add(addedDefinition); controller.ColumnsChanged();
        Assert(controller.VisibleColumns.Length==7 && controller.Columns.Last().Title=="自定义窗口","新增列表立即生成独立窗口");
        var customDraft=controller.NewTask(Lane.Refining,addedDefinition.Id); await Idle(); Descendants<TextBox>(customDraft.Root).First().Text="自定义列表实际输入"; await Idle();
        Invoke(Descendants<Button>(customDraft.Root).First(x=>Equals(x.Content,"保存任务"))); await Until(()=>!controller.Editors.Contains(customDraft));
        var customItem=controller.Engine.State.Tasks.Single(x=>x.Title=="自定义列表实际输入");
        Assert(controller.Columns.Last().Locate(customItem.Id) is not null && customItem.ColumnId==addedDefinition.Id,"真实新建保存进入自定义列");
        controller.MoveTask(customItem.Id,Lane.Action,null); Assert(customItem.ColumnId is null && customItem.Lane==Lane.Action,"自定义列可移动到改名后的待操作");
        controller.Undo(); Assert(controller.Engine.State.Tasks.Single(x=>x.Id==customItem.Id).ColumnId==addedDefinition.Id,"自定义列移列可撤销");
        controller.DeleteTask(customItem.Id); BoardColumns.Delete(controller.Engine.State,addedDefinition); controller.ColumnsChanged();
        Assert(controller.VisibleColumns.Length==6 && !controller.Columns.Any(x=>x.Definition.Id==addedDefinition.Id),"空自定义列删除后移除窗口");
        controller.Undo(); Assert(controller.VisibleColumns.Length==7 && controller.Columns.Any(x=>x.Definition.Id==addedDefinition.Id),"任务删除撤销可恢复原自定义窗口");
        controller.DeleteTask(customItem.Id); BoardColumns.Delete(controller.Engine.State,addedDefinition); actionDefinition.Name="待操作"; controller.ColumnsChanged();
        BoardColumns.SetVisibleCount(controller.Engine.State.Preferences,0); controller.ColumnsChanged();
        Assert(controller.VisibleColumns.Length==0 && controller.MaximumRange.IsEmpty,"全部隐藏的最大范围为空且不崩溃");
        BoardColumns.SetVisibleCount(controller.Engine.State.Preferences,6); controller.ColumnsChanged();
        Assert(HoverCorridor.Contains(new Rect(140,210,60,60),new Rect(0,0,200,200),new Point(170,205)) && HoverCorridor.Contains(new Rect(140,210,60,60),new Rect(0,0,200,200),new Point(50,70)) && !HoverCorridor.Contains(new Rect(140,210,60,60),new Rect(0,0,200,200),new Point(50,205)),"悬浮范围包含小窗口和上下通道，侧边范围外收起");
        var previousStore = controller.Store; var newDirectory = Path.Combine(testDirectory, "moved-data"); await controller.ChangeDataDirectory(newDirectory);
        Assert(controller.Store.DirectoryPath == newDirectory && controller.Engine.State.Tasks.Count == 300 && File.Exists(previousStore.MainPath), "切换到空数据路径复制数据且保留原目录");
        Assert(DataPaths.ReadSelection(Path.Combine(testDirectory, "launch-settings.json")) == newDirectory, "新数据路径入口已持久保存，可供重启读取");
        await controller.ChangeDataDirectory(testDirectory);
        Assert(controller.Store.DirectoryPath == testDirectory && controller.Engine.State.Tasks.Count == 300, "可以安全切回原数据路径，数据不混合");
        controller.SetExpanded(false); await controller.Flush();
        await PerformanceChecks.Run(controller);
        controller.Ball.Hide();
        File.WriteAllText(Path.Combine(testDirectory, "ui-phase.json"), JsonSerializer.Serialize(new { foregroundInteractionComplete = true, checks = checks.Count }));
        var process = Process.GetCurrentProcess(); var cpuStart = process.TotalProcessorTime; var idle = Stopwatch.StartNew();
        await Task.Delay(TimeSpan.FromSeconds(60)); idle.Stop();
        var cpuCorePercent = (process.TotalProcessorTime - cpuStart).TotalMilliseconds / idle.Elapsed.TotalMilliseconds * 100;
        var cpuPercent = cpuCorePercent / Environment.ProcessorCount;
        Assert(cpuPercent < 1, "空闲一分钟平均CPU低于1%");
        var result = new { passed = checks.Count, checks, initialLayoutMs = layoutMs, idleSeconds = idle.Elapsed.TotalSeconds, cpuPercent, cpuCorePercent, mouseMoved = false, runtime = Environment.Version.ToString(), artifactDirectory = output };
        File.WriteAllText(Path.Combine(testDirectory, "ui-report.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        await controller.Exit();
    }
    private static Task WaitWindow(SurfaceWindow window, bool visible) => Until(() => window.IsVisible == visible && !window.IsScrollAnimating && !window.IsContentSwapHandoff);
    private static async Task Idle()
    {
        await Until(() => !Application.Current.Windows.OfType<SurfaceWindow>().Any(x => x.IsScrollAnimating || x.IsContentSwapping));
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }
    private static async Task Until(Func<bool> condition, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(condition))] string expression = "")
    { var timer = Stopwatch.StartNew(); while (!condition()) { if (timer.Elapsed.TotalSeconds > 10) throw new TimeoutException("UI操作超时：" + expression + "; 动画状态：" + string.Join(",", Application.Current.Windows.OfType<SurfaceWindow>().Where(x => x.IsScrollAnimating || x.IsContentSwapping).Select(x => $"{x.Title}:scroll={x.IsScrollAnimating},swap={x.IsContentSwapping},visible={x.IsVisible}"))); await Task.Delay(25); } }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T result) yield return result;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var item in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return item;
    }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static void Capture(FrameworkElement root, string path)
    {
        root.UpdateLayout(); var bitmap = new RenderTargetBitmap(Math.Max(1, (int)root.ActualWidth), Math.Max(1, (int)root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual(); using (var context = drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(root), null, new Rect(root.RenderSize));
        bitmap.Render(drawing); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); png.Save(file);
    }
}
