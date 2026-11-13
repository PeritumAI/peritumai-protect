using Peritum.Protect.Core.Files;

namespace Peritum.Protect.Core.Tests;

public class SupportedFileTypesTests
{
    [Theory]
    [InlineData("notes.txt")]
    [InlineData("report.DOCX")]
    [InlineData("photo.webp")]
    public void Recognizes_supported_files_case_insensitively(string path) =>
        Assert.True(SupportedFileTypes.IsSupported(path));

    [Fact]
    public void Dialog_filter_and_summary_come_from_the_catalog()
    {
        Assert.Contains("*.xlsx", SupportedFileTypes.DialogFilter, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Office", SupportedFileTypes.DisplaySummary, StringComparison.Ordinal);
    }
}
