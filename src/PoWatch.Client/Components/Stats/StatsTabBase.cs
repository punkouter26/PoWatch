using System.Globalization;
using Microsoft.AspNetCore.Components;
using PoWatch.Client.Services;

namespace PoWatch.Client.Components.Stats;

/// <summary>A stats tab: loads its family whenever the range changes and renders nothing stale.</summary>
public abstract class StatsTabBase<T> : ComponentBase where T : class
{
    private StatsQuery? _loaded;

    [Inject] protected PoWatchApiClient Api { get; set; } = default!;

    [Parameter, EditorRequired] public StatsQuery Query { get; set; } = default!;

    protected T? Data { get; private set; }

    protected bool Loading { get; private set; }

    protected abstract Task<T?> LoadAsync(StatsQuery query);

    protected override async Task OnParametersSetAsync()
    {
        if (Query == _loaded) return;
        _loaded = Query;
        Loading = true;
        Data = await LoadAsync(Query);
        Loading = false;
    }

    protected static string Percent(double value) => value.ToString("0.0%", CultureInfo.InvariantCulture);

    protected static string Number(double value, string format = "0.##") => value.ToString(format, CultureInfo.InvariantCulture);

    protected static string Duration(double seconds) => DisplayText.Duration(seconds);

    protected static string Local(DateTimeOffset? instant, string format = "ddd HH:mm") =>
        instant?.ToLocalTime().ToString(format, CultureInfo.CurrentCulture) ?? "—";
}
