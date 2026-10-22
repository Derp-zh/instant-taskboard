// Runs OUR fixed read projection on isolated model objects. Never imports or
// executes installed Codex code, opens a debug endpoint, or reads real chats.
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import assert from 'node:assert/strict';
const output = path.resolve(process.argv[2] || '');
assert.ok(output.startsWith(path.resolve('test-output') + path.sep));
assert.ok(!fs.existsSync(output));
const source = fs.readFileSync('src/Taskboard.Desktop/CodexPendingBridge.cs', 'utf8');
const body = source.split('internal const string Projection = """')[1].split('""";')[0];
const project = vm.runInNewContext('(' + body + ')');
const checks = [];
function check(value, name) { assert.ok(value, name); checks.push(name); }
let state;
const conversation = { id: 'A' };
Object.defineProperty(conversation, 'text', { get() { throw Error('Must not read chat prose'); } });
const manager = { getHostId: () => 'local', getConversation: id => id === 'A' ? conversation : null,
  constructor: { name: 'Hfn' },
  disposed: false,
  threadStore: { stateReaders: { getListState: () => state } } };
const reset = () => state = { latestTurnId: 't', latestTurnStatus: 'inProgress', requests: [],
  pendingRequest: null, unansweredAsyncQuestions: null, isResponseInProgress: true };
const read = (objects = [manager]) => project.call(objects, 'A');
reset();
check(read().pending.length === 0 && read().executing, 'empty structured index and execution retained without reading prose');
state.unansweredAsyncQuestions = { turnId: 't', questionIds: ['q1', 'q2'] };
check(read().pending.length === 2 && read().pending.every(p => !p.approval), 'multiple async question IDs retained');
state.requests.push({ method: 'item/permissions/requestApproval', id: 7, params: { threadId: 'A', turnId: 't' } });
check(read().pending.some(p => p.id === 'rpc:7' && p.approval), 'permissions keep their actual request identity');
state.requests.push({ method: 'item/tool/requestUserInput', id: '7', params: { threadId: 'A', turnId: 't' } });
check(read().pending.some(p => p.id === 'rpc:"7"' && !p.approval), 'string and numeric request IDs cannot collide');
state.requests[0].completed = true;
check(!read().pending.some(p => p.id === 'rpc:7'), 'completed approval omitted from pending');
state.unansweredAsyncQuestions.turnId = 'old';
check(read() === null, 'old async turn makes projection inconclusive');
reset(); state.requests = [{ method: 'item/commandExecution/requestApproval', id: 1, params: { threadId: 'B', turnId: 't' } }];
check(read() === null, 'request for another thread rejected');
reset(); state.pendingRequest = { kind: 'unknown-kind' };
check(read() === null, 'unrepresented unknown blocking prompt cannot be called running');
reset(); state.pendingRequest = { kind: 'userInput', userInputRequestId: 'blocking' };
check(read().pending[0].id === 'rpc:"blocking"', 'blocking user input uses its actual indexed ID');
reset(); state.requests = [{ method: 'item/tool/call', id: 'tool', params: { threadId: 'A', turnId: 't' } }];
check(read().pending.length === 0, 'ordinary running tool request is not a user approval');
reset(); state.latestTurnStatus = 'failed';
check(read() === null, 'terminal turn cannot be used as recovery');
reset(); check(read([manager, manager]) === null, 'ambiguous managers rejected');
manager.disposed = true; check(read() === null, 'disposed manager cannot replay cached pending after host replacement'); manager.disposed = false;
check(read([]) === null, 'missing manager rejected without hydrating a thread');
reset(); state.unansweredAsyncQuestions = { turnId: 't', questionIds: Array.from({ length: 130 }, (_, i) => 'q' + i) };
check(read().overflow && read().pending.length === 128, 'overflow explicitly protected and bounded');
reset(); state.isResponseInProgress = false;
check(!read().executing, 'answer acknowledgement alone does not claim execution');
const upgraded = vm.runInNewContext('(' + body.replaceAll("'Hfn'", "'imn'") + ')');
manager.constructor.name = 'imn'; reset();
state.unansweredAsyncQuestions = { turnId: 't', questionIds: ['new-async-question'] };
state.requests = [{ method: 'item/permissions/requestApproval', id: 'new-approval', params: { threadId: 'A', turnId: 't' } }];
let latest = upgraded.call([manager], 'A');
check(latest.pending.length === 2 && latest.pending.some(p => p.approval), 'new manager retains async and approval IDs together');
state.unansweredAsyncQuestions = null; state.requests = []; state.isResponseInProgress = false;
check(!upgraded.call([manager], 'A').executing, 'new manager answer or approval alone does not signal recovery');
state.isResponseInProgress = true;
check(upgraded.call([manager], 'A').executing, 'new manager actual execution permits recovery after empty requests');
state.requests = [{ method: 'item/tool/requestOptionPicker', id: 'picker', params: { threadId: 'A', turnId: 't' } }];
check(upgraded.call([manager], 'A').pending[0].id === 'rpc:"picker"', 'new option picker retains its real request ID');
reset(); state.pendingRequest = { kind: 'setupCodexStep' };
check(upgraded.call([manager], 'A') === null, 'unknown indexed blocking kind remains inconclusive');
reset(); delete state.isResponseInProgress;
check(upgraded.call([manager], 'A') === null, 'missing execution boolean cannot produce a complete empty request snapshot');
fs.mkdirSync(output, { recursive: true });
fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify({ passed: checks.length, checks,
  syntheticFixture: true, installedCodeExecuted: false, desktopLifecycleVerified: false }, null, 2));
console.log(JSON.stringify({ passed: checks.length, syntheticFixture: true, desktopLifecycleVerified: false }));
