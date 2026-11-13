using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Peritum.Protect.App.Native;
using Peritum.Protect.Core.Clipboard;

namespace Peritum.Protect.App.Services;

public sealed class ClipboardWatcher : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly ClipboardNative _native = new();
    private readonly DispatcherTimer _debounce;
    private readonly DispatcherTimer _poll;
    private uint _lastSequence;
    private string? _previous;
    private string? _current;
    private string? _ignoreText;
    private bool _ignoreClear;
    private bool _started;

    public ClipboardWatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            ReadAndPublish();
        };
        _poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _poll.Tick += (_, _) =>
        {
            var sequence = GetClipboardSequenceNumber();
            if (sequence == _lastSequence)
                return;
            ReadAndPublish();
        };
    }

    public event Action<ClipboardScanInput, string?, uint>? Captured;
    public event Action<string>? Faulted;

    public string? PreviousText => _previous;

    public void Start()
    {
        if (_started)
            return;
        _lastSequence = GetClipboardSequenceNumber();
        try
        {
            _native.Updated += OnUpdated;
            _native.Start();
        }
        catch
        {
            // A message-only listener can fail. The poll below still sees new copies.
        }
        _poll.Start();
        _started = true;
    }

    public void SetText(string text) => SetRich(text, null, null);

    public void SetRich(string? plain, string? html, byte[]? png)
    {
        var text = plain ?? "";
        _ignoreText = text;
        _previous = _current;
        _current = text;
        RunOnUi(() =>
        {
            try
            {
                var data = new DataObject();
                data.SetText(text);
                if (!string.IsNullOrEmpty(html))
                    data.SetData(DataFormats.Html, html);
                if (png is { Length: > 0 })
                {
                    using var stream = new MemoryStream(png);
                    var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    data.SetImage(frame);
                }

                Clipboard.SetDataObject(data, true);
            }
            catch (Exception ex) when (ex is ExternalException or COMException)
            {
                Faulted?.Invoke("Clipboard busy. Will try again on the next copy.");
            }
        });
    }

    public void ApplyStop(bool restorePrevious)
    {
        if (restorePrevious && !string.IsNullOrEmpty(_previous))
        {
            SetText(_previous);
            return;
        }

        _ignoreClear = true;
        _current = "";
        RunOnUi(() =>
        {
            try
            {
                Clipboard.Clear();
            }
            catch (Exception ex) when (ex is ExternalException or COMException)
            {
                Faulted?.Invoke("Clipboard busy. Will try again on the next copy.");
            }
        });
    }

    public void Dispose()
    {
        _debounce.Stop();
        _poll.Stop();
        if (_started)
            _native.Updated -= OnUpdated;
        _native.Dispose();
        _started = false;
    }

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    private void OnUpdated()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void ReadAndPublish()
    {
        _lastSequence = GetClipboardSequenceNumber();
        var state = TryRead(out var input, out var error);
        if (state == ReadState.Busy)
        {
            Faulted?.Invoke(error ?? "Clipboard busy. Will try again on the next copy.");
            return;
        }
        if (state == ReadState.Empty)
        {
            if (_ignoreClear)
            {
                _ignoreClear = false;
                return;
            }
            return;
        }

        if (input.Text is not null && input.Text == _ignoreText)
        {
            _ignoreText = null;
            _current = input.Text;
            return;
        }

        if (input.Text is not null)
        {
            _previous = _current;
            _current = input.Text;
        }

        Captured?.Invoke(input, WindowNative.ForegroundProcessName(), _lastSequence);
    }

    private static ReadState TryRead(out ClipboardScanInput input, out string? error)
    {
        input = new ClipboardScanInput();
        error = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                string? text = null;
                if (Clipboard.ContainsText())
                    text = Clipboard.GetText();
                var files = new List<string>();
                if (Clipboard.ContainsFileDropList())
                {
                    foreach (string? file in Clipboard.GetFileDropList())
                    {
                        if (!string.IsNullOrWhiteSpace(file))
                            files.Add(file);
                    }
                }

                string? html = ReadFormat(DataFormats.Html);
                string? rtf = ReadFormat(DataFormats.Rtf);
                var image = ReadImage();
                if (text is null && files.Count == 0 && html is null && rtf is null && image.Bytes is null && !image.TooBig)
                    return ReadState.Empty;
                input = new ClipboardScanInput
                {
                    Text = text,
                    FilePaths = files,
                    Html = html,
                    Rtf = rtf,
                    ImageBytes = image.Bytes,
                    ImageWidth = image.Width,
                    ImageHeight = image.Height,
                    ImageExceedsLimit = image.TooBig
                };
                return ReadState.Ok;
            }
            catch (Exception ex) when (ex is ExternalException or COMException)
            {
                error = "Clipboard busy. Will try again on the next copy.";
                Thread.Sleep(40);
            }
        }

        return ReadState.Busy;
    }

    private static string? ReadFormat(string format)
    {
        if (!Clipboard.ContainsData(format))
            return null;
        var data = Clipboard.GetData(format);
        switch (data)
        {
            case string text when !string.IsNullOrWhiteSpace(text):
                return text;
            case MemoryStream stream:
                using (stream)
                {
                    using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
                    var text = reader.ReadToEnd();
                    return string.IsNullOrWhiteSpace(text) ? null : text;
                }
            default:
                return null;
        }
    }

    private static (byte[]? Bytes, int Width, int Height, bool TooBig) ReadImage()
    {
        if (!Clipboard.ContainsImage())
            return (null, 0, 0, false);
        var source = Clipboard.GetImage();
        if (source is null)
            return (null, 0, 0, false);
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        if (ClipboardImageLimits.Exceeds(0, width, height))
            return (null, width, height, true);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        var bytes = stream.ToArray();
        if (ClipboardImageLimits.Exceeds(bytes.Length, width, height))
            return (null, width, height, true);
        return (bytes, width, height, false);
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher.CheckAccess())
            action();
        else
            _dispatcher.Invoke(action);
    }

    private enum ReadState
    {
        Empty,
        Ok,
        Busy
    }
}
