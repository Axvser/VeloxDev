using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.CSharp;

namespace VeloxDev.Core.Test.WorkflowSystem;

[TestClass]
public class CSharpObjectTests
{
    [TestMethod]
    public void HostTypeChanged_RecreatesHostAndDiscoversMembers()
    {
        var node = new CSharpObject
        {
            HostType = typeof(ConfigurableHost).FullName!
        };

        Assert.IsInstanceOfType<ConfigurableHost>(node.Host);
        Assert.IsNotNull(node.InputSlot);
        Assert.IsNotNull(node.OutputSlot);
        Assert.AreEqual(SlotChannel.None, node.InputSlot.Channel);
        Assert.AreEqual(SlotChannel.None, node.OutputSlot.Channel);
        Assert.IsTrue(node.Values.Any(value => value.Path == nameof(ConfigurableHost.Count)));
        Assert.IsTrue(node.Collections.Any(collection => collection.Path == nameof(ConfigurableHost.Numbers)));
        var method = node.Methods.Single(
            candidate => candidate.Name == nameof(ConfigurableHost.FormatAsync));
        Assert.AreEqual(
            "FormatAsync(int input)",
            method.DisplayName);
        Assert.AreEqual(CSharpObjectMethodRole.Intermediate, method.Role);
        Assert.IsTrue(method.AcceptsWorkflowInput);
        Assert.IsTrue(method.ProducesWorkflowOutput);
        Assert.IsTrue(method.Parameters.Single().UseWorkflowInput);
        Assert.IsFalse(node.Methods.Any(
            candidate => candidate.Name == nameof(ConfigurableHost.Unsupported)));
        Assert.IsFalse(node.Methods.Any(
            candidate => candidate.Name == nameof(ConfigurableHost.WithOptionalDependency)));

        node.SelectedMethod = method.Signature;
        Assert.AreEqual(SlotChannel.OneSource, node.InputSlot.Channel);
        Assert.AreEqual(SlotChannel.MultipleTargets, node.OutputSlot.Channel);

        var original = node.Host;
        node.HostType = typeof(AlternateHost).FullName!;

        Assert.IsInstanceOfType<AlternateHost>(node.Host);
        Assert.AreNotSame(original, node.Host);
        Assert.IsFalse(node.Values.Any(value => value.Path == nameof(ConfigurableHost.Count)));
    }

    [TestMethod]
    public async Task WorkAsync_AppliesPathsAndCollectionsThenInvokesSelectedMethod()
    {
        var node = CreateConfiguredNode();
        var helper = (CSharpObjectHelper)node.GetHelper();

        await helper.WorkAsync(7, CancellationToken.None);

        var host = (ConfigurableHost)node.Host!;
        Assert.AreEqual("Velox", host.Settings.Name);
        Assert.AreEqual(11, host.Count);
        CollectionAssert.AreEqual(new[] { 3, 5 }, host.Numbers);
        Assert.AreEqual("Velox:7:tail:8", node.LastResult);
    }

    [TestMethod]
    public void CustomConverter_ReceivesRuntimeParameter()
    {
        var marker = new object();
        using var registration = CSharpObject.RegisterValueConverter(
            new WrappedValueConverter(marker));
        var node = new CSharpObject
        {
            HostType = typeof(CustomValueHost).FullName!
        };
        node.Values.Add(new ValueMember
        {
            Path = nameof(CustomValueHost.Value),
            ValueName = nameof(CustomValueHost.Value),
            ValueType = typeof(WrappedValue).FullName!,
            Value = "custom"
        });

        Assert.IsNotNull(node.Host);
        Assert.IsTrue(node.Values.Single().IsEnabled);
        node.ApplyConfiguration(marker);

        Assert.AreEqual("custom", ((CustomValueHost)node.Host!).Value.Text);
    }

    [TestMethod]
    public void MethodDiscovery_ClassifiesOnlySupportedWorkflowShapes()
    {
        var node = new CSharpObject
        {
            HostType = typeof(MethodShapeHost).FullName!
        };

        AssertMethodRole(
            node,
            nameof(MethodShapeHost.Start),
            CSharpObjectMethodRole.Start);
        AssertMethodRole(
            node,
            nameof(MethodShapeHost.Intermediate),
            CSharpObjectMethodRole.Intermediate);
        AssertMethodRole(
            node,
            nameof(MethodShapeHost.IntermediateAsync),
            CSharpObjectMethodRole.Intermediate);
        AssertMethodRole(
            node,
            nameof(MethodShapeHost.IntermediateValueAsync),
            CSharpObjectMethodRole.Intermediate);
        AssertMethodRole(
            node,
            nameof(MethodShapeHost.Terminal),
            CSharpObjectMethodRole.Terminal);
        AssertMethodRole(
            node,
            nameof(MethodShapeHost.TerminalAsync),
            CSharpObjectMethodRole.Terminal);
        AssertMethodRole(
            node,
            nameof(MethodShapeHost.TerminalValueAsync),
            CSharpObjectMethodRole.Terminal);

        Assert.IsFalse(node.Methods.Any(method =>
            method.Name == nameof(MethodShapeHost.AsyncStart)));
        Assert.IsFalse(node.Methods.Any(method =>
            method.Name == nameof(MethodShapeHost.EmptyStart)));
        Assert.IsFalse(node.Methods.Any(method =>
            method.Name == nameof(MethodShapeHost.TooMany)));
        Assert.IsFalse(node.Methods.Any(method =>
            method.Name == nameof(MethodShapeHost.WithCancellation)));
        Assert.IsTrue(node.Methods
            .Where(method => method.AcceptsWorkflowInput)
            .All(method => method.Parameters.Single().UseWorkflowInput));
    }

    [TestMethod]
    public async Task WorkAsync_ExecutesStartIntermediateAndTerminalRoles()
    {
        var node = new CSharpObject
        {
            HostType = typeof(MethodExecutionHost).FullName!
        };
        var helper = (CSharpObjectHelper)node.GetHelper();

        SelectMethod(node, nameof(MethodExecutionHost.Start));
        await helper.WorkAsync("ignored", CancellationToken.None);
        Assert.AreEqual(3, node.LastResult);
        Assert.AreEqual(CSharpObjectExecutionState.Completed, node.ExecutionState);
        Assert.AreEqual(1, node.ExecutionCount);
        Assert.AreEqual("3", node.LastExecutionSummary);

        SelectMethod(node, nameof(MethodExecutionHost.IntermediateAsync));
        await helper.WorkAsync(4, CancellationToken.None);
        Assert.AreEqual(8, node.LastResult);
        Assert.AreEqual(2, node.ExecutionCount);
        Assert.AreEqual("8", node.LastExecutionSummary);

        SelectMethod(node, nameof(MethodExecutionHost.TerminalAsync));
        await helper.WorkAsync(9, CancellationToken.None);
        Assert.AreEqual(9, ((MethodExecutionHost)node.Host!).TerminalValue);
        Assert.IsNull(node.LastResult);
        Assert.AreEqual(3, node.ExecutionCount);
        Assert.AreEqual("9", node.LastExecutionSummary);
        Assert.IsGreaterThanOrEqualTo(
            node.LastDurationMilliseconds,
            0L);
    }

    [TestMethod]
    public void SelectedMethod_UpdatesInputAndOutputChannelsByRole()
    {
        var node = new CSharpObject
        {
            HostType = typeof(MethodShapeHost).FullName!
        };

        SelectMethod(node, nameof(MethodShapeHost.Start));
        Assert.AreEqual(SlotChannel.None, node.InputSlot!.Channel);
        Assert.AreEqual(SlotChannel.MultipleTargets, node.OutputSlot!.Channel);

        SelectMethod(node, nameof(MethodShapeHost.Intermediate));
        Assert.AreEqual(SlotChannel.OneSource, node.InputSlot.Channel);
        Assert.AreEqual(SlotChannel.MultipleTargets, node.OutputSlot.Channel);

        SelectMethod(node, nameof(MethodShapeHost.Terminal));
        Assert.AreEqual(SlotChannel.OneSource, node.InputSlot.Channel);
        Assert.AreEqual(SlotChannel.None, node.OutputSlot.Channel);
    }

    [TestMethod]
    public void MultipleInstances_RegisterEachWorkflowSlotExactlyOnce()
    {
        var first = new CSharpObject();
        var second = new CSharpObject();

        Assert.HasCount(2, first.Slots);
        Assert.HasCount(2, second.Slots);
        Assert.AreEqual(2, first.Slots.Distinct().Count());
        Assert.AreEqual(2, second.Slots.Distinct().Count());
        CollectionAssert.AreEquivalent(
            new IWorkflowSlotViewModel[]
            {
                first.InputSlot!,
                first.OutputSlot!
            },
            first.Slots.ToArray());
        CollectionAssert.AreEquivalent(
            new IWorkflowSlotViewModel[]
            {
                second.InputSlot!,
                second.OutputSlot!
            },
            second.Slots.ToArray());
    }

    [TestMethod]
    public void CustomMemberProvider_OverridesDefaultDiscovery()
    {
        using var registration = CSharpObject.RegisterMemberProvider(
            new AlternateHostMemberProvider());
        var node = new CSharpObject
        {
            HostType = typeof(AlternateHost).FullName!
        };

        Assert.HasCount(1, node.Values);
        Assert.AreEqual("Name", node.Values[0].Path);
        Assert.IsEmpty(node.Collections);
        Assert.IsEmpty(node.Methods);
    }

    [TestMethod]
    public async Task ValidateBroadcastAsync_RejectsIncompatibleSelectedInput()
    {
        var source = new CSharpObject
        {
            HostType = typeof(IntSourceHost).FullName!
        };
        source.SelectedMethod = source.Methods.Single(
            method => method.Name == nameof(IntSourceHost.Produce)).Signature;

        var incompatible = new CSharpObject
        {
            HostType = typeof(StringTargetHost).FullName!
        };
        incompatible.SelectedMethod = incompatible.Methods.Single(
            method => method.Name == nameof(StringTargetHost.Accept)).Signature;

        var compatible = new CSharpObject
        {
            HostType = typeof(IntTargetHost).FullName!
        };
        compatible.SelectedMethod = compatible.Methods.Single(
            method => method.Name == nameof(IntTargetHost.Accept)).Signature;

        var sender = new SlotViewModelBase { Parent = source };
        var incompatibleReceiver = new SlotViewModelBase { Parent = incompatible };
        var compatibleReceiver = new SlotViewModelBase { Parent = compatible };
        var helper = (CSharpObjectHelper)source.GetHelper();

        Assert.IsFalse(await helper.ValidateBroadcastAsync(
            sender,
            incompatibleReceiver,
            42,
            CancellationToken.None));
        Assert.IsTrue(await helper.ValidateBroadcastAsync(
            sender,
            compatibleReceiver,
            42,
            CancellationToken.None));

        compatible.SelectedMethod = string.Empty;
        Assert.IsFalse(await helper.ValidateBroadcastAsync(
            sender,
            compatibleReceiver,
            42,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task CustomMethodConnectionValidator_IsOrderedAndOverridesDefault()
    {
        using var registration =
            CSharpObject.RegisterMethodConnectionValidator(
                new OrderedMethodConnectionValidator());
        var source = new CSharpObject
        {
            HostType = typeof(OverrideSourceHost).FullName!
        };
        source.SelectedMethod = source.Methods.Single(
            method => method.Name == nameof(OverrideSourceHost.Send)).Signature;

        var target = new CSharpObject
        {
            HostType = typeof(OverrideTargetHost).FullName!
        };
        target.SelectedMethod = target.Methods.Single(
            method => method.Name == nameof(OverrideTargetHost.Receive)).Signature;

        var reverseSource = new CSharpObject
        {
            HostType = typeof(ReverseOverrideSourceHost).FullName!
        };
        reverseSource.SelectedMethod = reverseSource.Methods.Single(
            method => method.Name == nameof(ReverseOverrideSourceHost.Send)).Signature;
        var reverseTarget = new CSharpObject
        {
            HostType = typeof(ReverseOverrideTargetHost).FullName!
        };
        reverseTarget.SelectedMethod = reverseTarget.Methods.Single(
            method => method.Name == nameof(ReverseOverrideTargetHost.Receive)).Signature;

        Assert.IsTrue(await ((CSharpObjectHelper)source.GetHelper())
            .ValidateBroadcastAsync(
                new SlotViewModelBase { Parent = source },
                new SlotViewModelBase { Parent = target },
                null,
                CancellationToken.None));
        Assert.IsFalse(await ((CSharpObjectHelper)reverseSource.GetHelper())
            .ValidateBroadcastAsync(
                new SlotViewModelBase { Parent = reverseSource },
                new SlotViewModelBase { Parent = reverseTarget },
                null,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task CustomMethodConnectionValidator_CannotOverrideMethodRoles()
    {
        using var registration =
            CSharpObject.RegisterMethodConnectionValidator(
                new AlwaysAllowMethodConnectionValidator());
        var terminal = new CSharpObject
        {
            HostType = typeof(IntTargetHost).FullName!
        };
        SelectMethod(terminal, nameof(IntTargetHost.Accept));

        var start = new CSharpObject
        {
            HostType = typeof(IntSourceHost).FullName!
        };
        SelectMethod(start, nameof(IntSourceHost.Produce));

        Assert.IsFalse(await ((CSharpObjectHelper)terminal.GetHelper())
            .ValidateBroadcastAsync(
                new SlotViewModelBase { Parent = terminal },
                new SlotViewModelBase { Parent = start },
                null,
                CancellationToken.None));
    }

    private static CSharpObject CreateConfiguredNode()
    {
        var node = new CSharpObject
        {
            HostType = typeof(ConfigurableHost).FullName!
        };

        node.Values.Add(new ValueMember
        {
            Path = "Settings.Name",
            ValueName = "Name",
            ValueType = typeof(string).FullName!,
            Value = "Velox"
        });
        node.Values.Single(value => value.Path == nameof(ConfigurableHost.Count)).Value = "11";
        node.Values.Single(value => value.Path == nameof(ConfigurableHost.Suffix)).Value = "tail";

        var numbers = node.Collections.Single(
            collection => collection.Path == nameof(ConfigurableHost.Numbers));
        numbers.Items.Add(new CollectionItem { Index = 0, Value = "3" });
        numbers.Items.Add(new CollectionItem { Index = 1, Value = "5" });

        var method = node.Methods.Single(
            candidate => candidate.Name == nameof(ConfigurableHost.FormatAsync));
        node.SelectedMethod = method.Signature;

        return node;
    }

    private static void AssertMethodRole(
        CSharpObject node,
        string methodName,
        CSharpObjectMethodRole expectedRole)
    {
        var method = node.Methods.Single(candidate =>
            candidate.Name == methodName);
        Assert.AreEqual(expectedRole, method.Role);
    }

    private static void SelectMethod(CSharpObject node, string methodName)
        => node.SelectedMethod = node.Methods.Single(method =>
            method.Name == methodName).Signature;

    public sealed class ConfigurableHost
    {
        public HostSettings Settings { get; set; } = new();
        public int Count { get; set; }
        public string Suffix { get; set; } = string.Empty;
        public List<int> Numbers { get; set; } = [];

        public async Task<string> FormatAsync(int input)
        {
            await Task.Delay(1);
            return $"{Settings.Name}:{input}:{Suffix}:{Numbers.Sum()}";
        }

        public string Unsupported(int input, Stream dependency)
            => $"{input}:{dependency.Length}";

        public string WithOptionalDependency(
            int input,
            Stream? dependency = null)
            => $"{input}:{dependency?.Length ?? 0}";
    }

    public sealed class HostSettings
    {
        public string Name { get; set; } = string.Empty;
    }

    public sealed class AlternateHost
    {
        public string Name { get; set; } = string.Empty;
    }

    public sealed class CustomValueHost
    {
        public WrappedValue Value { get; set; } = new(string.Empty);
    }

    public sealed class MethodShapeHost
    {
        public int Start() => 1;

        public Task<int> AsyncStart() => Task.FromResult(1);

        public void EmptyStart()
        {
        }

        public int Intermediate(int value) => value + 1;

        public Task<int> IntermediateAsync(int value)
            => Task.FromResult(value + 1);

        public ValueTask<int> IntermediateValueAsync(int value)
            => ValueTask.FromResult(value + 1);

        public void Terminal(int value)
        {
        }

        public Task TerminalAsync(int value) => Task.CompletedTask;

        public ValueTask TerminalValueAsync(int value)
            => ValueTask.CompletedTask;

        public int TooMany(int value, string suffix) => value;

        public int WithCancellation(
            int value,
            CancellationToken ct)
            => value;
    }

    public sealed class MethodExecutionHost
    {
        public int TerminalValue { get; private set; }

        public int Start() => 3;

        public Task<int> IntermediateAsync(int value)
            => Task.FromResult(value * 2);

        public Task TerminalAsync(int value)
        {
            TerminalValue = value;
            return Task.CompletedTask;
        }
    }

    public sealed record WrappedValue(string Text);

    public sealed class IntSourceHost
    {
        public int Produce() => 42;
    }

    public sealed class StringTargetHost
    {
        public void Accept(string value)
        {
        }
    }

    public sealed class OverrideSourceHost
    {
        public ConnectionTokenA Send() => new();
    }

    public sealed class OverrideTargetHost
    {
        public void Receive(ConnectionTokenB value)
        {
        }
    }

    public sealed class ReverseOverrideSourceHost
    {
        public ConnectionTokenB Send() => new();
    }

    public sealed class ReverseOverrideTargetHost
    {
        public void Receive(ConnectionTokenA value)
        {
        }
    }

    public sealed class ConnectionTokenA;

    public sealed class ConnectionTokenB;

    public sealed class IntTargetHost
    {
        public void Accept(int value)
        {
        }
    }

    private sealed class WrappedValueConverter(object expectedParameter)
        : ICSharpObjectValueConverter, ICSharpObjectValueConverterMetadata
    {
        public bool CanConvert(Type targetType)
            => targetType == typeof(WrappedValue);

        public bool TryConvert(
            string value,
            Type targetType,
            object? parameter,
            out object? result)
        {
            if (targetType == typeof(WrappedValue)
                && ReferenceEquals(parameter, expectedParameter))
            {
                result = new WrappedValue(value);
                return true;
            }

            result = null;
            return false;
        }
    }

    private sealed class AlternateHostMemberProvider
        : ICSharpObjectMemberProvider
    {
        public bool TryGetMembers(
            Type hostType,
            out CSharpObjectMembers members)
        {
            if (hostType != typeof(AlternateHost))
            {
                members = null!;
                return false;
            }

            members = new CSharpObjectMembers(
                values:
                [
                    new ValueMember
                    {
                        Path = nameof(AlternateHost.Name),
                        ValueName = nameof(AlternateHost.Name),
                        ValueType = typeof(string).FullName!
                    }
                ]);
            return true;
        }
    }

    private sealed class OrderedMethodConnectionValidator
        : ICSharpObjectMethodConnectionValidator
    {
        public bool TryValidate(
            MethodMember sender,
            MethodMember receiver,
            out bool canConnect)
        {
            var outputType = sender.Return.FirstOrDefault()?.ValueType;
            var inputType = receiver.Parameters.FirstOrDefault(
                parameter => parameter.UseWorkflowInput)?.ValueType;
            if (outputType == typeof(ConnectionTokenA).FullName
                && inputType == typeof(ConnectionTokenB).FullName)
            {
                canConnect = true;
                return true;
            }

            if (outputType == typeof(ConnectionTokenB).FullName
                && inputType == typeof(ConnectionTokenA).FullName)
            {
                canConnect = false;
                return true;
            }

            canConnect = false;
            return false;
        }
    }

    private sealed class AlwaysAllowMethodConnectionValidator
        : ICSharpObjectMethodConnectionValidator
    {
        public bool TryValidate(
            MethodMember sender,
            MethodMember receiver,
            out bool canConnect)
        {
            canConnect = true;
            return true;
        }
    }
}
