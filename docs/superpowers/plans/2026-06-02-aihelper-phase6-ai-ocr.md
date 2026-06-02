# AIHelperNET — Phase 6: Infrastructure – AI Providers & OCR

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans`.
> **Prerequisite:** Phase 5 complete.

**Goal:** Implement Claude (SSE streaming) and Ollama answer providers, the provider resolver, Windows OCR screen capture, and complete the Infrastructure DI registration.

**Architecture:** Both `ClaudeAnswerProvider` and `OllamaAnswerProvider` implement `IAnswerProvider`. `AnswerProviderResolver` picks the active one at runtime. Windows OCR uses `Windows.Media.Ocr` — no Tesseract.

**Tech Stack:** HttpClient (raw SSE), OllamaSharp, Windows.Media.Ocr (SDK), System.Drawing

---

### Task 33: Claude options and SSE parser

**Files:**
- Create: `src/AIHelperNET.Infrastructure/AI/ClaudeOptions.cs`
- Create: `src/AIHelperNET.Infrastructure/AI/OllamaOptions.cs`
- Create: `src/AIHelperNET.Infrastructure/AI/ClaudeSse.cs`

- [ ] **Step 1: Create options records**

```csharp
// src/AIHelperNET.Infrastructure/AI/ClaudeOptions.cs
namespace AIHelperNET.Infrastructure.AI;

public sealed class ClaudeOptions
{
    public string BaseUrl { get; set; } = "https://api.anthropic.com";
    public string Model   { get; set; } = "claude-sonnet-4-6";
    public string Version { get; set; } = "2023-06-01";
}
```

```csharp
// src/AIHelperNET.Infrastructure/AI/OllamaOptions.cs
namespace AIHelperNET.Infrastructure.AI;

public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model   { get; set; } = "qwen2.5-coder:7b";
}
```

- [ ] **Step 2: Create ClaudeSse parser**

Parses Claude's streaming SSE format. Each `data:` line is JSON with a `delta.text` field when the event type is `content_block_delta`.

```csharp
// src/AIHelperNET.Infrastructure/AI/ClaudeSse.cs
using System.Text.Json;

namespace AIHelperNET.Infrastructure.AI;

public static class ClaudeSse
{
    public static string? ParseTextDelta(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeEl)) return null;
            if (typeEl.GetString() != "content_block_delta") return null;

            if (!root.TryGetProperty("delta", out var delta)) return null;
            if (!delta.TryGetProperty("type", out var deltaType)) return null;
            if (deltaType.GetString() != "text_delta") return null;

            return delta.TryGetProperty("text", out var text) ? text.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string BuildRequestJson(
        string model, string systemPrompt, string userPrompt, int maxTokens)
    {
        return JsonSerializer.Serialize(new
        {
            model,
            max_tokens = maxTokens,
            stream = true,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = userPrompt } }
        });
    }
}
```

- [ ] **Step 3: Build**

```powershell
dotnet build src/AIHelperNET.Infrastructure/
```

- [ ] **Step 4: Commit**

```powershell
git add src/AIHelperNET.Infrastructure/AI/ClaudeOptions.cs src/AIHelperNET.Infrastructure/AI/OllamaOptions.cs src/AIHelperNET.Infrastructure/AI/ClaudeSse.cs
git commit -m "feat(infra): add Claude/Ollama options and SSE parser"
```

---

### Task 34: ClaudeAnswerProvider

**Files:**
- Create: `src/AIHelperNET.Infrastructure/AI/ClaudeAnswerProvider.cs`

- [ ] **Step 1: Implement**

API key is read per-request from `ISecretStore`, converted from `SecureString` to a plain string only inside the request scope, then zeroed. This satisfies security requirement #2 from the spec.

```csharp
// src/AIHelperNET.Infrastructure/AI/ClaudeAnswerProvider.cs
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;
using Microsoft.Extensions.Options;

namespace AIHelperNET.Infrastructure.AI;

public sealed class ClaudeAnswerProvider(
    HttpClient http,
    ISecretStore secrets,
    IOptions<ClaudeOptions> options) : IAnswerProvider
{
    public AiBackend Backend => AiBackend.Claude;

    public async IAsyncEnumerable<string> StreamAnswerAsync(
        AnswerPrompt prompt,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var keyResult = secrets.GetApiKey();
        if (keyResult.IsFailed)
            throw new InvalidOperationException("No Claude API key configured.");

        var opts = options.Value;
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{opts.BaseUrl}/v1/messages");

        var apiKey = SecureStringToString(keyResult.Value);
        try
        {
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", opts.Version);
            request.Headers.Add("Accept", "text/event-stream");

            var body = ClaudeSse.BuildRequestJson(
                opts.Model, prompt.System, prompt.User, prompt.MaxTokens);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                var json = line["data:".Length..].Trim();
                if (json is "" or "[DONE]") continue;
                var delta = ClaudeSse.ParseTextDelta(json);
                if (!string.IsNullOrEmpty(delta)) yield return delta;
            }
        }
        finally
        {
            // Zero the plain-text key as soon as possible
            if (apiKey.Length > 0)
            {
                unsafe
                {
                    fixed (char* p = apiKey)
                        for (int i = 0; i < apiKey.Length; i++) p[i] = '\0';
                }
            }
        }
    }

    private static string SecureStringToString(SecureString ss)
    {
        var ptr = Marshal.SecureStringToBSTR(ss);
        try { return Marshal.PtrToStringBSTR(ptr) ?? string.Empty; }
        finally { Marshal.ZeroFreeBSTR(ptr); }
    }
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build src/AIHelperNET.Infrastructure/
```

- [ ] **Step 3: Commit**

```powershell
git add src/AIHelperNET.Infrastructure/AI/ClaudeAnswerProvider.cs
git commit -m "feat(infra): add ClaudeAnswerProvider with SSE streaming"
```

---

### Task 35: OllamaAnswerProvider + AnswerProviderResolver

**Files:**
- Create: `src/AIHelperNET.Infrastructure/AI/OllamaAnswerProvider.cs`
- Create: `src/AIHelperNET.Infrastructure/AI/AnswerProviderResolver.cs`

- [ ] **Step 1: Implement OllamaAnswerProvider**

```csharp
// src/AIHelperNET.Infrastructure/AI/OllamaAnswerProvider.cs
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models;

namespace AIHelperNET.Infrastructure.AI;

public sealed class OllamaAnswerProvider(
    IOllamaApiClient client,
    IOptions<OllamaOptions> options) : IAnswerProvider
{
    public AiBackend Backend => AiBackend.Ollama;

    public async IAsyncEnumerable<string> StreamAnswerAsync(
        AnswerPrompt prompt,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var request = new GenerateRequest
        {
            Model  = options.Value.Model,
            Prompt = $"{prompt.System}\n\n{prompt.User}",
            Stream = true
        };

        await foreach (var token in client.GenerateAsync(request, ct))
        {
            if (token?.Response is { Length: > 0 } t)
                yield return t;
        }
    }
}
```

- [ ] **Step 2: Implement AnswerProviderResolver**

```csharp
// src/AIHelperNET.Infrastructure/AI/AnswerProviderResolver.cs
using AIHelperNET.Application.Abstractions;

namespace AIHelperNET.Infrastructure.AI;

public interface IAnswerProviderResolver
{
    IAnswerProvider Resolve(AiBackend backend);
}

public sealed class AnswerProviderResolver(
    ClaudeAnswerProvider claude,
    OllamaAnswerProvider ollama) : IAnswerProviderResolver
{
    public IAnswerProvider Resolve(AiBackend backend) => backend switch
    {
        AiBackend.Claude => claude,
        AiBackend.Ollama => ollama,
        _ => throw new ArgumentOutOfRangeException(nameof(backend))
    };
}
```

- [ ] **Step 3: Update GenerateAnswerHandler to use resolver**

The `GenerateAnswerHandler` currently injects `IAnswerProvider` directly. Update it to use `IAnswerProviderResolver` so the active backend is resolved per-request from settings:

Edit `src/AIHelperNET.Application/Answers/Commands/GenerateAnswerCommand.cs`:

Replace `IAnswerProvider answerProvider` parameter with `IAnswerProviderResolver providerResolver` and add `ISettingsStore settingsStore`. Resolve the provider before streaming:

```csharp
// Updated GenerateAnswerHandler constructor and Handle method:

public sealed class GenerateAnswerHandler(
    ISessionRepository repository,
    IAnswerProviderResolver providerResolver,
    ISettingsStore settingsStore,
    PromptBuilderService promptBuilder,
    IAnswerStreamSink streamSink,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<GenerateAnswerCommand, Result<AnswerId>>
{
    public async ValueTask<Result<AnswerId>> Handle(GenerateAnswerCommand cmd, CancellationToken ct)
    {
        var settings = await settingsStore.LoadAsync(ct);
        var provider = providerResolver.Resolve(settings.ActiveBackend);

        var get = await repository.GetAsync(cmd.SessionId, ct);
        if (get.IsFailed) return get.ToResult<AnswerId>();
        var session = get.Value;

        var question = session.Questions.FirstOrDefault(q => q.Id == cmd.QuestionId);
        if (question is null) return Result.Fail("Question not found.");

        var start = session.StartAnswer(cmd.QuestionId, clock.GetUtcNow());
        if (start.IsFailed) return Result.Fail(start.Error);
        var answer = start.Value;

        var prompt = promptBuilder.Build(session.CodeProfile, session.AnswerSettings, question, cmd.ScreenContext);

        try
        {
            await foreach (var chunk in provider.StreamAnswerAsync(prompt, ct))
            {
                answer.AppendChunk(chunk);
                await streamSink.PushAsync(answer.Id, chunk, ct);
            }
            answer.Complete(clock.GetUtcNow());
        }
        catch (OperationCanceledException) { answer.Cancel(clock.GetUtcNow()); }
        catch (Exception)                  { answer.Fail(clock.GetUtcNow()); }

        repository.Update(session);
        var save = await unitOfWork.SaveChangesAsync(ct);
        return save.IsFailed ? save.ToResult<AnswerId>() : Result.Ok(answer.Id);
    }
}
```

Note: `IAnswerProviderResolver` is declared in Infrastructure. To keep Application free of Infrastructure references, move the interface declaration to Application:

Create `src/AIHelperNET.Application/Abstractions/IAnswerProviderResolver.cs`:

```csharp
// src/AIHelperNET.Application/Abstractions/IAnswerProviderResolver.cs
namespace AIHelperNET.Application.Abstractions;

public interface IAnswerProviderResolver
{
    IAnswerProvider Resolve(AiBackend backend);
}
```

Then `AnswerProviderResolver` in Infrastructure implements this Application interface.

- [ ] **Step 4: Build**

```powershell
dotnet build AIHelperNET.sln
```

- [ ] **Step 5: Commit**

```powershell
git add src/AIHelperNET.Application/Abstractions/IAnswerProviderResolver.cs src/AIHelperNET.Infrastructure/AI/ src/AIHelperNET.Application/Answers/Commands/GenerateAnswerCommand.cs
git commit -m "feat(infra): add OllamaAnswerProvider, AnswerProviderResolver; update handler to resolve backend from settings"
```

---

### Task 36: Windows OCR — ScreenGrabber + ImagePreprocessor + WindowsOcrService

**Files:**
- Create: `src/AIHelperNET.Infrastructure/Ocr/ScreenGrabber.cs`
- Create: `src/AIHelperNET.Infrastructure/Ocr/ImagePreprocessor.cs`
- Create: `src/AIHelperNET.Infrastructure/Ocr/WindowsOcrService.cs`

- [ ] **Step 1: Create ScreenGrabber**

```csharp
// src/AIHelperNET.Infrastructure/Ocr/ScreenGrabber.cs
using System.Drawing;
using System.Drawing.Imaging;

namespace AIHelperNET.Infrastructure.Ocr;

public static class ScreenGrabber
{
    public static Bitmap CapturePrimary()
    {
        var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds
            ?? new Rectangle(0, 0, 1920, 1080);

        var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        return bmp;
    }
}
```

Note: `System.Windows.Forms` is available on `net10.0-windows` without extra packages. Add `<UseWindowsForms>true</UseWindowsForms>` to the Infrastructure csproj.

Edit `src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj` to add:

```xml
<PropertyGroup>
  <UseWindowsForms>true</UseWindowsForms>
</PropertyGroup>
```

- [ ] **Step 2: Create ImagePreprocessor**

```csharp
// src/AIHelperNET.Infrastructure/Ocr/ImagePreprocessor.cs
using System.Drawing;
using System.Drawing.Imaging;

namespace AIHelperNET.Infrastructure.Ocr;

public static class ImagePreprocessor
{
    public static Bitmap Enhance(Bitmap source)
    {
        // Scale 2x for better OCR accuracy on low-res text
        var scaled = new Bitmap(source.Width * 2, source.Height * 2);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, 0, 0, scaled.Width, scaled.Height);
        }

        // Convert to grayscale
        var gray = new Bitmap(scaled.Width, scaled.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(gray))
        {
            var cm = new ColorMatrix(new float[][]
            {
                [0.299f, 0.299f, 0.299f, 0, 0],
                [0.587f, 0.587f, 0.587f, 0, 0],
                [0.114f, 0.114f, 0.114f, 0, 0],
                [0,      0,      0,      1, 0],
                [0,      0,      0,      0, 1]
            });
            using var attrs = new ImageAttributes();
            attrs.SetColorMatrix(cm);
            g.DrawImage(scaled, new Rectangle(0, 0, gray.Width, gray.Height),
                0, 0, scaled.Width, scaled.Height, GraphicsUnit.Pixel, attrs);
        }

        scaled.Dispose();
        return gray;
    }
}
```

- [ ] **Step 3: Create WindowsOcrService**

`Windows.Media.Ocr` is part of the WinRT API surface accessible from .NET via the Windows SDK.

```csharp
// src/AIHelperNET.Infrastructure/Ocr/WindowsOcrService.cs
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using AIHelperNET.Application.Abstractions;
using FluentResults;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace AIHelperNET.Infrastructure.Ocr;

public sealed class WindowsOcrService : IScreenOcrService
{
    public async Task<Result<string>> CaptureAndReadAsync(CancellationToken ct)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null)
            return Result.Fail("No OCR engine available for the system's user profile languages.");

        using var bmp = ScreenGrabber.CapturePrimary();
        using var processed = ImagePreprocessor.Enhance(bmp);

        var softwareBitmap = await BitmapToSoftwareBitmapAsync(processed, ct);
        var result = await engine.RecognizeAsync(softwareBitmap);
        return Result.Ok(result.Text);
    }

    private static async Task<SoftwareBitmap> BitmapToSoftwareBitmapAsync(Bitmap bmp, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Bmp);
        ms.Seek(0, SeekOrigin.Begin);

        var decoder = await BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
        return await decoder.GetSoftwareBitmapAsync()
            .AsTask(ct);
    }
}
```

Note: `Windows.Media.Ocr` and `Windows.Graphics.Imaging` require `net10.0-windows10.0.17763.0` or higher as the TargetFramework. Update the Infrastructure csproj:

```xml
<TargetFramework>net10.0-windows10.0.17763.0</TargetFramework>
```

This enables WinRT projections. Verify the app targets the same or higher in the App project as well.

- [ ] **Step 4: Build**

```powershell
dotnet build src/AIHelperNET.Infrastructure/
```

- [ ] **Step 5: Register in DI**

Edit `src/AIHelperNET.Infrastructure/DependencyInjection.cs` — add:

```csharp
services.AddSingleton<IScreenOcrService, WindowsOcrService>();

// AI providers
services.AddHttpClient<ClaudeAnswerProvider>();
services.AddSingleton<OllamaAnswerProvider>();
services.AddSingleton<IAnswerProviderResolver, AnswerProviderResolver>();
services.Configure<ClaudeOptions>(config.GetSection("Claude"));
services.Configure<OllamaOptions>(config.GetSection("Ollama"));

// Ollama client
services.AddSingleton<IOllamaApiClient>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
    return new OllamaApiClient(opts.BaseUrl);
});
```

- [ ] **Step 6: Build entire solution**

```powershell
dotnet build AIHelperNET.sln
```

Expected: 0 errors.

- [ ] **Step 7: Run all tests**

```powershell
dotnet test AIHelperNET.sln --logger "console;verbosity=minimal"
```

Expected: All tests pass.

- [ ] **Step 8: Commit**

```powershell
git add src/AIHelperNET.Infrastructure/Ocr/ src/AIHelperNET.Infrastructure/DependencyInjection.cs src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj
git commit -m "feat(infra): add WindowsOcrService, AI providers wired in DI"
```

---

**Phase 6 complete.** Continue with `2026-06-02-aihelper-phase7-presentation.md`.
