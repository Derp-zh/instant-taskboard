using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed record NotificationListeningEntry(DateTime LocalTime, string App, string Source, string Result);

internal sealed class NotificationListeningService : IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly Func<NotificationListeningOptions> options;
    private readonly bool isolated;
    private readonly string journalFile;
    private readonly SemaphoreSlim changes = new(1);
    private readonly NotificationAudio audio = new();
    private readonly NotificationDeduplicator deduplicator = new();
    private readonly Queue<(Guid Rule, CapturedNotification Notification)> pending = new();
    private readonly Dictionary<Guid, string> ready = new();
    private readonly Dictionary<Guid, string> ruleStatus = new();
    private NotificationSoundLease? lease;
    private NotificationCapture? capture;
    private Mutex? soundLock;
    private bool ownsLock, disposed, playing;
    private int generation;
    private int capturedCount;
    private string failure = "";
    public string Status { get; private set; } = "监听已关闭";
    public string PlaybackStatus { get; private set; } = "";
    public List<NotificationListeningEntry> History { get; } = new();
    public List<NotificationApplication> SeenApps { get; } = new();
    public NotificationApplication[] Applications { get; private set; } = [];
    public event Action? Changed;
    public NotificationListeningService(Dispatcher dispatcher, Func<NotificationListeningOptions> options, bool isolated, string journalFile)
    { this.dispatcher = dispatcher; this.options = options; this.isolated = isolated; this.journalFile = journalFile; }
    public string RuleStatus(Guid id) => ruleStatus.GetValueOrDefault(id, "未启用替换");
    private void Notify() { if (!disposed) Changed?.Invoke(); }
    public async Task RefreshApplications()
    { var apps = await NotificationApplications.Read(); if (!disposed) { Applications = apps; Notify(); } }
    public async Task Apply(bool requestAccess = false)
    {
        var epoch = ++generation;
        StopCore();
        await changes.WaitAsync();
        try
        {
            if (disposed || epoch != generation) return;
            ruleStatus.Clear(); PlaybackStatus = "";
            if (isolated) { Status = "隔离模式：系统捕获和自动修改Windows声音均未启动"; Notify(); return; }
            // Recover a previous interrupted run even if the saved master switch is off.
            if (!EnsureLease()) { Notify(); return; }
            if (!options().Enabled) { Status = failure.Length > 0 ? failure : "监听已关闭，已恢复原通知声音"; ReleaseLock(); Notify(); return; }
            var configuration = Copies.Of(options());
            if (Applications.Length == 0) await RefreshApplications();
            if (disposed || epoch != generation) return;
            capture = new NotificationCapture(dispatcher, Applications.Select(x => x.Name).Concat(configuration.Rules.Select(x => x.AppName)).ToArray()); var active = capture;
            active.Captured += notification => { if (capture == active && epoch == generation) Receive(notification); };
            active.Changed += () =>
            {
                if (capture != active) return;
                var status = active.Status;
                if (!active.Available && ready.Count > 0) { Suspend(); status += "；已停止替换并尝试恢复原通知音"; }
                Status = failure.Length > 0 ? failure : status; Notify();
            };
            await active.Start(configuration.CaptureMode, requestAccess);
            if (disposed || epoch != generation || !active.Available) return;
            foreach (var rule in configuration.Rules)
            {
                if (disposed || epoch != generation) return;
                if (!rule.Enabled) { ruleStatus[rule.Id] = "替换已关闭"; continue; }
                var matches = Applications.Where(x => NotificationSoundRules.SameName(x.Name, rule.AppName) || x.Id == rule.AppName).ToArray();
                var id = rule.AppId.Length > 0 ? rule.AppId : matches.Length == 1 ? matches[0].Id : "";
                if (id.Length == 0) { ruleStatus[rule.Id] = "未确定Windows发送方，请从应用列表选择"; continue; }
                var error = await NotificationAudio.Validate(rule.AudioPath);
                if (disposed || epoch != generation) return;
                if (error is not null) { ruleStatus[rule.Id] = error + "；原通知音保留"; continue; }
                try { lease!.Acquire(id); ready[rule.Id] = id; ruleStatus[rule.Id] = "原通知音关闭设置已写入；横幅及应用声音保留，待实测"; }
                catch (Exception e) { ruleStatus[rule.Id] = "未启用替换：" + e.Message; }
                Notify();
            }
            Status = active.Status; Notify();
        }
        catch (Exception e) { StopCore(); Status = "监听未启用：" + e.Message; Notify(); }
        finally { changes.Release(); }
    }
    private bool EnsureLease()
    {
        try
        {
            if (!ownsLock)
            {
                soundLock ??= new Mutex(false, @"Local\Taskboard.NotificationSoundReplacement");
                try { ownsLock = soundLock.WaitOne(0); } catch (AbandonedMutexException) { ownsLock = true; }
                if (!ownsLock) { Status = "另一个即时任务看板正在管理通知声音，请先关闭它的监听或退出"; return false; }
            }
            lease ??= new NotificationSoundLease(journalFile, new WindowsNotificationSoundStore());
            var errors = lease.RestoreAll();
            if (errors.Length > 0) { failure = Status = "原通知声音恢复失败：" + string.Join("；", errors); return false; }
            failure = ""; return true;
        }
        catch (Exception e) { failure = Status = "自动声音设置不可用：" + e.Message; return false; }
    }
    internal void Receive(CapturedNotification notification)
    {
        if (disposed || !options().Enabled || NotificationSoundRules.IsSelf(notification.AppName)) return;
        if (!deduplicator.Accept(notification, DateTimeOffset.Now)) return;
        capturedCount++; Status = $"已捕获{capturedCount}条新通知 · 最近来源：{notification.Source}";
        if (!SeenApps.Any(x => NotificationSoundRules.SameName(x.Name, notification.AppName) && x.Id == notification.AppId))
        { if (SeenApps.Count >= 100) SeenApps.RemoveAt(0); SeenApps.Add(new(notification.AppName, notification.AppId)); }
        var rule = NotificationSoundRules.Resolve(options(), notification);
        if (rule is null) { AddHistory(notification, "未配置或替换已关闭"); return; }
        if (!ready.ContainsKey(rule.Id)) { AddHistory(notification, RuleStatus(rule.Id)); return; }
        if (notification.AppId.Length > 0 && !string.Equals(ready[rule.Id], notification.AppId, StringComparison.OrdinalIgnoreCase))
        { AddHistory(notification, "Windows发送方与配置不一致，本条未播放"); return; }
        if (pending.Count >= 16) { AddHistory(notification, "提醒队列已满，本条未播放"); return; }
        pending.Enqueue((rule.Id, notification)); AddHistory(notification, "已加入播放队列"); _ = Drain();
    }
    private void AddHistory(CapturedNotification n, string result)
    { History.Insert(0, new(DateTime.Now, n.AppName, n.Source, result)); if (History.Count > 20) History.RemoveAt(20); Notify(); }
    private async Task Drain()
    {
        if (playing) return; playing = true; var epoch = generation;
        try
        {
            while (!disposed && epoch == generation && pending.TryDequeue(out var item))
            {
                var rule = options().Rules.FirstOrDefault(x => x.Id == item.Rule && x.Enabled);
                if (!options().Enabled || rule is null || !ready.ContainsKey(item.Rule)) continue;
                PlaybackStatus = "正在播放：" + rule.AppName; Notify();
                var error = await audio.Play(rule.AudioPath, options().Volume);
                if (disposed || epoch != generation) return;
                PlaybackStatus = error ?? "播放完成：" + rule.AppName;
                if (error is not null && ready.Remove(rule.Id, out var id))
                {
                    try { lease?.Release(id); ruleStatus[rule.Id] = error + "；已恢复原通知音"; }
                    catch (Exception e) { failure = ruleStatus[rule.Id] = error + "；原声音恢复失败：" + e.Message; }
                }
                AddHistory(item.Notification, PlaybackStatus);
            }
        }
        finally { playing = false; if (!disposed && pending.Count > 0) _ = Drain(); }
    }
    public async Task Preview(string path)
    {
        if (playing) { PlaybackStatus = "通知音正在播放，请稍后试听"; Notify(); return; }
        playing = true;
        try { PlaybackStatus = "正在试听"; Notify(); PlaybackStatus = await audio.Play(path, options().Volume) ?? "试听完成"; }
        finally { playing = false; Notify(); if (pending.Count > 0) _ = Drain(); }
    }
    public void StopPlayback() { pending.Clear(); audio.Stop(); PlaybackStatus = "播放已停止"; Notify(); }
    private void StopCore()
    {
        capture?.Dispose(); capture = null; pending.Clear(); ready.Clear(); audio.Stop();
        var errors = lease?.RestoreAll() ?? [];
        failure = errors.Length > 0 ? "原声音恢复失败：" + string.Join("；", errors) : "";
        if (failure.Length > 0) Status = failure;
    }
    public void Suspend() { ++generation; StopCore(); Status = failure.Length > 0 ? failure : "监听已暂停，原通知声音已恢复"; Notify(); }
    public string[] RestoreOriginals() { Suspend(); return failure.Length == 0 ? [] : [failure]; }
    private void ReleaseLock() { if (ownsLock) { soundLock!.ReleaseMutex(); ownsLock = false; } }
    public void Dispose()
    {
        if (disposed) return; Suspend(); disposed = true; audio.Dispose();
        ReleaseLock(); soundLock?.Dispose();
    }
}
