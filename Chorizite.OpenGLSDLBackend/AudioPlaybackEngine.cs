using Microsoft.Extensions.Logging;
using Silk.NET.SDL;
using MP3Sharp;

namespace Chorizite.OpenGLSDLBackend {
    public class AudioPlaybackEngine : IDisposable {
        private static Sdl _sdl;
        private uint _currentDeviceId;
        private bool _disposed;
        private readonly int _sampleRate;
        private readonly int _channelCount;
        private readonly int _bitsPerSample;
        private static readonly ILogger<AudioPlaybackEngine>? _logger;
        private bool _deviceInitialized = false;

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
            
            // Initialize device immediately with fixed format
            InitializeDevice();
        }

        private void InitializeDevice() {
            if (_deviceInitialized) return;
            
            unsafe {
                // Use a fixed, high-quality format for all playback
                AudioSpec desiredSpec = new AudioSpec();
                desiredSpec.Freq = _sampleRate;
                desiredSpec.Format = 0x8010; // SDL_AUDIO_S16LSB (16-bit signed)
                desiredSpec.Channels = (byte)_channelCount;
                desiredSpec.Silence = 0;
                desiredSpec.Samples = 4096;
                desiredSpec.Size = 0;
                desiredSpec.Padding = 0;

                _currentDeviceId = _sdl.OpenAudioDevice((string?)null, 0, &desiredSpec, null, 0);
                if (_currentDeviceId == 0) {
                    var error = _sdl.GetErrorS();
                    _logger?.LogError("Failed to open audio device: {Error}", error);
                    throw new InvalidOperationException($"Failed to open audio device: {error}");
                }
                
                _deviceInitialized = true;
                _logger?.LogDebug("Audio device initialized with format: {SampleRate}Hz, {Channels} channels, {BitsPerSample}bit", 
                    _sampleRate, _channelCount, _bitsPerSample);
            }
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

                // Convert audio data to device format if needed
                pcmData = ConvertAudioToDeviceFormat(pcmData, channels, sampleRate, bitsPerSample, out channels, out sampleRate, out bitsPerSample);

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
                // Clear any queued audio to prevent buildup
                _sdl.ClearQueuedAudio(_currentDeviceId);

                // Queue audio data
                fixed (byte* dataPtr = pcmData) {
                    if (_sdl.QueueAudio(_currentDeviceId, dataPtr, (uint)pcmData.Length) < 0) {
                        var error = _sdl.GetErrorS();
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
                var decoder = new MP3Stream(stream);
                int channels = decoder.ChannelCount;
                int sampleRate = decoder.Frequency;

                List<byte> pcmData = new List<byte>();
                byte[] buffer = new byte[4096];
                int bytesRead;

                while ((bytesRead = decoder.Read(buffer, 0, buffer.Length)) > 0) {
                    pcmData.AddRange(buffer.AsSpan(0, bytesRead));
                }

                byte[] data = pcmData.ToArray();

                if (channels == 1) {
                    // fix bugged cross-platform mp3 decoders for A000393
                    for (int i = 0; i < data.Length; i += 4) {
                        data[i + 2] = data[i];
                        data[i + 3] = data[i + 1];
                    }
                    channels = 2;
                }

                return (data, channels, sampleRate);
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

        private byte[] ConvertAudioToDeviceFormat(byte[] inputData, int inputChannels, int inputSampleRate, int inputBitsPerSample, 
            out int outputChannels, out int outputSampleRate, out int outputBitsPerSample) {
            
            // If already in correct format, return as-is
            if (inputChannels == _channelCount && inputSampleRate == _sampleRate && inputBitsPerSample == _bitsPerSample) {
                outputChannels = inputChannels;
                outputSampleRate = inputSampleRate;
                outputBitsPerSample = inputBitsPerSample;
                return inputData;
            }

            // Convert to device format
            outputChannels = _channelCount;
            outputSampleRate = _sampleRate;
            outputBitsPerSample = _bitsPerSample;

            // First handle bit depth conversion if needed
            if (inputBitsPerSample != _bitsPerSample) {
                inputData = ConvertBitDepth(inputData, inputBitsPerSample, _bitsPerSample);
                inputBitsPerSample = _bitsPerSample;
            }

            // Then handle sample rate conversion
            if (inputSampleRate != _sampleRate) {
                inputData = ResampleAudio(inputData, inputChannels, inputSampleRate, _sampleRate, inputBitsPerSample);
            }

            // Finally handle channel conversion
            if (inputChannels != _channelCount) {
                inputData = ConvertChannels(inputData, inputChannels, _channelCount, inputBitsPerSample);
            }

            return inputData;
        }

        private byte[] ConvertBitDepth(byte[] inputData, int inputBits, int outputBits) {
            if (inputBits == 8 && outputBits == 16) {
                // Convert 8-bit to 16-bit
                byte[] outputData = new byte[inputData.Length * 2];
                for (int i = 0; i < inputData.Length; i++) {
                    // Convert unsigned 8-bit [0,255] to signed 16-bit [-32768,32767]
                    short sample = (short)((inputData[i] - 128) * 256);
                    byte[] bytes = BitConverter.GetBytes(sample);
                    outputData[i * 2] = bytes[0];
                    outputData[i * 2 + 1] = bytes[1];
                }
                return outputData;
            }
            else if (inputBits == 16 && outputBits == 8) {
                // Convert 16-bit to 8-bit
                byte[] outputData = new byte[inputData.Length / 2];
                for (int i = 0; i < outputData.Length; i++) {
                    short sample = BitConverter.ToInt16(inputData, i * 2);
                    // Convert signed 16-bit [-32768,32767] to unsigned 8-bit [0,255]
                    outputData[i] = (byte)((sample / 256) + 128);
                }
                return outputData;
            }
            
            // No conversion needed or not supported
            return inputData;
        }

        private byte[] ResampleAudio(byte[] inputData, int channels, int inputSampleRate, int outputSampleRate, int bitsPerSample) {
            if (inputSampleRate == outputSampleRate) return inputData;

            double ratio = (double)outputSampleRate / inputSampleRate;
            int inputSamplesPerChannel = inputData.Length / (channels * (bitsPerSample / 8));
            int outputSamplesPerChannel = (int)(inputSamplesPerChannel * ratio);
            byte[] outputData = new byte[outputSamplesPerChannel * channels * (bitsPerSample / 8)];

            if (bitsPerSample == 16) {
                for (int ch = 0; ch < channels; ch++) {
                    for (int i = 0; i < outputSamplesPerChannel; i++) {
                        double inputIndex = i / ratio;
                        int index0 = (int)Math.Floor(inputIndex);
                        int index1 = Math.Min(index0 + 1, inputSamplesPerChannel - 1);
                        double fraction = inputIndex - index0;

                        // Linear interpolation
                        short sample0 = BitConverter.ToInt16(inputData, (index0 * channels + ch) * 2);
                        short sample1 = BitConverter.ToInt16(inputData, (index1 * channels + ch) * 2);
                        short interpolated = (short)(sample0 + (sample1 - sample0) * fraction);

                        byte[] bytes = BitConverter.GetBytes(interpolated);
                        outputData[(i * channels + ch) * 2] = bytes[0];
                        outputData[(i * channels + ch) * 2 + 1] = bytes[1];
                    }
                }
            }

            return outputData;
        }

        private byte[] ConvertChannels(byte[] inputData, int inputChannels, int outputChannels, int bitsPerSample) {
            if (inputChannels == outputChannels) return inputData;

            int samplesPerChannel = inputData.Length / (inputChannels * (bitsPerSample / 8));
            byte[] outputData = new byte[samplesPerChannel * outputChannels * (bitsPerSample / 8)];

            if (bitsPerSample == 16) {
                if (inputChannels == 1 && outputChannels == 2) {
                    // Mono to stereo - duplicate samples
                    for (int i = 0; i < samplesPerChannel; i++) {
                        short sample = BitConverter.ToInt16(inputData, i * 2);
                        byte[] bytes = BitConverter.GetBytes(sample);
                        // Left channel
                        outputData[i * 4] = bytes[0];
                        outputData[i * 4 + 1] = bytes[1];
                        // Right channel
                        outputData[i * 4 + 2] = bytes[0];
                        outputData[i * 4 + 3] = bytes[1];
                    }
                }
                else if (inputChannels == 2 && outputChannels == 1) {
                    // Stereo to mono - average channels
                    for (int i = 0; i < samplesPerChannel; i++) {
                        short left = BitConverter.ToInt16(inputData, i * 4);
                        short right = BitConverter.ToInt16(inputData, i * 4 + 2);
                        short mono = (short)((left + right) / 2);
                        byte[] bytes = BitConverter.GetBytes(mono);
                        outputData[i * 2] = bytes[0];
                        outputData[i * 2 + 1] = bytes[1];
                    }
                }
            }

            return outputData;
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
