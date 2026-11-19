namespace VeloxDev.SamplerTest;

/// <summary>
/// Every sampler the adapters register, gathered from one file per adapter so each table can be written and reviewed
/// on its own.
/// </summary>
internal static class AdapterSamplerEntries
{
    internal static IReadOnlyList<SamplerEntry> All { get; } =
    [
        .. WpfEntries.All,
        .. WinFormsEntries.All,
        .. AvaloniaEntries.All,
        .. RazorEntries.All,
        .. JaliumEntries.All,
        .. WinUiEntries.All,
        .. MauiEntries.All,
    ];
}
