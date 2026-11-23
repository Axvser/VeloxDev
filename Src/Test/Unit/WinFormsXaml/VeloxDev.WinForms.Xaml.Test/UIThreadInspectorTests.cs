using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using VeloxDev.Threading;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The WinForms host must treat a dead dispatch queue as a refusal, never as an exception: a throw from
/// <c>WindowsFormsSynchronizationContext.Post</c> escapes into whatever continuation called it, and an unhandled
/// exception there takes the process down.
/// </summary>
[STATestClass]
public sealed class UIThreadInspectorTests
{
    // 捕获状态是进程级的；每个用例从「未捕获」开始，前一个用例留下的上下文不会替它接住投递。
    private static readonly FieldInfo SyncContextField = InspectorField("_uiSyncContext");
    private static readonly FieldInfo ThreadIdField = InspectorField("_uiThreadId");
    private static readonly FieldInfo AppAliveField = InspectorField("_isAppAlive");

    private static FieldInfo InspectorField(string name)
        => typeof(UIThreadInspector).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{name} moved; update UIThreadInspectorTests with it.");

    [TestInitialize]
    public void ResetInspectorState()
    {
        SyncContextField.SetValue(null, null);
        ThreadIdField.SetValue(null, -1);
        AppAliveField.SetValue(null, true);
    }

    [TestCleanup]
    public void RestoreInspectorState() => ResetInspectorState();

    private sealed class TestHost : UIThreadInspector
    {
        public bool TryPost(object target, ThreadRef thread, Action action, NonPriority priority)
            => PostCore(target, thread, action, priority);
    }

    // 模拟失效的 WindowsFormsSynchronizationContext：编组窗口没有句柄时 Post 抛的就是这个异常。
    private sealed class RefusingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
            => throw new InvalidOperationException(
                "Cannot call Invoke or BeginInvoke on a control until the window handle has been created.");
    }

    private sealed class InlineSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);
    }

    private sealed class CountingSynchronizationContext : SynchronizationContext
    {
        private int _posted;

        public int Posted => Volatile.Read(ref _posted);

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _posted);
            d(state);
        }
    }

    // 一个永远拒收的宿主：验证基类的 PostAsync / Run 不会停在「永远等不到」的完成源上。
    private sealed class RefusingHost : TransitionHostBase<NonPriority>
    {
        public override ThreadRef ThreadFor(object target) => ThreadRef.None;

        protected override bool IsCurrentThread(ThreadRef thread) => false;

        protected override bool PostCore(object target, ThreadRef thread, Action action, NonPriority priority) => false;
    }

    [TestMethod]
    public void PostCore_ReturnsFalse_WhenThePinnedContextCannotPost()
    {
        var host = new TestHost();
        using var target = new Control(); // 有对象、没有窗口句柄
        var ran = false;

        var accepted = host.TryPost(
            target,
            ThreadRef.From(new RefusingSynchronizationContext()),
            () => ran = true,
            default);

        Assert.IsFalse(accepted, "a refused post must report false, not throw through the caller");
        Assert.IsFalse(ran);
    }

    [TestMethod]
    public void Post_ReturnsFalse_WhenThePinnedContextCannotPost()
    {
        var host = new TestHost();
        using var target = new Control();

        var accepted = host.Post(
            target,
            ThreadRef.From(new RefusingSynchronizationContext()),
            static () => { },
            default);

        Assert.IsFalse(accepted);
    }

    [TestMethod]
    public void PostCore_UsesALiveOpenForm_ForATargetWithoutItsOwnWindow()
    {
        // 非控件目标（例如 WorkflowFlowClock）没有自己的线程；活着的窗体是唯一能把它写回 UI 线程的通道。
        using var form = new Form { ShowInTaskbar = false, Size = new Size(120, 80) };
        form.Show();

        try
        {
            var host = new TestHost();
            var ran = 0;

            var accepted = host.TryPost(
                new object(),
                ThreadRef.From(new RefusingSynchronizationContext()),
                () => Interlocked.Increment(ref ran),
                default);

            Assert.IsTrue(accepted, "a live form must be preferred over the dead context");
            Assert.IsTrue(XamlTestSurface.PumpUntil(() => Volatile.Read(ref ran) == 1),
                "the action must arrive through the form's message queue");
        }
        finally
        {
            form.Close();
        }
    }

    [TestMethod]
    public void PostCore_DoesNotSwallowAnExceptionThrownByTheAction()
    {
        var host = new TestHost();
        using var target = new Control();
        var failure = new InvalidOperationException("application failure");

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(() =>
            host.TryPost(
                target,
                ThreadRef.From(new InlineSynchronizationContext()),
                () => throw failure,
                default));

        Assert.AreSame(failure, thrown, "an action that throws is not a refused post");
    }

    [TestMethod]
    public void PostCore_RefusesOnceTheApplicationHasExited()
    {
        var host = new TestHost();
        using var target = new Control();
        var context = new CountingSynchronizationContext();
        var ran = false;

        // ApplicationExit 没有公共触发入口；直接按住它写下的那个标志。
        var original = (bool)AppAliveField.GetValue(null)!;

        try
        {
            AppAliveField.SetValue(null, false);

            var accepted = host.TryPost(target, ThreadRef.From(context), () => ran = true, default);

            Assert.IsFalse(accepted, "a post after ApplicationExit never runs, so it must be refused");
            Assert.IsFalse(ran);
            Assert.AreEqual(0, context.Posted);
        }
        finally
        {
            AppAliveField.SetValue(null, original);
        }
    }

    [TestMethod]
    public async Task PostAsyncAndRun_CompleteWhenTheHostRefuses()
    {
        var host = new RefusingHost();

        var post = host.PostAsync(new object(), static () => Assert.Fail("a refused action must not run"), default);

        Assert.IsTrue(post.Wait(TimeSpan.FromSeconds(2)), "a refused post must complete instead of hanging");
        Assert.IsFalse(await post);
        Assert.AreEqual(0, host.Run(new object(), static () => 7), "Run reports refusal as default");
    }
}
