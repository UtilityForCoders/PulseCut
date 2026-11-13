using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PulseCut;

public partial class MainWindow : Window
{
    private const double TimelineInset = 16;
    private const int FilmstripFrames = 8;
    private readonly DispatcherTimer playbackTimer;
    private readonly Stack<EditorState> undoHistory = new();
    private readonly Stack<EditorState> redoHistory = new();
    private MediaFile? source;
    private CancellationTokenSource? operation;
    private EditorState? lastState;
    private EditorState? dragStartState;
    private string? lastExportPath;
    private bool previewReady;
    private bool playing;
    private bool updatingPosition;
    private bool applyingState;
    private bool suppressHistory;
    private bool closing;
    private bool fullscreen;
    private double trimDragOffset;
    private WindowState stateBeforeFullscreen;

    private sealed record EditorState(double Start, double End, double Speed, string Format,
        string Resolution, string Quality);

    public MainWindow()
    {
        InitializeComponent();
        SpeedBox.ItemsSource = new[] { 0.5, 0.75, 1, 1.5, 2 };
        SpeedBox.SelectedItem = 1d;
        playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        playbackTimer.Tick += (_, _) =>
        {
            if (previewReady)
                ShowPosition(Preview.Position.TotalSeconds);
        };
        UpdatePropertyPanel("Trim");
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
        if (operation is not null)
            return;
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
        e.Effects = operation is null && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy : DragDropEffects.None;
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
            applyingState = true;
            FileTitle.Text = Path.GetFileName(media.Path);
            StartBox.Text = "0";
            EndBox.Text = DisplayNumber(media.Duration);
            FormatBox.ItemsSource = new[] { "mp4", "mp3", "wav" }
                .Where(media.Supports).Select(format => format.ToUpperInvariant()).ToArray();
            FormatBox.SelectedIndex = 0;
            ResolutionBox.IsEnabled = media.VideoIndex.HasValue;
            QualityBox.IsEnabled = media.VideoIndex.HasValue;
            if (!media.VideoIndex.HasValue)
                ResolutionBox.SelectedIndex = 0;
            PositionSlider.Maximum = media.Duration;
            UpdateTimelineTicks();
            ShowPosition(0);
            ShowPreviewMessage("Preparando vista previa…", "También puedes editar usando los tiempos exactos.", false);
            Preview.Source = new Uri(media.Path);
            Preview.Volume = VolumeSlider.Value;
            Preview.Play();
            undoHistory.Clear();
            redoHistory.Clear();
            applyingState = false;
            lastState = ReadEditorState();
            UpdateHistoryButtons();
            UpdateSettings();
            StatusLabel.Text = "Preparando la cinta de vista previa…";
            await LoadTimelinePreviewAsync(media, operation!.Token);
            StatusLabel.Text = "Archivo listo para editar";
        }
        catch (OperationCanceledException) { StatusLabel.Text = "Importación cancelada"; }
        catch (Exception error) { ShowError("No se pudo abrir el archivo", error); }
        finally
        {
            applyingState = false;
            EndOperation();
        }
    }

    private async Task LoadTimelinePreviewAsync(MediaFile media, CancellationToken cancellationToken)
    {
        ResetTimelinePreview();
        if (!media.VideoIndex.HasValue)
        {
            TimelineMediaIcon.HorizontalAlignment = HorizontalAlignment.Center;
            TimelineMediaIcon.Text = "\uE8D6";
            TimelineFileName.Visibility = Visibility.Collapsed;
            TimelineTrack.ToolTip = Path.GetFileName(media.Path);
            return;
        }

        IReadOnlyList<byte[]> frames;
        try { frames = await MediaEngine.RenderFilmstripAsync(media, FilmstripFrames, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { frames = Array.Empty<byte[]>(); }
        if (frames.Count == 0)
        {
            TimelineMediaIcon.HorizontalAlignment = HorizontalAlignment.Center;
            TimelineFileName.Text = Path.GetFileName(media.Path);
            TimelineTrack.ToolTip = Path.GetFileName(media.Path);
            return;
        }

        TimelineMediaIcon.Visibility = Visibility.Collapsed;
        TimelineFileName.Visibility = Visibility.Collapsed;
        TimelineFilmstrip.Columns = frames.Count;
        foreach (var bytes in frames)
        {
            var image = new Image
            {
                Source = CreateFrame(bytes),
                Stretch = Stretch.UniformToFill,
                ClipToBounds = true,
                SnapsToDevicePixels = true
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            TimelineFilmstrip.Children.Add(image);
        }
        TimelineFilmstrip.Visibility = Visibility.Visible;
        TimelineFilmstripShade.Visibility = Visibility.Visible;
        TimelineTrack.ToolTip = Path.GetFileName(media.Path);
    }

    private void ResetTimelinePreview()
    {
        TimelineFilmstrip.Children.Clear();
        TimelineFilmstrip.Visibility = Visibility.Collapsed;
        TimelineFilmstripShade.Visibility = Visibility.Collapsed;
        TimelineMediaIcon.Visibility = Visibility.Visible;
        TimelineMediaIcon.HorizontalAlignment = HorizontalAlignment.Left;
        TimelineMediaIcon.Text = "\uE714";
        TimelineFileName.Text = "Abre un archivo para comenzar";
        TimelineFileName.Visibility = Visibility.Visible;
        TimelineTrack.ToolTip = null;
    }

    private static BitmapImage CreateFrame(byte[] data)
    {
        var image = new BitmapImage();
        using var stream = new MemoryStream(data);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void OnMediaOpened(object sender, RoutedEventArgs e)
    {
        Preview.Pause();
        previewReady = true;
        PlayButton.IsEnabled = true;
        PositionSlider.IsEnabled = true;
        MediaBadge.Visibility = Visibility.Visible;
        PreviewInfo.Text = source?.VideoIndex is not null
            ? $"{Preview.NaturalVideoWidth} × {Preview.NaturalVideoHeight}"
            : "AUDIO";
        if (source?.VideoIndex is null)
            ShowPreviewMessage("Vista previa de audio", "Usa la línea de tiempo para navegar y definir el recorte.", false, audio: true);
        else
            EmptyPreview.Visibility = Visibility.Collapsed;
        ShowPosition(0);
    }

    private void OnMediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        ResetPreview();
        ShowPreviewMessage("Vista previa no disponible",
            "Windows no puede reproducir este formato, pero puedes recortarlo por tiempo y exportarlo.", false);
        StatusLabel.Text = "El archivo se puede editar sin vista previa";
    }

    private void OnMediaEnded(object sender, RoutedEventArgs e)
    {
        Preview.Pause();
        playing = false;
        playbackTimer.Stop();
        PlayButton.Content = "\uE768";
        PlayButton.ToolTip = "Reproducir (Espacio)";
        ShowPosition(source?.Duration ?? 0);
    }

    private void ShowPreviewMessage(string title, string hint, bool showOpenButton, bool audio = false)
    {
        EmptyPreview.Visibility = Visibility.Visible;
        EmptyIcon.Text = audio ? "\uE8D6" : "\uE714";
        PreviewMessage.Text = title;
        PreviewHint.Text = hint;
        OpenButton.Visibility = showOpenButton ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ResetPreview()
    {
        previewReady = false;
        playing = false;
        playbackTimer.Stop();
        Preview.Close();
        Preview.Source = null;
        PlayButton.Content = "\uE768";
        PlayButton.IsEnabled = false;
        PositionSlider.IsEnabled = false;
        MediaBadge.Visibility = Visibility.Collapsed;
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
            PlayButton.Content = "\uE768";
            PlayButton.ToolTip = "Reproducir (Espacio)";
        }
        else
        {
            if (source is not null && PositionSlider.Value >= source.Duration - 0.05)
                Preview.Position = TimeSpan.Zero;
            Preview.Play();
            playbackTimer.Start();
            PlayButton.Content = "\uE769";
            PlayButton.ToolTip = "Pausar (Espacio)";
        }
        playing = !playing;
    }

    private void OnSeekBackward(object sender, RoutedEventArgs e) => SeekBy(-5);
    private void OnSeekForward(object sender, RoutedEventArgs e) => SeekBy(5);

    private void SeekBy(double seconds)
    {
        if (!previewReady || source is null)
            return;
        var position = Math.Clamp(PositionSlider.Value + seconds, 0, source.Duration);
        Preview.Position = TimeSpan.FromSeconds(position);
        ShowPosition(position);
    }

    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Preview is not null)
            Preview.Volume = e.NewValue;
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
        var duration = clip is null ? "—" : DisplayTime(clip.Duration);
        DurationLabel.Text = duration;
        SpeedDurationLabel.Text = duration;
        var format = FormatBox.SelectedItem as string;
        ExportButton.Content = format is null ? "Exportar clip" : $"Exportar {format}";
        CodecLabel.Text = format switch
        {
            "MP4" => source?.AudioIndex is not null ? "H.264 · AAC" : "H.264",
            "MP3" => "MP3 · 192 kb/s",
            "WAV" => "PCM · 16 bits",
            _ => ""
        };
        var videoOutput = format == "MP4";
        ResolutionBox.IsEnabled = videoOutput && operation is null;
        QualityBox.IsEnabled = videoOutput && operation is null;
        ExportButton.IsEnabled = operation is null && clip is not null;
        TrimSummary.Text = clip is null ? "Revisa los valores del recorte" :
            $"{DisplayTime(clip.Start)}  →  {DisplayTime(clip.End)}   ·   {DisplayTime(clip.Duration)}";
        UpdateTrimTrack();
        if (!applyingState && !suppressHistory)
            RecordCurrentState();
    }

    private void RecordCurrentState()
    {
        var current = ReadEditorState();
        if (current is null || current == lastState)
            return;
        if (lastState is not null)
            undoHistory.Push(lastState);
        lastState = current;
        redoHistory.Clear();
        UpdateHistoryButtons();
    }

    private EditorState? ReadEditorState()
    {
        if (source is null || SpeedBox.SelectedItem is not double speed ||
            FormatBox.SelectedItem is not string format ||
            !TrySeconds(StartBox.Text, out var start) || !TrySeconds(EndBox.Text, out var end))
            return null;
        return new EditorState(start, end, speed, format, SelectedTag(ResolutionBox), SelectedTag(QualityBox));
    }

    private void ApplyState(EditorState state)
    {
        applyingState = true;
        StartBox.Text = DisplayNumber(state.Start);
        EndBox.Text = DisplayNumber(state.End);
        SpeedBox.SelectedItem = state.Speed;
        FormatBox.SelectedItem = state.Format;
        SelectComboTag(ResolutionBox, state.Resolution);
        SelectComboTag(QualityBox, state.Quality);
        SelectSpeedChoice(state.Speed);
        applyingState = false;
        lastState = state;
        UpdateSettings();
    }

    private void OnUndo(object sender, RoutedEventArgs e)
    {
        if (undoHistory.Count == 0 || lastState is null || operation is not null)
            return;
        redoHistory.Push(lastState);
        ApplyState(undoHistory.Pop());
        UpdateHistoryButtons();
    }

    private void OnRedo(object sender, RoutedEventArgs e)
    {
        if (redoHistory.Count == 0 || lastState is null || operation is not null)
            return;
        undoHistory.Push(lastState);
        ApplyState(redoHistory.Pop());
        UpdateHistoryButtons();
    }

    private void UpdateHistoryButtons()
    {
        if (UndoButton is null)
            return;
        UndoButton.IsEnabled = operation is null && undoHistory.Count > 0;
        RedoButton.IsEnabled = operation is null && redoHistory.Count > 0;
    }

    private void OnClearSelection(object sender, RoutedEventArgs e)
    {
        if (source is null || operation is not null)
            return;
        StartBox.Text = "0";
        EndBox.Text = DisplayNumber(source.Duration);
        StatusLabel.Text = "Selección de recorte restablecida";
    }

    private void OnTimelineSizeChanged(object sender, SizeChangedEventArgs e) => UpdateTrimTrack();

    private void UpdateTimelineTicks()
    {
        var duration = source?.Duration ?? 0;
        TickQuarter.Text = DisplayTime(duration / 4, precise: false);
        TickHalf.Text = DisplayTime(duration / 2, precise: false);
        TickThreeQuarter.Text = DisplayTime(duration * 3 / 4, precise: false);
        TickEnd.Text = DisplayTime(duration, precise: false);
    }

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
        dragStartState = lastState;
        suppressHistory = true;
    }

    private void OnTrimDrag(object sender, DragDeltaEventArgs e)
    {
        var width = TimelineSurface.ActualWidth - 2 * TimelineInset;
        if (source is null || width <= 0)
            return;
        var relativeX = Mouse.GetPosition(TimelineSurface).X - trimDragOffset - TimelineInset;
        SetTrimBoundary(sender == StartHandle, relativeX / width * source.Duration);
    }

    private void OnTrimDragCompleted(object sender, DragCompletedEventArgs e)
    {
        suppressHistory = false;
        lastState = dragStartState;
        dragStartState = null;
        RecordCurrentState();
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
            ? seconds : fallback;

    private Clip? ReadClip()
    {
        if (source is null || SpeedBox.SelectedItem is not double speed ||
            FormatBox.SelectedItem is not string format || !TrySeconds(StartBox.Text, out var start) ||
            !TrySeconds(EndBox.Text, out var end))
            return null;
        var clip = new Clip(start, end, speed, format.ToLowerInvariant(), SelectedTag(ResolutionBox), SelectedTag(QualityBox));
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
            lastExportPath = dialog.FileName;
            OpenFolderButton.Visibility = Visibility.Visible;
            StatusLabel.Text = $"Exportación terminada · {Path.GetFileName(dialog.FileName)}";
        }
        catch (OperationCanceledException) { StatusLabel.Text = "Exportación cancelada"; }
        catch (Exception error) { ShowError("No se pudo exportar el clip", error); }
        finally { EndOperation(); }
    }

    private void OnOpenExportFolder(object sender, RoutedEventArgs e)
    {
        if (lastExportPath is null || !File.Exists(lastExportPath))
            return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{lastExportPath}\"") { UseShellExecute = true });
    }

    private void BeginOperation(string message, bool exporting)
    {
        operation = new CancellationTokenSource();
        OpenButton.IsEnabled = false;
        EditControls.IsEnabled = false;
        ExportButton.IsEnabled = false;
        CancelButton.Visibility = Visibility.Visible;
        OpenFolderButton.Visibility = Visibility.Collapsed;
        ExportProgress.Value = 0;
        ExportProgress.Visibility = exporting ? Visibility.Visible : Visibility.Collapsed;
        StatusLabel.Text = message;
        UpdateHistoryButtons();
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
        CancelButton.Visibility = Visibility.Collapsed;
        ExportProgress.Visibility = Visibility.Collapsed;
        UpdateHistoryButtons();
        UpdateSettings();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => operation?.Cancel();

    private void OnPropertyTab(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string panel })
            UpdatePropertyPanel(panel);
    }

    private void UpdatePropertyPanel(string panel)
    {
        TrimPanel.Visibility = panel == "Trim" ? Visibility.Visible : Visibility.Collapsed;
        SpeedPanel.Visibility = panel == "Speed" ? Visibility.Visible : Visibility.Collapsed;
        OutputPanel.Visibility = panel == "Output" ? Visibility.Visible : Visibility.Collapsed;
        TrimTab.IsChecked = panel == "Trim";
        SpeedTab.IsChecked = panel == "Speed";
        OutputTab.IsChecked = panel == "Output";
    }

    private void OnShowTrim(object sender, RoutedEventArgs e) => UpdatePropertyPanel("Trim");
    private void OnShowSpeed(object sender, RoutedEventArgs e) => UpdatePropertyPanel("Speed");
    private void OnShowOutput(object sender, RoutedEventArgs e) => UpdatePropertyPanel("Output");
    private void OnNavigateEdit(object sender, RoutedEventArgs e) => UpdatePropertyPanel("Trim");
    private void OnNavigateSettings(object sender, RoutedEventArgs e) => UpdatePropertyPanel("Output");

    private void OnNavigateExport(object sender, RoutedEventArgs e)
    {
        UpdatePropertyPanel("Output");
        ExportButton.Focus();
    }

    private void OnSpeedSelected(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string value } &&
            double.TryParse(value, CultureInfo.InvariantCulture, out var speed))
            SpeedBox.SelectedItem = speed;
    }

    private void SelectSpeedChoice(double speed)
    {
        foreach (var choice in VisualChildren<RadioButton>(SpeedPanel))
            if (choice.Tag is string value && double.TryParse(value, CultureInfo.InvariantCulture, out var itemSpeed))
                choice.IsChecked = itemSpeed == speed;
    }

    private void OnZoomIn(object sender, RoutedEventArgs e) => ZoomSlider.Value = Math.Min(ZoomSlider.Maximum, ZoomSlider.Value + 1);
    private void OnZoomOut(object sender, RoutedEventArgs e) => ZoomSlider.Value = Math.Max(ZoomSlider.Minimum, ZoomSlider.Value - 1);

    private void OnZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TimelineSurface is null || TimelineViewport is null)
            return;
        TimelineSurface.Width = Math.Max(600, TimelineViewport.ViewportWidth * e.NewValue);
        UpdateTrimTrack();
    }

    private void OnTimelineMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            return;
        ZoomSlider.Value = Math.Clamp(ZoomSlider.Value + (e.Delta > 0 ? 1 : -1), ZoomSlider.Minimum, ZoomSlider.Maximum);
        e.Handled = true;
    }

    private void OnToggleFullscreen(object sender, RoutedEventArgs e)
    {
        if (!fullscreen)
        {
            stateBeforeFullscreen = WindowState;
            SidebarColumn.Width = new GridLength(0);
            PropertiesColumn.Width = new GridLength(0);
            TimelineRow.Height = new GridLength(0);
            WindowState = WindowState.Maximized;
            fullscreen = true;
        }
        else
        {
            SidebarColumn.Width = new GridLength(76);
            PropertiesColumn.Width = new GridLength(310);
            TimelineRow.Height = new GridLength(202);
            WindowState = stateBeforeFullscreen;
            fullscreen = false;
        }
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBoxBase or ComboBox)
            return;
        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (control && e.Key == Key.O) { OnOpen(sender, e); e.Handled = true; }
        else if (control && e.Key == Key.E) { OnExport(sender, e); e.Handled = true; }
        else if (control && e.Key == Key.Z) { OnUndo(sender, e); e.Handled = true; }
        else if (control && e.Key == Key.Y) { OnRedo(sender, e); e.Handled = true; }
        else if (e.Key == Key.Space) { OnPlay(sender, e); e.Handled = true; }
        else if (e.Key == Key.I) { OnMarkStart(sender, e); e.Handled = true; }
        else if (e.Key == Key.O) { OnMarkEnd(sender, e); e.Handled = true; }
        else if (e.Key == Key.Delete) { OnClearSelection(sender, e); e.Handled = true; }
        else if (e.Key == Key.Left) { SeekBy(-1); e.Handled = true; }
        else if (e.Key == Key.Right) { SeekBy(1); e.Handled = true; }
        else if (e.Key == Key.Escape && fullscreen) { OnToggleFullscreen(sender, e); e.Handled = true; }
    }

    private void OnAbout(object sender, RoutedEventArgs e) =>
        MessageBox.Show(this, "PulseCut\nEditor local y ligero de video y audio.\n\nTus archivos no salen del equipo.",
            "Acerca de PulseCut", MessageBoxButton.OK, MessageBoxImage.Information);

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

    private static string SelectedTag(ComboBox comboBox) =>
        comboBox.SelectedItem is ComboBoxItem { Tag: string tag } ? tag : "original";

    private static void SelectComboTag(ComboBox comboBox, string tag)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
            if (Equals(item.Tag, tag))
            {
                comboBox.SelectedItem = item;
                return;
            }
    }

    private static IEnumerable<T> VisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;
            foreach (var descendant in VisualChildren<T>(child))
                yield return descendant;
        }
    }

    private static bool TrySeconds(string text, out double seconds) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);

    private static string DisplayNumber(double value) => value.ToString("0.###", CultureInfo.CurrentCulture);

    private static string DisplayTime(double value, bool precise = true)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, value));
        var text = $"{(long)time.TotalMinutes:00}:{time.Seconds:00}";
        return precise ? $"{text}.{time.Milliseconds / 10:00}" : text;
    }
}
