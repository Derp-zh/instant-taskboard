using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;

// Audits the ORIGINAL published assembly. The fixture is deliberately synthetic;
// its results cannot establish that the user's desktop service is connected.
internal static class Program
{
    private const string FixtureVariable = "INSTANT_TASKBOARD_CODEX_AUDIT_FIXTURE";
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static Action<object, JsonElement, long, string, bool>? receive;
    private static void Capture(object watch, JsonElement message, long sequence, string epoch, bool authoritative)
        => receive?.Invoke(watch, message, sequence, epoch, authoritative);

    private static object? Property(object value, string name) => value.GetType().GetProperty(name, Members)!.GetValue(value);
    private static void Set(object value, string name, object? data) => value.GetType().GetProperty(name, Members)!.SetValue(value, data);
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    public static async Task<int> Main(string[] args)
    {
        Console.InputEncoding = new UTF8Encoding(false);
        Console.OutputEncoding = new UTF8Encoding(false);
        if (args.Contains("app-server")) return await Fixture();
        var assemblyIndex = Array.IndexOf(args, "--assembly");
        var dataIndex = Array.IndexOf(args, "--data-dir");
        if (assemblyIndex < 0 || dataIndex < 0) throw new ArgumentException("--assembly and --data-dir required");
        var assemblyPath = Path.GetFullPath(args[assemblyIndex + 1]);
        var output = Path.GetFullPath(args[dataIndex + 1]);
        var isolatedRoot = Path.Combine(Environment.CurrentDirectory, "test-output") + Path.DirectorySeparatorChar;
        if (!output.StartsWith(isolatedRoot, StringComparison.OrdinalIgnoreCase) || Directory.Exists(output))
            throw new ArgumentException("Use a NEW output directory under test-output");
        Directory.CreateDirectory(output);
        var publishedDirectory = Path.GetDirectoryName(assemblyPath)!;
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            var candidate = Path.Combine(publishedDirectory, name.Name + ".dll");
            return File.Exists(candidate) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate) : null;
        };
        var desktop = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
        var core = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(publishedDirectory, "Taskboard.Core.dll"));
        var observerType = desktop.GetType("Taskboard.Desktop.CodexObserver", true)!;
        var watchType = desktop.GetType("Taskboard.Desktop.CodexWatch", true)!;
        var workItemType = core.GetType("Taskboard.Core.WorkItem", true)!;
        var bindingType = core.GetType("Taskboard.Core.CodexBinding", true)!;
        var apply = core.GetType("Taskboard.Core.CodexState", true)!.GetMethods().Single(x => x.Name == "Apply" && x.GetParameters().Length == 7);

        var task = Activator.CreateInstance(workItemType)!;
        Set(task, "Lane", Enum.Parse(core.GetType("Taskboard.Core.Lane", true)!, "Running"));
        var stage = ((System.Collections.IEnumerable)Property(task, "Stages")!).Cast<object>().First();
        Set(stage, "IsRunning", true);
        var binding = Activator.CreateInstance(bindingType)!;
        Set(binding, "ThreadId", "audit-thread"); Set(stage, "Codex", binding);
        var watch = Activator.CreateInstance(watchType, Property(task, "Id"), Property(stage, "Id"), Property(binding, "BindingId"), "audit-thread", 1)!;
        var watchArray = Array.CreateInstance(watchType, 1); watchArray.SetValue(watch, 0);
        var observer = Activator.CreateInstance(observerType, true)!;
        var observations = new ConcurrentQueue<object>();
        // Subscribe to the actual packaged observer, then invoke the packaged Core.
        // No reconstructed fixture record is used to drive the tested task.
        receive = (receivedWatch, message, sequence, epoch, authoritative) =>
        {
            lock (task)
            {
                apply.Invoke(null, [task, Property(receivedWatch, "StageId"), message, sequence,
                    Property(receivedWatch, "Generation"), epoch, authoritative]);
                var parameters = message.TryGetProperty("params", out var parsed) ? parsed : default;
                var status = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("status", out var state) ? state : Json(new { type = "unavailable" });
                observations.Enqueue(new { sequence, method = message.GetProperty("method").GetString(), status = status.Clone(),
                    phase = Property(binding, "Phase")!.ToString(), lane = Property(task, "Lane")!.ToString() });
            }
        };
        var eventInfo = observerType.GetEvent("Event", Members)!;
        var handlerType = eventInfo.EventHandlerType!;
        var handlerParameters = handlerType.GetMethod("Invoke")!.GetParameters().Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
        var capture = typeof(Program).GetMethod(nameof(Capture), Members)!;
        var handler = Expression.Lambda(handlerType, Expression.Call(capture,
            Expression.Convert(handlerParameters[0], typeof(object)), handlerParameters[1], handlerParameters[2], handlerParameters[3], handlerParameters[4]), handlerParameters).Compile();
        eventInfo.GetAddMethod(true)!.Invoke(observer, [handler]);
        var fixturePath = Path.Combine(output, "fixture-requests.jsonl");
        Environment.SetEnvironmentVariable(FixtureVariable, fixturePath);
        observerType.GetMethod("Watch", Members)!.Invoke(observer, [watchArray]);
        observerType.GetMethod("Start", Members)!.Invoke(observer, [Environment.ProcessPath!, ""]);
        try
        {
            await Task.Delay(7000);
        }
        finally
        {
            var dispose = (ValueTask)observerType.GetMethod("DisposeAsync", Members)!.Invoke(observer, null)!;
            await dispose;
            receive = null;
            Environment.SetEnvironmentVariable(FixtureVariable, null);
        }
        var requests = File.Exists(fixturePath) ? File.ReadAllLines(fixturePath).Select(x => JsonDocument.Parse(x).RootElement.Clone()).ToArray() : [];
        var turnReads = requests.Count(x => x.GetProperty("method").GetString() == "thread/turns/list");
        var report = new { targetVersion = desktop.GetName().Version!.ToString(), syntheticFixture = true,
            originalAssemblyUsed = true, originalObserverEventsUsed = true, originalObserverStatus = Property(observer, "Status"),
            observations, turnHistoryReadCount = turnReads, finalLane = Property(task, "Lane")!.ToString(),
            finalPhase = Property(binding, "Phase")!.ToString(),
            waitingSnapshotRecognized = observations.Any(x => Json(x).GetProperty("lane").GetString() == "Action"),
            completionPollingGapReproduced = turnReads == 0 && Property(task, "Lane")!.ToString() == "Running",
            desktopLifecycleVerified = false, formalDataOpened = false, mouseOrKeyboardInjected = false };
        File.WriteAllText(Path.Combine(output, "audit-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(report));
        return report.waitingSnapshotRecognized && report.completionPollingGapReproduced ? 0 : 1;
    }

    private static async Task<int> Fixture()
    {
        var path = Environment.GetEnvironmentVariable(FixtureVariable);
        if (path is null) return 2;
        var clock = Stopwatch.StartNew(); long sequence = 0;
        string? line;
        while ((line = await Console.In.ReadLineAsync()) is not null)
        {
            using var doc = JsonDocument.Parse(line); var request = doc.RootElement;
            var method = request.GetProperty("method").GetString();
            var ms = clock.ElapsedMilliseconds;
            var status = ms < 1000 ? Json(new { type = "active", activeFlags = Array.Empty<string>() }) :
                ms < 2300 ? Json(new { type = "active", activeFlags = new[] { "waitingOnUserInput" } }) :
                ms < 3400 ? Json(new { type = "active", activeFlags = Array.Empty<string>() }) : Json(new { type = "idle" });
            File.AppendAllText(path, JsonSerializer.Serialize(new { sequence = ++sequence, method, status }) + "\n");
            if (!request.TryGetProperty("id", out var id)) continue;
            object result = method switch
            {
                "initialize" => new { userAgent = "isolated audit fixture" },
                "thread/read" => new { thread = new { id = "audit-thread", status } },
                "thread/turns/list" => new { data = new[] { new { id = "audit-turn", status = "completed" } } },
                _ => new { data = Array.Empty<string>() }
            };
            Console.WriteLine(JsonSerializer.Serialize(new { id, result })); await Console.Out.FlushAsync();
        }
        return 0;
    }
}
