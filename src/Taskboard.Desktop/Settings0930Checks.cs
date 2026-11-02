using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Taskboard.Core;

namespace Taskboard.Desktop;

// Offscreen settings checks only. No pointer, desktop, external app or system
// settings action is invoked; the controller must use an isolated data directory.
internal static class Settings0930Checks
{
    internal static Task Run(BoardController controller, string root)
    {
        if (!controller.Isolated) throw new InvalidOperationException("设置检查必须使用隔离数据目录");
        Directory.CreateDirectory(root);
        var checks = new List<string>();
        void Check(bool value, string text) { if (!value) throw new InvalidOperationException(text); checks.Add(text); }
        var config = controller.Engine.State.Preferences.ConnectionConfigs;
        var originalConfig = new Dictionary<string, string>(config);
        var savedReport = controller.AiSetupReport;
        var savedTest = controller.AiConversationTest;
        var savedTransport = controller.Codex.TransportConnected;
        var savedIdentity = controller.Codex.SourceIdentityVerified;
        var capabilitiesField = typeof(CodexObserver).GetField("capabilities", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var savedCapabilities = capabilitiesField.GetValue(controller.Codex);
        var item = new WorkItem { Title = "仅隔离设置能力样本" };
        item.Stages[0].Codex = new CodexBinding { ThreadId = "11111111-1111-4111-8111-111111111111" };
        var settings = new SettingsSurface(controller);
        try
        {
            config[CodexInitialization.InspectorKey] = "http://retained.invalid:9339";
            config["codex.endpoint"] = "ws://retained.invalid:19339";
            config["dsh"] = "retained-dsh-note";
            config["飞书"] = "retained-feishu-note";
            foreach (var name in SettingsSurface.Sections)
            {
                settings.SelectSection(name); Layout(settings.Root, 760, 680);
                Check(settings.CurrentSection == name, "原设置导航编号仍可到达：" + name);
                Check(Logical<Button>(settings.Root).Count(x => Equals(x.Tag, "SettingsSave")) == 1, "保留统一手动保存入口：" + name);
                Check(Logical<StackPanel>(settings.Root).Count(x => Equals(x.Tag, "SettingsPageHeading")) == 1, "单一页头与用途说明：" + name);
                Check(Logical<Button>(settings.Root).Count(x => Equals(x.Tag, "SettingsNavigation")) == 7, "七个设置入口完整：" + name);
                Snapshot(settings.Root, 760, 680, Path.Combine(root, "settings-" + Array.IndexOf(SettingsSurface.Sections, name) + ".png"));
            }
            foreach (var (name, method) in new[] { ("主设置", "MainSettings"), ("窗口", "WindowSettings"), ("监听", "ListeningSettings"), ("归档", "ArchiveSettings"), ("数据与备份", "DataSettings") })
            {
                var raw = (StackPanel)typeof(SettingsSurface).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(settings, null)!;
                var controls = Logical<Control>(raw).ToArray();
                var options = controls.OfType<System.Windows.Controls.Primitives.Selector>().ToDictionary(x => x, x => x.Items.Count);
                SettingsSurface.OrganizeSettingsPage(name, raw);
                Check(controls.All(x => Logical<Control>(raw).Contains(x)) && options.All(x => x.Key.Items.Count == x.Value), "重排保留原控件、事件实例与可选项：" + name);
            }
            settings.SelectSection("AI平台接入情况"); Layout(settings.Root, 760, 680);
            var diagnostics = Logical<Expander>(settings.Root).Single(x => Equals(x.Tag, "AiDiagnostics"));
            Check(!diagnostics.IsExpanded, "AI技术诊断默认收起");
            Check(!Logical<TextBox>(settings.Root).Any(x => Equals(x.Tag, CodexInitialization.InspectorKey) || Equals(x.Tag, "codex.endpoint")), "旧观察端口和服务地址不再作为设置输入项");
            Check(Logical<Button>(settings.Root).Any(x => Equals(x.Tag, "AiChooseDesktop")) && Logical<Button>(settings.Root).Any(x => Equals(x.Tag, "AiChooseCli")), "桌面路径修复与可选兼容工具均保留");
            var compatibility = Logical<Expander>(diagnostics).Single(x => x.Header?.ToString() == "兼容工具（可选）");
            Check(!compatibility.IsExpanded && Logical<TextBox>(compatibility).Any(x => Equals(x.Tag, CodexInitialization.CliKey)), "CLI路径只在二级兼容工具中提供");
            Check(!Logical<TextBlock>(settings.Root).Any(x => x.Text.Contains("本机状态事件：", StringComparison.Ordinal) || x.Text.Contains("后台聊天查询：", StringComparison.Ordinal)), "AI页移除内部事件和查询计数");
            Check(Logical<Button>(settings.Root).Single(x => Equals(x.Tag, "AiInitialize")).Content?.ToString() == "检查连接", "主动作使用用户可理解的检查连接");
            var testDetails = Logical<Expander>(settings.Root).Single(x => Equals(x.Tag, "AiTestDetails"));
            var testText = "已有监测结果（查看于12:00:00）\n" + string.Join("\n", Enumerable.Range(1, 20).Select(x => $"聊天 {x}：状态待确认，保留原有提醒。"));
            Set(controller, "AiConversationTest", testText); settings.RefreshAiInitialization(); Layout(settings.Root, 760, 680);
            Check(testDetails.Visibility == Visibility.Visible && !testDetails.IsExpanded &&
                Logical<TextBlock>(settings.Root).Single(x => Equals(x.Tag, "AiTestSummary")).Text.Contains("12:00:00", StringComparison.Ordinal), "多聊天测试后主视图保留一行时间摘要，详细结果默认收起");
            Check(Logical<TextBlock>(testDetails).Single(x => Equals(x.Tag, "AiTestResult")).Text == testText, "结果折叠只改变排版，不截断或丢失逐聊天详情");
            Snapshot(settings.Root, 760, 680, Path.Combine(root, "settings-ai-results-collapsed.png"));
            testDetails.IsExpanded = true; Layout(settings.Root, 760, 680);
            Snapshot(settings.Root, 760, 680, Path.Combine(root, "settings-ai-results-expanded.png"));
            Check(testDetails.IsExpanded && Logical<TextBlock>(testDetails).Single(x => Equals(x.Tag, "AiTestResult")).Text.Contains("聊天 20", StringComparison.Ordinal), "用户展开后可读到全部聊天结果");
            testDetails.IsExpanded = false;
            Set(controller, "AiConversationTest", "尚未关联聊天。请先在任务阶段选择已有聊天，再测试。"); settings.RefreshAiInitialization();
            Check(Logical<TextBlock>(settings.Root).Single(x => Equals(x.Tag, "AiTestSummary")).Text.StartsWith("尚未关联聊天。", StringComparison.Ordinal) && testDetails.Visibility == Visibility.Visible, "没有关联聊天时结果提示也在主视图可发现");
            Set(controller, "AiConversationTest", savedTest); settings.RefreshAiInitialization();
            controller.Engine.State.Tasks.Add(item);
            Set(controller, "AiSetupReport", new CodexSetupReport(CodexSetupState.Ready, "连接已建立", "", Catalog: true, SourceReady: true, DesktopConnected: true));
            Set(controller.Codex, "TransportConnected", true); Set(controller.Codex, "SourceIdentityVerified", true);
            capabilitiesField.SetValue(controller.Codex, Array.Empty<CodexThreadCapability>());
            settings.RefreshAiInitialization();
            Check(Capabilities(settings).Contains("0/", StringComparison.Ordinal) && !Capabilities(settings).Contains("已准备好", StringComparison.Ordinal), "只有握手和旧成功报告时不宣称聊天提问批准可用");
            capabilitiesField.SetValue(controller.Codex, new[] { new CodexThreadCapability(item.CurrentCodex!.ThreadId, false, "synthetic-unavailable", "", true) });
            settings.RefreshAiInitialization();
            Check(Capabilities(settings).Contains("0/", StringComparison.Ordinal), "不可实时读取的聊天不能因PendingAvailable标记冒称可观察");
            capabilitiesField.SetValue(controller.Codex, new[] { new CodexThreadCapability(item.CurrentCodex!.ThreadId, true, "synthetic-live", "active", true) });
            settings.RefreshAiInitialization();
            var bound = controller.Engine.State.Tasks.SelectMany(x => x.Stages).Where(x => x.Codex is not null).Select(x => x.Codex!.ThreadId).Distinct(StringComparer.Ordinal).Count();
            Check(Capabilities(settings).Contains($"1/{bound} 提问／批准可读", StringComparison.Ordinal), "已验证逐聊天完整能力按实际数量呈现");
            Set(controller, "AiSetupReport", new CodexSetupReport(CodexSetupState.CatalogUnavailable, "聊天列表暂不可用", "请修复本地目录", Catalog: false, DesktopConnected: true));
            settings.RefreshAiInitialization();
            Check(Logical<TextBlock>(settings.Root).Single(x => Equals(x.Tag, "AiInitializationResult")).Text == "聊天列表暂不可用" &&
                Logical<TextBlock>(settings.Root).Single(x => Equals(x.Tag, "AiNextStep")).Text == "请修复本地目录" && Capabilities(settings).Contains("暂不可用", StringComparison.Ordinal), "后台通道仍连接时本次目录失败和修复步骤仍优先显示");
            Set(controller, "AiSetupReport", new CodexSetupReport(CodexSetupState.Failed, "配置未能保存", "请重试保存", Catalog: true, DesktopConnected: true));
            settings.RefreshAiInitialization();
            Check(Logical<TextBlock>(settings.Root).Single(x => Equals(x.Tag, "AiInitializationResult")).Text == "配置未能保存" &&
                Logical<TextBlock>(settings.Root).Single(x => Equals(x.Tag, "AiNextStep")).Text == "请重试保存", "连接成功不能覆盖保存失败或其下一步操作");
            Set(controller, "AiSetupReport", new CodexSetupReport(CodexSetupState.Ready, "连接已建立", "", Catalog: true, SourceReady: true, DesktopConnected: true));
            Set(controller.Codex, "TransportConnected", false); settings.RefreshAiInitialization();
            Check(Capabilities(settings).Contains("0/", StringComparison.Ordinal) && Logical<TextBlock>(settings.Root).Single(x => Equals(x.Tag, "AiInitializationResult")).Text == "连接暂时中断", "断线后旧能力不保留成功外观");
            diagnostics.IsExpanded = true; Layout(settings.Root, 760, 680);
            Snapshot(settings.Root, 760, 680, Path.Combine(root, "settings-ai-diagnostics.png"));
            Check(Logical<TextBlock>(diagnostics).Any(x => x.Text.Contains("连接成功不代表所有聊天均可观察", StringComparison.Ordinal)), "能力边界放入展开后的诊断");
            settings.SelectSection("飞书接入情况");
            Check(!Logical<TextBox>(settings.Root).Any(), "未接入飞书不展示无效连接表单");
            settings.SelectSection("AI平台接入情况");
            Check(!Logical<TextBox>(settings.Root).Any(x => x.Text == "retained-dsh-note"), "未接入其他AI不展示无效配置输入");
            Check(config[CodexInitialization.InspectorKey] == "http://retained.invalid:9339" && config["codex.endpoint"] == "ws://retained.invalid:19339" && config["dsh"] == "retained-dsh-note" && config["飞书"] == "retained-feishu-note", "移除输入界面不删除已有连接配置值");
            settings.SelectSection("数据与备份");
            var storage = Logical<Expander>(settings.Root).Single(x => x.Header?.ToString() == "存储位置");
            Check(!storage.IsExpanded && Logical<Button>(storage).Any(x => x.Content?.ToString() == "切换数据路径") && Logical<Button>(storage).Any(x => x.Content?.ToString() == "切换配置路径"), "存储路径折叠但保留两个切换操作");
            File.WriteAllText(Path.Combine(root, "settings0930-report.json"), JsonSerializer.Serialize(new
            {
                passed = checks.Count, checks, isolated = true, originalDesktopOrMouseOperated = false,
                source = "actual WPF controls, offscreen layout and synthetic capability fields",
                scope = "Settings navigation, retained controls/options, disclosure, capability honesty and removed obsolete fields; no live Codex lifecycle claim"
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            controller.Engine.State.Tasks.Remove(item);
            Set(controller, "AiSetupReport", savedReport); Set(controller, "AiConversationTest", savedTest); Set(controller.Codex, "TransportConnected", savedTransport); Set(controller.Codex, "SourceIdentityVerified", savedIdentity);
            capabilitiesField.SetValue(controller.Codex, savedCapabilities);
            config.Clear(); foreach (var pair in originalConfig) config[pair.Key] = pair.Value;
            settings.Destroy(false);
        }
        return Task.CompletedTask;
    }
    private static string Capabilities(SettingsSurface settings) => Logical<TextBlock>(settings.Root).Single(x => Equals(x.Tag, "AiCapabilities")).Text;
    private static void Set(object target, string property, object value) => target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static void Layout(FrameworkElement view, double width, double height) { view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout(); }
    private static void Snapshot(FrameworkElement view, int width, int height, string path)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual(); using (var drawing = background.RenderOpen()) drawing.DrawRectangle(Ui.Surface, null, new Rect(0, 0, width, height));
        bitmap.Render(background); bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
    private static IEnumerable<T> Logical<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) foreach (var descendant in Logical<T>(child)) yield return descendant;
    }
}
