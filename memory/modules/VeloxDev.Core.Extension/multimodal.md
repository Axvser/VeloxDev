# VeloxDev.Core.Extension — 多模态输入

> 代码：`Agent/Media/`（`AgentMedia.cs`、`AgentMediaReference.cs`、`AgentMediaOptions.cs`、
> `IAgentMediaTranscoder.cs`、`AgentMessage.cs`、`AgentMediaAgentExtensions.cs`、`ImageHeaderReader.cs`）。
> 测试：`Agent/Media/` 下的 `AgentMediaTests` / `AgentMessageTests` / `MediaFixtures`。

---

## 一、这一层解决什么，不解决什么

MAF / M.E.AI 把多模态内容当作普通 `AIContent`：本地字节用 `DataContent(bytes, mediaType)`，远程用
`UriContent(url, mediaType)`，放进 **user** `ChatMessage` 即可。这一层不重复那套协议，补的是它留给
宿主的边界：**按字节识别容器与种类、纠正错误 MIME、读取图片尺寸、执行按种类的格式/字节/尺寸策略、
在发送前经过宿主转码器**。

支持的 kind：Image / Audio / Video / Document / Text / Binary。识别范围见
`AgentMediaFormats.Detect`：图片 PNG/JPEG/WebP/GIF/BMP/TIFF/AVIF/HEIC/SVG；音频 MP3/WAV/FLAC/Ogg/AAC/M4A；
视频 MP4/WebM/MKV/AVI/MPEG/QuickTime/FLV；文档 PDF/RTF/Office/OpenDocument/EPUB；文本 plain/Markdown/
HTML/CSV/JSON/XML；其余为 Binary（默认不在 `AllowedKinds` 里）。

它**不做**像素/音频/视频编解码。Core.Extension 是平台中立的，引入原生媒体库会把依赖强加给每个
消费者；宿主的 WPF、Avalonia/SkiaSharp、MAUI、ffmpeg 本来就有编解码器，所以转码是
`IAgentMediaTranscoder` 这条缝。

---

## 二、契约

- **字节胜过声明。** `AgentMedia.FromBytes` 嗅探容器；声明的 `image/jpg` 等归一化，冲突时 `MediaType`
  取字节的结论，声明留在 `DeclaredMediaType`。`AgentMediaOptions.StrictMediaType` 才把冲突变成
  `MediaTypeMismatch`。声明成图片但字节没有图片魔数 → 构造时 `UnknownFormat`。
- **只放 user 消息。** OpenAI 兼容 chat-completions 的 tool 消息只有文本通道；`AIFunction` 返回的
  `DataContent` 到模型那里是一段 base64 JSON，不是像素/声音/文档。截图或音频工具返回报告/路径，
  宿主再用 `AgentMessage` 把载荷作为 user 轮发回。
- **不认识的容器**：声明为 image 但字节没有图片魔数 → 仍记为 Image，`Format = Unknown`；`Normalize`
  要求转码（`UnrecognizedFormat`），没有转码器就抛 `UnknownFormat`，**不会**按声明的类型直接发。
  非图片声明且无魔数 → 信任声明（有些容器没有可嗅探头），仍受 kind/类型/字节策略约束。无声明 →
  合法 UTF-8 文本记 `text/plain`，否则 `application/octet-stream` / Binary（`AllowedKinds` 需显式加入）。
  `AgentMediaReference` 不下载 URL，字节正确性归 provider。
- **后缀是提示，不是事实**：`FromFile` 把 `.md` 映射到 `text/markdown`，把常见源码/配置后缀
  （`.cs`、`.py`、`.ts`、`.yaml` 等）映射到 `text/plain`；文本类提示只有在字节真的像文本时才被接受，
  所以二进制文件改名 `.md` 会落回 Binary 而不是 Markdown。显式传入的 `mediaType` 压过后缀；已识别的
  魔数压过两者；未知后缀交给内容启发式。
- **按 kind 的策略**：`AllowedKinds`（Binary 需显式加入）、`AllowedMediaTypes`（`null` = 该 kind 的
  任意类型；默认是各 provider 常见子集）、`MaxBytesByKind`（Image 10 MiB / Audio 25 / Video 100 /
  Document 25 / Text 2）、图片的 `MaxWidth/MaxHeight/AllowAnimated`。
- **转码目标**：图片 JPEG 源保持 JPEG，静止 WebP 在允许时保持 WebP，动画与不支持格式走
  `TranscodeFormat`（默认 PNG）；非图片走 `TranscodeMediaType`（`null` = 用源类型重新编码，kind 不被
  允许时必须显式给一个允许的目标，例如音频转文字）。转码结果会再次校验 kind/类型/字节/尺寸/动画。
- **附件是显示记录。** `AgentTurnStarted.Attachments` / `AgentTranscriptEntry.Attachments` 只携带
  name / mediaType / byteCount / uri，`AgentAttachment.Kind` 从 mediaType 推导；`ToMarkdown` 渲染
  `附件：…`。
- **`AgentMessage.FromContents`** 是未包装 `AIContent` 的逃生口（自定义内容、`FunctionResultContent`），
  不做校验。

---

## 三、踩过的坑（依据在代码与下列 URI）

| 坑 | 事实 |
|---|---|
| 没有 `ImageContent` | M.E.AI 10.10.0 只有 `TextContent` / `DataContent` / `UriContent`；旧例子的 `ImageContent` 不适用 |
| 工具结果带媒体无效 | tool 消息只有文本通道；载荷必须由宿主作为 user 消息补发 |
| MIME 必须正确 | `image/png` / `image/jpeg` / `audio/mpeg` / `application/pdf` 等，不要统一 `application/octet-stream` |
| 容器与 kind 可能不唯一 | ISO BMFF / EBML 同时承载音频和视频，ZIP 同时是普通压缩包与 OOXML/ODF；用声明类型细化，但字节仍决定容器 |
| base64 膨胀 | 约 +33%；字节上限量的是原始字节，请求体更大 |
| 会话历史重复计费 | 媒体留在 `AgentSession` 历史里，后续每轮都可能再次计费；长会话只发必要载荷 |
| 模型未必消费该模态 | endpoint 接受 `image_url` / 音频 / 文件块 ≠ 模型支持；能力要按模型确认 |
| 动画 | GIF/WebP 动画默认拒绝；转成静帧或显式 `AllowAnimated` |
| 远程载荷 | 只接受 http/https；`file:` 由宿主读字节后走 `FromBytes` |
| 逐字视频/长音频 | 转码是宿主职责；库只提供目标 kind/类型、字节与尺寸的限制 |

外部协议事实的 URI 来源：
- OpenAI file inputs: <https://developers.openai.com/api/docs/guides/file-inputs>
- OpenAI Chat Completions reference: <https://platform.openai.com/docs/api-reference/chat/create>
- Anthropic vision: <https://docs.anthropic.com/en/docs/vision>
- Anthropic PDF support: <https://platform.claude.com/docs/en/build-with-claude/pdf-support>
- MAF multimodal: <https://learn.microsoft.com/agent-framework/agents/multimodal>
- M.E.AI AIContent: <https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.aicontent>
