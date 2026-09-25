using System.Globalization;
using PoWatch.Shared.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PoWatch.Api.Infrastructure;

/// <summary>
/// Renders a <see cref="RecapDto"/> to a one-to-two page PDF with QuestPDF (Community licence).
/// </summary>
public static class RecapReportRenderer
{
    /// <summary>
    /// The PDF engine cannot run on this machine. QuestPDF renders through a native Skia library and
    /// ships no <c>win-arm64</c> build; callers turn this into an explained 503 instead of a bare 500.
    /// </summary>
    public sealed class RendererUnavailableException(string message, Exception inner) : InvalidOperationException(message, inner);

    public static byte[] Render(RecapDto recap, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(recap);
        try
        {
            return RenderCore(recap, zone);
        }
        catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException or BadImageFormatException)
        {
            throw new RendererUnavailableException(
                "The PDF engine could not start on this machine. QuestPDF needs a native library that is not available for "
                + $"{System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}. The recap itself is fine — read it in the app.",
                ex);
        }
    }

    private static byte[] RenderCore(RecapDto recap, TimeZoneInfo zone)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        string Local(DateTimeOffset at, string format) => TimeZoneInfo.ConvertTime(at, zone).ToString(format, CultureInfo.InvariantCulture);

        return Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            page.DefaultTextStyle(ts => ts.FontSize(10).FontFamily("Arial"));

            page.Header().Column(col =>
            {
                col.Item().Text("PoWatch — Recap").FontSize(9).FontColor(Colors.Orange.Darken2).Bold();
                col.Item().Text(recap.Title).FontSize(20).Bold();
                col.Item().Text($"{recap.Subtitle}  ·  {Local(recap.FromUtc, "yyyy-MM-dd HH:mm")} – {Local(recap.ToUtc, "HH:mm")} ({zone.Id})")
                    .FontSize(9).FontColor(Colors.Grey.Darken2);
                col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Orange.Lighten2);
            });

            page.Content().PaddingTop(10).Column(col =>
            {
                col.Spacing(10);
                col.Item().Text(recap.Summary).FontSize(12).LineHeight(1.4f);

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); });
                    foreach (var number in recap.Numbers)
                    {
                        table.Cell().Padding(4).Column(cell =>
                        {
                            cell.Item().Text(number.Label.ToUpperInvariant()).FontSize(7).FontColor(Colors.Grey.Darken1);
                            cell.Item().Text(number.Value).FontSize(14).Bold();
                        });
                    }
                });

                if (recap.Highlights.Count > 0)
                {
                    col.Item().Text("Highlights").FontSize(12).Bold();
                    foreach (var highlight in recap.Highlights)
                        col.Item().Row(row => { row.ConstantItem(12).Text("•"); row.RelativeItem().Text(highlight); });
                }

                if (recap.Moments.Count > 0)
                {
                    col.Item().Text("Moments").FontSize(12).Bold();
                    foreach (var moment in recap.Moments)
                        col.Item().Row(row =>
                        {
                            row.ConstantItem(60).Text(Local(moment.AtUtc, "HH:mm:ss")).FontColor(Colors.Grey.Darken2);
                            row.RelativeItem().Text(moment.Text);
                        });
                }
            });

            page.Footer().AlignRight().Text(text =>
            {
                text.Span($"Written by: {recap.Source} · page ").FontSize(8).FontColor(Colors.Grey.Medium);
                text.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
            });
        })).GeneratePdf();
    }
}
