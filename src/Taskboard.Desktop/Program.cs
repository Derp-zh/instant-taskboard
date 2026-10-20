using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;

namespace Taskboard.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var startupClock = Stopwatch.StartNew();
        var performanceVerify=false;
        var scrollVerify = false;
        var scrollRestoreVerify = false;
        var scrollStackVerify = false;
        var outsideObserverVerify = false;
        var gameInputVerify = false;
        var floatingVerify = false;
        var workspaceVerify = false;
        var workspaceGeometryVerify = false;
        var appearanceVerify = false;
        var compactVerify = false;
        var stageVerify = false;
        var ux098Verify = false;
        var ux099Verify = false;
        var ux099DesktopVerify = false;
        var ux0910Verify = false; var hintDisplayVerify = false;
        var ux0911Verify = false;
        var ux0912RenderVerify = false;
        var ux0912Verify = false; var ux0913Verify = false; var ux0914Verify = false; var ux0914DesktopVerify = false; var ux0913RenderVerify = false;
        var featureDesktopVerify = false;
        var sharedVerify = false;
        var integrationVerify = false; var startupEntryVerify = false; var startupInvocation = false;
        var ux0915Verify = false; var ux0915RenderVerify = false; var codexLifecycleVerify = false; var ux0917Verify = false; var ux0917GlassOnly = false; var ux0920Verify = false;
        var defaultData = DataPaths.DefaultDataDirectory;
        var data = defaultData; var isolated = false; var smoke = false; var startupCheck = false; var desktopVerify = false; var hasDataDirectory = false; var codexVerify = false; var animationVerify=false; var listeningVerify = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--data-dir" && i + 1 < args.Length) { data = Path.GetFullPath(args[++i]); isolated = true; hasDataDirectory = true; }
            else if (args[i] == "--no-integrations") isolated = true;
            else if (args[i] == "--ui-smoke") smoke = true;
            else if (args[i] == "--startup-check") startupCheck = true;
            else if (args[i] == "--desktop-verify") desktopVerify = true;
            else if (args[i] == "--codex-verify") { codexVerify=true; startupCheck=true; }
            else if (args[i] == "--codex-lifecycle-verify") codexLifecycleVerify = true;
            else if (args[i] == "--ux0917-verify") { ux0917Verify = true; stageVerify = true; desktopVerify = true; }
            else if (args[i] == "--ux0917-glass-verify") { ux0917Verify = true; ux0917GlassOnly = true; stageVerify = true; desktopVerify = true; }
            else if (args[i] == "--animation-verify") animationVerify=true;
            else if (args[i] == "--performance-verify") performanceVerify=true;
            else if (args[i] == "--scroll-verify") scrollVerify=true;
            else if (args[i] == "--scroll-restore-verify") { scrollVerify = true; scrollRestoreVerify = true; }
            else if (args[i] == "--scroll-stack-verify") { scrollVerify = true; scrollRestoreVerify = true; scrollStackVerify = true; }
            else if (args[i] == "--outside-observer-verify") outsideObserverVerify = true;
            else if (args[i] == "--game-input-verify") { outsideObserverVerify = true; gameInputVerify = true; }
            else if (args[i] == "--floating-verify") { outsideObserverVerify = true; floatingVerify = true; }
            else if (args[i] == "--workspace-verify") { outsideObserverVerify = true; workspaceVerify = true; }
            else if (args[i] == "--workspace-geometry-verify") { outsideObserverVerify = true; workspaceVerify = true; workspaceGeometryVerify = true; }
            else if (args[i] == "--appearance-verify") { appearanceVerify = true; startupCheck = true; }
            else if (args[i] == "--compact-verify") { compactVerify = true; startupCheck = true; }
            else if (args[i] == "--stage-verify") { stageVerify = true; startupCheck = true; }
            else if (args[i] == "--ux098-verify") { ux098Verify = true; stageVerify = true; startupCheck = true; }
            else if (args[i] == "--ux099-verify") { ux099Verify = true; stageVerify = true; startupCheck = true; }
            else if (args[i] == "--ux099-desktop-verify") { ux099DesktopVerify = true; stageVerify = true; desktopVerify = true; }
            else if (args[i] == "--ux0910-verify") { ux0910Verify = true; stageVerify = true; startupCheck = true; }
            else if (args[i] == "--ux0911-verify") { ux0911Verify = true; stageVerify = true; startupCheck = true; }
            else if (args[i] == "--ux0912-render-verify") { ux0912RenderVerify = true; stageVerify = true; startupCheck = true; }
            else if (args[i] == "--ux0913-render-verify") { ux0913RenderVerify = true; stageVerify = true; startupCheck = true; }
            else if (args[i] == "--ux0914-desktop-verify") { ux0914DesktopVerify = true; stageVerify = true; desktopVerify = true; }
            else if (args[i] == "--ux0914-verify") { ux0914Verify = true; stageVerify = true; startupCheck = true; }
            else if (args[i] == "--ux0915-verify") { ux0915Verify = true; stageVerify = true; startupCheck = true; }
            else if (args[i] == "--ux0920-verify") { ux0920Verify = true; stageVerify = true; startupCheck = true; }
            else if (args[i] == "--ux0915-render-verify") { ux0915RenderVerify = true; stageVerify = true; startupCheck = true; }
            else if (args[i] == "--ux0913-verify") { ux0913Verify = true; stageVerify = true; startupCheck = true; }
            else if (args[i] == "--ux0912-verify") { ux0912Verify = true; stageVerify = true; startupCheck = true; }
            else if (args[i] == "--hint-display-verify") { ux0910Verify = true; hintDisplayVerify = true; stageVerify = true; startupCheck = true; }
            else if (args[i] == "--feature-desktop-verify") { featureDesktopVerify = true; outsideObserverVerify = true; }
            else if (args[i] == "--shared-verify") sharedVerify = true;
            else if (args[i] == "--integration-verify") { integrationVerify = true; startupCheck = true; }
            else if (args[i] == "--startup-entry-verify") { startupEntryVerify = true; startupCheck = true; }
            else if (args[i] == "--startup") startupInvocation = true;
            else if (args[i] == "--listening-verify") { listeningVerify = true; startupCheck = true; }
            else { MessageBox.Show("不支持的启动参数：" + args[i]); return 2; }
        }
        if ((smoke || startupCheck || desktopVerify || animationVerify || performanceVerify || scrollVerify || sharedVerify || outsideObserverVerify || codexLifecycleVerify) && !hasDataDirectory)
        { Console.Error.WriteLine("验证模式必须明确指定隔离数据目录。"); return 2; }
        if (codexVerify)
        {
            if (string.Equals(Path.TrimEndingDirectorySeparator(data), Path.TrimEndingDirectorySeparator(defaultData), StringComparison.OrdinalIgnoreCase)) return 2;
            try { return CodexChecks.Run(data).GetAwaiter().GetResult() ? 0 : 3; }
            catch (Exception error) { Directory.CreateDirectory(data); File.WriteAllText(Path.Combine(data, "codex-error.txt"), error.ToString()); return 1; }
        }
        if (startupEntryVerify)
        {
            try { StartupLinks.VerifyAndUpdateExistingEntry(data); return 0; }
            catch (Exception e) { Directory.CreateDirectory(data); File.WriteAllText(Path.Combine(data, "startup-error.txt"), e.ToString()); return 1; }
        }
        if (!hasDataDirectory)
        {
            if (!isolated)
            {
                try
                {
                    var latest = StartupLinks.RegisterCurrent();
                    if (!string.Equals(latest, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                    { Process.Start(new ProcessStartInfo(latest) { WorkingDirectory = Path.GetDirectoryName(latest)!, UseShellExecute = false }); return 0; }
                }
                catch (Exception e) { MessageBox.Show("程序版本登记失败：" + e.Message, "即时任务看板"); return 1; }
            }
            try
            {
                if (!isolated && DataPaths.NeedsLegacyChoice())
                {
                    var choice = MessageBox.Show("默认在系统用户目录保存任务和配置。如果从旧版升级，请选择原data目录，复制后原件保留。是否现在选择原数据？\n首次使用请选择‘否’。", "即时任务看板首次保存路径", MessageBoxButton.YesNoCancel);
                    if (choice == MessageBoxResult.Cancel) return 0;
                    if (choice == MessageBoxResult.Yes)
                    {
                        var legacy = new Microsoft.Win32.OpenFolderDialog { Title = "选择旧版原任务数据目录" }; if (legacy.ShowDialog() != true) return 0;
                        data = DataPaths.MigrateSelectedDirectory(legacy.FolderName, DataPaths.SystemRoot);
                    }
                    else data = DataPaths.DefaultDirectory();
                }
                else data = DataPaths.DefaultDirectory();
            }
            catch (DirectoryNotFoundException e)
            {
                if (MessageBox.Show(e.Message + "\n是否选择原数据的新位置？取消会退出，不创建空数据。", "即时任务看板", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return 1;
                var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择原任务数据所在目录" };
                if (dialog.ShowDialog() != true) return 1;
                data = Path.GetFullPath(dialog.FolderName);
                if (!File.Exists(Path.Combine(data, "board.json")) && !File.Exists(Path.Combine(data, "board.json.bak"))) { MessageBox.Show("所选目录没有原任务数据，程序不会建立空数据替代。", "即时任务看板"); return 1; }
                try { Directory.CreateDirectory(Path.Combine(DataPaths.SystemRoot, "config")); DataPaths.Save(DataPaths.SelectionFile, data, Path.Combine(DataPaths.SystemRoot, "config")); }
                catch (Exception error) { MessageBox.Show(error.Message, "即时任务看板"); return 1; }
            }
            catch (Exception e) { MessageBox.Show(e.Message, "即时任务看板"); return 1; }
        }
        if ((smoke || startupCheck || desktopVerify || animationVerify || performanceVerify || scrollVerify || sharedVerify || outsideObserverVerify || codexLifecycleVerify) && (!hasDataDirectory || string.Equals(Path.TrimEndingDirectorySeparator(data), Path.TrimEndingDirectorySeparator(defaultData), StringComparison.OrdinalIgnoreCase)))
        { Console.Error.WriteLine("验证模式必须明确指定非正式目录的 --data-dir。"); return 2; }
        using var mutex = DataPaths.Lock(data, out var acquired);
        if (!acquired) { MessageBox.Show("这一份任务数据已经打开，请从托盘找到即时任务看板。"); return 0; }
        var configurationAcquired = true;
        using var configurationMutex = isolated ? null : DataPaths.Lock(DataPaths.ConfigurationDirectory(), out configurationAcquired);
        if (!configurationAcquired) { MessageBox.Show("程序配置已由另一份任务看板打开，请从托盘找到已运行的程序。"); return 0; }
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        System.Windows.Media.Animation.Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(System.Windows.Media.Animation.Timeline),new FrameworkPropertyMetadata(120));
        application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Taskboard;component/Theme.xaml", UriKind.Relative) });
        BoardController? controller = null;
        application.Startup += async (_, _) =>
        {
            try
            {
                controller = new BoardController(data, isolated, smoke || startupCheck); if (!listeningVerify && !appearanceVerify && !integrationVerify && !compactVerify && !stageVerify && !sharedVerify && !workspaceGeometryVerify) await controller.Start();
                if (startupInvocation && !isolated) File.WriteAllText(Path.Combine(DataPaths.SystemRoot, "last-startup.json"), System.Text.Json.JsonSerializer.Serialize(new { utc = DateTime.UtcNow, version = typeof(Program).Assembly.GetName().Version!.ToString(), executable = Environment.ProcessPath }));
                startupClock.Stop();
                if (smoke || startupCheck) File.WriteAllText(Path.Combine(data, "startup-report.json"), System.Text.Json.JsonSerializer.Serialize(new { readyMs = startupClock.Elapsed.TotalMilliseconds, tasks = controller.Engine.State.Tasks.Count, draftsAndEdits = controller.Engine.State.Sessions.Count }));
                if (ux0917Verify) { await Ux0917Checks.Run(controller, ux0917GlassOnly); await controller.Exit(); }
                else if (codexLifecycleVerify) { await CodexDesktopLifecycleChecks.Run(controller); await controller.Exit(); }
                else if (outsideObserverVerify) { if (featureDesktopVerify) await FeatureDesktopChecks.Run(controller); else if (workspaceVerify) await WorkspaceChecks.Run(controller, workspaceGeometryVerify); else if (floatingVerify) await FloatingInteractionChecks.Run(controller); else await OutsideObserverChecks.Run(controller, gameInputVerify); await controller.Exit(); }
                else if (sharedVerify) { await SharedMotionChecks.Run(controller); await controller.Exit(); }
                else if (compactVerify) { await CompactChecks.Run(controller); await controller.Exit(); }
                else if (stageVerify) { if (ux0920Verify) await Ux0920Checks.Run(controller); else if (ux0915RenderVerify) await Ux0915RenderChecks.Run(controller); else if (ux0915Verify) await Ux0915Checks.Run(controller); else if (ux0914DesktopVerify) await Ux0914DesktopChecks.Run(controller); else if (ux0914Verify) await Ux0914Checks.Run(controller); else if (ux0913RenderVerify) await Ux0913RenderChecks.Run(controller); else if (ux0913Verify) await Ux0913Checks.Run(controller); else if (ux0912Verify) await Ux0912Checks.Run(controller); else if (ux0912RenderVerify) await Ux0912RenderChecks.Run(controller); else if (ux0911Verify) await Ux0911Checks.Run(controller); else if (ux0910Verify) await Ux0910Checks.Run(controller, hintDisplayVerify); else if (ux099DesktopVerify) await Ux099DesktopChecks.Run(controller); else if (ux099Verify) await Ux099Checks.Run(controller); else if (ux098Verify) await Ux098Checks.Run(controller); else await StageFeatureChecks.Run(controller); await controller.Exit(); }
                else if (integrationVerify) { await IntegrationChecks.Run(controller); await controller.Exit(); }
                else if (appearanceVerify) { AppearanceChecks.Run(controller); await controller.Exit(); }
                else if (scrollVerify) { await ScrollMotionChecks.Run(controller, scrollRestoreVerify, scrollStackVerify); await controller.Exit(); }
                else if (listeningVerify) { await NotificationListeningChecks.Run(controller); await controller.Exit(); }
                else if (performanceVerify) {controller.SuppressCollapse=true; await PerformanceChecks.Run(controller); await controller.Exit();}
                else if (animationVerify) { controller.SuppressCollapse=true; await PerformanceChecks.SampleBallAnimation(controller); await controller.Exit(); }
                else if (desktopVerify) { controller.SuppressCollapse = true; await VisualChecks.Run(controller); await MotionChecks.Run(controller); await PerformanceChecks.SampleBallAnimation(controller); controller.SuppressCollapse = true; await DesktopChecks.Run(controller); }
                else if (smoke) await DesktopChecks.Run(controller);
                else if (startupCheck) { if (codexVerify) await CodexChecks.Run(data); await controller.Exit(); }
            }
            catch (Exception e)
            {
                Directory.CreateDirectory(data); File.WriteAllText(Path.Combine(data, "startup-error.txt"), e.ToString());
                if (!smoke && !startupCheck && !desktopVerify && !scrollVerify && !outsideObserverVerify && !codexLifecycleVerify) Ui.Error("即时任务看板不能启动：" + e.Message);
                controller?.Dispose(); application.Shutdown(1);
            }
        };
        application.DispatcherUnhandledException += (_, e) =>
        {
            try { Directory.CreateDirectory(data); File.AppendAllText(Path.Combine(data, "errors.log"), DateTime.Now + " " + e.Exception + Environment.NewLine); } catch { }
            if (smoke || startupCheck || desktopVerify || codexLifecycleVerify) { application.Shutdown(1); }
            else Ui.Error("操作失败，未提交内容仍在窗口中：" + e.Exception.Message);
            e.Handled = true;
        };
        application.SessionEnding += (_, e) =>
        {
            // Windows logout is not a normal app exit: flush without waiting on this UI dispatcher.
            if (controller is null) return;
            foreach (var column in controller.Columns) column.PrepareHide();
            foreach (var editor in controller.Editors) editor.PrepareHide();
            try { controller.Store.SaveAsync(Taskboard.Core.Copies.Of(controller.Engine.State)).GetAwaiter().GetResult(); }
            catch (Exception error) { e.Cancel = true; Ui.Error("退出前保存失败：" + error.Message); }
            var soundErrors = controller.Listening.RestoreOriginals();
            if (soundErrors.Length > 0) { e.Cancel = true; Ui.Error(string.Join("；", soundErrors)); }
        };
        try { return application.Run(); }
        finally { mutex.ReleaseMutex(); }
    }
}




