using Microsoft.Extensions.AI;
using PoWatch.Shared.Models;

namespace PoWatch.Application.Contracts;

/// <summary>The configured server models and their shared, model-isolated chat pipelines.</summary>
public interface IAiModelRegistry
{
    IReadOnlyList<AiModelDto> Models { get; }
    string DefaultId { get; }
    IChatClient? GetClient(string id);
}
