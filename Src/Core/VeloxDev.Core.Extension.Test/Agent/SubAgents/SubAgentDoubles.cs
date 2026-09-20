using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI;
using VeloxDev.AI.SubAgents;
using VeloxDev.AI.Workflow;
using VeloxDev.AI.Workflow.Functions;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.SubAgents;

/// <summary>
/// A chat client that answers at once, so a spawned sub-agent reaches <c>Completed</c> without a wait.
/// </summary>
internal sealed class InstantChatClient(string answer = "done") : IChatClient
{
    private int _calls;

    /// <summary>How many turns were asked for.</summary>
    public int CallCount => Volatile.Read(ref _calls);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield return new ChatResponseUpdate(ChatRole.Assistant, answer);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

/// <summary>
/// A chat client that stops inside <c>GetResponseAsync</c> until the test lets it go.
/// <para>
/// The double the suite was missing: everything about dispatch-and-poll — several children running at
/// once, a wait that times out, a cancellation, a spawn that must return while the child keeps working —
/// needs a child that is reliably still running when the assertion is made.
/// </para>
/// </summary>
internal sealed class GateChatClient(string answer = "done") : IChatClient
{
    private readonly object _gate = new();
    private readonly List<TaskCompletionSource<bool>> _pending = [];
    private bool _open;
    private int _calls;

    /// <summary>How many turns have been asked for, including the ones still blocked.</summary>
    public int CallCount { get { lock (_gate) return _calls; } }

    /// <summary>How many turns are blocked right now.</summary>
    public int WaitingCount { get { lock (_gate) return _pending.Count; } }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<bool> wait;
        lock (_gate)
        {
            _calls++;
            if (_open) return new ChatResponse(new ChatMessage(ChatRole.Assistant, answer));
            wait = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Add(wait);
        }

        // Registered on the waiting side only, exactly as the tool wrapper does: a turn already under way
        // must not be abandoned half-done by a token that fires later.
        using (cancellationToken.Register(() => wait.TrySetCanceled(cancellationToken)))
            await wait.Task.ConfigureAwait(false);

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, answer));
    }

    /// <summary>Lets every blocked turn finish, and every later one stop blocking.</summary>
    public void ReleaseAll()
    {
        List<TaskCompletionSource<bool>> waiting;
        lock (_gate)
        {
            _open = true;
            waiting = [.. _pending];
            _pending.Clear();
        }
        foreach (var wait in waiting) wait.TrySetResult(true);
    }

    /// <summary>Blocks the test thread until at least <paramref name="count"/> turns have arrived.</summary>
    public void WaitForCalls(int count, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (CallCount < count)
        {
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException($"Only {CallCount} of {count} turn(s) arrived within {timeoutMs} ms.");
            Thread.Sleep(5);
        }
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield return new ChatResponseUpdate(ChatRole.Assistant, answer);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

/// <summary>
/// A chat client whose first turns ask for a tool and whose last answers, so a test can drive real child
/// calls through the agent loop and observe what they cost.
/// </summary>
internal sealed class ToolCallingChatClient(
    string toolName, int repeat = 1, string answer = "done", IDictionary<string, object?>? arguments = null) : IChatClient
{
    /// <summary>How many times the child asked for the tool, so a test can tell a loop from a one-shot.</summary>
    public int ToolRequests { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (ToolRequests >= repeat)
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));

        ToolRequests++;
        var call = new FunctionCallContent(
            $"call-{ToolRequests}", toolName,
            arguments is null ? null : new Dictionary<string, object?>(arguments));
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [call])));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield return new ChatResponseUpdate(ChatRole.Assistant, answer);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

/// <summary>
/// A chat client whose every turn fails, so a child reaches <c>Failed</c>.
/// <para>
/// The counterpart to <see cref="GateChatClient"/> plus a cancellation: between them every finish a row can
/// have is reachable without a model, which is what lets the panel's counters be tested at all.
/// </para>
/// </summary>
internal sealed class FaultingChatClient(string message = "the model is unreachable") : IChatClient
{
    private int _calls;

    /// <summary>How many turns were attempted — the failure is per turn, not once for the whole run.</summary>
    public int CallCount => Volatile.Read(ref _calls);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _calls);

        // Returned rather than thrown: the framework's own try is around the await, and a synchronous throw
        // would look to a caller like a broken double rather than a failing model.
        return Task.FromException<ChatResponse>(new InvalidOperationException(message));
    }

    /// <summary>
    /// Fails for a streaming caller too. The throw sits in front of the yield because an async iterator's
    /// body runs only once it is enumerated, which is where the failure belongs; the unreachable yield is
    /// what makes the body an iterator at all.
    /// </summary>
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        throw new InvalidOperationException(message);
#pragma warning disable CS0162 // unreachable by design — see the remarks above
        yield return new ChatResponseUpdate(ChatRole.Assistant, string.Empty);
#pragma warning restore CS0162
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

/// <summary>
/// A <see cref="SynchronizationContext"/> with a real pump of its own, so a test can tell marshalling that
/// works from marshalling that only appears to.
/// <para>
/// Deliberately not the blocking <c>Send</c> fake the thread-affinity tests use: tool bodies are marshalled
/// by posting and awaiting, and a context that only implements <c>Send</c> would deadlock on the very call
/// it is supposed to prove. This one queues, and a background thread drains the queue — which is what a
/// dispatcher does, and what makes "the UI thread stays free while a child runs" a testable claim.
/// </para>
/// </summary>
internal sealed class CountingUIContext : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly Thread _pump;
    private readonly ConcurrentQueue<int> _ranOn = new();

    public CountingUIContext()
    {
        _pump = new Thread(Drain) { IsBackground = true, Name = "counting-ui" };
        _pump.Start();
    }

    /// <summary>The thread every posted callback runs on.</summary>
    public int PumpThreadId => _pump.ManagedThreadId;

    /// <summary>
    /// How many callbacks have been posted. Counted at <see cref="Post"/> rather than at the drain, so a
    /// test can assert "nothing was posted" without waiting and without being raced by a callback that is
    /// still on its way through the pump.
    /// </summary>
    public int PostCount { get; private set; }

    /// <summary>The thread id each drained callback observed, for asserting nothing ran elsewhere.</summary>
    public IReadOnlyCollection<int> RanOnThreads => _ranOn.ToArray();

    private void Drain()
    {
        SynchronizationContext.SetSynchronizationContext(this);
        foreach (var (callback, state) in _queue.GetConsumingEnumerable())
        {
            _ranOn.Enqueue(Environment.CurrentManagedThreadId);
            try { callback(state); }
            catch { /* a marshalled body settles its own task; the pump outlives it */ }
        }
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        lock (_queue) PostCount++;
        _queue.Add((d, state));
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        if (Environment.CurrentManagedThreadId == _pump.ManagedThreadId)
        {
            d(state);
            return;
        }

        using var done = new ManualResetEventSlim();
        Exception? failure = null;
        Post(_ =>
        {
            try { d(state); }
            catch (Exception ex) { failure = ex; }
            finally { done.Set(); }
        }, null);

        done.Wait();
        if (failure is not null) throw failure;
    }

    public void Dispose() => _queue.CompleteAdding();
}

/// <summary>
/// A tree, a scope over it, and a sub-agent subsystem attached — the arrangement every test in this folder
/// starts from.
/// </summary>
internal sealed class SubAgentFixture : IAsyncDisposable
{
    public SubAgentFixture(
        IChatClient? client = null,
        Func<WorkflowAgentScope, AIAgent>? factory = null,
        SynchronizationContext? ui = null,
        int? maxToolCalls = null,
        int? maxDepth = null,
        int? spawnBudget = null)
    {
        Tree = new TreeDefaultViewModel();
        Tree.GetHelper().CreateNode(new NodeDefaultViewModel());

        Scope = new WorkflowAgentScope(Tree);
        if (maxToolCalls is { } cap) Scope.WithMaxToolCalls(cap);
        Scope.WithSynchronizationContext(ui);

        SubAgents = factory is not null
            ? new SubAgentScope(factory)
            : SubAgentScope.ForClient(client ?? new InstantChatClient());
        if (maxDepth is { } depth) SubAgents.WithSubAgentDepth(depth);
        if (spawnBudget is { } budget) SubAgents.WithSpawnBudget(budget);

        Scope.WithSubAgents(SubAgents);
    }

    public TreeDefaultViewModel Tree { get; }
    public WorkflowAgentScope Scope { get; }
    public SubAgentScope SubAgents { get; }

    /// <summary>The rows as they stand, read through the snapshot so a background child cannot race it.</summary>
    public IReadOnlyList<SubAgentSummary> Rows => SubAgents.Snapshot;

    /// <summary>
    /// The scope a spawned row stands for, reached the way the tree panel reaches it. This is what a test
    /// inspects to see what a child was actually given, as opposed to what it was told it was given.
    /// </summary>
    public WorkflowAgentScope ChildScope(string id) => SubAgents.SubAgentsOf(id)?.Parent
        ?? throw new InvalidOperationException($"No child scope for '{id}'.");

    /// <summary>The subsystem a spawned row may itself spawn with — the edge the tree panel walks.</summary>
    public SubAgentScope ChildSubAgents(string id) => SubAgents.SubAgentsOf(id)
        ?? throw new InvalidOperationException($"No child subsystem for '{id}'.");

    /// <summary>
    /// The subsystem of a descendant, reached by walking down from the root one row at a time.
    /// <para>
    /// A roster holds only its own direct children — deliberately, so one branch cannot enumerate another —
    /// so a grandchild is reachable only through the row that dispatched it. That is also the walk the tree
    /// panel makes, which is why the fixture does it here rather than holding a flat handle table a panel
    /// could never have.
    /// </para>
    /// </summary>
    public SubAgentScope DescendantSubAgents(params string[] path)
    {
        var scope = SubAgents;
        foreach (var id in path)
            scope = scope.SubAgentsOf(id)
                ?? throw new InvalidOperationException($"No child subsystem for '{id}' along that path.");
        return scope;
    }

    /// <summary>The workflow scope a descendant stands for, reached the same way.</summary>
    public WorkflowAgentScope DescendantScope(params string[] path)
        => DescendantSubAgents(path).Parent
            ?? throw new InvalidOperationException("The subsystem along that path was never attached to a scope.");

    /// <summary>One row from the immutable snapshot — the thread-safe half of the roster.</summary>
    public SubAgentSummary RowOf(string id) => Rows.FirstOrDefault(r => r.Id == id)
        ?? throw new InvalidOperationException($"No row '{id}'.");

    /// <summary>
    /// One row as the panel holds it. Only for the spawn-time fields the snapshot reduces — the granted
    /// names — which a test asserts on; anything thread-sensitive goes through <see cref="RowOf"/>.
    /// </summary>
    public SubAgentStatusViewModel RowVm(string id) => SubAgents.Children.FirstOrDefault(r => r.Id == id)
        ?? throw new InvalidOperationException($"No row '{id}'.");

    /// <summary>
    /// The workflow tool surface a scope actually offers, reached the way the model reaches it rather than
    /// rebuilt by hand. Comparing this with what a spawn reported is what turns "capability narrowing" from
    /// a claim in a row into a fact about the child.
    /// </summary>
    public static HashSet<string> SurfaceOf(WorkflowAgentScope scope)
        => [.. scope.ProvideTools().Select(t => t.Name)];

    /// <summary>The sub-agent tools a scope's providers contribute, which the workflow toolkit never holds.</summary>
    public static HashSet<string> SubAgentSurfaceOf(WorkflowAgentScope scope)
        => [.. scope.CreateContextProviders()
            .OfType<SubAgentAgentContextProvider>()
            .SelectMany(p => p.BuildContext().Tools ?? [])
            .Select(t => t.Name)];

    /// <summary>The tools the sub-agent subsystem contributes to the host — the five it manages children with.</summary>
    public IEnumerable<AITool> Tools
        => Scope.CreateContextProviders().OfType<SubAgentAgentContextProvider>().Single().BuildContext().Tools!;

    /// <summary>Invokes one of the sub-agent tools and returns its text result.</summary>
    public string Invoke(string toolName, params (string Name, object? Value)[] args)
    {
        var tool = Tools.OfType<AIFunction>()
            .FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Tool '{toolName}' was not contributed.");

        return InvokeTool(tool, args);
    }

    /// <summary>
    /// Invokes one tool of a scope's own surface, reached the way the model reaches it — wrapped, gated by
    /// that scope's budget, and counted against its ledger.
    /// </summary>
    public static string InvokeOn(WorkflowAgentScope scope, string toolName, params (string Name, object? Value)[] args)
    {
        var tool = scope.ProvideTools().OfType<AIFunction>()
            .FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Tool '{toolName}' is not on that scope's surface.");

        return InvokeTool(tool, args);
    }

    /// <summary>Invokes a tool directly, bypassing the surface filter the scope applies.</summary>
    public static string InvokeTool(AIFunction tool, params (string Name, object? Value)[] args)
    {
        var aiArgs = new AIFunctionArguments();
        foreach (var (name, value) in args)
            if (value is not null) aiArgs[name] = value;

        var result = tool.InvokeAsync(aiArgs, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return result switch
        {
            string text => text,
            JsonElement element => element.GetString() ?? string.Empty,
            _ => result?.ToString() ?? string.Empty,
        };
    }

    /// <summary>Finds a tool by name among those a toolkit can offer, whether or not the switch is on.</summary>
    public static AIFunction ToolOn(WorkflowAgentToolkit toolkit, string toolName)
        => (AIFunction?)toolkit.CreateAllTools()
            .FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Tool '{toolName}' is not in that toolkit.");

    /// <summary>
    /// One of the sub-agent tools as a given scope contributes it. Used to ask a <i>child</i> what it can
    /// see, which is the only way to show that a child's view of the roster does not include its siblings.
    /// </summary>
    public static AIFunction SubAgentToolOf(WorkflowAgentScope scope, string toolName)
        => (AIFunction?)scope.CreateContextProviders()
            .OfType<SubAgentAgentContextProvider>().Single().BuildContext().Tools!
            .FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"'{toolName}' was not contributed to that scope.");

    /// <summary>Dispatches a child and returns its id, failing loudly on a refusal.</summary>
    public string Spawn(string task, params (string Name, object? Value)[] args)
    {
        var reply = JObject.Parse(Invoke("SpawnSubAgent", [("task", task), .. args]));
        if ((string?)reply["status"] != "ok")
            throw new InvalidOperationException($"The spawn was refused: {reply["message"]}");
        return (string)reply["id"]!;
    }

    /// <summary>Waits until the predicate holds, so a background child can settle.</summary>
    public static void WaitFor(Func<bool> predicate, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!predicate())
        {
            if (Environment.TickCount64 > deadline) throw new TimeoutException($"The condition was not met within {timeoutMs} ms.");
            Thread.Sleep(5);
        }
    }

    public async ValueTask DisposeAsync() => await SubAgents.DisposeAsync().ConfigureAwait(false);
}
