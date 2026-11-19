using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Demo.ViewModels;
using VeloxDev.AI;
using VeloxDev.AI.Workflow;
using VeloxDev.AI.Workflow.Functions;
using VeloxDev.Serialization;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// The two Agent-facing promises that cross the archive's closed world are checked against the whole assembly,
/// not against one example.
/// </summary>
/// <remarks>
/// <para>
/// Both defects this file exists for were found one at a time, by a model, mid-conversation: a port provider the
/// archive had never been told about, and an enum the read path could not take. Neither is visible from the file
/// that causes it — the provider is declared in one project and the closure is decided in another — so an example
/// test would have had to be written for the specific type that broke, which is exactly what did not happen.
/// </para>
/// <para>
/// These two are written the other way round: they enumerate every declaration of the shape and ask whether each
/// one can work. A new <c>[SlotSelectors]</c> property or a new command parameter type therefore fails here on
/// the day it is written, with a message naming it, instead of the day an Agent trips over it.
/// </para>
/// </remarks>
[TestClass]
public class ClosedWorldBoundaryTests
{
    /// <summary>
    /// Every type the Agent can hand to <c>SetEnumSlotCollection</c> as a non-enum selector can actually be
    /// rebuilt by it.
    /// </summary>
    /// <remarks>
    /// The tool reads the provider back through the archive serializer, so a non-enum <c>[SlotSelectors]</c> type
    /// with no generated reader is a port the Agent can be told to build and cannot build. <c>[Archivable]</c> on
    /// the provider — or on something that declares it — is the fix, and it has to be on the type's own side of
    /// the assembly boundary.
    /// </remarks>
    [TestMethod]
    public void EveryNonEnumSlotSelector_HasAnArchiveReader()
    {
        // The one deliberate exception: this fixture exists precisely to have no reader, so that the diagnostic
        // the tool now returns can be asserted. Anything else appearing here is a real gap.
        var deliberate = new HashSet<Type> { typeof(NotArchivablePortProvider) };

        var offenders = new List<string>();
        var inspected = 0;

        // 逐个成员扫，不是逐个类型：[SlotSelectors] 挂在属性/字段上（作者通常写在 [VeloxProperty] 的私有字段上），
        // 所以拿到类型上找只会一 Attribute 都找不到、让这条用例空过。
        foreach (var (owner, selector) in SlotSelectorDeclarations())
        {
            foreach (var named in selector.AllowedEnumTypes)
            {
                if (named is null || named.IsEnum || named == typeof(bool)) continue;

                inspected++;
                if (deliberate.Contains(named)) continue;
                if (VeloxJsonRegistry.ReaderFor(named) is not null) continue;

                offenders.Add($"{owner} names {named.FullName}");
            }
        }

        // 「一个都没扫到」和「都合规」必须分得开，否则重命名一次属性就能让这条用例永远变绿。
        Assert.IsTrue(inspected >= 1,
            "the walk found no non-enum [SlotSelectors] declaration at all — it is no longer looking at what it was written to look at");

        Assert.IsEmpty(offenders,
            "a [SlotSelectors] provider the archive carries no reader for cannot be rebuilt by SetEnumSlotCollection. "
            + "Add [Archivable] to it (or to a type that declares it):\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// Every command parameter the Agent can be given resolves to something the archive can read, an enum, or a
    /// declared member of the interface allow-list below.
    /// </summary>
    /// <remarks>
    /// <c>CommandInvoker</c> deserializes the model's JSON to the type <c>[AgentCommandParameter]</c> declares, so
    /// a parameter type the archive has no reader for is a command the Agent can be told to run and cannot run.
    /// The four interfaces are the known, deliberate case: the Agent reaches those actions through their own
    /// dedicated tools (CreateNode, ConnectSlots, ExecuteNode) rather than by building the interface from JSON,
    /// which an interface cannot do anyway without a type discriminator. They are listed rather than skipped so
    /// that a <b>new</b> interface parameter has to be looked at instead of silently joining them.
    /// </remarks>
    [TestMethod]
    public void EveryCommandParameter_IsReadableOrOnTheDeclaredAllowList()
    {
        // The scalar types the archive reads without a generated reader (VeloxJsonSerializer.TryReadScalar).
        var scalars = new HashSet<Type>
        {
            typeof(string), typeof(bool), typeof(int), typeof(long), typeof(double), typeof(float),
            typeof(decimal), typeof(byte), typeof(short), typeof(char), typeof(byte[]),
            typeof(Guid), typeof(DateTime), typeof(TimeSpan),
        };

        // Reached through dedicated tools, never by materialising the interface from JSON.
        var interfacesReachedByTheirOwnTool = new HashSet<Type>
        {
            typeof(IWorkflowNodeViewModel),
            typeof(IWorkflowSlotViewModel),
            typeof(ITaskContext),
            typeof(IWorkflowActionPair),
        };

        var offenders = new List<string>();
        var seenParameterTypes = new HashSet<Type>();

        foreach (var component in ComponentInstances())
        {
            var typeName = component.GetType().FullName ?? component.GetType().Name;
            var accessor = AIContextTreeRegistry.FindAccessor(typeName);
            if (accessor is null) continue;

            foreach (var command in AIContextDirectory.Shared.MembersAcross(typeName, "Commands"))
            {
                var parameter = accessor.ParameterType(command.Name);
                if (parameter is null || parameter == typeof(object)) continue;

                seenParameterTypes.Add(parameter);

                var underlying = Nullable.GetUnderlyingType(parameter) ?? parameter;
                if (underlying.IsEnum) continue;
                if (scalars.Contains(underlying)) continue;
                if (interfacesReachedByTheirOwnTool.Contains(underlying)) continue;
                if (VeloxJsonRegistry.ReaderFor(underlying) is not null) continue;

                offenders.Add($"{typeName}.{command.Name} takes {underlying.FullName}");
            }
        }

        // A guard on the guard, and a sharp one: the walk has to have reached the specific shapes that make this
        // test worth having. A rename, a generator change or a component dropped from the list would otherwise
        // leave it green while looking at nothing.
        foreach (var expected in interfacesReachedByTheirOwnTool)
        {
            Assert.Contains(expected, seenParameterTypes,
                $"the walk never reached {expected.Name}, so it is no longer looking at what it was written to look at");
        }

        Assert.Contains(typeof(VeloxDev.WorkflowSystem.Offset), seenParameterTypes,
            "the walk never reached the archivable command parameters either");

        Assert.IsEmpty(offenders,
            "a command parameter the archive cannot read is a command ExecuteCommandOnNode can be told to run and "
            + "cannot run. Either make the parameter type archivable, or add it to the allow-list above with the "
            + "reason its own tool is the route:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// Every <c>[SlotSelectors]</c> declaration in the assemblies the Agent's surface spans, paired with the
    /// member that carries it so a failure can name where to look.
    /// </summary>
    private static IEnumerable<(string Owner, SlotSelectorsAttribute Selector)> SlotSelectorDeclarations()
    {
        const BindingFlags declared =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var type in AgentFacingTypes())
        {
            foreach (var member in type.GetMembers(declared))
            {
                var selector = member switch
                {
                    PropertyInfo property => property.GetCustomAttribute<SlotSelectorsAttribute>(inherit: false),
                    FieldInfo field => field.GetCustomAttribute<SlotSelectorsAttribute>(inherit: false),
                    _ => null,
                };

                if (selector is not null) yield return ($"{type.FullName}.{member.Name}", selector);
            }
        }
    }

    /// <summary>
    /// The assemblies the Agent's declarations live in — the framework's, the demo's, and this test project's.
    /// </summary>
    private static IEnumerable<Type> AgentFacingTypes()
        => new[]
        {
            typeof(TreeDefaultViewModel).Assembly,          // VeloxDev.Core
            typeof(WorkflowAgentScope).Assembly,            // VeloxDev.Core.Extension
            typeof(PythonPortProvider).Assembly,            // the demo Lib
            typeof(ClosedWorldBoundaryTests).Assembly,      // the fixtures here
        }
        .Distinct()
        .SelectMany(SafeTypes);

    /// <summary>One instance of every component whose commands the Agent can be handed.</summary>
    private static IEnumerable<object> ComponentInstances()
    {
        yield return new TreeDefaultViewModel();
        yield return new NodeDefaultViewModel();
        yield return new SlotDefaultViewModel();
        yield return new LinkDefaultViewModel();

        yield return new PythonScriptNodeViewModel();
        yield return new EnumSelectorNodeViewModel();
        yield return new TimerNodeViewModel();
        yield return new ControllerViewModel();
    }

    /// <summary>
    /// <see cref="Assembly.GetTypes"/> throws when one referenced type cannot be loaded, which would turn a
    /// missing optional dependency into a red test about nothing. The walk wants what it can see.
    /// </summary>
    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
