using System.Drawing;
using Easel;
using Easel.Controls;
using Easel.Visuals;
using Easel.Visuals.Controls;
using Button = Easel.Visuals.Controls.Button;
using CheckBox = Easel.Visuals.Controls.CheckBox;

namespace Demo.Views.Workbench;

/// <summary>
/// The Agent's selection prompt as an in-app card: single-select buttons or multi-select checks, plus the free
/// text field every prompt carries. The host completes its pending task on <see cref="Completed"/>.
/// </summary>
public sealed partial class AgentSelectionView : WorkbenchViewBase
{
    private readonly List<CheckBox> _checks = [];

    /// <summary>Creates the card from XAML.</summary>
    public AgentSelectionView()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the user chose, confirmed or cancelled.</summary>
    public event EventHandler? Completed;

    /// <summary>For single-select: the chosen option.</summary>
    public string? ChosenOption { get; private set; }

    /// <summary>For multi-select: the chosen options.</summary>
    public IReadOnlyList<string> ChosenOptions { get; private set; } = [];

    /// <summary>Free text typed by the user, trimmed; null when empty.</summary>
    public string? FreeTextResponse { get; private set; }

    /// <summary>Whether the user cancelled without choosing.</summary>
    public bool Cancelled { get; private set; }

    /// <summary>Fills the card from the Agent's request.</summary>
    public void Configure(string prompt, IReadOnlyList<string> options, string freeTextPrompt, bool allowMultiSelect)
    {
        TitleLabel.Text = allowMultiSelect ? "Agent · 请多选" : "Agent · 请选择";
        PromptLabel.Text = prompt;
        FreeTextPromptLabel.Text = freeTextPrompt;

        OptionsPanel.Children.Clear();
        ButtonRow.Children.Clear();
        _checks.Clear();

        if (allowMultiSelect)
        {
            foreach (var option in options)
            {
                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Height = 30,
                };
                var check = new CheckBox { IsChecked = false, VerticalAlignment = Easel.Controls.VerticalAlignment.Center };
                var label = new TextBlock
                {
                    Text = option,
                    FontSize = 10,
                    Foreground = WorkbenchPalette.TextPrimary,
                    VerticalAlignment = Easel.Controls.VerticalAlignment.Center,
                };
                row.Add(check);
                row.Add(label);
                _checks.Add(check);
                OptionsPanel.Add(row);
            }

            ButtonRow.Add(CreateButton("确认选择", WorkbenchPalette.DialogButton, WorkbenchPalette.AccentText, OnConfirmMulti));
            ButtonRow.Add(CreateButton("取消", WorkbenchPalette.DialogCancel, WorkbenchPalette.TextTertiary, OnCancel));
            return;
        }

        foreach (var option in options)
        {
            var captured = option;
            var button = CreateButton(option, WorkbenchPalette.DialogButton, WorkbenchPalette.TextPrimary, () =>
            {
                ChosenOption = captured;
                Finish();
            });
            button.Height = 38;
            button.Margin = new Thickness(0, 0, 0, 6);
            OptionsPanel.Add(button);
        }

        ButtonRow.Add(CreateButton("取消（不选择）", WorkbenchPalette.DialogCancel, WorkbenchPalette.TextTertiary, OnCancel));
        ButtonRow.Add(CreateButton("使用输入", WorkbenchPalette.DialogButton, WorkbenchPalette.AccentText, Finish));
    }

    private void OnConfirmMulti()
    {
        ChosenOptions = _checks
            .Where(check => check.IsChecked == true)
            .Select(check => (check.ParentElement as StackPanel)?.Children.OfType<TextBlock>().FirstOrDefault()?.Text)
            .Where(text => !string.IsNullOrEmpty(text))
            .Select(text => text!)
            .ToList();
        Finish();
    }

    private void OnCancel()
    {
        Cancelled = true;
        Finish();
    }

    private void Finish()
    {
        var text = FreeTextInput.Text?.Trim();
        FreeTextResponse = string.IsNullOrWhiteSpace(text) ? null : text;
        Completed?.Invoke(this, EventArgs.Empty);
    }

    private static Button CreateButton(string text, Color background, Color foreground, Action action)
    {
        var button = new Button
        {
            Text = text,
            Height = 34,
            Background = new SolidColorBrush(background),
            Foreground = foreground,
            BorderBrush = new SolidColorBrush(foreground),
            BorderThickness = new Thickness(1),
            FontSize = 10,
            CornerRadius = 3,
            Padding = new Thickness(10, 0, 10, 0),
        };
        button.Click += (_, _) => action();
        return button;
    }
}
