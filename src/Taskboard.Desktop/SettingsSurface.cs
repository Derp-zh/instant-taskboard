using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed partial class SettingsSurface : SurfaceWindow
{
    protected override bool ScrollEnabled => true;
    private readonly BoardController controller;
    private readonly Grid content = new();
    private readonly Dictionary<string, ScrollViewer> pages = new();
    private readonly Dictionary<string, Button> navigation = new();
    internal string CurrentSection { get; private set; } = "主设置";
    internal static readonly string[] Sections = ["主设置", "窗口", "监听", "归档", "数据与备份", "AI平台接入情况", "飞书接入情况"];
    public SettingsSurface(BoardController controller) : base(760, 680)
    {
        this.controller = controller; Title = "即时任务看板设置";
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) }); Root.RowDefinitions.Add(new RowDefinition());
        var header = new DockPanel { Margin = new Thickness(18, 8, 18, 8), Background = Brushes.Transparent };
        var close = Ui.Button("×", Hide); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close); header.Children.Add(Ui.Label("设置", 18));
        var saved = Ui.Label("", 11, Ui.Muted); DockPanel.SetDock(saved, Dock.Right); header.Children.Insert(1, saved);
        var save = Ui.Button("保存设置", () => { }, true); save.Tag = "SettingsSave"; DockPanel.SetDock(save, Dock.Right); header.Children.Insert(1, save);
        save.ToolTip = "设置仍自动保存；也可点击立即保存到本机。快捷键继续使用其原有应用按钮。";
        save.Click += async (_, _) =>
        {
            save.IsEnabled = false;
            try { saved.Text = await controller.Flush() ? "上次保存：成功" : "保存失败，请重试"; }
            finally { save.IsEnabled = true; }
        };
        Root.Children.Add(header); MoveWith(header);
        var body = new Grid { Margin = new Thickness(12, 0, 16, 16) }; body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(166) }); body.ColumnDefinitions.Add(new ColumnDefinition());
        var sidebar = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        foreach (var section in Sections)
        { var button = Ui.Button(section, () => SelectSection(section)); button.FontSize = 12; navigation.Add(section, button); sidebar.Children.Add(button); }
        body.Children.Add(sidebar); Grid.SetColumn(content, 1); body.Children.Add(content); Grid.SetRow(body, 1); Root.Children.Add(body); SelectSection(CurrentSection);
        BeforeOpen += () => { if (controller.IsRestoringWorkspace) return; archiveAllLoaded = false; pages.Remove("归档"); if (CurrentSection == "归档") SelectSection("归档"); };
    }
    internal void RefreshSection() { pages.Clear(); SelectSection(CurrentSection); }
    internal void SelectSection(string name)
    {
        if (!navigation.ContainsKey(name)) return;
        if (name == "归档" && CurrentSection != "归档") { archiveAllLoaded = false; pages.Remove("归档"); }
        CurrentSection = name;
        foreach (var (section, button) in navigation) { button.Background = section == name ? Ui.Brush("#36574F") : Ui.Card; button.Foreground = section == name ? Ui.Accent : Ui.Text; }
        if (!pages.TryGetValue(name, out var page))
        {
            var panel = name switch { "主设置" => MainSettings(), "窗口" => WindowSettings(), "监听" => ListeningSettings(), "归档" => ArchiveSettings(), "数据与备份" => DataSettings(), "AI平台接入情况" => PlatformSettings(false), _ => PlatformSettings(true) };
            OrganizeSettingsPage(name, panel);
            page = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(10, 0, 4, 10) }; pages.Add(name, page);
        }
        content.Children.Clear(); content.Children.Add(page);
    }
    private StackPanel MainSettings()
    {
        var panel = new StackPanel(); panel.Children.Add(Ui.Label("主设置", 18));
        var quickEntry = new CheckBox { Content = "灵感库使用快捷新建卡片", Tag = "InspirationQuickEntry", IsChecked = controller.Engine.State.Preferences.InspirationQuickEntry, Margin = new Thickness(0, 8, 0, 12) };
        quickEntry.Click += (_, _) =>
        {
            controller.Engine.State.Preferences.InspirationQuickEntry = quickEntry.IsChecked == true;
            foreach (var column in controller.Columns.Where(x => x.Lane == Lane.Inspiration)) column.RefreshEntryMode();
            controller.Engine.Touch();
        };
        panel.Children.Add(quickEntry);
        var edgeHide = new CheckBox { Content = "悬浮球吸附到桌面边缘并自动隐藏", Tag = "BallEdgeAutoHide", IsChecked = controller.Engine.State.Preferences.BallEdgeAutoHide, Margin = new Thickness(0, 8, 0, 4) };
        edgeHide.Click += (_, _) =>
        {
            controller.Engine.State.Preferences.BallEdgeAutoHide = edgeHide.IsChecked == true;
            controller.Ball.RefreshEdgeAutoHide(); controller.Engine.Touch();
        };
        panel.Children.Add(edgeHide);
        panel.Children.Add(Ui.Label("吸附在屏幕右边缘，只能上下拖动。面板收起、鼠标离开2秒后收起，露出1/4；移上弹出，双向动画均为0.1秒。后台任务照常运行。", 12, Ui.Muted));
        panel.Children.Add(HotkeySettings());
        var startup = new CheckBox { Content = "开机启动", Tag = "SettingsAutoStart", IsChecked = controller.Engine.State.Preferences.AutoStart, Margin = new Thickness(0, 10, 0, 14), IsEnabled = !controller.Isolated };
        startup.Click += (_, _) =>
        {
            try { StartupLinks.SetAutoStart(startup.IsChecked == true); controller.Engine.State.Preferences.AutoStart = startup.IsChecked == true; controller.Engine.Touch(); }
            catch (Exception e) { Ui.Error("设置开机启动失败：" + e.Message); startup.IsChecked = controller.Engine.State.Preferences.AutoStart; }
        };
        panel.Children.Add(startup);
        var shortcut = Ui.Button("创建桌面快捷方式", () => Try(StartupLinks.Shortcut)); shortcut.IsEnabled = !controller.Isolated; panel.Children.Add(shortcut);
        var repair = Ui.Button("修复开机启动路径", () => Try(() => StartupLinks.SetAutoStart(controller.Engine.State.Preferences.AutoStart))); repair.IsEnabled = !controller.Isolated; panel.Children.Add(repair);
        if (!controller.Isolated && StartupLinks.PathChanged()) panel.Children.Add(Ui.Label("文件夹位置已变化，可重新创建快捷方式并修复启动路径。", 12, Ui.Muted));
        if (!controller.Isolated)
        {
            panel.Children.Add(Ui.Label(StartupLinks.AutoStartStatus(), 12, Ui.Muted));
            panel.Children.Add(Ui.Button("Windows启动应用设置", () => Try(() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true }))));
        }
        panel.Children.Add(Ui.Label("即时任务看板 " + typeof(Program).Assembly.GetName().Version!.ToString(3) + " · " + DraftSwitchMotion.VariantLabel + "。", 12, Ui.Muted)); return panel;
    }
    private StackPanel WindowSettings()
    {
        var panel = new StackPanel(); panel.Children.Add(Ui.Label("窗口", 18));
        panel.Children.Add(ListSettings());
        var caption = Ui.Label($"多列表窗口透明度 {controller.Engine.State.Preferences.GlassTransparency:0}%", 14); panel.Children.Add(caption);
        panel.Children.Add(Ui.Label("数值越大越透明；0%为暗色遮罩，100%仍保留背景模糊。默认20%。", 12, Ui.Muted));
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = controller.Engine.State.Preferences.GlassTransparency, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(6, 12, 6, 18), ToolTip = "多列表窗口透明度" };
        slider.ValueChanged += (_, _) => { caption.Text = $"多列表窗口透明度 {slider.Value:0}%"; controller.SetGlassTransparency(slider.Value); }; panel.Children.Add(slider);
        var floatingCaption = Ui.Label($"悬浮窗口透明度 {controller.Engine.State.Preferences.FloatingTransparency:0}%", 14); panel.Children.Add(floatingCaption);
        panel.Children.Add(Ui.Label("新建、编辑任务、设置等悬浮窗口独立调节，默认10%；数值越大越透明，背景模糊始终保留。", 12, Ui.Muted));
        var floatingSlider = new Slider { Minimum = 0, Maximum = 100, Value = controller.Engine.State.Preferences.FloatingTransparency, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(6, 12, 6, 18), ToolTip = "悬浮窗口透明度" };
        floatingSlider.ValueChanged += (_, _) => { floatingCaption.Text = $"悬浮窗口透明度 {floatingSlider.Value:0}%"; controller.SetFloatingTransparency(floatingSlider.Value); }; panel.Children.Add(floatingSlider);
        var ballCaption = Ui.Label($"悬浮球透明度 {controller.Engine.State.Preferences.BallTransparency:0}%", 14); panel.Children.Add(ballCaption);
        var ballSlider = new Slider { Minimum = 0, Maximum = 100, Value = controller.Engine.State.Preferences.BallTransparency, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(6, 12, 6, 18), ToolTip = "悬浮球透明度" };
        ballSlider.ValueChanged += (_, _) => { ballCaption.Text = $"悬浮球透明度 {ballSlider.Value:0}%"; controller.SetBallTransparency(ballSlider.Value); }; panel.Children.Add(ballSlider);
        var resize = new CheckBox { Content = "显示各列右下角独立尺寸入口", Tag = "SettingsResize", IsChecked = controller.Engine.State.Preferences.ResizeCorner, Margin = new Thickness(0, 8, 0, 14) };
        resize.Click += (_, _) => { controller.Engine.State.Preferences.ResizeCorner = resize.IsChecked == true; controller.ApplySettings(); controller.Engine.Touch(); }; panel.Children.Add(resize);
        panel.Children.Add(Ui.Button("恢复窗口默认", controller.RestoreDefaults)); panel.Children.Add(Ui.Label("恢复整组的默认大小和位置。列表右键可只恢复该窗口，并保留当前最大范围的缩放。", 12, Ui.Muted)); return panel;
    }
    private StackPanel ListSettings()
    {
        var panel = new StackPanel(); panel.Children.Add(Ui.Label("多列表独立窗口",15));
        var countRow = new WrapPanel { VerticalAlignment = VerticalAlignment.Center }; var number = Ui.Input(controller.VisibleColumns.Length.ToString()); number.Width = 80; number.ToolTip = "显示的窗口数量（隐藏不计入）";
        var countCaption = Ui.Label("显示窗口数", 12); countCaption.VerticalAlignment = VerticalAlignment.Center; countRow.Children.Add(countCaption);
        number.Margin = new Thickness(8, 0, 8, 0);
        var note = Ui.Label("",11,Ui.Accent);
        void ApplyCount(int count)
        {
            var limit = Math.Max(1,(int)(controller.WorkArea(this).Width*7/8/80));
            if (count<0 || count>limit) { note.Text = $"当前屏幕可并排显示0至{limit}个列表（每列至少80宽）。"; return; }
            BoardColumns.SetVisibleCount(controller.Engine.State.Preferences,count); controller.ColumnsChanged(); RefreshSection();
        }
        var apply = Ui.Button("应用数量",() => { if (int.TryParse(number.Text,out var count)) ApplyCount(count); else note.Text = "请输入非负整数。"; });
        countRow.Children.Add(number); countRow.Children.Add(apply); panel.Children.Add(countRow);
        panel.Children.Add(Ui.Label("隐藏列表不计入数量，任务仍保留。改名不影响提醒及Codex流转。",12,Ui.Muted));
        panel.Children.Add(Ui.Label("六个内置列表均不可删除；可随时隐藏或重新显示。", 11, Ui.Muted));
        foreach (var column in controller.Engine.State.Preferences.Columns.Where(x => !x.Deleted).ToArray())
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) }; var remove = Ui.Button(column.Protected ? "内置" : "删除",() =>
            { try { BoardColumns.Delete(controller.Engine.State,column); controller.ColumnsChanged(); RefreshSection(); } catch (InvalidOperationException e) { note.Text = e.Message; } });
            remove.Tag = "ListDelete"; remove.DataContext = column; remove.MinWidth = 54;
            remove.IsEnabled = !column.Protected; remove.ToolTip = column.Protected ? "此列表可改名或隐藏，不能删除" : "有任务或非空草稿时不能删除";
            DockPanel.SetDock(remove,Dock.Right); row.Children.Add(remove);
            var visible = new CheckBox { IsChecked = column.Visible, Content = "显示", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) };
            visible.Click += (_,_) => { column.Visible = visible.IsChecked == true; controller.ColumnsChanged(); RefreshSection(); };
            DockPanel.SetDock(visible,Dock.Right); row.Children.Add(visible);
            var name = Ui.Input(column.Name); name.Tag = "ListName"; name.DataContext = column; name.ToolTip = column.BuiltinLane is Lane role ? "原列表："+Names.Lanes[(int)role] : "自定义列表";
            name.MinHeight = 30; name.Margin = new Thickness(0, 2, 0, 2); name.Padding = new Thickness(9, 4, 9, 4);
            void CommitName() { if (string.IsNullOrWhiteSpace(name.Text)) { name.Text = column.Name; return; } column.Name = name.Text.Trim(); controller.ColumnsChanged(false); }
            name.LostKeyboardFocus += (_,_) => CommitName();
            name.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { CommitName(); e.Handled = true; } };
            row.Children.Add(name); panel.Children.Add(row);
        }
        panel.Children.Add(Ui.Button("＋ 新增列表",() =>
        { var limit=Math.Max(1,(int)(controller.WorkArea(this).Width*7/8/80)); if(controller.VisibleColumns.Length>=limit) {note.Text="当前屏幕已达到并排显示数量，请先隐藏其他列表。";return;} controller.Engine.State.Preferences.Columns.Add(new BoardColumn()); controller.ColumnsChanged(); RefreshSection(); }));
        panel.Children.Add(note); return panel;
    }
    private StackPanel DataSettings()
    {
        var panel = new StackPanel(); panel.Children.Add(Ui.Label("数据与备份", 18));
        var path = Ui.Label(controller.Store.DirectoryPath, 12, Ui.Muted); panel.Children.Add(path); var status = Ui.Label("", 12, Ui.Accent);
        panel.Children.Add(Ui.Button("切换数据路径", async () =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择本地数据目录", InitialDirectory = controller.Store.DirectoryPath }; if (dialog.ShowDialog(this) != true) return;
            try
            {
                var target = dialog.FolderName; var populated = File.Exists(Path.Combine(target, "board.json")) || File.Exists(Path.Combine(target, "board.json.bak"));
                var message = populated ? "目标目录已有数据，切换后打开那份数据，不自动合并；当前输入会先保存，原目录保留。是否切换？" : "当前任务、草稿和配置会复制到新目录，原目录保留。是否切换？";
                if (!await Ui.Confirm(message, "切换路径")) return;
                await controller.ChangeDataDirectory(target); path.Text = controller.Store.DirectoryPath; status.Text = "数据路径已切换，原目录保留。";
            }
            catch (Exception e) { Ui.Error("路径未切换：" + e.Message); }
        }));
        panel.Children.Add(Ui.Label("配置备份包含窗口、选项及平台接入配置，不包含任务、草稿和阶段详情。", 12, Ui.Muted));
        panel.Children.Add(Ui.Label("配置保存路径", 14));
        panel.Children.Add(Ui.Label(controller.Store.ConfigurationDirectory ?? "当前任务目录内", 12, Ui.Muted));
        panel.Children.Add(Ui.Button("切换配置路径", async () =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择配置保存目录" }; if (dialog.ShowDialog(this) != true) return;
            try
            {
                if (File.Exists(Path.Combine(dialog.FolderName, "settings.json")) && !await Ui.Confirm("目标已有程序配置，采用这些配置并保留当前任务及归档。是否切换？", "切换配置")) return;
                await controller.ChangeConfigurationDirectory(dialog.FolderName); RefreshSection();
            }
            catch (Exception e) { Ui.Error("配置路径未切换：" + e.Message); }
        }));
        panel.Children.Add(Ui.Button("导出配置备份", async () =>
        {
            if (!await controller.Flush()) return;
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "即时任务看板配置|*.json", FileName = "即时任务看板配置-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".json" }; if (dialog.ShowDialog(this) != true) return;
            try { ConfigurationBackup.Capture(controller.Engine.State, controller.Store.DirectoryPath).Write(dialog.FileName); status.Text = "配置已导出，不含任务数据。"; }
            catch (Exception e) { Ui.Error("不能导出配置：" + e.Message); }
        }));
        panel.Children.Add(Ui.Button("导入配置备份", async () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "即时任务看板配置|*.json" }; if (dialog.ShowDialog(this) != true) return;
            try
            {
                var configuration = ConfigurationBackup.Read(dialog.FileName);
                if (!await Ui.Confirm("恢复程序配置，不覆盖当前任务或草稿。飞书尚未接入，本轮不会拉取云端任务。", "导入配置")) return;
                await controller.ImportConfiguration(configuration);
                if (!string.Equals(configuration.DataDirectory, controller.Store.DirectoryPath, StringComparison.OrdinalIgnoreCase) && Directory.Exists(configuration.DataDirectory) &&
                    await Ui.Confirm("备份中的数据路径可用，是否也切换到该路径？目标目录的任务不会与当前任务混合。", "采用备份路径")) await controller.ChangeDataDirectory(configuration.DataDirectory);
                RefreshSection(); Ui.Error("配置已导入。本地任务保持；飞书未接入，尚未拉取云端数据。数据路径：" + controller.Store.DirectoryPath);
            }
            catch (Exception e) { Ui.Error("不能导入配置：" + e.Message); }
        }));
        panel.Children.Add(status); return panel;
    }
    private StackPanel PlatformSettings(bool feishu)
    {
        var panel = new StackPanel(); panel.Children.Add(Ui.Label(feishu ? "飞书接入情况" : "AI平台接入情况", 18));
        foreach (var name in feishu ? new[] { "飞书" } : new[] { "codex", "dsh", "workbuddy" })
        {
            if (name == "codex") { panel.Children.Add(CodexSettings()); continue; }
            var body = new StackPanel(); body.Children.Add(Ui.Label(name + " · 未接入", 15, Ui.Muted));
            body.Children.Add(Ui.Label(feishu ? "实际连接、同步与配置恢复后拉取云端任务留飞书专项。" : "实际连接、会话关联与监听留平台专项。", 12, Ui.Muted));
            var input = Ui.Input(controller.Engine.State.Preferences.ConnectionConfigs.GetValueOrDefault(name, "")); input.ToolTip = "接入配置备注；尚未建立连接";
            input.TextChanged += (_, _) => { controller.Engine.State.Preferences.ConnectionConfigs[name] = input.Text; controller.Engine.Touch(); }; body.Children.Add(input); panel.Children.Add(Ui.Panel(body));
        }
        return panel;
    }
    private TextBlock? codexStatus;
    internal void RefreshCodexStatus()
    {
        if (codexStatus is not null)
        {
            codexStatus.Text = "Codex · " + controller.CodexStatus;
            codexStatus.Foreground = controller.AiSetupReport.SourceReady && controller.Codex.SourceIdentityVerified ? Ui.Accent : Ui.Muted;
        }
        RefreshAiInitialization();
    }
    private Border CodexSettings()
    {
        var body = new StackPanel(); codexStatus = Ui.Label("Codex · "+controller.CodexStatus,14,Ui.Muted); body.Children.Add(codexStatus);
        body.Children.Add(AiInitializationSettings());
        return Ui.Panel(body);
    }
    private static void Try(Action action) { try { action(); } catch (Exception e) { Ui.Error(e.Message); } }
}

