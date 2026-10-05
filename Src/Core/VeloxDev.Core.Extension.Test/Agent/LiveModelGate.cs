using System;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;

namespace VeloxDev.Core.Extension.Test.Agent;

/// <summary>
/// The one switch that decides whether the live-model tests run.
/// </summary>
/// <remarks>
/// <para>
/// It used to be "is there an API key in the environment", and that turned out to be the wrong question. A
/// developer who holds a key — which is everyone who can usefully run these — got every live test on every full
/// run: seven real model calls, each one costing money and taking seconds, and each one only as green as the
/// model's answer that time. The suite stopped being deterministic for the people most likely to run it.
/// </para>
/// <para>
/// So the key stays where it belongs — it is the credential, not the switch — and <c>VELOXDEV_LIVE</c> decides.
/// Unset, every live test is <c>Inconclusive</c> and the offline suite is what runs. Set it (to anything but
/// <c>0</c>) and the live tests join in, still needing the key.
/// </para>
/// <para>
/// <c>Src/Verification/README.md</c> is where the other out-of-band runs are described; this one is small enough
/// to live beside the tests it gates.
/// </para>
/// </remarks>
internal static class LiveModelGate
{
    /// <summary>The switch. Anything but empty or <c>0</c> turns the live tests on.</summary>
    internal const string Switch = "VELOXDEV_LIVE";

    /// <summary>The credential. Present or not, it does not by itself start a live run.</summary>
    internal const string KeyVariable = "API_KEY_DEEPSEEK";

    private const string Endpoint = "https://api.deepseek.com";
    private const string Model = "deepseek-v4-flash";

    /// <summary>Whether the switch asked for a live run at all.</summary>
    internal static bool Enabled => Environment.GetEnvironmentVariable(Switch) is { Length: > 0 } and not "0";

    /// <summary>
    /// The client for a live test, or <see langword="null"/> when the switch is off or the key is missing.
    /// </summary>
    /// <returns>The client, or <see langword="null"/>.</returns>
    internal static IChatClient? ClientOrNull()
    {
        if (!Enabled) return null;

        var key = Environment.GetEnvironmentVariable(KeyVariable);
        if (string.IsNullOrWhiteSpace(key)) return null;

        return new OpenAIClient(
            new ApiKeyCredential(key),
            new OpenAIClientOptions { Endpoint = new Uri(Endpoint) })
            .GetChatClient(Model)
            .AsIChatClient();
    }

    /// <summary>What an <c>Inconclusive</c> live test tells whoever is looking at the run.</summary>
    internal static string Skipped => $"Set {Switch}=1 (with {KeyVariable} in the environment) to run this against a real model.";
}
