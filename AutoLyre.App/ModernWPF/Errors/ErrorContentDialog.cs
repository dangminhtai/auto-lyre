using System;
using System.Collections.Generic;
using System.Linq;
using Humanizer;
using ModernWpf.Controls;

namespace AutoLyre.App.ModernWPF.Errors;

public class ErrorContentDialog : ContentDialog
{
    public ErrorContentDialog(Exception e, IReadOnlyCollection<Enum>? options = null, string? closeText = null)
    {
        System.Diagnostics.Trace.TraceError(e.ToString());
        Title = "Không thể hoàn tất thao tác";
        Content = "Không đọc được dữ liệu. Hãy chọn một bản nhạc khác hoặc thử lại.";

        PrimaryButtonText = Translate(options?.ElementAtOrDefault(0));
        SecondaryButtonText = Translate(options?.ElementAtOrDefault(1));

        CloseButtonText = closeText ?? "Hủy";
    }

    private static string? Translate(Enum? value) => value?.ToString() switch
    {
        null => null,
        "SnapToLimits" => "Đưa về giới hạn hợp lệ",
        "ReadValid" => "Đọc phần hợp lệ",
        "ReadAsUnknownChunk" => "Giữ khối dữ liệu chưa xác định",
        "SkipStatusByte" => "Bỏ qua byte trạng thái",
        _ => "Bỏ qua"
    };
}
