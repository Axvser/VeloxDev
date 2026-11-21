using System.Drawing;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml;

/// <summary>Receives the errors a binding publishes on a control.</summary>
/// <param name="sender">The <see cref="Validation"/> class.</param>
/// <param name="e">The error that was added or removed.</param>
public sealed class ValidationErrorEventArgs : EventArgs
{
    /// <summary>Creates arguments for one error entering or leaving a control.</summary>
    /// <param name="error">The error involved.</param>
    /// <param name="added">Whether the error was added; otherwise it was removed.</param>
    public ValidationErrorEventArgs(ValidationError error, bool added)
    {
        Error = error;
        Added = added;
    }

    /// <summary>The error involved.</summary>
    public ValidationError Error { get; }

    /// <summary>Whether the error was added; <see langword="false"/> means it was removed.</summary>
    public bool Added { get; }
}

/// <summary>One reason a value failed validation.</summary>
/// <remarks>
/// The binding engine publishes these through <see cref="Validation.ErrorsProperty"/>; a rule or a source error
/// interface may produce them.
/// </remarks>
public sealed class ValidationError
{
    /// <summary>Creates an error produced by a validation rule.</summary>
    /// <param name="rule">The rule that produced the error, or <see langword="null"/> for a source error.</param>
    /// <param name="errorContent">The message or object describing the failure.</param>
    public ValidationError(ValidationRule? rule, object? errorContent)
    {
        Rule = rule;
        ErrorContent = errorContent;
    }

    /// <summary>Creates an error produced by an exception.</summary>
    /// <param name="rule">The rule that produced the error, or <see langword="null"/>.</param>
    /// <param name="exception">The exception that failed validation.</param>
    public ValidationError(ValidationRule? rule, Exception exception)
        : this(rule, exception?.Message)
    {
        Exception = exception;
    }

    /// <summary>The rule that produced the error, or <see langword="null"/> when a source error interface did.</summary>
    public ValidationRule? Rule { get; }

    /// <summary>The value describing the failure, usually a string.</summary>
    public object? ErrorContent { get; }

    /// <summary>The exception behind the error, or <see langword="null"/>.</summary>
    public Exception? Exception { get; }

    /// <summary>The error text: the string content, the exception message, or the content's text.</summary>
    public string? Message => ErrorContent as string ?? Exception?.Message ?? ErrorContent?.ToString();
}

/// <summary>One rule a <see cref="Binding"/> runs over each value it converts.</summary>
/// <remarks>
/// A rule sees the converted target value, so a range rule is written against the type the target property accepts.
/// Returning <see cref="ValidationResult.ValidResult"/> clears the rule's contribution; returning an invalid result
/// keeps the value and marks the target, matching WPF.
/// </remarks>
public abstract class ValidationRule
{
    /// <summary>Whether the value is acceptable.</summary>
    /// <param name="value">The value after conversion to the target property type.</param>
    /// <param name="cultureInfo">The culture in use for the conversion.</param>
    /// <returns>The outcome of the rule.</returns>
    public abstract ValidationResult Validate(object? value, CultureInfo cultureInfo);
}

/// <summary>The outcome of one <see cref="ValidationRule"/>.</summary>
public sealed class ValidationResult
{
    /// <summary>The shared success value.</summary>
    public static ValidationResult ValidResult { get; } = new(true, null);

    /// <summary>Creates a result.</summary>
    /// <param name="isValid">Whether the value passed.</param>
    /// <param name="errorContent">The message or object describing a failure; ignored when the value passed.</param>
    public ValidationResult(bool isValid, object? errorContent)
    {
        IsValid = isValid;
        ErrorContent = errorContent;
    }

    /// <summary>Whether the value passed.</summary>
    public bool IsValid { get; }

    /// <summary>The message or object describing a failure.</summary>
    public object? ErrorContent { get; }
}

/// <summary>
/// Attached validation state for any <see cref="System.Windows.Forms.Control"/>: bindings publish errors here, and styles or triggers read
/// <see cref="HasErrorProperty"/> to react.
/// </summary>
/// <remarks>
/// <para>
/// The properties are registered as attached dependency properties, so they work on the XAML
/// <see cref="VeloxDev.WinForms.Xaml.Controls.Control"/> and on native WinForms controls alike. A value written by a binding never
/// blocks the write itself: WPF keeps the rejected value and marks the target, and so does this layer.
/// </para>
/// <para>
/// <see cref="ErrorBrushProperty"/> and <see cref="ErrorTemplateProperty"/> are hints only. No visual control reads them
/// on its own; a style or trigger decides what a failing control looks like.
/// </para>
/// </remarks>
public static class Validation
{
    /// <summary>Whether the bound control currently holds at least one validation error.</summary>
    public static readonly DependencyProperty HasErrorProperty = DependencyProperty.RegisterAttached(
        "HasError",
        typeof(bool),
        typeof(Validation),
        new PropertyMetadata(false));

    /// <summary>The errors currently published for the control, or <see langword="null"/> when there are none.</summary>
    public static readonly DependencyProperty ErrorsProperty = DependencyProperty.RegisterAttached(
        "Errors",
        typeof(IReadOnlyList<ValidationError>),
        typeof(Validation),
        new PropertyMetadata(null) { AffectsRender = false });

    /// <summary>An optional brush a style can bind to a failing control's border.</summary>
    public static readonly DependencyProperty ErrorBrushProperty = DependencyProperty.RegisterAttached(
        "ErrorBrush",
        typeof(Color),
        typeof(Validation),
        new PropertyMetadata(Color.Empty));

    /// <summary>An optional template object a style can use as the failing control's error presentation.</summary>
    public static readonly DependencyProperty ErrorTemplateProperty = DependencyProperty.RegisterAttached(
        "ErrorTemplate",
        typeof(object),
        typeof(Validation),
        new PropertyMetadata(null) { AffectsRender = false });

    /// <summary>Raised when an error is added to or removed from a control and the binding asked for notification.</summary>
    public static event EventHandler<ValidationErrorEventArgs>? Error;

    /// <summary>Reads whether <paramref name="control"/> has an error.</summary>
    /// <param name="control">The control to inspect.</param>
    public static bool GetHasError(System.Windows.Forms.Control control)
    {
        if (control is null) throw new ArgumentNullException(nameof(control));
        return (bool)HasErrorProperty.GetValue(control)!;
    }

    /// <summary>Writes whether <paramref name="control"/> has an error.</summary>
    /// <param name="control">The control to mark.</param>
    /// <param name="value">The new flag.</param>
    public static void SetHasError(System.Windows.Forms.Control control, bool value)
    {
        if (control is null) throw new ArgumentNullException(nameof(control));
        HasErrorProperty.SetValue(control, value);
    }

    /// <summary>Reads the errors published for <paramref name="control"/>.</summary>
    /// <param name="control">The control to inspect.</param>
    public static IReadOnlyList<ValidationError>? GetErrors(System.Windows.Forms.Control control)
    {
        if (control is null) throw new ArgumentNullException(nameof(control));
        return (IReadOnlyList<ValidationError>?)ErrorsProperty.GetValue(control);
    }

    /// <summary>Writes the errors published for <paramref name="control"/>.</summary>
    /// <param name="control">The control to mark.</param>
    /// <param name="value">The errors, or <see langword="null"/> to clear them.</param>
    public static void SetErrors(System.Windows.Forms.Control control, IReadOnlyList<ValidationError>? value)
    {
        if (control is null) throw new ArgumentNullException(nameof(control));
        ErrorsProperty.SetValue(control, value);
    }

    /// <summary>Reads the optional error brush.</summary>
    /// <param name="control">The control to inspect.</param>
    public static Color GetErrorBrush(System.Windows.Forms.Control control)
    {
        if (control is null) throw new ArgumentNullException(nameof(control));
        return (Color)ErrorBrushProperty.GetValue(control)!;
    }

    /// <summary>Writes the optional error brush.</summary>
    /// <param name="control">The control to mark.</param>
    /// <param name="value">The brush colour.</param>
    public static void SetErrorBrush(System.Windows.Forms.Control control, Color value)
    {
        if (control is null) throw new ArgumentNullException(nameof(control));
        ErrorBrushProperty.SetValue(control, value);
    }

    /// <summary>Reads the optional error template.</summary>
    /// <param name="control">The control to inspect.</param>
    public static object? GetErrorTemplate(System.Windows.Forms.Control control)
    {
        if (control is null) throw new ArgumentNullException(nameof(control));
        return ErrorTemplateProperty.GetValue(control);
    }

    /// <summary>Writes the optional error template.</summary>
    /// <param name="control">The control to mark.</param>
    /// <param name="value">The template object, or <see langword="null"/>.</param>
    public static void SetErrorTemplate(System.Windows.Forms.Control control, object? value)
    {
        if (control is null) throw new ArgumentNullException(nameof(control));
        ErrorTemplateProperty.SetValue(control, value);
    }

    private static readonly ConditionalWeakTable<System.Windows.Forms.Control, ValidationAggregate> Aggregates = new();

    internal static void PublishErrors(
        System.Windows.Forms.Control target,
        object source,
        IReadOnlyList<ValidationError> errors,
        bool notify)
    {
        if (target is null || target.IsDisposed || target.Disposing)
        {
            return;
        }

        var aggregate = Aggregates.GetOrCreateValue(target);
        if (errors.Count == 0)
        {
            aggregate.Sources.Remove(source);
        }
        else
        {
            aggregate.Sources[source] = errors;
        }

        PublishAggregate(target, aggregate, notify);
    }

    internal static void ClearErrors(System.Windows.Forms.Control target, object source)
    {
        if (target is null || target.IsDisposed || target.Disposing)
        {
            return;
        }

        if (!Aggregates.TryGetValue(target, out var aggregate))
        {
            return;
        }

        aggregate.Sources.Remove(source);
        PublishAggregate(target, aggregate, notify: false);
    }

    private static void PublishAggregate(
        System.Windows.Forms.Control target,
        ValidationAggregate aggregate,
        bool notify)
    {
        var previous = GetErrors(target);
        var next = Flatten(aggregate);
        if (SameErrors(previous, next))
        {
            return;
        }

        // 先写 Errors 再写 HasError：监听 HasError 的触发器读到的必须是新错误集。
        SetErrors(target, next.Count == 0 ? null : next);
        SetHasError(target, next.Count > 0);

        if (notify)
        {
            RaiseDiff(previous, next);
        }
    }

    private static List<ValidationError> Flatten(ValidationAggregate aggregate)
    {
        var all = new List<ValidationError>();
        foreach (var errors in aggregate.Sources.Values)
        {
            all.AddRange(errors);
        }

        return all;
    }

    private sealed class ValidationAggregate
    {
        public Dictionary<object, IReadOnlyList<ValidationError>> Sources { get; } = [];
    }

    private static void RaiseDiff(IReadOnlyList<ValidationError>? previous, IReadOnlyList<ValidationError> next)
    {
        if (previous is not null)
        {
            for (var index = 0; index < previous.Count; index++)
            {
                if (!Contains(next, previous[index]))
                {
                    Error?.Invoke(null, new ValidationErrorEventArgs(previous[index], added: false));
                }
            }
        }

        for (var index = 0; index < next.Count; index++)
        {
            if (previous is null || !Contains(previous, next[index]))
            {
                Error?.Invoke(null, new ValidationErrorEventArgs(next[index], added: true));
            }
        }
    }

    private static bool Contains(IReadOnlyList<ValidationError> list, ValidationError error)
    {
        for (var index = 0; index < list.Count; index++)
        {
            var candidate = list[index];
            if (ReferenceEquals(candidate, error))
            {
                return true;
            }

            if (candidate is not null
                && ReferenceEquals(candidate.Rule, error.Rule)
                && Equals(candidate.ErrorContent, error.ErrorContent))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SameErrors(IReadOnlyList<ValidationError>? left, IReadOnlyList<ValidationError> right)
    {
        if (left is null || left.Count != right.Count)
        {
            return left is null && right.Count == 0;
        }

        for (var index = 0; index < right.Count; index++)
        {
            var a = left[index];
            var b = right[index];
            if (!ReferenceEquals(a, b)
                && (a is null
                    || b is null
                    || !ReferenceEquals(a.Rule, b.Rule)
                    || !Equals(a.ErrorContent, b.ErrorContent)))
            {
                return false;
            }
        }

        return true;
    }
}
