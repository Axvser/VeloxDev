using System.Reflection;

namespace VeloxDev.TransitionSystem.Abstractions;

/// <summary>
/// Builds a per-animation sampler that interpolates a struct (value type) implementing <see cref="ISampleable"/>:
/// each declared member is interpolated by its own sampler, then the struct is reconstructed through
/// <see cref="ISampleable.CreateFrameValue"/> (a compile-time constructor call — zero reflection).
/// </summary>
internal static class StructAssembler
{
    /// <summary>Returns a struct-assembling sampler, or null when the struct cannot be assembled (unresolvable
    /// member sampler or missing member value) — the property is skipped.</summary>
    public static ISampler? Create(ITransitionProperty property, ISampleable sampleable, object? start, object? end)
    {
        try
        {
            var members = sampleable.GetAnimatableMembers();
            if (members.Count == 0) return null;

            var memberSamplers = new ISampler[members.Count];
            var memberStarts = new object?[members.Count];
            var memberEnds = new object?[members.Count];
            for (var i = 0; i < members.Count; i++)
            {
                if (!InterpolatorCore.TryGetInterpolator(members[i].PropertyType, out var sampler) || sampler is null)
                {
                    return null; // a member cannot be interpolated → skip the property
                }
                memberSamplers[i] = sampler;
                memberStarts[i] = members[i].GetValue(start);
                memberEnds[i] = members[i].GetValue(end);
            }

            return new StructAssemblerSampler(sampleable, members, memberSamplers, memberStarts, memberEnds);
        }
        catch
        {
            return null; // member access failure → skip (never break the animation)
        }
    }
}

internal sealed class StructAssemblerSampler : ISampler
{
    private readonly ISampleable _sampleable;
    private readonly IReadOnlyList<ITransitionProperty> _members;
    private readonly ISampler[] _memberSamplers;
    private readonly object?[] _memberStarts;
    private readonly object?[] _memberEnds;
    private readonly object?[] _workings;
    private readonly CaptureProperty[] _captures;
    private readonly object?[] _values;

    public StructAssemblerSampler(
        ISampleable sampleable,
        IReadOnlyList<ITransitionProperty> members,
        ISampler[] memberSamplers,
        object?[] memberStarts,
        object?[] memberEnds)
    {
        _sampleable = sampleable;
        _members = members;
        _memberSamplers = memberSamplers;
        _memberStarts = memberStarts;
        _memberEnds = memberEnds;
        _workings = new object?[members.Count];
        _captures = new CaptureProperty[members.Count];
        _values = new object?[members.Count];
        for (var i = 0; i < members.Count; i++) _captures[i] = new CaptureProperty();
    }

    public object? NormalizeStart(object? start, object? end, object? options) => start;
    public object? NormalizeEnd(object? start, object? end, object? options) => end;

    public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
    {
        if (t <= 0) { property.SetValue(target, start); return; }
        if (t >= 1) { property.SetValue(target, end); return; }

        for (var i = 0; i < _members.Count; i++)
        {
            _memberSamplers[i].InsertFrame(null!, _captures[i], ref _workings[i], _memberStarts[i], _memberEnds[i], null, t);
            _values[i] = _captures[i].Value;
        }

        property.SetValue(target, _sampleable.CreateFrameValue(_values));
    }
}

/// <summary>A fake <see cref="ITransitionProperty"/> that captures the value a member sampler writes, so the struct
/// assembler can collect each interpolated member without writing to a real path (which would hit a boxed copy for a
/// value-type intermediate).</summary>
internal sealed class CaptureProperty : ITransitionProperty
{
    public object? Value;

    public string Path => "$capture";
    public Type PropertyType { get; set; } = typeof(object);
    public PropertyInfo PropertyInfo => throw new NotSupportedException("CaptureProperty has no backing PropertyInfo.");
    public IReadOnlyList<PropertyInfo> Segments => [];
    public bool CanRead => true;
    public bool CanWrite => true;
    public object? GetValue(object target) => Value;
    public bool SetValue(object target, object? value) { Value = value; return true; }
}
