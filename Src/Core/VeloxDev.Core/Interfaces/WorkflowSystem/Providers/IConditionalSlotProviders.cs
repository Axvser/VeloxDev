using System.Collections.Generic;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Optional capability of a node's Helper: the slot enumerators the node declares.
/// </summary>
/// <remarks>
/// <para>
/// A card that draws a node's ports needs two things a node does not publish through
/// <see cref="IWorkflowNodeViewModel"/>: which side of the node each slot faces, and what an enumerated port is
/// called. The first is <see cref="IWorkflowSlotViewModel.Channel"/> and needs nothing added. The second lives on
/// the <see cref="IConditionalSlot"/> entries inside a <see cref="SlotEnumerator{TSlot}"/>, and the enumerator is
/// held in a property the node names itself — so a caller that holds only the node has no way to reach it.
/// </para>
/// <para>
/// This is where it reaches it. The node hands each enumerator over as it installs it — the generated
/// <c>InitializeWorkflowCore</c> calls <see cref="SlotEnumerator{TSlot}.Install"/>, which registers here — so the
/// enumerators are reachable from the node without anyone knowing what the properties are called.
/// </para>
/// <para>
/// The capability is deliberately not a member of <c>IWorkflow…ViewModelHelper</c>: adding members there would
/// break every existing implementer, and a helper whose node has no enumerators should not have to answer for it.
/// A helper that derives from <c>NodeHelper&lt;T&gt;</c> gets this for free.
/// </para>
/// </remarks>
/// <seealso cref="IConditionalSlotProvider"/>
public interface IConditionalSlotProviders
{
    /// <summary>
    /// The node's slot enumerators, in the order the node installed them.
    /// </summary>
    /// <remarks>
    /// Mutable because installation is the only thing that writes it: <see cref="SlotEnumerator{TSlot}.Install"/>
    /// adds the enumerator and <see cref="SlotEnumerator{TSlot}.Uninstall"/> takes it back out. Readers should
    /// treat it as read-only.
    /// </remarks>
    IList<IConditionalSlotProvider> Providers { get; }
}
