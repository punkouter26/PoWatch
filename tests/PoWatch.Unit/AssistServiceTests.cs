using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using PoWatch.Application.Services;

namespace PoWatch.Unit;

public sealed class AssistServiceTests
{
    /// <summary>A model that always says the same thing.</summary>
    private sealed class Canned(string reply) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private static AssistService With(string reply) => new([new Canned(reply)], NullLogger<AssistService>.Instance);

    [Fact]
    public async Task A_model_drafts_only_rules_and_headlines_that_survive_the_checks()
    {
        var ct = CancellationToken.None;

        // A rule must name something the detector can see, with real hours.
        var dog = await With("""{"class":"Dog","fromHour":22,"toHour":6}""").DraftRuleAsync("a dog at night", ct);
        Assert.Equal(("dog", 22, 6), (dog!.Class, dog.FromHour, dog.ToHour));
        Assert.Null(await With("""{"class":"burglar","fromHour":22,"toHour":6}""").DraftRuleAsync("a burglar at night", ct));
        Assert.Null(await With("""{"class":"cat","fromHour":25,"toHour":6}""").DraftRuleAsync("a cat at 25", ct));
        Assert.Null(await With("not json").DraftRuleAsync("a cat", ct));

        // A headline may only use the summary's numbers.
        const string summary = "The room was occupied 62% of the time, with 251 visits.";
        Assert.Equal("251 visits and a busy room", await With("""{"headline":"251 visits and a busy room"}""").HeadlineAsync(summary, ct));
        Assert.Null(await With("""{"headline":"Three hundred visits!"}""").HeadlineAsync(summary, ct));

        // No model: nothing, and no error.
        Assert.Null(await new AssistService([], NullLogger<AssistService>.Instance).HeadlineAsync(summary, ct));
    }
}
