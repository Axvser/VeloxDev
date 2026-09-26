using Microsoft.Agents.AI;
using System;

namespace VeloxDev.AI;

/// <summary>
/// Turns on the Agent Framework's own OpenTelemetry instrumentation, in the same middleware slot
/// <see cref="AgentPipelineExtensions.UseAgentPipeline"/> uses.
/// </summary>
/// <remarks>
/// <para>
/// This library emits no telemetry of its own: <see cref="Pipelines.AgentEvent"/> and
/// <see cref="Pipelines.AgentTranscript"/> are the host-facing surface for what an agent did, and neither
/// leaves the process. Traces and metrics come from the framework, and this type exists to make the
/// recommended wiring one call with a safe default rather than a paragraph in a README.
/// </para>
/// <para>
/// <b>Prefer <see cref="AIAgentBuilder"/> over wrapping the finished agent.</b> Order matters to the
/// framework: instrumenting through the builder keeps the telemetry outside whatever middleware is applied
/// after it, which is what the framework's own logging and telemetry extensions do.
/// </para>
/// </remarks>
/// <seealso cref="AgentPipelineExtensions.UseAgentPipeline"/>
public static class AgentTelemetryExtensions
{
    /// <summary>
    /// Instruments every run of the agent <paramref name="builder"/> builds.
    /// </summary>
    /// <param name="builder">The builder to add the instrumentation to.</param>
    /// <param name="sourceName">
    /// The <see cref="System.Diagnostics.ActivitySource"/> name the host must also register with its
    /// <c>TracerProvider</c> / <c>MeterProvider</c>. <c>null</c> uses
    /// <see cref="OpenTelemetryAgent.DefaultSourceName"/>.
    /// </param>
    /// <param name="enableSensitiveData">
    /// Records prompts, responses, function-call arguments and their results. Defaults to <c>false</c>, and
    /// should stay that way outside development: see the remarks.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
    /// <remarks>
    /// <para>
    /// <b>The host still owns the pipeline.</b> This only creates the activities and metrics; registering
    /// <paramref name="sourceName"/> with a provider, choosing exporters, flushing and shutting them down
    /// are the application's, and the framework deliberately does not do them. A host that never registers
    /// the source gets no overhead beyond the framework's own checks.
    /// </para>
    /// <para>
    /// <b>Sensitive data is off unless asked for.</b> With <paramref name="enableSensitiveData"/> the spans
    /// carry the full text of the conversation — including anything a tool returned. That belongs in
    /// development and testing only; a production exporter with the wrong retention policy turns it into a
    /// data-leak path.
    /// </para>
    /// <para>
    /// <b>Instrument one side, not both.</b> Instrumenting the agent <i>and</i> the chat client under the
    /// same source duplicates every span — the chat context is captured by whichever layers are instrumented.
    /// </para>
    /// </remarks>
    public static AIAgentBuilder UseAgentTelemetry(
        this AIAgentBuilder builder, string? sourceName = null, bool enableSensitiveData = false)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));

        // 读框架的默认源名，而不是把它的字面量抄一份：它将来改了，宿主照它注册的 AddSource 仍然对得上。
        // MAAI001 只落在这一行，且本方法的参数类型是 string? —— 实验面不进公开签名，与
        // WorkflowAgentScope 隔离 Compaction 的既有做法一致。它若被移除，这里是编译期报错，不会静默走偏。
#pragma warning disable MAAI001
        var source = sourceName ?? OpenTelemetryAgent.DefaultSourceName;
#pragma warning restore MAAI001

        return builder.UseOpenTelemetry(
            source, agent => agent.EnableSensitiveData = enableSensitiveData);
    }

    /// <summary>Instruments <paramref name="agent"/> directly, for callers not already using a builder.</summary>
    /// <param name="agent">The agent to instrument.</param>
    /// <param name="sourceName">
    /// The source name the host must also register; <c>null</c> uses
    /// <see cref="OpenTelemetryAgent.DefaultSourceName"/>.
    /// </param>
    /// <param name="enableSensitiveData">See <see cref="UseAgentTelemetry"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="agent"/> is <c>null</c>.</exception>
    public static AIAgent WithAgentTelemetry(
        this AIAgent agent, string? sourceName = null, bool enableSensitiveData = false)
        => (agent ?? throw new ArgumentNullException(nameof(agent)))
            .AsBuilder()
            .UseAgentTelemetry(sourceName, enableSensitiveData)
            .Build();
}
