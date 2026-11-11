using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PulseCut;

public partial class MainWindow : Window
{
    private const double TimelineInset = 16;
    private readonly DispatcherTimer playbackTimer;
    private MediaFile? source;
    private CancellationTokenSource? operation;
    private bool previewReady;
    private bool playing;
    private bool updatingPosition;
    private bool closing;
    private double trimDragOffset;

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

    private void OnMinimizeWindow(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnToggleWindowState(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

    private void OnCloseWindow(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

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
            LocalFileLabel.Visibility = Visibility.Visible;
            StartBox.Text = "0";
            EndBox.Text = DisplayNumber(media.Duration);
            FormatBox.ItemsSource = new[] { "mp4", "mp3", "wav" }
                .Where(media.Supports).Select(format => format.ToUpperInvariant()).ToArray();
            FormatBox.SelectedIndex = 0;
            PositionSlider.Maximum = media.Duration;
            TickQuarter.Text = DisplayTime(media.Duration / 4, precise: false);
            TickHalf.Text = DisplayTime(media.Duration / 2, precise: false);
            TickThreeQuarter.Text = DisplayTime(media.Duration * 3 / 4, precise: false);
            TickEnd.Text = DisplayTime(media.Duration, precise: false);
            ShowPosition(0);
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
        MarkControls.IsEnabled = operation is null;
        PreviewInfo.Text = source?.VideoIndex is not null
            ? $"{Preview.NaturalVideoWidth} × {Preview.NaturalVideoHeight}"
            : "Audio";
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
        MarkControls.IsEnabled = false;
        PreviewInfo.Text = "";
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
        ShowPositionLabel(e.NewValue);
    }

    private void ShowPosition(double position)
    {
        updatingPosition = true;
        PositionSlider.Value = position;
        ShowPositionLabel(position);
        updatingPosition = false;
    }

    private void ShowPositionLabel(double position) =>
        PositionLabel.Text = $"{DisplayTime(position)} / {DisplayTime(source?.Duration ?? 0)}";

    private void OnMarkStart(object sender, RoutedEventArgs e) => MarkPosition(StartBox);
    private void OnMarkEnd(object sender, RoutedEventArgs e) => MarkPosition(EndBox);

    private void MarkPosition(TextBox field)
    {
        if (previewReady && operation is null)
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
        DurationLabel.Text = clip is null ? "—" : DisplayTime(clip.Duration);
        var format = FormatBox.SelectedItem as string;
        ExportButton.Content = format is null ? "_Exportar clip" : $"_Exportar {format}";
        CodecLabel.Text = format switch
        {
            "MP4" => source?.AudioIndex is not null ? "H.264 · AAC" : "H.264",
            "MP3" => "MP3 · 192 kb/s",
            "WAV" => "PCM · 16 bits",
            _ => ""
        };
        ExportButton.IsEnabled = operation is null && clip is not null;
        UpdateTrimTrack();
    }

    private void OnTimelineSizeChanged(object sender, SizeChangedEventArgs e) => UpdateTrimTrack();

    private void UpdateTrimTrack()
    {
        if (source is null || StartHandle is null || EndHandle is null)
            return;
        var duration = source.Duration;
        var width = Math.Max(0, TimelineSurface.ActualWidth - 2 * TimelineInset);
        var start = ReadBoundary(StartBox, 0, duration);
        var end = ReadBoundary(EndBox, duration, duration);
        var startX = TimelineInset + width * start / duration;
        var endX = TimelineInset + width * end / duration;

        Canvas.SetLeft(StartHandle, startX - StartHandle.Width);
        Canvas.SetLeft(EndHandle, endX);
        Canvas.SetLeft(TrimSelection, startX);
        TrimSelection.Width = Math.Max(0, endX - startX);
        TrimSelection.Visibility = start < end ? Visibility.Visible : Visibility.Collapsed;
        StartHandle.Visibility = EndHandle.Visibility = Visibility.Visible;
        StartHandle.IsEnabled = EndHandle.IsEnabled = operation is null;
        TrimSelection.Opacity = operation is null ? 1 : 0.45;
    }

    private void OnTrimDragStarted(object sender, DragStartedEventArgs e)
    {
        var handle = (Thumb)sender;
        handle.Focus();
        var boundary = Canvas.GetLeft(handle) + (handle == StartHandle ? handle.Width : 0);
        trimDragOffset = Mouse.GetPosition(TimelineSurface).X - boundary;
    }

    private void OnTrimDrag(object sender, DragDeltaEventArgs e)
    {
        var width = TimelineSurface.ActualWidth - 2 * TimelineInset;
        if (source is null || width <= 0)
            return;
        var relativeX = Mouse.GetPosition(TimelineSurface).X - trimDragOffset - TimelineInset;
        SetTrimBoundary(sender == StartHandle, relativeX / width * source.Duration);
    }

    private void OnTrimKeyDown(object sender, KeyEventArgs e)
    {
        if (source is null || e.Key is not (Key.Left or Key.Right))
            return;
        var isStart = sender == StartHandle;
        var current = ReadBoundary(isStart ? StartBox : EndBox, isStart ? 0 : source.Duration, source.Duration);
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1.0 : 0.1;
        SetTrimBoundary(isStart, current + (e.Key == Key.Right ? step : -step));
        e.Handled = true;
    }

    private void SetTrimBoundary(bool isStart, double seconds)
    {
        if (source is null || operation is not null)
            return;
        var duration = source.Duration;
        var gap = Math.Min(0.01, duration);
        var position = Math.Round(seconds, 3);
        if (isStart)
        {
            var end = ReadBoundary(EndBox, duration, duration);
            StartBox.Text = DisplayNumber(Math.Clamp(position, 0, Math.Max(0, end - gap)));
        }
        else
        {
            var start = ReadBoundary(StartBox, 0, duration);
            EndBox.Text = DisplayNumber(Math.Clamp(position, Math.Min(duration, start + gap), duration));
        }
    }

    private static double ReadBoundary(TextBox field, double fallback, double duration) =>
        TrySeconds(field.Text, out var seconds) && double.IsFinite(seconds) && seconds >= 0 && seconds <= duration
            ? seconds
            : fallback;

    private Clip? ReadClip()
    {
        if (source is null || SpeedBox.SelectedItem is not double speed ||
            FormatBox.SelectedItem is not string format || !TrySeconds(StartBox.Text, out var start) ||
            !TrySeconds(EndBox.Text, out var end))
            return null;
        var clip = new Clip(start, end, speed, format.ToLowerInvariant());
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
        MarkControls.IsEnabled = false;
        ExportButton.IsEnabled = false;
        CancelButton.Visibility = Visibility.Visible;
        ExportProgress.Value = 0;
        ExportProgress.Visibility = exporting ? Visibility.Visible : Visibility.Collapsed;
        StatusLabel.Text = message;
        UpdateTrimTrack();
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
        MarkControls.IsEnabled = previewReady;
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
    private static string DisplayTime(double value, bool precise = true)
    {
        var time = TimeSpan.FromSeconds(value);
        var text = $"{(long)time.TotalMinutes:00}:{time.Seconds:00}";
        return precise ? $"{text}.{time.Milliseconds / 10:00}" : text;
    }
}
