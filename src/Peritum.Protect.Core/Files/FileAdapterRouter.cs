using Peritum.Protect.Core.Redaction;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Files;

public sealed class FileAdapterRouter
{
    public const long MaxBytes = 32 * 1024 * 1024;

    private readonly IFileAdapter[] _adapters;

    public FileAdapterRouter(IOcrEngine? ocr = null)
    {
        var eml = new EmlFileAdapter();
        var zip = new ZipFileAdapter();
        _adapters =
        [
            new TextFileAdapter(),
            new HtmlFileAdapter(),
            new DocxFileAdapter(),
            new XlsxFileAdapter(),
            new PptxFileAdapter(),
            new PdfFileAdapter(),
            new ImageFileAdapter(ocr),
            eml,
            zip
        ];
        eml.Bind(this);
        zip.Bind(this);
    }

    public DocumentRead Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return DocumentRead.Fail("Could not read this file. Original was not changed.");
        var uncheckedReason = UncheckedFiles.Explain(path);
        if (uncheckedReason is not null)
            return DocumentRead.Fail(uncheckedReason, UncheckedFiles.ReasonCode(uncheckedReason));
        if (!SupportedFileTypes.IsSupported(path))
            return DocumentRead.Fail("This file type is not supported in this version.");

        var adapter = Resolve(path);
        if (adapter is null)
            return DocumentRead.Fail("This file type is not supported in this version.");

        try
        {
            var length = new FileInfo(path).Length;
            if (length > MaxBytes)
                return DocumentRead.Fail("This file is too large to scan.");
            return adapter.Read(path);
        }
        catch (PackageLimitException limit)
        {
            return new DocumentRead { Incomplete = true, Note = limit.Message };
        }
        catch
        {
            return DocumentRead.Fail("Could not read this file. Original was not changed.");
        }
    }

    public DocumentRead ReadLoaded(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return DocumentRead.Fail("Could not read this file. Original was not changed.");
        var extension = Path.GetExtension(path);
        if (UncheckedFiles.IsLegacy(extension))
            return DocumentRead.Fail(UncheckedFiles.LegacyType);
        if (!SupportedFileTypes.IsSupported(path))
            return DocumentRead.Fail("This file type is not supported in this version.");
        var adapter = Resolve(path);
        if (adapter is null)
            return DocumentRead.Fail("This file type is not supported in this version.");
        try
        {
            return adapter.Read(path);
        }
        catch (PackageLimitException limit)
        {
            return new DocumentRead { Incomplete = true, Note = limit.Message };
        }
        catch
        {
            return DocumentRead.Fail("Could not read this file. Original was not changed.");
        }
    }

    public DocumentRead ReadNested(string path, PackageLimits.Budget budget, int depth)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return DocumentRead.Fail("Could not read this file. Original was not changed.");
        if (depth > PackageLimits.MaxNestingDepth)
            return new DocumentRead { Incomplete = true, Note = PackageLimits.NestedTooDeep };

        var extension = Path.GetExtension(path);
        if (UncheckedFiles.IsLegacy(extension))
            return DocumentRead.Fail(UncheckedFiles.LegacyType);
        if (!SupportedFileTypes.IsSupported(path))
            return DocumentRead.Fail("This file type is not supported in this version.");

        var adapter = Resolve(path);
        if (adapter is null)
            return DocumentRead.Fail("This file type is not supported in this version.");

        try
        {
            if (adapter is IPackageFile package)
                return package.Read(path, budget, depth);
            var length = new FileInfo(path).Length;
            PackageLimits.Charge(budget, length);
            if (length > MaxBytes)
                return DocumentRead.Fail("This file is too large to scan.");
            return adapter.Read(path);
        }
        catch (PackageLimitException limit)
        {
            return new DocumentRead { Incomplete = true, Note = limit.Message };
        }
        catch
        {
            return DocumentRead.Fail("Could not read this file. Original was not changed.");
        }
    }

    public byte[] BuildProtected(string path, DocumentRead read, IReadOnlyList<Finding> findings, CopyRedaction? redaction = null)
    {
        var adapter = Resolve(path) ?? throw new InvalidOperationException("This file type is not supported in this version.");
        return adapter.BuildProtected(path, read, findings, redaction);
    }

    private IFileAdapter? Resolve(string path)
    {
        var ext = Path.GetExtension(path);
        foreach (var adapter in _adapters)
        {
            if (adapter.CanHandle(ext))
                return adapter;
        }
        return null;
    }
}
