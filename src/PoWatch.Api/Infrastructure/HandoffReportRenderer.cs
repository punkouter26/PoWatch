using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using PoWatch.Shared.Models;

namespace PoWatch.Api.Infrastructure;

/// <summary>
/// Renders a <see cref="ShiftHandoffReportDto"/> to a PDF byte array using QuestPDF.
/// QuestPDF license: Community (free for open-source/revenue below threshold). Set via QuestPDF.Settings.LicenseType.
/// </summary>
public static class HandoffReportRenderer
{
    public static byte[] Render(ShiftHandoffReportDto report)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(ts => ts.FontSize(10).FontFamily("Arial"));

                page.Header().Column(col =>
                {
                    col.Item().Text($"PoWatch — Shift Handoff Report")
                        .FontSize(18).Bold().FontColor(Colors.Indigo.Darken3);
                    col.Item().Text($"{report.Date:yyyy-MM-dd}  ·  {report.ShiftWindow} Shift")
                        .FontSize(11).FontColor(Colors.Grey.Darken2);
                    col.Item().Text($"Generated: {report.GeneratedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm} local")
                        .FontSize(9).Italic().FontColor(Colors.Grey.Medium);
                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Indigo.Lighten3);
                });

                page.Content().PaddingTop(12).Column(col =>
                {
                    // Summary stats
                    col.Item().Text("Summary").FontSize(13).Bold().FontColor(Colors.Indigo.Darken2);
                    col.Item().PaddingBottom(6).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2);
                            c.RelativeColumn(3);
                        });

                        void Row(string label, string value)
                        {
                            table.Cell().Padding(3).Text(label).Bold().FontColor(Colors.Grey.Darken2);
                            table.Cell().Padding(3).Text(value);
                        }

                        Row("Primary Subject", report.PrimarySubject);
                        Row("Dominant Activity", report.DominantActivity);
                        Row("Total Events", report.TotalEvents.ToString());
                        Row("Significant Events", report.SignificantCount.ToString());
                        Row("Clinical Outliers", report.OutlierCount.ToString());
                    });

                    // Clinical narrative
                    col.Item().PaddingTop(8).Text("Clinical Narrative").FontSize(13).Bold().FontColor(Colors.Indigo.Darken2);
                    col.Item().PaddingBottom(8).Background(Colors.Grey.Lighten5).Padding(8).Text(report.ClinicalNarrative);

                    // Significant events
                    if (report.SignificantEvents.Count > 0)
                    {
                        col.Item().PaddingTop(8).Text("Significant Events").FontSize(13).Bold().FontColor(Colors.Orange.Darken3);
                        col.Item().PaddingBottom(8).Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(60);
                                c.RelativeColumn(2);
                                c.RelativeColumn(3);
                                c.RelativeColumn(3);
                            });

                            table.Header(h =>
                            {
                                h.Cell().Text("Time").Bold();
                                h.Cell().Text("Subject").Bold();
                                h.Cell().Text("Activity").Bold();
                                h.Cell().Text("Reason").Bold();
                            });

                            foreach (var ev in report.SignificantEvents)
                            {
                                table.Cell().Text(ev.ObservedAtUtc.ToLocalTime().ToString("HH:mm:ss"));
                                table.Cell().Text(ev.SubjectDisplayName);
                                table.Cell().Text(ev.Activity);
                                table.Cell().Text(ev.SignificantReason ?? "-");
                            }
                        });
                    }

                    // Outlier events
                    if (report.OutlierEvents.Count > 0)
                    {
                        col.Item().PaddingTop(8).Text("Clinical Outliers").FontSize(13).Bold().FontColor(Colors.Red.Darken3);
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(60);
                                c.RelativeColumn(2);
                                c.RelativeColumn(5);
                            });

                            table.Header(h =>
                            {
                                h.Cell().Text("Time").Bold();
                                h.Cell().Text("Subject").Bold();
                                h.Cell().Text("Description").Bold();
                            });

                            foreach (var ev in report.OutlierEvents)
                            {
                                table.Cell().Text(ev.ObservedAtUtc.ToLocalTime().ToString("HH:mm:ss"));
                                table.Cell().Text(ev.SubjectDisplayName);
                                table.Cell().Text(ev.ClinicalDescription);
                            }
                        });
                    }
                });

                page.Footer().AlignRight().Text(text =>
                {
                    text.Span("Page ").FontSize(8).FontColor(Colors.Grey.Medium);
                    text.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                    text.Span(" of ").FontSize(8).FontColor(Colors.Grey.Medium);
                    text.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });
        }).GeneratePdf();
    }
}
