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
        var defaultData = Path.Combine(AppContext.BaseDirectory, "data");
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
            else if (args[i] == "--listening-verify") { listeningVerify = true; startupCheck = true; }
            else { MessageBox.Show("不支持的启动参数：" + args[i]); return 2; }
        }
        if (!hasDataDirectory)
        {
            try { data = DataPaths.DefaultDirectory(); }
            catch (DirectoryNotFoundException e)
            {
                if (MessageBox.Show(e.Message + "\n是否选择原数据的新位置？取消会退出，不创建空数据。", "即时任务看板", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return 1;
                var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择原任务数据所在目录" };
                if (dialog.ShowDialog() != true) return 1;
                data = Path.GetFullPath(dialog.FolderName);
                if (!File.Exists(Path.Combine(data, "board.json")) && !File.Exists(Path.Combine(data, "board.json.bak"))) { MessageBox.Show("所选目录没有原任务数据，程序不会建立空数据替代。", "即时任务看板"); return 1; }
                try { DataPaths.Save(Path.Combine(AppContext.BaseDirectory, "launch-settings.json"), data); }
                catch (Exception error) { MessageBox.Show(error.Message, "即时任务看板"); return 1; }
            }
            catch (Exception e) { MessageBox.Show(e.Message, "即时任务看板"); return 1; }
        }
        if ((smoke || startupCheck || desktopVerify || animationVerify || performanceVerify) && (!hasDataDirectory || string.Equals(Path.TrimEndingDirectorySeparator(data), Path.TrimEndingDirectorySeparator(defaultData), StringComparison.OrdinalIgnoreCase)))
        { Console.Error.WriteLine("验证模式必须明确指定非正式目录的 --data-dir。"); return 2; }
        using var mutex = DataPaths.Lock(data, out var acquired);
        if (!acquired) { MessageBox.Show("这一份任务数据已经打开，请从托盘找到即时任务看板。"); return 0; }
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        System.Windows.Media.Animation.Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(System.Windows.Media.Animation.Timeline),new FrameworkPropertyMetadata(120));
        application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Taskboard;component/Theme.xaml", UriKind.Relative) });
        BoardController? controller = null;
        application.Startup += async (_, _) =>
        {
            try
            {
                controller = new BoardController(data, isolated, smoke || startupCheck); if (!listeningVerify) await controller.Start();
                startupClock.Stop();
                if (smoke || startupCheck) File.WriteAllText(Path.Combine(data, "startup-report.json"), System.Text.Json.JsonSerializer.Serialize(new { readyMs = startupClock.Elapsed.TotalMilliseconds, tasks = controller.Engine.State.Tasks.Count, draftsAndEdits = controller.Engine.State.Sessions.Count }));
                if (listeningVerify) { await NotificationListeningChecks.Run(controller); await controller.Exit(); }
                else if (performanceVerify) {controller.SuppressCollapse=true; await PerformanceChecks.Run(controller); await controller.Exit();}
                else if (animationVerify) { controller.SuppressCollapse=true; await PerformanceChecks.SampleBallAnimation(controller); await controller.Exit(); }
                else if (desktopVerify) { controller.SuppressCollapse = true; await VisualChecks.Run(controller); await MotionChecks.Run(controller); await PerformanceChecks.SampleBallAnimation(controller); controller.SuppressCollapse = true; await DesktopChecks.Run(controller); }
                else if (smoke) await DesktopChecks.Run(controller);
                else if (startupCheck) { if (codexVerify) await CodexChecks.Run(data); await controller.Exit(); }
            }
            catch (Exception e)
            {
                Directory.CreateDirectory(data); File.WriteAllText(Path.Combine(data, "startup-error.txt"), e.ToString());
                if (!smoke && !startupCheck && !desktopVerify) Ui.Error("即时任务看板不能启动：" + e.Message);
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
