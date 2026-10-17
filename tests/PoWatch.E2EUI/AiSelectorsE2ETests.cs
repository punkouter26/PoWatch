using Microsoft.Playwright;

namespace PoWatch.E2EUI;

[Collection(nameof(PlaywrightCollection))]
public sealed class AiSelectorsE2ETests(PlaywrightFixture fixture)
{
    [Fact]
    public async Task Header_models_persist_switch_during_a_session_and_fit_on_a_phone()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        await using var page = await PoWatchPage.SignedInAsync(fixture.Browser, user: $"ai-selectors-{Guid.NewGuid():N}");
        var captions = page.GetByTestId("header-caption-model");
        var chat = page.GetByTestId("header-chat-model");
        await Assertions.Expect(chat).ToBeEnabledAsync();
        await captions.SelectOptionAsync("smolvlm-256m");
        await chat.SelectOptionAsync("Ollama");
        await Assertions.Expect(chat).ToBeEnabledAsync();
        await page.ReloadAsync();
        await Assertions.Expect(captions).ToHaveValueAsync("smolvlm-256m", new() { Timeout = 60_000 });
        await Assertions.Expect(chat).ToHaveValueAsync("Ollama");

        await page.RouteAsync("**/api/ai/models", async route =>
        {
            if (route.Request.Method == "PUT") await route.FulfillAsync(new() { Status = 503 });
            else await route.ContinueAsync();
        });
        await chat.SelectOptionAsync("Template");
        await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Could not switch server AI");
        await Assertions.Expect(chat).ToHaveValueAsync("Ollama");
        await page.UnrouteAsync("**/api/ai/models");

        // Exercise the real session scheduler without downloading GPU models or requesting a camera.
        await page.EvaluateAsync("""
            () => {
                window.captionCalls = [];
                powatchInference.startPreview = async () => 'OK';
                powatchInference.captureAndInfer = async (...args) => {
                    captionCalls.push(args[3]);
                    return { isAvailable: false, status: 'test' };
                };
                powatchInference.getInferenceDiagnostics = async () => ({
                    modelKey: captionCalls.at(-1), loadState: 'ready', device: 'webgpu', loadError: null
                });
                powatchDetector.load = async () => ({ modelId: 'test', device: 'wasm' });
                powatchDetector.start = () => {};
            }
            """);
        await page.GetByTestId("start-camera").ClickAsync();
        await page.WaitForFunctionAsync("() => captionCalls.includes('smolvlm-256m')");
        await Assertions.Expect(page.GetByTestId("header-caption-status")).ToContainTextAsync("WEBGPU");
        await captions.SelectOptionAsync("florence-2");
        await page.WaitForFunctionAsync("() => captionCalls.at(-1) === 'florence-2'");
        await Assertions.Expect(page.GetByTestId("session-status")).ToContainTextAsync("OBSERVING");
        await captions.SelectOptionAsync("off");
        await Assertions.Expect(page.GetByTestId("header-caption-status")).ToHaveTextAsync("OFF");
        await captions.SelectOptionAsync("smolvlm-500m");
        await page.WaitForFunctionAsync("() => captionCalls.at(-1) === 'smolvlm-500m'");
        await page.GetByTestId("header-stop").ClickAsync();

        await page.GotoAsync($"{PlaywrightFixture.BaseUrl}/settings");
        await page.GetByTestId("advanced-toggle").ClickAsync();
        await captions.SelectOptionAsync("smolvlm-256m");
        await Assertions.Expect(page.GetByTestId("caption-model")).ToContainTextAsync("256M");
        await page.GetByTestId("caption-model").ClickAsync();
        await Assertions.Expect(captions).ToHaveValueAsync("florence-2");

        await page.SetViewportSizeAsync(375, 812);
        var ai = await page.GetByTestId("header-ai").BoundingBoxAsync();
        Assert.NotNull(ai);
        Assert.True(ai.X >= 0 && ai.X + ai.Width <= 375);
        Assert.True((await captions.BoundingBoxAsync())!.Height >= 44);
        await page.AssertNoBlazorErrorAsync();
    }
}
