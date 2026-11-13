using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace Peritum.Protect.Core.Files;

public static class OfficePackageValidator
{
    public const string CleanCopyError = "Could not make a clean copy of this file.";

    public static string? Check(byte[] package, string extension, byte[]? original = null)
    {
        try
        {
            using var stream = new MemoryStream(package);
            using var document = Open(stream, extension);
            if (document is null)
                return null;
            var introduced = Descriptions(document);
            if (original is not null)
            {
                try
                {
                    using var source = new MemoryStream(original);
                    using var sourceDocument = Open(source, extension);
                    if (sourceDocument is not null)
                        introduced.ExceptWith(Descriptions(sourceDocument));
                }
                catch (Exception)
                {
                    // The original could not be opened. Any output error still counts.
                }
            }
            return introduced.Count == 0 ? null : CleanCopyError;
        }
        catch (Exception)
        {
            return CleanCopyError;
        }
    }

    private static HashSet<string> Descriptions(OpenXmlPackage document)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var error in new OpenXmlValidator().Validate(document))
        {
            if (!string.IsNullOrWhiteSpace(error.Description))
                set.Add(error.Description);
        }
        return set;
    }

    private static OpenXmlPackage? Open(Stream stream, string extension)
    {
        if (extension.Equals(".docx", StringComparison.OrdinalIgnoreCase))
            return WordprocessingDocument.Open(stream, false);
        if (extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            return SpreadsheetDocument.Open(stream, false);
        if (extension.Equals(".pptx", StringComparison.OrdinalIgnoreCase))
            return PresentationDocument.Open(stream, false);
        return null;
    }
}
