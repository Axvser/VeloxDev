namespace Demo.Views.Workbench;

/// <summary>An in-app message card, used where the native shell used <c>MessageBox</c>.</summary>
public sealed partial class MessageView : WorkbenchViewBase
{
    /// <summary>Creates the card from XAML.</summary>
    public MessageView()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the user dismisses the card.</summary>
    public event EventHandler? Dismissed;

    /// <summary>Sets the header and body text.</summary>
    public void SetMessage(string title, string body)
    {
        TitleLabel.Text = title;
        BodyLabel.Text = body;
    }

    private void OnOk(object? sender, EventArgs e) => Dismissed?.Invoke(this, EventArgs.Empty);
}
