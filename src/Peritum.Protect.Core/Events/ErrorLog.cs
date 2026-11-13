using System.Text;

namespace Peritum.Protect.Core.Events;

public static class ErrorLog
{
    public static void Write(Exception exception, string? version = null, string? path = null)
    {
        if (exception is null)
            return;
        var file = path ?? Paths.ErrorLogFile;
        var dir = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var body = new StringBuilder();
        body.Append(DateTimeOffset.UtcNow.ToString("O"));
        body.Append(" version=").Append(version ?? "unknown");
        Append(body, exception);
        body.AppendLine();
        File.AppendAllText(file, body.ToString());
    }

    private static void Append(StringBuilder body, Exception exception)
    {
        body.AppendLine();
        body.Append(exception.GetType().FullName);
        body.Append(": ").Append(exception.Message);
        if (!string.IsNullOrWhiteSpace(exception.StackTrace))
        {
            body.AppendLine();
            body.Append(exception.StackTrace);
        }
        if (exception.InnerException is not null)
            Append(body, exception.InnerException);
    }
}
