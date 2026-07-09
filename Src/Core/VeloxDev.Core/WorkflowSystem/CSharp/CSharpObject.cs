using System.Collections.ObjectModel;
using System.Diagnostics;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem.CSharp;

[WorkflowBuilder.Node<CSharpObjectHelper>]
public partial class CSharpObject
{
    private static readonly object RegistryGate = new();
    private static readonly List<ICSharpObjectValueConverter> ValueConverters = [];
    private static readonly List<ICSharpObjectMemberProvider> MemberProviders = [];
    private static readonly List<ICSharpObjectActivator> Activators = [];
    private static readonly List<ICSharpObjectMethodConnectionValidator>
        MethodConnectionValidators = [];

    private object? host;
    private object? lastResult;
    private string? lastError;
    private CSharpObjectExecutionState executionState;
    private int executionCount;
    private long lastDurationMilliseconds;
    private string lastExecutionSummary = string.Empty;

    [VeloxProperty] private string hostType = string.Empty;
    [VeloxProperty] private string selectedMethod = string.Empty;
    [VeloxProperty] private ObservableCollection<ValueMember> values = null!;
    [VeloxProperty] private ObservableCollection<CollectionMember> collections = null!;
    [VeloxProperty] private ObservableCollection<MethodMember> methods = null!;
    [VeloxProperty] private SlotViewModelBase? inputSlot;
    [VeloxProperty] private SlotViewModelBase? outputSlot;

    public CSharpObject()
    {
        Values = [];
        Collections = [];
        Methods = [];
        InitializeWorkflow();
        UpdateMethodSlots();
    }

    /// <summary>The current runtime host. This get-only property is not serialized.</summary>
    public object? Host => host;

    /// <summary>The result produced by the most recent selected-method invocation.</summary>
    public object? LastResult => lastResult;

    /// <summary>The most recent host refresh or execution error.</summary>
    public string? LastError => lastError;

    public CSharpObjectExecutionState ExecutionState => executionState;

    public int ExecutionCount => executionCount;

    public long LastDurationMilliseconds => lastDurationMilliseconds;

    public string LastExecutionSummary => lastExecutionSummary;

    /// <summary>The selected method configuration resolved from <see cref="SelectedMethod"/>.</summary>
    public MethodMember? SelectedMethodMember => Methods.FirstOrDefault(method =>
        string.Equals(method.Signature, SelectedMethod, StringComparison.Ordinal));

    public static IDisposable RegisterValueConverter(ICSharpObjectValueConverter converter)
    {
        if (converter is null) throw new ArgumentNullException(nameof(converter));

        lock (RegistryGate)
        {
            ValueConverters.Insert(0, converter);
        }

        return new CSharpObjectRegistration(() =>
        {
            lock (RegistryGate)
            {
                ValueConverters.Remove(converter);
            }
        });
    }

    public static IDisposable RegisterMemberProvider(ICSharpObjectMemberProvider provider)
    {
        if (provider is null) throw new ArgumentNullException(nameof(provider));

        lock (RegistryGate)
        {
            MemberProviders.Insert(0, provider);
        }

        return new CSharpObjectRegistration(() =>
        {
            lock (RegistryGate)
            {
                MemberProviders.Remove(provider);
            }
        });
    }

    public static IDisposable RegisterActivator(ICSharpObjectActivator activator)
    {
        if (activator is null) throw new ArgumentNullException(nameof(activator));

        lock (RegistryGate)
        {
            Activators.Insert(0, activator);
        }

        return new CSharpObjectRegistration(() =>
        {
            lock (RegistryGate)
            {
                Activators.Remove(activator);
            }
        });
    }

    public static IDisposable RegisterMethodConnectionValidator(
        ICSharpObjectMethodConnectionValidator validator)
    {
        if (validator is null) throw new ArgumentNullException(nameof(validator));

        lock (RegistryGate)
        {
            MethodConnectionValidators.Insert(0, validator);
        }

        return new CSharpObjectRegistration(() =>
        {
            lock (RegistryGate)
            {
                MethodConnectionValidators.Remove(validator);
            }
        });
    }

    /// <summary>Recreates the host and refreshes all discoverable configuration entries.</summary>
    public bool RefreshHost()
    {
        SelectedMethod = string.Empty;
        Values.Clear();
        Collections.Clear();
        Methods.Clear();

        var type = CSharpObjectTypeTool.ResolveType(HostType);
        if (type is null)
        {
            SetHost(null);
            SetLastError(string.IsNullOrWhiteSpace(HostType)
                ? null
                : $"Unable to resolve host type '{HostType}'.");
            return false;
        }

        try
        {
            SetHost(CSharpObjectActivationTool.CreateHost(type, GetActivators()));

            var members = CSharpObjectMemberDiscoveryTool.Discover(
                type,
                GetMemberProviders());
            foreach (var value in members.Values) Values.Add(value);
            foreach (var collection in members.Collections) Collections.Add(collection);
            foreach (var method in members.Methods) Methods.Add(method);

            SetLastError(null);
            return true;
        }
        catch (Exception ex)
        {
            SetHost(null);
            SetLastError(ex.Message);
            return false;
        }
    }

    /// <summary>Applies all scalar and collection string configuration to the current host.</summary>
    public void ApplyConfiguration(object? conversionParameter = null)
    {
        if (host is null && !TryCreateHost())
        {
            throw new InvalidOperationException(
                LastError ?? $"Unable to create host '{HostType}'.");
        }

        try
        {
            CSharpObjectMemberConfigurationTool.Apply(
                host!,
                Values,
                Collections,
                conversionParameter,
                GetValueConverters());
            SetLastError(null);
        }
        catch (Exception ex)
        {
            SetLastError(ex.Message);
            throw;
        }
    }

    internal async Task<object?> InvokeSelectedMethodAsync(
        object? workflowInput,
        object? conversionParameter,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        SetExecutionCount(executionCount + 1);
        SetExecutionState(CSharpObjectExecutionState.Running);

        try
        {
            ApplyConfiguration(conversionParameter);
            var result = await CSharpObjectMethodTool.InvokeAsync(
                host!,
                SelectedMethod,
                Methods,
                workflowInput,
                ct).ConfigureAwait(false);

            SetLastResult(result);
            SetLastError(null);
            SetLastExecutionSummary(
                result?.ToString()
                ?? workflowInput?.ToString()
                ?? "Completed without a result.");
            SetExecutionState(CSharpObjectExecutionState.Completed);
            return result;
        }
        catch (OperationCanceledException)
        {
            SetExecutionState(CSharpObjectExecutionState.Canceled);
            SetLastExecutionSummary("Execution canceled.");
            throw;
        }
        catch (Exception ex)
        {
            SetLastError(ex.Message);
            SetExecutionState(CSharpObjectExecutionState.Failed);
            SetLastExecutionSummary(ex.Message);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            SetLastDurationMilliseconds(stopwatch.ElapsedMilliseconds);
        }
    }

    internal bool HasSelectedMethod()
        => Methods.Any(method => string.Equals(
            method.Signature,
            SelectedMethod,
            StringComparison.Ordinal));

    internal bool CanConnectTo(CSharpObject receiver)
    {
        var senderMethod = SelectedMethodMember;
        var receiverMethod = receiver.SelectedMethodMember;
        return senderMethod is not null
            && receiverMethod is not null
            && CSharpObjectMethodConnectionTool.CanConnect(
                senderMethod,
                receiverMethod,
                GetMethodConnectionValidators());
    }

    [VeloxCommand]
    private void RefreshMembers() => RefreshHost();

    partial void OnHostTypeChanged(string oldValue, string newValue)
        => RefreshHost();

    partial void OnSelectedMethodChanged(string oldValue, string newValue)
    {
        OnPropertyChanged(nameof(SelectedMethodMember));
        UpdateMethodSlots();
    }

    partial void OnItemAddedToValues(IEnumerable<ValueMember> items)
        => AttachValues(items);

    partial void OnItemAddedToCollections(IEnumerable<CollectionMember> items)
        => AttachCollections(items);

    partial void OnItemAddedToMethods(IEnumerable<MethodMember> items)
        => AttachMethods(items);

    partial void OnItemRemovedFromValues(IEnumerable<ValueMember> items)
    {
        foreach (var item in items)
        {
            if (!Values.Contains(item)) item.Parent = null;
        }
    }

    partial void OnItemRemovedFromCollections(IEnumerable<CollectionMember> items)
    {
        foreach (var item in items)
        {
            if (!Collections.Contains(item)) item.Parent = null;
        }
    }

    partial void OnItemRemovedFromMethods(IEnumerable<MethodMember> items)
    {
        foreach (var item in items)
        {
            if (!Methods.Contains(item)) item.Parent = null;
        }
    }

    private void AttachValues(IEnumerable<ValueMember> items)
    {
        foreach (var item in items.ToArray())
        {
            item.Parent = this;
            var stale = Values.FirstOrDefault(candidate =>
                !ReferenceEquals(candidate, item)
                && string.Equals(candidate.Path, item.Path, StringComparison.Ordinal));
            if (stale is not null) Values.Remove(stale);
        }
    }

    private void AttachCollections(IEnumerable<CollectionMember> items)
    {
        foreach (var item in items.ToArray())
        {
            item.Parent = this;
            var stale = Collections.FirstOrDefault(candidate =>
                !ReferenceEquals(candidate, item)
                && string.Equals(candidate.Path, item.Path, StringComparison.Ordinal));
            if (stale is not null) Collections.Remove(stale);
        }
    }

    private void AttachMethods(IEnumerable<MethodMember> items)
    {
        var hostType = CSharpObjectTypeTool.ResolveType(HostType);
        foreach (var item in items.ToArray())
        {
            if (hostType is null
                || !CSharpObjectMethodTool.TryNormalizeMember(hostType, item))
            {
                Methods.Remove(item);
                continue;
            }

            item.Parent = this;
            var stale = Methods.FirstOrDefault(candidate =>
                !ReferenceEquals(candidate, item)
                && string.Equals(candidate.Signature, item.Signature, StringComparison.Ordinal));
            if (stale is not null) Methods.Remove(stale);
        }
        OnPropertyChanged(nameof(SelectedMethodMember));
        UpdateMethodSlots();
    }

    private void UpdateMethodSlots()
    {
        var method = SelectedMethodMember;
        if (InputSlot is not null)
        {
            InputSlot.Channel = method?.AcceptsWorkflowInput == true
                ? SlotChannel.OneSource
                : SlotChannel.None;
        }

        if (OutputSlot is not null)
        {
            OutputSlot.Channel = method?.ProducesWorkflowOutput == true
                ? SlotChannel.MultipleTargets
                : SlotChannel.None;
        }
    }

    private static ICSharpObjectValueConverter[] GetValueConverters()
    {
        lock (RegistryGate)
        {
            return [.. ValueConverters];
        }
    }

    private static ICSharpObjectMemberProvider[] GetMemberProviders()
    {
        lock (RegistryGate)
        {
            return [.. MemberProviders];
        }
    }

    private static ICSharpObjectActivator[] GetActivators()
    {
        lock (RegistryGate)
        {
            return [.. Activators];
        }
    }

    private static ICSharpObjectMethodConnectionValidator[]
        GetMethodConnectionValidators()
    {
        lock (RegistryGate)
        {
            return [.. MethodConnectionValidators];
        }
    }

    private bool TryCreateHost()
    {
        var type = CSharpObjectTypeTool.ResolveType(HostType);
        if (type is null)
        {
            SetLastError($"Unable to resolve host type '{HostType}'.");
            return false;
        }

        try
        {
            SetHost(CSharpObjectActivationTool.CreateHost(type, GetActivators()));
            SetLastError(null);
            return true;
        }
        catch (Exception ex)
        {
            SetLastError(ex.Message);
            return false;
        }
    }

    private void SetHost(object? value)
    {
        if (ReferenceEquals(host, value)) return;
        OnPropertyChanging(nameof(Host));
        host = value;
        OnPropertyChanged(nameof(Host));
    }

    private void SetLastResult(object? value)
    {
        if (ReferenceEquals(lastResult, value)) return;
        OnPropertyChanging(nameof(LastResult));
        lastResult = value;
        OnPropertyChanged(nameof(LastResult));
    }

    private void SetLastError(string? value)
    {
        if (string.Equals(lastError, value, StringComparison.Ordinal)) return;
        OnPropertyChanging(nameof(LastError));
        lastError = value;
        OnPropertyChanged(nameof(LastError));
    }

    private void SetExecutionState(CSharpObjectExecutionState value)
    {
        if (executionState == value) return;
        OnPropertyChanging(nameof(ExecutionState));
        executionState = value;
        OnPropertyChanged(nameof(ExecutionState));
    }

    private void SetExecutionCount(int value)
    {
        if (executionCount == value) return;
        OnPropertyChanging(nameof(ExecutionCount));
        executionCount = value;
        OnPropertyChanged(nameof(ExecutionCount));
    }

    private void SetLastDurationMilliseconds(long value)
    {
        if (lastDurationMilliseconds == value) return;
        OnPropertyChanging(nameof(LastDurationMilliseconds));
        lastDurationMilliseconds = value;
        OnPropertyChanged(nameof(LastDurationMilliseconds));
    }

    private void SetLastExecutionSummary(string value)
    {
        if (string.Equals(
                lastExecutionSummary,
                value,
                StringComparison.Ordinal))
        {
            return;
        }

        OnPropertyChanging(nameof(LastExecutionSummary));
        lastExecutionSummary = value;
        OnPropertyChanged(nameof(LastExecutionSummary));
    }

}
