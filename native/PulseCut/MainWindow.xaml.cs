using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PulseCut;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer playbackTimer;
    private MediaFile? source;
    private CancellationTokenSource? operation;
    private bool previewReady;
    private bool playing;
    private bool updatingPosition;
    private bool closing;

    public MainWindow()
    {
        InitializeComponent();
        SpeedBox.ItemsSource = new[] { 0.25, 0.5, 0.75, 1, 1.25, 1.5, 2, 3, 4 };
        SpeedBox.SelectedItem = 1d;
        playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        playbackTimer.Tick += (_, _) =>
        {
            if (previewReady)
                ShowPosition(Preview.Position.TotalSeconds);
        };
    }

    private async void OnOpen(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Abrir video o audio",
            Filter = "Video y audio|*.mp4;*.mov;*.mkv;*.webm;*.avi;*.wmv;*.m4v;*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.opus|Todos los archivos|*.*"
        };
        if (dialog.ShowDialog(this) == true)
            await OpenAsync(dialog.FileName);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = operation is null && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (operation is null && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            await OpenAsync(files[0]);
    }

    private async Task OpenAsync(string path)
    {
        BeginOperation("Leyendo archivo…", exporting: false);
        try
        {
            var media = await MediaEngine.ProbeAsync(path, operation!.Token);
            ResetPreview();
            source = media;
            FileTitle.Text = Path.GetFileName(media.Path);
            StartBox.Text = "0";
            EndBox.Text = DisplayNumber(media.Duration);
            FormatBox.ItemsSource = new[] { "mp4", "mp3", "wav" }.Where(media.Supports).ToArray();
            FormatBox.SelectedIndex = 0;
            PositionSlider.Maximum = media.Duration;
            PreviewMessage.Text = "Preparando vista previa…";
            PreviewMessage.Visibility = Visibility.Visible;
            StatusLabel.Text = "Archivo listo para editar";
            Preview.Source = new Uri(media.Path);
            Preview.Play();
        }
        catch (OperationCanceledException) { StatusLabel.Text = "Importación cancelada"; }
        catch (Exception error) { ShowError("No se pudo abrir el archivo", error); }
        finally { EndOperation(); }
    }

    private void OnMediaOpened(object sender, RoutedEventArgs e)
    {
        Preview.Pause();
        previewReady = true;
        PlayButton.IsEnabled = true;
        PositionSlider.IsEnabled = true;
        PreviewMessage.Text = "Vista previa de audio";
        PreviewMessage.Visibility = source?.VideoIndex is null ? Visibility.Visible : Visibility.Collapsed;
        ShowPosition(0);
    }

    private void OnMediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        ResetPreview();
        PreviewMessage.Text = "Windows no puede reproducir este formato. Puedes recortarlo por tiempo y exportarlo.";
        PreviewMessage.Visibility = Visibility.Visible;
    }

    private void OnMediaEnded(object sender, RoutedEventArgs e)
    {
        Preview.Pause();
        playing = false;
        playbackTimer.Stop();
        PlayButton.Content = "_Reproducir";
        ShowPosition(source?.Duration ?? 0);
    }

    private void ResetPreview()
    {
        previewReady = false;
        playing = false;
        playbackTimer.Stop();
        Preview.Close();
        Preview.Source = null;
        PlayButton.Content = "_Reproducir";
        PlayButton.IsEnabled = false;
        PositionSlider.IsEnabled = false;
        ShowPosition(0);
    }

    private void OnPlay(object sender, RoutedEventArgs e)
    {
        if (!previewReady)
            return;
        if (playing)
        {
            Preview.Pause();
            playbackTimer.Stop();
            PlayButton.Content = "_Reproducir";
        }
        else
        {
            if (source is not null && PositionSlider.Value >= source.Duration - 0.05)
                Preview.Position = TimeSpan.Zero;
            Preview.Play();
            playbackTimer.Start();
            PlayButton.Content = "_Pausar";
        }
        playing = !playing;
    }

    private void OnPositionChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!previewReady || updatingPosition)
            return;
        Preview.Position = TimeSpan.FromSeconds(e.NewValue);
        PositionLabel.Text = DisplayTime(e.NewValue);
    }

    private void ShowPosition(double position)
    {
        updatingPosition = true;
        PositionSlider.Value = position;
        PositionLabel.Text = DisplayTime(position);
        updatingPosition = false;
    }

    private void OnMarkStart(object sender, RoutedEventArgs e) => MarkPosition(StartBox);
    private void OnMarkEnd(object sender, RoutedEventArgs e) => MarkPosition(EndBox);

    private void MarkPosition(System.Windows.Controls.TextBox field)
    {
        if (previewReady)
            field.Text = DisplayNumber(PositionSlider.Value);
        else
            StatusLabel.Text = "La posición requiere una vista previa compatible";
    }

    private void OnSettingsChanged(object sender, RoutedEventArgs e) => UpdateSettings();

    private void UpdateSettings()
    {
        if (DurationLabel is null || ExportButton is null)
            return;
        var clip = ReadClip();
        DurationLabel.Text = clip is null ? "Duración final: —" : $"Duración final: {DisplayTime(clip.Duration)}";
        ExportButton.IsEnabled = operation is null && clip is not null;
    }

    private Clip? ReadClip()
    {
        if (source is null || SpeedBox.SelectedItem is not double speed ||
            FormatBox.SelectedItem is not string format || !TrySeconds(StartBox.Text, out var start) ||
            !TrySeconds(EndBox.Text, out var end))
            return null;
        var clip = new Clip(start, end, speed, format);
        return clip.IsValid(source) ? clip : null;
    }

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        if (source is not { } media || ReadClip() is not { } clip || operation is not null)
            return;
        var dialog = new SaveFileDialog
        {
            Title = "Guardar clip",
            Filter = $"Archivo {clip.Format.ToUpperInvariant()}|*.{clip.Format}",
            DefaultExt = clip.Format,
            FileName = Path.GetFileNameWithoutExtension(media.Path) + "-pulsecut"
        };
        if (dialog.ShowDialog(this) != true)
            return;
        BeginOperation("Exportando clip…", exporting: true);
        try
        {
            var progress = new Progress<double>(percent => ExportProgress.Value = percent);
            await MediaEngine.ExportAsync(media, clip, dialog.FileName, progress, operation!.Token);
            StatusLabel.Text = $"Clip guardado en {dialog.FileName}";
        }
        catch (OperationCanceledException) { StatusLabel.Text = "Exportación cancelada"; }
        catch (Exception error) { ShowError("No se pudo exportar el clip", error); }
        finally { EndOperation(); }
    }

    private void BeginOperation(string message, bool exporting)
    {
        operation = new CancellationTokenSource();
        OpenButton.IsEnabled = false;
        EditControls.IsEnabled = false;
        ExportButton.IsEnabled = false;
        CancelButton.Visibility = Visibility.Visible;
        ExportProgress.Value = 0;
        ExportProgress.Visibility = exporting ? Visibility.Visible : Visibility.Collapsed;
        StatusLabel.Text = message;
    }

    private void EndOperation()
    {
        operation?.Dispose();
        operation = null;
        if (closing)
        {
            Close();
            return;
        }
        OpenButton.IsEnabled = true;
        EditControls.IsEnabled = source is not null;
        CancelButton.Visibility = Visibility.Collapsed;
        ExportProgress.Visibility = Visibility.Collapsed;
        UpdateSettings();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => operation?.Cancel();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (operation is not null)
        {
            e.Cancel = true;
            closing = true;
            operation.Cancel();
        }
        else
            ResetPreview();
    }

    private void ShowError(string message, Exception error)
    {
        StatusLabel.Text = message;
        if (!closing)
            MessageBox.Show(this, error.Message, message, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static bool TrySeconds(string text, out double seconds) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);

    private static string DisplayNumber(double value) => value.ToString("R", CultureInfo.CurrentCulture);
    private static string DisplayTime(double value) => TimeSpan.FromSeconds(value).ToString(@"hh\:mm\:ss\.ff");
}
