using System.ComponentModel;
using System.Windows.Media;

namespace PictureViewer;

/// <summary>
/// One cell of the thumbnail strip. The thumbnail loads the first time the UI asks for it,
/// so the virtualizing strip only decodes cells that are actually on screen.
/// </summary>
public sealed class ThumbItem(string filePath) : INotifyPropertyChanged
{
    const int ThumbSize = 160;
    static readonly SemaphoreSlim Gate = new(3); // decode a few at a time

    ImageSource? _thumb;
    bool _requested;

    public string FilePath { get; } = filePath;
    public string FileName => Path.GetFileName(FilePath);

    public event PropertyChangedEventHandler? PropertyChanged;

    public ImageSource? Thumb
    {
        get
        {
            if (!_requested)
            {
                _requested = true;
                _ = LoadAsync();
            }
            return _thumb;
        }
    }

    async Task LoadAsync()
    {
        await Gate.WaitAsync();
        try
        {
            _thumb = await Task.Run(() => ImageLoader.LoadThumbnail(FilePath, ThumbSize));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumb)));
        }
        catch (Exception)
        {
            // Undecodable file: leave the cell blank.
        }
        finally
        {
            Gate.Release();
        }
    }
}
