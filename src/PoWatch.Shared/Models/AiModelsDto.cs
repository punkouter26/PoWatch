namespace PoWatch.Shared.Models;

/// <summary>A server-configured AI model, without credentials or endpoint details.</summary>
public sealed record AiModelDto(string Id, string Provider, string Model);

/// <summary>The models this server offers and this browser's current selection.</summary>
public sealed record AiModelsDto(string SelectedId, IReadOnlyList<AiModelDto> Models);

/// <summary>Switches this browser to one of the server's configured models.</summary>
public sealed record SelectAiModelDto(string ModelId);
