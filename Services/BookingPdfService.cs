using System.Security.Cryptography;
using CourierApi.Contracts;
using CourierApi.Data;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CourierApi.Services;

public sealed class BookingPdfService(AppDbContext db)
{
    private const string FontFamily = "Be Vietnam Pro";

    public static string GenerateOpaqueToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public async Task<byte[]> RenderAsync(BookingDetailDto booking, string qrValue, CancellationToken ct = default)
    {
        var companyInfo = await db.CompanyInformation.AsNoTracking().OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        var qrImage = BuildQrPng(qrValue);

        var document = QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontFamily(FontFamily).FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Text(companyInfo?.CompanyName ?? "AB Courier").FontSize(13).Bold();
                    col.Item().Text("PHIẾU GỬI HÀNG").FontSize(11).SemiBold().FontColor(Colors.Grey.Darken2);
                    col.Item().PaddingTop(4).Text(booking.Id).FontSize(18).Bold();
                });

                page.Content().PaddingTop(8).Column(col =>
                {
                    col.Spacing(8);

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("NGƯỜI GỬI").Bold().FontColor(Colors.Grey.Darken2);
                            WriteAddress(c, booking.Sender);
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("NGƯỜI NHẬN").Bold().FontColor(Colors.Grey.Darken2);
                            WriteAddress(c, booking.Receiver);
                        });
                    });

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });
                        AddInfoCell(table, "Chiều", $"{booking.Direction} · {booking.FromCountry} → {booking.ToCountry}");
                        AddInfoCell(table, "Dịch vụ", $"{booking.Service}{(string.IsNullOrWhiteSpace(booking.CarrierCode) ? "" : $" ({booking.CarrierCode})")}");
                        AddInfoCell(table, "Ngày gửi", booking.ShipDate.ToString("dd/MM/yyyy"));
                        AddInfoCell(table, "Trạng thái", booking.Status);
                        AddInfoCell(table, "Ngày tạo", booking.CreatedAt.ToString("dd/MM/yyyy HH:mm"));
                    });

                    if (booking.Packages.Count > 0)
                    {
                        col.Item().Text("KIỆN HÀNG").Bold().FontColor(Colors.Grey.Darken2);
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(30);
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });
                            AddTableHeader(table, "SL", "KT thực (kg)", "Kích thước (cm)", "KT tính cước (kg)", "Loại");
                            foreach (var pkg in booking.Packages)
                            {
                                AddCell(table, pkg.Pieces.ToString());
                                AddCell(table, pkg.ActualWeight.ToString("0.##"));
                                AddCell(table, $"{pkg.Length:0.#}×{pkg.Width:0.#}×{pkg.Height:0.#}");
                                AddCell(table, pkg.ChargeableWeight.ToString("0.##"));
                                AddCell(table, pkg.PackageType ?? "");
                            }
                        });
                    }

                    if (booking.Commodities.Count > 0)
                    {
                        col.Item().Text("HÀNG HÓA").Bold().FontColor(Colors.Grey.Darken2);
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2);
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });
                            AddTableHeader(table, "Tên hàng", "SL", "Trị giá", "Xuất xứ");
                            foreach (var item in booking.Commodities)
                            {
                                AddCell(table, item.ItemName);
                                AddCell(table, item.Quantity.ToString("0.##"));
                                AddCell(table, $"{item.UnitValue:0.##} {item.Currency}");
                                AddCell(table, item.CountryOfOrigin ?? "");
                            }
                        });
                    }

                    if (booking.Quote is { } quote)
                    {
                        col.Item().Text("CƯỚC PHÍ").Bold().FontColor(Colors.Grey.Darken2);
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });
                            AddInfoCell(table, "Zone", quote.Zone);
                            AddInfoCell(table, "Cước", quote.Freight.ToString("N0"));
                            AddInfoCell(table, "Phụ phí nhiên liệu", quote.Fuel.ToString("N0"));
                            AddInfoCell(table, "Phụ phí khác", quote.Demand.ToString("N0"));
                            AddInfoCell(table, "Xử lý", quote.Handling.ToString("N0"));
                            AddInfoCell(table, "VAT", quote.Vat.ToString("N0"));
                        });
                        col.Item().AlignRight().Text($"Tổng: {quote.Total:N0}").FontSize(12).Bold();
                    }

                    if (!string.IsNullOrWhiteSpace(booking.Note))
                    {
                        col.Item().Text($"Ghi chú: {booking.Note}").FontSize(8).Italic();
                    }
                });

                page.Footer().AlignRight().Height(90).Image(qrImage).FitHeight();
            });
        });

        return document.GeneratePdf();
    }

    private static void WriteAddress(ColumnDescriptor column, BookingAddressDto? address)
    {
        if (address is null)
        {
            column.Item().Text("(chưa có thông tin)").FontSize(8).Italic();
            return;
        }

        column.Item().Text(address.Name).SemiBold();
        if (!string.IsNullOrWhiteSpace(address.Company)) column.Item().Text(address.Company);
        if (!string.IsNullOrWhiteSpace(address.Phone)) column.Item().Text($"ĐT: {address.Phone}");
        column.Item().Text(FormatAddressLines(address));
    }

    private static string FormatAddressLines(BookingAddressDto address)
    {
        var parts = new[] { address.Line1, address.City, address.State, address.PostalCode, address.CountryCode }
            .Where(x => !string.IsNullOrWhiteSpace(x));
        return string.Join(", ", parts);
    }

    private static void AddInfoCell(TableDescriptor table, string label, string value)
    {
        table.Cell().Text(label).FontColor(Colors.Grey.Darken1).FontSize(8);
        table.Cell().Text(value).SemiBold();
    }

    private static void AddTableHeader(TableDescriptor table, params string[] headers)
    {
        foreach (var header in headers)
            table.Cell().Background(Colors.Grey.Lighten3).Padding(2).Text(header).FontSize(8).Bold();
    }

    private static void AddCell(TableDescriptor table, string value)
    {
        table.Cell().Padding(2).Text(value).FontSize(8);
    }

    private static byte[] BuildQrPng(string value)
    {
        using var generator = new QRCodeGenerator();
        var data = generator.CreateQrCode(value, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data);
        return png.GetGraphic(10);
    }
}
