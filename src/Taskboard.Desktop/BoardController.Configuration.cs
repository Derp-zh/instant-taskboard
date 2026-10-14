using System;
using System.IO;
using System.Threading.Tasks;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed partial class BoardController
{
    public async Task ChangeConfigurationDirectory(string directory)
    {
        var target = Path.GetFullPath(directory);
        if (string.Equals(target, Store.ConfigurationDirectory, StringComparison.OrdinalIgnoreCase)) return;
        if (dataTransition) throw new InvalidOperationException("正在切换或保存路径，请稍后重试");
        if (Isolated && !target.StartsWith(Path.GetFullPath("test-output") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("隔离验证只能使用test-output内的配置目录");
        var lease = DataPaths.Lock(target, out var acquired); if (!acquired) { lease.Dispose(); throw new InvalidOperationException("配置目录已被另一份程序使用"); }
        var retained = false; var originalStore = Store; SetDataTransition(true);
        try
        {
            foreach (var editor in Editors) editor.PrepareHide();
            if (!await Flush()) throw new IOException("当前修改未保存，配置路径保持原样");
            var candidate = Copies.Of(Engine.State); var file = Path.Combine(target, "settings.json");
            if (File.Exists(file)) candidate = ConfigurationBackup.Read(file).Apply(candidate);
            else if (File.Exists(file + ".bak")) candidate = ConfigurationBackup.Read(file + ".bak").Apply(candidate);
            var next = new AtomicStore(Store.DirectoryPath, target);
            await saveGate.WaitAsync();
            try
            {
                await next.SaveAsync(candidate); DataPaths.Save(pathSelectionFile, Store.DirectoryPath, target);
                Store = next; Engine.State.Preferences = candidate.Preferences; Engine.State.AiOptions = candidate.AiOptions; Engine.State.PriorityOptions = candidate.PriorityOptions;
                ColumnsChanged(false); OptionsChanged(); ApplySettings(); RestorePositions();
                directoryLocks.Add(lease); retained = true; Engine.Touch();
            }
            catch { await originalStore.SaveAsync(Copies.Of(Engine.State)); throw; }
            finally { saveGate.Release(); }
            await Listening.Apply(); if (!Isolated) { StartupLinks.SetAutoStart(Engine.State.Preferences.AutoStart); await RestartCodex(); }
        }
        finally { SetDataTransition(false); if (!retained) { lease.ReleaseMutex(); lease.Dispose(); } }
    }
}
