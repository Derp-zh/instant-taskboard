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
        var floatingVerify = false;
        var appearanceVerify = false;
        var compactVerify = false;
        var sharedVerify = false;
        var integrationVerify = false; var startupEntryVerify = false; var startupInvocation = false;
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
            else if (args[i] == "--animation-verify") animationVerify=true;
            else if (args[i] == "--performance-verify") performanceVerify=true;
            else if (args[i] == "--scroll-verify") scrollVerify=true;
            else if (args[i] == "--scroll-restore-verify") { scrollVerify = true; scrollRestoreVerify = true; }
            else if (args[i] == "--scroll-stack-verify") { scrollVerify = true; scrollRestoreVerify = true; scrollStackVerify = true; }
            else if (args[i] == "--outside-observer-verify") outsideObserverVerify = true;
            else if (args[i] == "--floating-verify") { outsideObserverVerify = true; floatingVerify = true; }
            else if (args[i] == "--appearance-verify") { appearanceVerify = true; startupCheck = true; }
            else if (args[i] == "--compact-verify") { compactVerify = true; startupCheck = true; }
            else if (args[i] == "--shared-verify") sharedVerify = true;
            else if (args[i] == "--integration-verify") { integrationVerify = true; startupCheck = true; }
            else if (args[i] == "--startup-entry-verify") { startupEntryVerify = true; startupCheck = true; }
            else if (args[i] == "--startup") startupInvocation = true;
            else if (args[i] == "--listening-verify") { listeningVerify = true; startupCheck = true; }
            else { MessageBox.Show("不支持的启动参数：" + args[i]); return 2; }
        }
        if ((smoke || startupCheck || desktopVerify || animationVerify || performanceVerify || scrollVerify || sharedVerify || outsideObserverVerify) && !hasDataDirectory)
        { Console.Error.WriteLine("验证模式必须明确指定隔离数据目录。"); return 2; }
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
        if ((smoke || startupCheck || desktopVerify || animationVerify || performanceVerify || scrollVerify || sharedVerify || outsideObserverVerify) && (!hasDataDirectory || string.Equals(Path.TrimEndingDirectorySeparator(data), Path.TrimEndingDirectorySeparator(defaultData), StringComparison.OrdinalIgnoreCase)))
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
                controller = new BoardController(data, isolated, smoke || startupCheck); if (!listeningVerify && !appearanceVerify && !integrationVerify && !compactVerify && !sharedVerify) await controller.Start();
                if (startupInvocation && !isolated) File.WriteAllText(Path.Combine(DataPaths.SystemRoot, "last-startup.json"), System.Text.Json.JsonSerializer.Serialize(new { utc = DateTime.UtcNow, version = typeof(Program).Assembly.GetName().Version!.ToString(), executable = Environment.ProcessPath }));
                startupClock.Stop();
                if (smoke || startupCheck) File.WriteAllText(Path.Combine(data, "startup-report.json"), System.Text.Json.JsonSerializer.Serialize(new { readyMs = startupClock.Elapsed.TotalMilliseconds, tasks = controller.Engine.State.Tasks.Count, draftsAndEdits = controller.Engine.State.Sessions.Count }));
                if (outsideObserverVerify) { if (floatingVerify) await FloatingInteractionChecks.Run(controller); else await OutsideObserverChecks.Run(controller); await controller.Exit(); }
                else if (sharedVerify) { await SharedMotionChecks.Run(controller); await controller.Exit(); }
                else if (compactVerify) { await CompactChecks.Run(controller); await controller.Exit(); }
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
                if (!smoke && !startupCheck && !desktopVerify && !scrollVerify && !outsideObserverVerify) Ui.Error("即时任务看板不能启动：" + e.Message);
                controller?.Dispose(); application.Shutdown(1);
            }
        };
        application.DispatcherUnhandledException += (_, e) =>
        {
            try { Directory.CreateDirectory(data); File.AppendAllText(Path.Combine(data, "errors.log"), DateTime.Now + " " + e.Exception + Environment.NewLine); } catch { }
            if (smoke || startupCheck || desktopVerify) { application.Shutdown(1); }
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
