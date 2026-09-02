using System;
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ClassRoom_Control.Services.Teacher;

public class AudioRecordingService : IDisposable
{
    private WasapiCapture? _micCapture;
    private WasapiLoopbackCapture? _systemCapture;

    private BufferedWaveProvider? _micBuffer;
    private BufferedWaveProvider? _systemBuffer;

    private WaveFileWriter? _writer;
    private string? _outputWavPath;
    private bool _isRecording = false;

    private System.Threading.Timer? _mixTimer;
    private MixingSampleProvider? _mixer;
    private IWaveProvider? _finalProvider;
    private readonly byte[] _transferBuffer = new byte[8192];

    public bool IsRecording => _isRecording;

    public void StartRecording(string outputWavPath)
    {
        if (_isRecording) return;
        _outputWavPath = outputWavPath;

        try
        {
            var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
            var sampleProviders = new System.Collections.Generic.List<ISampleProvider>();

            // 1. System audio capture (WASAPI Loopback)
            try
            {
                _systemCapture = new WasapiLoopbackCapture();
                _systemBuffer = new BufferedWaveProvider(_systemCapture.WaveFormat)
                {
                    DiscardOnBufferOverflow = true,
                    ReadFully = false
                };

                _systemCapture.DataAvailable += (s, a) =>
                {
                    if (_systemBuffer != null && a.BytesRecorded > 0)
                    {
                        _systemBuffer.AddSamples(a.Buffer, 0, a.BytesRecorded);
                    }
                };

                var sysSampleProvider = _systemBuffer.ToSampleProvider();
                if (_systemCapture.WaveFormat.SampleRate != 48000 || _systemCapture.WaveFormat.Channels != 2)
                {
                    sysSampleProvider = new WdlResamplingSampleProvider(sysSampleProvider, 48000);
                    if (_systemCapture.WaveFormat.Channels == 1)
                        sysSampleProvider = new MonoToStereoSampleProvider(sysSampleProvider);
                }
                sampleProviders.Add(sysSampleProvider);
                _systemCapture.StartRecording();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WASAPI Loopback error (no speakers?): {ex.Message}");
            }

            // 2. Microphone capture (WASAPI)
            try
            {
                _micCapture = new WasapiCapture();
                _micBuffer = new BufferedWaveProvider(_micCapture.WaveFormat)
                {
                    DiscardOnBufferOverflow = true,
                    ReadFully = false
                };

                _micCapture.DataAvailable += (s, a) =>
                {
                    if (_micBuffer != null && a.BytesRecorded > 0)
                    {
                        _micBuffer.AddSamples(a.Buffer, 0, a.BytesRecorded);
                    }
                };

                var micSampleProvider = _micBuffer.ToSampleProvider();
                if (_micCapture.WaveFormat.SampleRate != 48000 || _micCapture.WaveFormat.Channels != 2)
                {
                    micSampleProvider = new WdlResamplingSampleProvider(micSampleProvider, 48000);
                    if (_micCapture.WaveFormat.Channels == 1)
                        micSampleProvider = new MonoToStereoSampleProvider(micSampleProvider);
                }
                sampleProviders.Add(micSampleProvider);
                _micCapture.StartRecording();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WASAPI Microphone capture error (no mic?): {ex.Message}");
            }

            if (sampleProviders.Count > 0)
            {
                _mixer = new MixingSampleProvider(sampleProviders)
                {
                    ReadFully = true
                };

                // Convert float to 16-bit PCM for universal compatibility
                _finalProvider = new SampleToWaveProvider16(_mixer);
                _writer = new WaveFileWriter(outputWavPath, _finalProvider.WaveFormat);

                _isRecording = true;

                // Background timer to read from mixer and write to WAV file
                _mixTimer = new System.Threading.Timer(MixCallback, null, 50, 50);
            }
            else
            {
                // Fallback dummy WAV if no audio devices found
                var silentFormat = new WaveFormat(48000, 16, 2);
                _writer = new WaveFileWriter(outputWavPath, silentFormat);
                _isRecording = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AudioRecordingService.StartRecording failed: {ex.Message}");
            StopRecording();
            throw;
        }
    }

    private void MixCallback(object? state)
    {
        if (!_isRecording || _finalProvider == null || _writer == null) return;

        try
        {
            while (true)
            {
                int read = _finalProvider.Read(_transferBuffer, 0, _transferBuffer.Length);
                if (read > 0)
                {
                    _writer.Write(_transferBuffer, 0, read);
                }
                else
                {
                    break;
                }
            }
        }
        catch { }
    }

    public void StopRecording()
    {
        if (!_isRecording) return;
        _isRecording = false;

        _mixTimer?.Dispose();
        _mixTimer = null;

        try
        {
            _systemCapture?.StopRecording();
            _systemCapture?.Dispose();
            _systemCapture = null;
        }
        catch { }

        try
        {
            _micCapture?.StopRecording();
            _micCapture?.Dispose();
            _micCapture = null;
        }
        catch { }

        try
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }
        catch { }
    }

    public void Dispose()
    {
        StopRecording();
    }
}
