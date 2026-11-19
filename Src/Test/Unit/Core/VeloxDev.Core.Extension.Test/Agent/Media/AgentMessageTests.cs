using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using VeloxDev.AI;
using VeloxDev.AI.Pipelines;

namespace VeloxDev.Core.Extension.Test.Agent.Media;

/// <summary>
/// The message and run helpers: the text block comes first, the images follow in order, and the pipeline sees
/// the attachments so the transcript can show what the user sent.
/// </summary>
[TestClass]
public class AgentMessageTests
{
    private sealed class CapturingChatClient : IChatClient
    {
        public IReadOnlyList<ChatMessage>? Last { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Last = messages.ToList();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Last = messages.ToList();
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private sealed class MediaChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [
                new TextContent("ok"),
                new DataContent(MediaFixtures.Png1x1(), "image/png") { Name = "result.png" },
            ])));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant,
            [
                new DataContent(MediaFixtures.Png1x1(), "image/png") { Name = "result.png" },
            ]);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private static AgentMedia Png(string name) => AgentMedia.FromBytes(MediaFixtures.Png1x1(), name: name);

    [TestMethod]
    public void User_PutsTextFirst_ThenImagesInOrder()
    {
        var first = Png("a.png");
        var second = Png("b.png");

        var message = AgentMessage.User("look", first, second);

        Assert.AreEqual(ChatRole.User, message.Role);
        Assert.HasCount(3, message.Contents);
        Assert.IsInstanceOfType<TextContent>(message.Contents[0]);
        var a = message.Contents[1] as DataContent;
        var b = message.Contents[2] as DataContent;
        Assert.AreEqual("a.png", a!.Name);
        Assert.AreEqual("b.png", b!.Name);
        Assert.AreEqual("image/png", a.MediaType);
    }

    [TestMethod]
    public void User_WithNoTextAndNoImage_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => AgentMessage.User(""));
    }

    [TestMethod]
    public async Task RunWithMedia_SendsDataContent_OnTheUserMessage()
    {
        var client = new CapturingChatClient();
        var agent = client.AsAIAgent(Array.Empty<AIContextProvider>());

        await agent.RunWithMediaAsync("check", new[] { Png("frame.png") });

        var sent = client.Last!.Last();
        Assert.AreEqual(ChatRole.User, sent.Role);
        Assert.IsInstanceOfType<TextContent>(sent.Contents[0]);
        var image = sent.Contents[1] as DataContent;
        Assert.IsNotNull(image);
        Assert.AreEqual("image/png", image!.MediaType);
        Assert.AreEqual("frame.png", image.Name);
        CollectionAssert.AreEqual(Png("frame.png").Bytes.ToArray(), image.Data.ToArray());
    }

    [TestMethod]
    public async Task RunStreamingWithMedia_SendsDataContent_OnTheUserMessage()
    {
        var client = new CapturingChatClient();
        var agent = client.AsAIAgent(Array.Empty<AIContextProvider>());

        await foreach (var _ in agent.RunStreamingWithMediaAsync("check", new[] { Png("frame.png") }))
        {
        }

        var sent = client.Last!.Last();
        Assert.AreEqual(ChatRole.User, sent.Role);
        Assert.IsInstanceOfType<DataContent>(sent.Contents[1]);
    }

    [TestMethod]
    public async Task RunWithMedia_SendsImagesAudioAndDocuments_InOrder()
    {
        var client = new CapturingChatClient();
        var agent = client.AsAIAgent(Array.Empty<AIContextProvider>());
        var image = Png("frame.png");
        var audio = AgentMedia.FromBytes(MediaFixtures.Mp3(), "audio/mpeg", "note.mp3");
        var pdf = AgentMedia.FromBytes(MediaFixtures.Pdf(), "application/pdf", "spec.pdf");

        await agent.RunWithMediaAsync("review", new[] { image, audio, pdf });

        var contents = client.Last!.Last().Contents;
        Assert.HasCount(4, contents);
        Assert.IsInstanceOfType<TextContent>(contents[0]);
        Assert.AreEqual("image/png", ((DataContent)contents[1]).MediaType);
        Assert.AreEqual("audio/mpeg", ((DataContent)contents[2]).MediaType);
        Assert.AreEqual("application/pdf", ((DataContent)contents[3]).MediaType);
        Assert.AreEqual("note.mp3", ((DataContent)contents[2]).Name);
        Assert.AreEqual("spec.pdf", ((DataContent)contents[3]).Name);
    }

    [TestMethod]
    public void FromContents_PassesRawBlocksThrough()
    {
        var message = AgentMessage.FromContents(
            new TextContent("raw"),
            new DataContent(new byte[] { 1, 2, 3 }, "application/octet-stream"));

        Assert.HasCount(2, message.Contents);
        Assert.IsInstanceOfType<TextContent>(message.Contents[0]);
    }

    [TestMethod]
    public async Task RunWithTextMedia_InlinesItAsTextContent()
    {
        var client = new CapturingChatClient();
        var agent = client.AsAIAgent(Array.Empty<AIContextProvider>());
        var markdown = AgentMedia.FromBytes(
            Encoding.UTF8.GetBytes("The secret word is MARKDOWN."), "text/markdown", "probe.md");

        await agent.RunWithMediaAsync("What is the secret word?", new[] { markdown });

        var contents = client.Last!.Last().Contents;
        Assert.HasCount(2, contents);
        Assert.IsInstanceOfType<TextContent>(contents[1]);
        var file = (TextContent)contents[1];
        StringAssert.Contains(file.Text, "probe.md");
        StringAssert.Contains(file.Text, "MARKDOWN");
        Assert.IsFalse(contents.Any(c => c is DataContent),
            "a text file must not travel as DataContent: the OpenAI chat adapter drops it");
    }

    [TestMethod]
    public async Task RunWithMedia_NormalisesThroughTheTranscoder_BeforeSending()
    {
        var client = new CapturingChatClient();
        var agent = client.AsAIAgent(Array.Empty<AIContextProvider>());
        var options = AgentMediaOptions.CreateDefault();
        options.Transcoder = new DelegateAgentMediaTranscoder((source, request, ct) =>
            Task.FromResult(AgentMedia.FromBytes(MediaFixtures.Png(1, 1), "image/png", "converted.png")));

        await agent.RunWithMediaAsync(
            "check", new[] { AgentMedia.FromBytes(MediaFixtures.Bmp(10, 10), name: "frame.bmp") }, options);

        var image = client.Last!.Last().Contents[1] as DataContent;
        Assert.IsNotNull(image);
        Assert.AreEqual("image/png", image!.MediaType);
        Assert.AreEqual("converted.png", image.Name);
    }

    [TestMethod]
    public async Task Pipeline_ReportsMediaProducedByTheModel()
    {
        var events = new List<AgentEvent>();
        var pipeline = new AgentPipeline().Use((agentEvent, next, _) =>
        {
            events.Add(agentEvent);
            return next(agentEvent);
        });
        var agent = new MediaChatClient()
            .AsAIAgent(Array.Empty<AIContextProvider>())
            .WithPipeline(pipeline);

        await foreach (var _ in agent.RunStreamingAsync("draw")) { }

        var received = events.OfType<AgentMediaReceived>().Single();
        Assert.AreEqual(AgentMediaKind.Image, received.Attachment.Kind);
        Assert.AreEqual("image/png", received.Attachment.MediaType);
        Assert.AreEqual("result.png", received.Attachment.Name);
        Assert.IsInstanceOfType<DataContent>(received.Content);
    }

    [TestMethod]
    public async Task Pipeline_ReportsTheAttachments_AndTheTranscriptRendersThem()
    {
        var transcript = new AgentTranscript();
        var pipeline = new AgentPipeline().Use(new TextPipeline(() => transcript));
        var client = new CapturingChatClient();
        var agent = client.AsAIAgent(Array.Empty<AIContextProvider>()).WithPipeline(pipeline);

        await agent.RunWithMediaAsync("check", new[] { Png("frame.png") });

        var user = transcript.Entries.First(e => e.Role == AgentTranscriptRole.User);
        Assert.HasCount(1, user.Attachments);
        Assert.AreEqual("frame.png", user.Attachments[0].Name);
        Assert.AreEqual("image/png", user.Attachments[0].MediaType);
        Assert.AreEqual(MediaFixtures.Png1x1().Length, user.Attachments[0].ByteCount);

        var markdown = transcript.ToMarkdown();
        StringAssert.Contains(markdown, "附件：frame.png");
        StringAssert.Contains(markdown, "image/png");
    }

    [TestMethod]
    public void Transcript_JsonRoundTripsAttachments()
    {
        var transcript = new AgentTranscript();
        transcript.AddUser("check", [new AgentAttachment("frame.png", "image/png", 128)]);

        var restored = new AgentTranscript();
        restored.Restore(transcript.ToJson());

        var entry = restored.Entries.Single();
        Assert.HasCount(1, entry.Attachments);
        Assert.AreEqual("frame.png", entry.Attachments[0].Name);
        Assert.AreEqual("image/png", entry.Attachments[0].MediaType);
        Assert.AreEqual(128L, entry.Attachments[0].ByteCount);
    }
}
