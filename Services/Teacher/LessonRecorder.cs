using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace ClassRoom_Control.Services.Teacher;

public class LessonRecorder : IDisposable
{
    private readonly AudioRecordingService _audioService = new();
    private FileStream? _videoFileStream;
    private string? _tempVideoPath;
    private string? _tempAudioPath;
    private string? _outputMp4Path;
    private int _fps = 30;
    private bool _isRecording = false;
    private DateTime _recordingStartTime;

    public bool IsRecording => _isRecording;
    public TimeSpan Duration => _isRecording ? DateTime.UtcNow - _recordingStartTime : TimeSpan.Zero;

    public void StartRecording(int width, int height, int fps = 30)
    {
        if (_isRecording) return;

        _fps = fps > 0 ? fps : 30;
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "ClassRoom Recordings");
        Directory.CreateDirectory(folder);

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        _tempVideoPath = Path.Combine(folder, $"temp_video_{timestamp}.h264");
        _tempAudioPath = Path.Combine(folder, $"temp_audio_{timestamp}.wav");
        _outputMp4Path = Path.Combine(folder, $"Урок_{timestamp}.mp4");

        _videoFileStream = new FileStream(_tempVideoPath, FileMode.Create, FileAccess.Write, FileShare.Read, 65536);

        // Start audio capture
        _audioService.StartRecording(_tempAudioPath);

        _recordingStartTime = DateTime.UtcNow;
        _isRecording = true;
    }

    public void WriteVideoFrame(byte[] buffer, int offset, int count)
    {
        if (!_isRecording || _videoFileStream == null || count <= 0) return;

        try
        {
            _videoFileStream.Write(buffer, offset, count);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error writing video frame to recording: {ex.Message}");
        }
    }

    public async Task<string?> StopRecordingAsync()
    {
        if (!_isRecording) return null;
        _isRecording = false;

        // 1. Flush and close video stream
        try
        {
            if (_videoFileStream != null)
            {
                await _videoFileStream.FlushAsync();
                _videoFileStream.Dispose();
                _videoFileStream = null;
            }
        }
        catch { }

        // 2. Stop audio capture
        try
        {
            _audioService.StopRecording();
        }
        catch { }

        // 3. Remux to MP4 using FFmpeg if available
        string? ffmpegPath = FindFfmpeg();
        if (!string.IsNullOrEmpty(ffmpegPath) && File.Exists(_tempVideoPath) && File.Exists(_tempAudioPath) && _outputMp4Path != null)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-y -r {_fps} -i \"{_tempVideoPath}\" -i \"{_tempAudioPath}\" -c:v copy -c:a aac -b:a 192k -movflags +faststart \"{_outputMp4Path}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    await process.WaitForExitAsync();

                    if (File.Exists(_outputMp4Path))
                    {
                        // Clean up temporary files
                        try { File.Delete(_tempVideoPath); } catch { }
                        try { File.Delete(_tempAudioPath); } catch { }
                        return _outputMp4Path;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FFmpeg muxing failed: {ex.Message}");
            }
        }

        // If FFmpeg was not found or failed, return the video path as fallback
        return _outputMp4Path ?? _tempVideoPath;
    }

    private static string? FindFfmpeg()
    {
        // 1. Check in application folder
        string appFfmpeg = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(appFfmpeg)) return appFfmpeg;

        // 2. Check in tools/ subfolder
        string toolsFfmpeg = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe");
        if (File.Exists(toolsFfmpeg)) return toolsFfmpeg;

        // 3. Check in System PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var part in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    string candidate = Path.Combine(part.Trim(), "ffmpeg.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_isRecording)
        {
            _ = StopRecordingAsync();
        }
    }
}
