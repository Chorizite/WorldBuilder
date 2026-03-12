using Microsoft.Extensions.Logging;
using Silk.NET.SDL;
using NLayer;

namespace Chorizite.OpenGLSDLBackend {
    public class AudioPlaybackEngine : IDisposable {
        private static Sdl _sdl;
        private uint _currentDeviceId;
        private bool _disposed;
        private readonly int _sampleRate;
        private readonly int _channelCount;
        private readonly int _bitsPerSample;
        private static readonly ILogger<AudioPlaybackEngine>? _logger;

        // Track active playback streams for cleanup
        private readonly Queue<(byte[] data, Stream stream)> _playbackQueue = new();
        private readonly object _queueLock = new();
        private CancellationTokenSource _cleanupCts;

        static AudioPlaybackEngine() {
            _sdl = Sdl.GetApi();

            // TODO: Inject logger properly when available
            _logger = null;

            unsafe {
                if (_sdl.Init(Sdl.InitAudio) < 0) {
                    var error = _sdl.GetErrorS();
                    _logger?.LogError("Failed to initialize SDL audio: {Error}", error);
                    throw new InvalidOperationException($"Failed to initialize SDL audio: {error}");
                }
            }

            _logger?.LogDebug("SDL audio initialized successfully");
        }

        public AudioPlaybackEngine(int sampleRate = 44100, int channelCount = 2, int bitsPerSample = 16) {
            _sampleRate = sampleRate;
            _channelCount = channelCount;
            _bitsPerSample = bitsPerSample;
            _cleanupCts = new CancellationTokenSource();
        }

        public void PlaySound(Stream audioStream, bool isMp3 = false) {
            if (_disposed) {
                _logger?.LogWarning("Attempted to play sound on disposed AudioPlaybackEngine");
                return;
            }

            try {
                byte[] pcmData;
                int channels;
                int sampleRate;
                int bitsPerSample = 16;

                if (isMp3) {
                    // Read MP3 data from stream
                    using var ms = new MemoryStream();
                    audioStream.CopyTo(ms);
                    byte[] mp3Data = ms.ToArray();
                    (pcmData, channels, sampleRate) = DecodeMp3(mp3Data);
                    bitsPerSample = 16; // MP3 always decodes to 16-bit
                }
                else {
                    // Read WAV data from stream
                    (pcmData, channels, sampleRate, bitsPerSample) = ReadWavStream(audioStream);
                }

                PlayAudioData(pcmData, channels, sampleRate, bitsPerSample, audioStream);
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Failed to play sound");
                audioStream?.Dispose();
            }
        }

        private void PlayAudioData(byte[] pcmData, int channels, int sampleRate, int bitsPerSample, Stream originalStream) {
            // Track this playback for cleanup
            lock (_queueLock) {
                _playbackQueue.Enqueue((pcmData, originalStream));
            }

            unsafe {
                // Only close device if format parameters changed significantly
                if (_currentDeviceId != 0) {
                    // Check if we need to reopen device with different format
                    var currentSpec = _sdl.GetAudioDeviceStatus(_currentDeviceId);
                    if (channels != _channelCount || sampleRate != _sampleRate || bitsPerSample != _bitsPerSample) {
                        _sdl.CloseAudioDevice(_currentDeviceId);
                        _currentDeviceId = 0;
                    }
                }

                // Determine SDL audio format based on bit depth
                ushort format = (ushort)((bitsPerSample == 8) ? 0x0008 : 0x8010); // SDL_AUDIO_U8 or SDL_AUDIO_S16LSB

                // Create audio spec with correct format
                AudioSpec desiredSpec = new AudioSpec();
                desiredSpec.Freq = sampleRate;
                desiredSpec.Format = format;
                desiredSpec.Channels = (byte)channels;
                desiredSpec.Silence = 0;
                desiredSpec.Samples = 4096;
                desiredSpec.Size = 0;
                desiredSpec.Padding = 0;

                // Open audio device only if needed
                if (_currentDeviceId == 0) {
                    _currentDeviceId = _sdl.OpenAudioDevice((string?)null, 0, &desiredSpec, null, 0);
                    if (_currentDeviceId == 0) {
                        var error = _sdl.GetErrorS();
                        _logger?.LogError("Failed to open audio device: {Error}", error);
                        return;
                    }
                }

                // Clear any queued audio to prevent buildup
                _sdl.ClearQueuedAudio(_currentDeviceId);

                // Queue audio data
                fixed (byte* dataPtr = pcmData) {
                    if (_sdl.QueueAudio(_currentDeviceId, dataPtr, (uint)pcmData.Length) < 0) {
                        var error = _sdl.GetErrorS();
                        _sdl.CloseAudioDevice(_currentDeviceId);
                        _currentDeviceId = 0;
                        _logger?.LogError("Failed to queue audio: {Error}", error);
                        return;
                    }
                }

                // Start playback
                _sdl.PauseAudioDevice(_currentDeviceId, 0);
            }

            _logger?.LogDebug("Audio playback started");

            // Schedule cleanup
            _ = CleanupPlaybackTask(_cleanupCts.Token);
        }

        // The async cleanup method
        private async Task CleanupPlaybackTask(CancellationToken cancellationToken) {
            try {
                // Wait for audio to finish playing
                while (!cancellationToken.IsCancellationRequested) {
                    await Task.Delay(100, cancellationToken);

                    if (_currentDeviceId != 0) {
                        unsafe {
                            var queuedBytes = _sdl.GetQueuedAudioSize(_currentDeviceId);
                            if (queuedBytes == 0) {
                                break;
                            }
                        }
                    }
                }

                // Dispose completed streams
                lock (_queueLock) {
                    while (_playbackQueue.Count > 0) {
                        var (data, stream) = _playbackQueue.Dequeue();
                        stream?.Dispose();
                    }
                }
            }
            catch (OperationCanceledException) {
                // Cleanup during disposal
            }
        }

        public void Stop() {
            unsafe {
                if (_currentDeviceId != 0) {
                    _sdl.PauseAudioDevice(_currentDeviceId, 1); // 1 = pause
                    _sdl.ClearQueuedAudio(_currentDeviceId);
                }
            }
        }

        private (byte[] pcmData, int channels, int sampleRate) DecodeMp3(byte[] mp3Data) {
            using (var stream = new MemoryStream(mp3Data)) {
                var decoder = new MpegFile(stream);

                int channels = decoder.Channels;
                int sampleRate = decoder.SampleRate;

                // Pre-allocate with estimated size to reduce reallocations
                var pcmSamples = new List<short>((int)(mp3Data.Length / 4)); // Rough estimate
                float[] buffer = new float[8192]; // Larger buffer for better performance

                int samplesRead;
                while ((samplesRead = decoder.ReadSamples(buffer, 0, buffer.Length)) > 0) {
                    // Convert float samples to 16-bit PCM with proper clipping
                    for (int i = 0; i < samplesRead; i++) {
                        float sample = buffer[i];

                        // Clamp to [-1.0f, 1.0f] to prevent distortion
                        if (sample > 1.0f) sample = 1.0f;
                        if (sample < -1.0f) sample = -1.0f;

                        // Convert to 16-bit
                        short pcmSample = (short)(sample * 32767f);
                        pcmSamples.Add(pcmSample);
                    }
                }

                // Convert to byte array in one operation
                byte[] pcmBuffer = new byte[pcmSamples.Count * 2];
                Buffer.BlockCopy(pcmSamples.ToArray(), 0, pcmBuffer, 0, pcmBuffer.Length);

                return (pcmBuffer, channels, sampleRate);
            }
        }

        private (byte[] data, int channels, int sampleRate, int bitsPerSample) ReadWavStream(Stream stream) {
            using (var reader = new BinaryReader(stream)) {
                if (reader.ReadInt32() != 0x46464952)
                    throw new InvalidOperationException("Not a valid WAV file");

                reader.ReadInt32();

                if (reader.ReadInt32() != 0x45564157)
                    throw new InvalidOperationException("Not a valid WAV file");

                int channels = 0, sampleRate = 0, bitsPerSample = 0;
                byte[]? data = null;

                while (stream.Position < stream.Length) {
                    int chunkId = reader.ReadInt32();
                    int chunkSize = reader.ReadInt32();

                    if (chunkId == 0x20746D66) // "fmt "
                    {
                        reader.ReadInt16(); // audio format (1 = PCM)
                        channels = reader.ReadInt16();
                        sampleRate = reader.ReadInt32();
                        reader.ReadInt32(); // byte rate
                        reader.ReadInt16(); // block align
                        bitsPerSample = reader.ReadInt16();

                        stream.Seek(chunkSize - 16, SeekOrigin.Current);
                    }
                    else if (chunkId == 0x61746164) // "data"
                    {
                        data = reader.ReadBytes(chunkSize);
                    }
                    else {
                        stream.Seek(chunkSize, SeekOrigin.Current);
                    }
                }

                if (data == null)
                    throw new InvalidOperationException("WAV file has no data chunk");

                return (data, channels, sampleRate, bitsPerSample);
            }
        }

        public void Dispose() {
            if (_disposed) return;

            Stop();

            _cleanupCts?.Cancel();
            _cleanupCts?.Dispose();

            unsafe {
                if (_currentDeviceId != 0) {
                    _sdl.CloseAudioDevice(_currentDeviceId);
                    _currentDeviceId = 0;
                }
            }

            // Clean up any remaining streams
            lock (_queueLock) {
                while (_playbackQueue.Count > 0) {
                    var (data, stream) = _playbackQueue.Dequeue();
                    stream?.Dispose();
                }
            }

            _disposed = true;
            _logger?.LogDebug("AudioPlaybackEngine disposed");
        }
    }
}