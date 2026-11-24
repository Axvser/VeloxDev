using VeloxDev.AI;

namespace Demo.Views.Workbench;

/// <summary>The Agent's operation confirmation as an in-app card.</summary>
public sealed partial class AgentConfirmationView : WorkbenchViewBase
{
    /// <summary>Creates the card from XAML.</summary>
    public AgentConfirmationView()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the user answered.</summary>
    public event EventHandler? Completed;

    /// <summary>The user's answer; deny until one of the buttons is pressed.</summary>
    public AgentConfirmationResult Result { get; private set; } = AgentConfirmationResult.Deny;

    /// <summary>Fills the card from the Agent's request.</summary>
    public void Configure(string operationKey, string description)
    {
        OperationKeyLabel.Text = operationKey;
        DescriptionLabel.Text = description;
    }

    private void OnAlways(object? sender, EventArgs e) => Finish(AgentConfirmationResult.AllowAlways);

    private void OnOnce(object? sender, EventArgs e) => Finish(AgentConfirmationResult.AllowOnce);

    private void OnDeny(object? sender, EventArgs e) => Finish(AgentConfirmationResult.Deny);

    private void Finish(AgentConfirmationResult result)
    {
        Result = result;
        Completed?.Invoke(this, EventArgs.Empty);
    }
}
