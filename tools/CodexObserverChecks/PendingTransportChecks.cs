using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Taskboard.Desktop;

internal static class PendingTransportChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        var prior = Environment.GetEnvironmentVariable(CodexPendingBridge.Setting);
        await using var fixture = new InspectorFixture();
        Environment.SetEnvironmentVariable(CodexPendingBridge.Setting, fixture.Endpoint);
        try
        {
            await using (var rejected = new CodexPendingBridge())
            {
                check(await rejected.Read("transport-fixture", Environment.ProcessId + 1, CancellationToken.None) is null &&
                    rejected.Reason == "inspector-peer-mismatch" && fixture.Connections == 0,
                    "TCP监听进程不匹配时在任何HTTP请求或脚本查询前拒绝");
            }
            await using var bridge = new CodexPendingBridge();
            var snapshot = await bridge.Read("transport-fixture", Environment.ProcessId, CancellationToken.None);
            check(snapshot is { Executing: true, Requests.Length: 1 } && snapshot.Requests[0].Id == "question:transport",
                "受控CDP传输完整执行发现、固定原型查询、对象定位及只读投影");
            var queries = fixture.Queries;
            snapshot = await bridge.Read("transport-fixture", Environment.ProcessId, CancellationToken.None);
            check(snapshot is not null && fixture.Queries == queries && queries == 1, "连续采样复用经理对象，仅首次queryObjects避免每轮触发GC");
            check(fixture.Evaluations.All(x => x.Contains(CodexPendingBridge.BaseModuleName) && x.Contains("m.Dot?.prototype")) &&
                fixture.Functions.All(x => x == CodexPendingBridge.Projection), "协议只发送固定进程/版本核验和固定投影，不接受外部脚本");
            fixture.FailProjection = true;
            check(await bridge.Read("transport-fixture", Environment.ProcessId, CancellationToken.None) is null && bridge.Reason == "pending-source-unavailable",
                "CDP异常导致观察源不可用，不产生空pending或恢复信号");
            fixture.FailProjection = false; fixture.HashMatches = false;
            var evaluationsBefore = fixture.Evaluations.Count;
            await using var incompatible = new CodexPendingBridge();
            check(await incompatible.Read("transport-fixture", Environment.ProcessId, CancellationToken.None) is null &&
                incompatible.Reason == "unsupported-desktop-module" && fixture.Evaluations.Count == evaluationsBefore,
                "源码哈希不符时在导入模块或查询应用对象前拒绝");
            foreach (var forbidden in new[] { "Debugger.pause", "Debugger.setBreakpoint", "Debugger.setScriptSource", "Runtime.terminateExecution", "Runtime.getProperties", "Page.reload" })
                check(!CodexPendingBridge.Allowed(forbidden), "观察桥拒绝暂停、改源码、任意作用域枚举等操作：" + forbidden);
        }
        finally { Environment.SetEnvironmentVariable(CodexPendingBridge.Setting, prior); }
    }

    // Plain owned TCP, not HTTP.sys: socket ownership is genuinely this test
    // process. The mock returns protocol fixtures; it never executes expressions.
    private sealed class InspectorFixture : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource lifetime = new();
        private readonly Task worker;
        private readonly List<Task> clients = [];
        internal string Endpoint { get; }
        internal int Connections, Queries;
        internal bool FailProjection, HashMatches = true;
        internal readonly List<string> Evaluations = [], Functions = [];
        internal InspectorFixture()
        {
            listener.Start(); Endpoint = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
            worker = Task.Run(Accept);
        }
        private async Task Accept()
        {
            try
            {
                while (!lifetime.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(lifetime.Token);
                    Interlocked.Increment(ref Connections); clients.Add(Handle(client));
                }
            }
            catch (OperationCanceledException) { }
        }
        private async Task Handle(TcpClient client)
        {
            using (client)
            {
                var stream = client.GetStream(); var bytes = new List<byte>(); var single = new byte[1];
                while (bytes.Count < 8192)
                {
                    if (await stream.ReadAsync(single.AsMemory(), lifetime.Token) == 0) return;
                    bytes.Add(single[0]);
                    if (bytes.Count >= 4 && Encoding.ASCII.GetString(bytes.TakeLast(4).ToArray()) == "\r\n\r\n") break;
                }
                var header = Encoding.ASCII.GetString(bytes.ToArray());
                if (header.StartsWith("GET /json/list ", StringComparison.Ordinal))
                {
                    var body = JsonSerializer.Serialize(new[] {
                        new { type = "page", url = "app://-/index.html?initialRoute=%2Favatar-overlay", webSocketDebuggerUrl = Endpoint.Replace("http://", "ws://") + "/must-not-select-avatar" },
                        new { type = "page", url = "app://-/index.html", webSocketDebuggerUrl = Endpoint.Replace("http://", "ws://") + "/fixture" } });
                    var response = Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Type: application/json\r\nContent-Length: " + Encoding.UTF8.GetByteCount(body) + "\r\n\r\n" + body);
                    await stream.WriteAsync(response.AsMemory(), lifetime.Token); return;
                }
                if (!header.StartsWith("GET /fixture ", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected fixture path");
                var key = header.Split("\r\n").Single(x => x.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase)).Split(':', 2)[1].Trim();
                var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n"), lifetime.Token);
                using var socket = WebSocket.CreateFromStream(stream, true, null, TimeSpan.FromSeconds(30));
                try
                {
                    while (!lifetime.IsCancellationRequested)
                    {
                        using var message = new MemoryStream(); var buffer = new byte[8192]; ValueWebSocketReceiveResult part;
                        do
                        {
                            part = await socket.ReceiveAsync(buffer.AsMemory(), lifetime.Token);
                            if (part.MessageType != WebSocketMessageType.Text) return;
                            message.Write(buffer, 0, part.Count);
                        } while (!part.EndOfMessage);
                        using var document = JsonDocument.Parse(message.ToArray()); var rpc = document.RootElement; var parameters = rpc.GetProperty("params");
                        var id = rpc.GetProperty("id").GetInt32(); var method = rpc.GetProperty("method").GetString();
                        object result;
                        if (method == "Debugger.enable")
                        {
                            await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { method = "Debugger.scriptParsed", @params = new {
                                url = "app://-/assets/" + CodexPendingBridge.ModuleName, hash = HashMatches ? CodexPendingBridge.ModuleHash : "wrong-version", isModule = true, scriptId = "fixture-module" } }).AsMemory(), WebSocketMessageType.Text, true, lifetime.Token);
                            await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { method = "Debugger.scriptParsed", @params = new {
                                url = "app://-/assets/" + CodexPendingBridge.BaseModuleName, hash = CodexPendingBridge.BaseModuleHash, isModule = true, scriptId = "fixture-base-module" } }).AsMemory(), WebSocketMessageType.Text, true, lifetime.Token);
                            result = new { };
                        }
                        else if (method == "Debugger.disable") result = new { };
                        else if (method == "Runtime.evaluate")
                        {
                            Evaluations.Add(parameters.GetProperty("expression").GetString()!);
                            result = new { result = new { type = "object", objectId = "prototype" } };
                        }
                        else if (method == "Runtime.queryObjects")
                        { Queries++; result = new { objects = new { type = "object", objectId = "managers" } }; }
                        else if (method == "Runtime.callFunctionOn")
                        {
                            Functions.Add(parameters.GetProperty("functionDeclaration").GetString()!);
                            if (parameters.GetProperty("arguments")[0].GetProperty("value").GetString() != "transport-fixture") throw new InvalidOperationException("Wrong thread");
                            result = FailProjection ? new { exceptionDetails = new { text = "fixture error" } } : (object)new {
                                result = new { type = "object", value = new { schemaVersion = 1, threadId = "transport-fixture", hostId = "local", turnId = "transport-turn",
                                    status = "inProgress", executing = true, complete = true, overflow = false, pending = new[] { new { id = "question:transport", approval = false } } } } };
                        }
                        else throw new InvalidOperationException("Non-read method sent");
                        // Notifications have no id; the client must not mistake
                        // them for responses or abort a healthy connection.
                        await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { method = "fixture/notice" }).AsMemory(), WebSocketMessageType.Text, true, lifetime.Token);
                        await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { id, result }).AsMemory(), WebSocketMessageType.Text, true, lifetime.Token);
                    }
                }
                catch (WebSocketException) { }
                catch (OperationCanceledException) { }
            }
        }
        public async ValueTask DisposeAsync()
        {
            lifetime.Cancel(); listener.Stop(); await worker;
            await Task.WhenAll(clients); lifetime.Dispose();
        }
    }
}
