using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace PulseCut;

internal sealed record MediaFile(string Path, double Duration, int? VideoIndex, int? AudioIndex)
{
    public bool Supports(string format) => format == "mp4" ? VideoIndex.HasValue :
        format is "mp3" or "wav" && AudioIndex.HasValue;
}

internal sealed record Clip(double Start, double End, double Speed, string Format,
    string Resolution = "original", string Quality = "balanced")
{
    public double Duration => (End - Start) / Speed;

    public bool IsValid(MediaFile source) =>
        double.IsFinite(Start) && double.IsFinite(End) && Start >= 0 && End > Start &&
        End <= source.Duration && Speed is >= 0.25 and <= 4 && source.Supports(Format) &&
        Resolution is "original" or "1080" or "720" or "480" &&
        Quality is "fast" or "balanced" or "high";
}

internal static class MediaEngine
{
    public static async Task<MediaFile> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        path = Path.GetFullPath(path);
        var output = await RunAsync("ffprobe", [
            "-v", "error", "-protocol_whitelist", "file", "-show_format", "-show_streams", "-of", "json", path
        ], null, cancellationToken);
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        int? video = null;
        int? audio = null;
        foreach (var stream in root.GetProperty("streams").EnumerateArray())
        {
            var type = stream.GetProperty("codec_type").GetString();
            var index = stream.GetProperty("index").GetInt32();
            if (type == "audio")
                audio ??= index;
            if (type == "video" && !IsCoverArt(stream))
                video ??= index;
        }
        if ((!video.HasValue && !audio.HasValue) ||
            !root.GetProperty("format").TryGetProperty("duration", out var durationValue) ||
            !double.TryParse(durationValue.GetString(), CultureInfo.InvariantCulture, out var duration) ||
            !double.IsFinite(duration) || duration <= 0)
            throw new InvalidDataException("El archivo debe contener video o audio con una duración válida.");
        return new MediaFile(path, duration, video, audio);
    }

    public static async Task ExportAsync(MediaFile source, Clip clip, string destination,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (!clip.IsValid(source))
            throw new ArgumentException("Revisa el tramo, la velocidad y el formato.");
        destination = Path.GetFullPath(destination);
        if (string.Equals(source.Path, destination, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Elige un nombre diferente al del archivo original.");
        if (!string.Equals(Path.GetExtension(destination), "." + clip.Format, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"La extensión del destino debe ser .{clip.Format}.");

        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".pulsecut-{Guid.NewGuid():N}.{clip.Format}");
        var args = new List<string> {
            "-hide_banner", "-loglevel", "error", "-nostdin", "-n", "-protocol_whitelist", "file",
            "-ss", Number(clip.Start), "-t", Number(clip.End - clip.Start), "-i", source.Path
        };
        if (clip.Format == "mp4")
        {
            var (preset, crf) = clip.Quality switch
            {
                "fast" => ("veryfast", "24"),
                "high" => ("slow", "19"),
                _ => ("medium", "22")
            };
            args.AddRange([
                "-map", $"0:{source.VideoIndex}",
                "-vf", $"setpts=(PTS-STARTPTS)/{Number(clip.Speed)},{ScaleFilter(clip.Resolution)}",
                "-c:v", "libx264", "-preset", preset, "-crf", crf, "-pix_fmt", "yuv420p",
                "-movflags", "+faststart"
            ]);
        }
        else
            args.Add("-vn");

        if (source.AudioIndex is int audio)
        {
            args.AddRange(["-map", $"0:{audio}", "-af", Tempo(clip.Speed), "-c:a",
                clip.Format switch { "wav" => "pcm_s16le", "mp3" => "libmp3lame", _ => "aac" }]);
            if (clip.Format != "wav")
                args.AddRange(["-b:a", "192k"]);
        }
        args.AddRange(["-progress", "pipe:1", "-nostats", temporary]);

        try
        {
            await RunAsync("ffmpeg", args, line =>
            {
                const string prefix = "out_time_us=";
                if (progress is not null && line.StartsWith(prefix, StringComparison.Ordinal) &&
                    long.TryParse(line.AsSpan(prefix.Length), out var microseconds))
                    progress.Report(Math.Clamp(microseconds / 1_000_000.0 / clip.Duration * 100, 0, 99));
            }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
            progress?.Report(100);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static bool IsCoverArt(JsonElement stream) =>
        stream.TryGetProperty("disposition", out var disposition) &&
        disposition.TryGetProperty("attached_pic", out var attached) && attached.GetInt32() == 1;

    private static string Tempo(double speed)
    {
        if (speed < 0.5)
            return $"atempo=0.5,atempo={Number(speed / 0.5)}";
        if (speed > 2)
            return $"atempo=2,atempo={Number(speed / 2)}";
        return $"atempo={Number(speed)}";
    }

    private static string Number(double value) => value.ToString("0.#########", CultureInfo.InvariantCulture);

    private static string ScaleFilter(string resolution)
    {
        var limit = resolution switch { "1080" => 1080, "720" => 720, "480" => 480, _ => 0 };
        return limit == 0
            ? "scale=max(2\\,trunc(iw/2)*2):max(2\\,trunc(ih/2)*2)"
            : $"scale=-2:min({limit}\\,ih)";
    }

    private static async Task<string> RunAsync(string tool, IEnumerable<string> args,
        Action<string>? onLine, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var executable = Path.Combine(AppContext.BaseDirectory, "bin", tool + ".exe");
        if (!File.Exists(executable))
            throw new FileNotFoundException($"Falta bin\\{tool}.exe. Extrae la carpeta completa de PulseCut.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException($"No se pudo iniciar {tool}.");
        using var cancellation = cancellationToken.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        var stderr = process.StandardError.ReadToEndAsync();
        string stdout = "";
        if (onLine is null)
            stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        else
        {
            while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
                onLine(line);
        }
        await process.WaitForExitAsync().ConfigureAwait(false);
        var errors = await stderr.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
            throw new InvalidDataException(string.IsNullOrWhiteSpace(errors) ? $"{tool} no pudo procesar el archivo." : errors.Trim());
        return stdout;
    }
}
