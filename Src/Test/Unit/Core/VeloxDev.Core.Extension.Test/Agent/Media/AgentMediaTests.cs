using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using VeloxDev.AI;

namespace VeloxDev.Core.Extension.Test.Agent.Media;

/// <summary>
/// The media value object: what it sniffs, what it corrects, what it refuses, and what the transcode seam
/// owes. The fixtures are header-valid containers, which is the part this layer reads.
/// </summary>
[TestClass]
public class AgentMediaTests
{
    [TestMethod]
    public void FromBytes_ReadsPngFormatMediaTypeAndDimensions()
    {
        var image = AgentMedia.FromBytes(MediaFixtures.Png1x1(), name: "frame.png");

        Assert.AreEqual(AgentMediaFormat.Png, image.Format);
        Assert.AreEqual("image/png", image.MediaType);
        Assert.AreEqual(1, image.Width);
        Assert.AreEqual(1, image.Height);
        Assert.AreEqual("frame.png", image.Name);
    }

    [TestMethod]
    public void FromBytes_DeclaredJpegOverPngBytes_UsesTheBytes()
    {
        var image = AgentMedia.FromBytes(MediaFixtures.Png1x1(), "image/jpeg", "frame.png");

        Assert.AreEqual("image/png", image.MediaType, "the bytes decide the media type");
        Assert.AreEqual("image/jpeg", image.DeclaredMediaType);
    }

    [TestMethod]
    public void StrictMediaType_RejectsTheContradiction()
    {
        var image = AgentMedia.FromBytes(MediaFixtures.Png1x1(), "image/jpeg");
        var options = AgentMediaOptions.CreateDefault();
        options.StrictMediaType = true;

        var error = Assert.ThrowsExactly<AgentMediaException>(() => image.Normalize(options));
        Assert.AreEqual(AgentMediaError.MediaTypeMismatch, error.Error);
    }

    [TestMethod]
    public void FromBytes_RejectsEmptyAndUnknownBytes()
    {
        Assert.AreEqual(AgentMediaError.Empty,
            Assert.ThrowsExactly<AgentMediaException>(() => AgentMedia.FromBytes(ReadOnlyMemory<byte>.Empty)).Error);

        var binary = AgentMedia.FromBytes(MediaFixtures.Unknown());
        Assert.AreEqual(AgentMediaKind.Binary, binary.Kind);
        Assert.AreEqual("application/octet-stream", binary.MediaType);

        // A declared image with no image magic is kept for the policy layer; it must be transcoded, never
        // sent as-is under the declared type.
        var claimed = AgentMedia.FromBytes(MediaFixtures.Unknown(), "image/png");
        Assert.AreEqual(AgentMediaKind.Image, claimed.Kind);
        Assert.AreEqual(AgentMediaFormat.Unknown, claimed.Format);
        Assert.AreEqual(AgentMediaError.UnknownFormat,
            Assert.ThrowsExactly<AgentMediaException>(() => claimed.Normalize()).Error);
    }

    [TestMethod]
    public async Task UnrecognizedImage_IsHandedToTheTranscoder()
    {
        var source = AgentMedia.FromBytes(MediaFixtures.Unknown(), "image/png", "icon.ico");
        var options = AgentMediaOptions.CreateDefault();
        var transcoder = new FixedTranscoder(AgentMedia.FromBytes(MediaFixtures.Png(1, 1)));
        options.Transcoder = transcoder;

        var converted = await source.NormalizeAsync(options);

        Assert.AreEqual(AgentMediaTranscodeReason.UnrecognizedFormat, transcoder.LastRequest!.Reason);
        Assert.AreEqual("image/png", transcoder.LastRequest.TargetMediaType);
        Assert.AreEqual(AgentMediaFormat.Png, converted.Format);
    }

    [TestMethod]
    public void HeaderReader_KnowsJpegGifWebpAndBmpDimensions()
    {
        AssertDimensions(MediaFixtures.Jpeg(320, 200), AgentMediaFormat.Jpeg, 320, 200);
        AssertDimensions(MediaFixtures.Gif(64, 32), AgentMediaFormat.Gif, 64, 32);
        AssertDimensions(MediaFixtures.Webp(1280, 720), AgentMediaFormat.Webp, 1280, 720);
        AssertDimensions(MediaFixtures.Bmp(800, 600), AgentMediaFormat.Bmp, 800, 600);
    }

    [TestMethod]
    public void AnimatedWebp_IsDetected_AndRejectedByDefault()
    {
        var image = AgentMedia.FromBytes(MediaFixtures.Webp(100, 100, animated: true));
        Assert.IsTrue(image.IsAnimated);

        var error = Assert.ThrowsExactly<AgentMediaException>(() => image.Normalize());
        Assert.AreEqual(AgentMediaError.Animated, error.Error);
    }

    [TestMethod]
    public void DataUri_RoundTripsTheBytes()
    {
        var source = AgentMedia.FromBytes(MediaFixtures.Png1x1(), name: "frame.png");

        var parsed = AgentMedia.FromDataUri(source.ToDataUri(), name: "frame.png");

        CollectionAssert.AreEqual(source.Bytes.ToArray(), parsed.Bytes.ToArray());
        Assert.AreEqual("image/png", parsed.MediaType);
        Assert.AreEqual("frame.png", parsed.Name);
    }

    [TestMethod]
    public void DataUri_PercentEncodedPayload_IsDecoded()
    {
        var bytes = MediaFixtures.Png(2, 3);
        var uri = "data:image/png," + MediaFixtures.PercentEncode(bytes);

        var image = AgentMedia.FromDataUri(uri);

        Assert.AreEqual(AgentMediaFormat.Png, image.Format);
        Assert.AreEqual(2, image.Width);
        Assert.AreEqual(3, image.Height);
        CollectionAssert.AreEqual(bytes, image.Bytes.ToArray());
    }

    [TestMethod]
    public void DataContent_CarriesTheCanonicalTypeAndName()
    {
        var image = AgentMedia.FromBytes(MediaFixtures.Png1x1(), "image/jpg", "frame.png");

        var content = image.ToDataContent();

        Assert.AreEqual("image/png", content.MediaType);
        Assert.AreEqual("frame.png", content.Name);
        CollectionAssert.AreEqual(image.Bytes.ToArray(), content.Data.ToArray());
    }

    [TestMethod]
    public void Normalize_RejectsAnUnlistedMediaType_WithoutATranscoder()
    {
        var image = AgentMedia.FromBytes(MediaFixtures.Bmp(10, 10), name: "frame.bmp");

        var error = Assert.ThrowsExactly<AgentMediaException>(() => image.Normalize());

        Assert.AreEqual(AgentMediaError.UnsupportedMediaType, error.Error);
        StringAssert.Contains(error.Message, "image/bmp");
    }

    [TestMethod]
    public void Normalize_RejectsOversizeBytesAndDimensions()
    {
        var bytesOptions = AgentMediaOptions.CreateDefault();
        bytesOptions.MaxBytesByKind[AgentMediaKind.Image] = 8;
        Assert.AreEqual(AgentMediaError.TooLarge,
            Assert.ThrowsExactly<AgentMediaException>(() => AgentMedia.FromBytes(MediaFixtures.Png1x1()).Normalize(bytesOptions)).Error);

        var dimensionOptions = AgentMediaOptions.CreateDefault();
        dimensionOptions.MaxWidth = 100;
        Assert.AreEqual(AgentMediaError.TooWide,
            Assert.ThrowsExactly<AgentMediaException>(() => AgentMedia.FromBytes(MediaFixtures.Png(640, 480)).Normalize(dimensionOptions)).Error);
    }

    [TestMethod]
    public void Normalize_WithATranscoderConfigured_AsksForTheAsyncPath()
    {
        var options = AgentMediaOptions.CreateDefault();
        options.Transcoder = new FixedTranscoder(AgentMedia.FromBytes(MediaFixtures.Png(1, 1)));
        var image = AgentMedia.FromBytes(MediaFixtures.Bmp(10, 10));

        var error = Assert.ThrowsExactly<AgentMediaException>(() => image.Normalize(options));

        Assert.AreEqual(AgentMediaError.TranscoderRequired, error.Error);
    }

    [TestMethod]
    public async Task NormalizeAsync_CallsTheTranscoder_WithTheReasonAndTarget()
    {
        var transcoder = new FixedTranscoder(AgentMedia.FromBytes(MediaFixtures.Png(1, 1)));
        var options = AgentMediaOptions.CreateDefault();
        options.Transcoder = transcoder;
        var image = AgentMedia.FromBytes(MediaFixtures.Bmp(10, 10), name: "frame.bmp");

        var converted = await image.NormalizeAsync(options);

        Assert.AreEqual(AgentMediaFormat.Png, converted.Format);
        Assert.IsNotNull(transcoder.LastRequest);
        Assert.AreEqual(AgentMediaTranscodeReason.UnsupportedMediaType, transcoder.LastRequest!.Reason);
        Assert.AreEqual("image/png", transcoder.LastRequest.TargetMediaType);
        Assert.AreEqual("frame.png", converted.Name, "the name follows the new container");
    }

    [TestMethod]
    public async Task NormalizeAsync_RejectsATranscoderResultThatStillBreaksTheLimits()
    {
        // The transcoder hands the BMP back; it is still not an allowed container.
        var source = AgentMedia.FromBytes(MediaFixtures.Bmp(10, 10));
        var options = AgentMediaOptions.CreateDefault();
        options.Transcoder = new FixedTranscoder(source);

        var error = await Assert.ThrowsExactlyAsync<AgentMediaException>(() => source.NormalizeAsync(options));

        Assert.AreEqual(AgentMediaError.InvalidTranscoderResult, error.Error);
    }

    [TestMethod]
    public void MediaTypeAliases_AreCanonicalised()
    {
        var image = AgentMedia.FromBytes(MediaFixtures.Png1x1(), "IMAGE/X-PNG; charset=binary");

        Assert.AreEqual("image/png", image.MediaType);
        Assert.AreEqual("image/png", image.DeclaredMediaType);
    }

    [TestMethod]
    public async Task NormalizeAsync_UsesTheDimensionReason_AndTheRequestedLimit()
    {
        var transcoder = new FixedTranscoder(AgentMedia.FromBytes(MediaFixtures.Png(100, 75)));
        var options = AgentMediaOptions.CreateDefault();
        options.MaxWidth = 100;
        options.Transcoder = transcoder;

        var converted = await AgentMedia.FromBytes(MediaFixtures.Png(640, 480)).NormalizeAsync(options);

        Assert.AreEqual(AgentMediaFormat.Png, converted.Format);
        Assert.AreEqual(AgentMediaTranscodeReason.OversizeDimensions, transcoder.LastRequest!.Reason);
        Assert.AreEqual(100, transcoder.LastRequest.MaxWidth);
        Assert.AreEqual(AgentMediaOptions.CreateDefault().MaxHeight, transcoder.LastRequest.MaxHeight);
    }

    [TestMethod]
    public void VisionDefaults_RejectAFullHdFrame_WithoutATranscoder()
    {
        var error = Assert.ThrowsExactly<AgentMediaException>(
            () => AgentMedia.FromBytes(MediaFixtures.Png(1920, 1080)).Normalize(AgentMediaOptions.CreateVisionDefault()));

        Assert.AreEqual(AgentMediaError.TooWide, error.Error);
    }

    [TestMethod]
    public void Animation_IsAcceptedWhenExplicitlyAllowed()
    {
        var options = AgentMediaOptions.CreateDefault();
        options.AllowAnimated = true;

        var image = AgentMedia.FromBytes(MediaFixtures.Webp(100, 100, animated: true)).Normalize(options);

        Assert.IsTrue(image.IsAnimated);
    }

    [TestMethod]
    public async Task Svg_IsReportedAsUnsupported_AndTranscodedWhenPossible()
    {
        var svg = AgentMedia.FromBytes(MediaFixtures.Svg(), name: "logo.svg");
        Assert.AreEqual(AgentMediaError.UnsupportedMediaType,
            Assert.ThrowsExactly<AgentMediaException>(() => svg.Normalize()).Error);

        var options = AgentMediaOptions.CreateDefault();
        var transcoder = new FixedTranscoder(AgentMedia.FromBytes(MediaFixtures.Png(10, 10)));
        options.Transcoder = transcoder;

        var converted = await svg.NormalizeAsync(options);

        Assert.AreEqual(AgentMediaTranscodeReason.UnsupportedMediaType, transcoder.LastRequest!.Reason);
        Assert.AreEqual(AgentMediaFormat.Png, converted.Format);
    }

    [TestMethod]
    public void AudioContainers_AreClassified()
    {
        AssertKind(MediaFixtures.Mp3(), AgentMediaKind.Audio, "audio/mpeg");
        AssertKind(MediaFixtures.Wav(), AgentMediaKind.Audio, "audio/wav");
        AssertKind(MediaFixtures.Flac(), AgentMediaKind.Audio, "audio/flac");
        AssertKind(MediaFixtures.Ogg(), AgentMediaKind.Audio, "audio/ogg");
    }

    [TestMethod]
    public void VideoAndDocumentContainers_AreClassified()
    {
        AssertKind(MediaFixtures.Mp4(), AgentMediaKind.Video, "video/mp4");
        AssertKind(MediaFixtures.Webm(), AgentMediaKind.Video, "video/webm");
        AssertKind(MediaFixtures.Pdf(), AgentMediaKind.Document, "application/pdf");
        AssertKind(MediaFixtures.Zip(), AgentMediaKind.Binary, "application/zip");
    }

    [TestMethod]
    public void TextAndOfficeContainers_AreClassified()
    {
        AssertKind(MediaFixtures.Text("hello"), AgentMediaKind.Text, "text/plain");
        AssertKind(MediaFixtures.Text("a,b\n1,2"), AgentMediaKind.Text, "text/csv", "text/csv");

        var docx = AgentMedia.FromBytes(
            MediaFixtures.Zip(),
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        Assert.AreEqual(AgentMediaKind.Document, docx.Kind);
        Assert.AreEqual(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            docx.MediaType);
    }

    [TestMethod]
    public void FromFile_TextAndCodeExtensions_AreResolvedFromTheExtension()
    {
        var root = Path.Combine(Path.GetTempPath(), "veloxdev-media-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var markdown = Path.Combine(root, "notes.md");
            File.WriteAllText(markdown, "# Title\n\nbody");
            var md = AgentMedia.FromFile(markdown);
            Assert.AreEqual(AgentMediaKind.Text, md.Kind);
            Assert.AreEqual("text/markdown", md.MediaType);

            var code = Path.Combine(root, "Program.cs");
            File.WriteAllText(code, "class Program { static void Main() { } }");
            var cs = AgentMedia.FromFile(code);
            Assert.AreEqual(AgentMediaKind.Text, cs.Kind);
            Assert.AreEqual("text/plain", cs.MediaType);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void FromFile_TextExtensionWithBinaryBytes_FallsBackToBinary()
    {
        var root = Path.Combine(Path.GetTempPath(), "veloxdev-media-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "fake.md");
            File.WriteAllBytes(path, MediaFixtures.Unknown());

            var file = AgentMedia.FromFile(path);

            Assert.AreEqual(AgentMediaKind.Binary, file.Kind);
            Assert.AreEqual("application/octet-stream", file.MediaType);
            Assert.AreEqual(AgentMediaError.UnsupportedKind,
                Assert.ThrowsExactly<AgentMediaException>(() => file.Normalize()).Error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void FromFile_ExplicitMediaTypeWinsOverTheExtension()
    {
        var root = Path.Combine(Path.GetTempPath(), "veloxdev-media-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "notes.md");
            File.WriteAllText(path, "# Title");

            var file = AgentMedia.FromFile(path, "text/plain");

            Assert.AreEqual("text/plain", file.MediaType);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void PerKindByteLimits_UseTheKindsOwnBudget()
    {
        var options = AgentMediaOptions.CreateDefault();
        options.MaxBytesByKind[AgentMediaKind.Image] = 8;

        Assert.AreEqual(AgentMediaError.TooLarge,
            Assert.ThrowsExactly<AgentMediaException>(
                () => AgentMedia.FromBytes(MediaFixtures.Png1x1()).Normalize(options)).Error);

        options.MaxBytesByKind[AgentMediaKind.Audio] = 4;
        Assert.AreEqual(AgentMediaError.TooLarge,
            Assert.ThrowsExactly<AgentMediaException>(
                () => AgentMedia.FromBytes(MediaFixtures.Mp3()).Normalize(options)).Error);
    }

    [TestMethod]
    public async Task NonImageTranscode_TargetsAnAllowedKind()
    {
        var options = AgentMediaOptions.CreateDefault();
        options.AllowedKinds.Remove(AgentMediaKind.Audio);
        options.TranscodeMediaType = "text/plain";
        var transcoder = new FixedTranscoder(AgentMedia.FromBytes(MediaFixtures.Text("transcript")));
        options.Transcoder = transcoder;

        var converted = await AgentMedia.FromBytes(MediaFixtures.Mp3()).NormalizeAsync(options);

        Assert.AreEqual(AgentMediaKind.Text, converted.Kind);
        Assert.AreEqual("text/plain", converted.MediaType);
        Assert.AreEqual(AgentMediaTranscodeReason.UnsupportedKind, transcoder.LastRequest!.Reason);
        Assert.AreEqual(AgentMediaKind.Text, transcoder.LastRequest.TargetKind);
        Assert.AreEqual("text/plain", transcoder.LastRequest.TargetMediaType);
    }

    [TestMethod]
    public void NullAllowedMediaTypes_AllowsAnyTypeOfAnAllowedKind()
    {
        var options = AgentMediaOptions.CreateDefault();
        options.AllowedMediaTypes = null;

        var bmp = AgentMedia.FromBytes(MediaFixtures.Bmp(10, 10)).Normalize(options);

        Assert.AreEqual(AgentMediaKind.Image, bmp.Kind);
        Assert.AreEqual("image/bmp", bmp.MediaType);
    }

    [TestMethod]
    public void StrictMediaType_AppliesToDocumentsToo()
    {
        var pdf = AgentMedia.FromBytes(MediaFixtures.Pdf(), "text/plain");
        var options = AgentMediaOptions.CreateDefault();
        options.StrictMediaType = true;

        Assert.AreEqual(AgentMediaError.MediaTypeMismatch,
            Assert.ThrowsExactly<AgentMediaException>(() => pdf.Normalize(options)).Error);
    }

    [TestMethod]
    public void Reference_InfersTheMediaType_AndBecomesUriContent()
    {
        var reference = AgentMediaReference.FromUrl("https://example.com/frames/a.png");

        Assert.AreEqual("image/png", reference.MediaType);
        Assert.AreEqual("a.png", reference.Name);
        var content = reference.ToContent();
        var uri = content as UriContent;
        Assert.IsNotNull(uri);
        Assert.AreEqual("image/png", uri!.MediaType);
        Assert.AreEqual("https://example.com/frames/a.png", uri.Uri.ToString());
    }

    [TestMethod]
    public void Reference_RejectsFileUrls()
    {
        var error = Assert.ThrowsExactly<AgentMediaException>(
            () => AgentMediaReference.FromUrl("file:///C:/frames/a.png"));

        Assert.AreEqual(AgentMediaError.UnsupportedKind, error.Error);
    }

    private static void AssertKind(byte[] bytes, AgentMediaKind kind, string mediaType, string? declared = null)
    {
        var media = AgentMedia.FromBytes(bytes, declared);
        Assert.AreEqual(kind, media.Kind);
        Assert.AreEqual(mediaType, media.MediaType);
    }

    private static void AssertDimensions(byte[] bytes, AgentMediaFormat format, int width, int height)
    {
        var image = AgentMedia.FromBytes(bytes);
        Assert.AreEqual(format, image.Format);
        Assert.AreEqual(width, image.Width);
        Assert.AreEqual(height, image.Height);
    }

    private sealed class FixedTranscoder(AgentMedia result) : IAgentMediaTranscoder
    {
        public AgentMediaTranscodeRequest? LastRequest { get; private set; }

        public Task<AgentMedia> TranscodeAsync(
            AgentMedia source, AgentMediaTranscodeRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(result);
        }
    }
}
