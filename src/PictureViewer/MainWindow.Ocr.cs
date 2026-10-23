using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;
using Windows.Globalization;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace PictureViewer;

// Text recognition: drag an area of the open Picture and its text goes to the clipboard.
public partial class MainWindow
{
    /// <summary>Languages tried, in this order; only those with an OCR language pack installed are used.</summary>
    static readonly string[] OcrLanguageTags = ["zh-Hant-TW", "zh-Hans-CN", "en-US", "ja", "ko"];

    /// <summary>Spaces the recognizer puts between two CJK characters.</summary>
    static readonly Regex CjkGap = new(@"(?<=[　-鿿가-힯＀-￯]) (?=[　-鿿가-힯＀-￯])", RegexOptions.Compiled);
    static readonly Regex Cjk = new(@"[぀-鿿가-힯]", RegexOptions.Compiled);

    void InitOcr() => View.Layer.OcrAreaChosen += (_, area) => RecognizeText(area);

    void OnOcrClick(object sender, RoutedEventArgs e) => OcrAction();

    /// <summary>Turns area selection for text recognition on or off.</summary>
    void OcrAction()
    {
        if (_current is null)
        {
            OcrButton.IsChecked = false;
            return;
        }
        var layer = View.Layer;
        if (layer.OcrMode)
        {
            layer.CropMode = false;
            return;
        }

        if (View.QuarterTurns != 0) FlattenPending(); // the area is chosen in the Picture's own pixels
        layer.CropMode = false;
        layer.CropAspect = null;
        layer.OcrMode = true;
        layer.CropMode = true;
        ShowToast("Drag an area to recognize its text (Esc cancels)");
    }

    async void RecognizeText(Int32Rect area)
    {
        View.Layer.CropMode = false;
        if (_current is null) return;

        byte[] png;
        using (var buffer = new MemoryStream())
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(ImageEdit.Crop(_current, area)));
            encoder.Save(buffer);
            png = buffer.ToArray();
        }

        ShowToast("Recognizing text…");
        try
        {
            string? text = await Task.Run(() => RunOcr(png));
            if (text is null)
            {
                ShowToast("No OCR language is installed. Add a language in Settings → Time & language → Language & region.");
                return;
            }
            if (text.Length == 0)
            {
                ShowToast("No text found in that area");
                return;
            }

            try
            {
                Clipboard.SetText(text);
            }
            catch (COMException)
            {
                ShowToast("Couldn't reach the clipboard; try again.");
                return;
            }
            string first = text.Split('\n')[0];
            if (first.Length > 60) first = first[..60] + "…";
            ShowToast($"Copied {text.Length} characters: {first}");
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException or IOException)
        {
            ShowToast($"Text recognition failed: {ex.Message}");
        }
    }

    /// <summary>The text in a PNG, or null when no OCR language is installed. Runs on a worker thread.</summary>
    static async Task<string?> RunOcr(byte[] png)
    {
        var engines = new List<(string Tag, OcrEngine Engine)>();
        foreach (string tag in OcrLanguageTags)
        {
            try
            {
                var language = new Language(tag);
                if (OcrEngine.IsLanguageSupported(language) && OcrEngine.TryCreateFromLanguage(language) is { } engine)
                    engines.Add((tag, engine));
            }
            catch (ArgumentException)
            {
                // a tag this Windows doesn't know
            }
        }
        if (engines.Count == 0 && OcrEngine.TryCreateFromUserProfileLanguages() is { } fallback)
            engines.Add((fallback.RecognizerLanguage.LanguageTag, fallback));
        if (engines.Count == 0) return null;

        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(png);
            await writer.StoreAsync().AsTask();
            writer.DetachStream();
        }
        stream.Seek(0);
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream).AsTask();

        // The recognizer refuses pictures larger than its limit on either side.
        var transform = new Windows.Graphics.Imaging.BitmapTransform();
        uint longSide = Math.Max(decoder.PixelWidth, decoder.PixelHeight);
        if (longSide > OcrEngine.MaxImageDimension)
        {
            double scale = (double)OcrEngine.MaxImageDimension / longSide;
            transform.ScaledWidth = (uint)Math.Max(1, decoder.PixelWidth * scale);
            transform.ScaledHeight = (uint)Math.Max(1, decoder.PixelHeight * scale);
        }
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
            transform, Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,
            Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage).AsTask();

        // There is no language detection, so read it with every installed language and keep the fullest result.
        var results = new List<(string Tag, string Text, int Count)>();
        foreach (var (tag, engine) in engines)
        {
            var result = await engine.RecognizeAsync(bitmap).AsTask();
            string text = CjkGap.Replace(string.Join("\n", result.Lines.Select(l => l.Text)), "").Trim();
            results.Add((tag, text, text.Count(c => !char.IsWhiteSpace(c))));
        }

        var best = results.MaxBy(r => r.Count);
        // Plain Latin text reads best (word spacing) with the English recognizer, if it got nearly as much.
        var english = results.FirstOrDefault(r => r.Tag == "en-US");
        if (english.Text != null && !Cjk.IsMatch(best.Text) && english.Count >= best.Count * 0.9)
            return english.Text;
        return best.Text;
    }
}
