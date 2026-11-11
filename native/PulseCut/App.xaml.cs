using System.IO;
using System.Text.Json;
using System.Windows;

namespace PulseCut;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args is ["--smoke-test", var input, var outputDirectory])
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Directory.CreateDirectory(outputDirectory);
            try
            {
                MainWindow = new MainWindow();
                var source = await MediaEngine.ProbeAsync(input, CancellationToken.None);
                var outputs = new Dictionary<string, double>();
                foreach (var (format, speed) in new[] { ("mp4", 2d), ("mp3", 4d), ("wav", 0.25d) })
                {
                    var path = Path.Combine(outputDirectory, $"clip.{format}");
                    var clip = new Clip(1, 3, speed, format);
                    await MediaEngine.ExportAsync(source, clip, path, null, CancellationToken.None);
                    var exported = await MediaEngine.ProbeAsync(path, CancellationToken.None);
                    if (Math.Abs(exported.Duration - clip.Duration) > 0.15 ||
                        !exported.AudioIndex.HasValue || (format == "mp4") != exported.VideoIndex.HasValue)
                        throw new InvalidDataException($"La salida {format} no tiene la duración o las pistas esperadas.");
                    outputs.Add(format, exported.Duration);
                }
                await File.WriteAllTextAsync(Path.Combine(outputDirectory, "report.json"),
                    JsonSerializer.Serialize(outputs, new JsonSerializerOptions { WriteIndented = true }));
                Shutdown(0);
            }
            catch (Exception error)
            {
                await File.WriteAllTextAsync(Path.Combine(outputDirectory, "error.txt"), error.ToString());
                Shutdown(1);
            }
            return;
        }
        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
