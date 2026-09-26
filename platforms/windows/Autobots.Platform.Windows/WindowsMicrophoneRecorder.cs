using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Autobots.Platform;

namespace Autobots.Platform.Windows;

/// <summary>
/// Push-to-talk capture from the default Windows microphone through WinMM. Audio is 16 kHz mono PCM,
/// kept in memory, and recorded only until the owner stops, trailing silence is detected or the
/// maximum duration is reached. Windows shows its own microphone-in-use indicator while recording.
/// </summary>
public sealed class WindowsMicrophoneRecorder : IMicrophoneRecorder
{
    private const int SampleRate = 16_000;
    private const int BytesPerSample = 2;
    private const int ChunkMilliseconds = 100;
    private const int ChunkBytes = SampleRate * BytesPerSample * ChunkMilliseconds / 1000;
    private const int BufferCount = 6;
    private const uint WaveMapper = uint.MaxValue;
    private const uint WhdrDone = 0x00000001;
    private const int MmSysErrAllocated = 4;
    private static readonly TimeSpan NoSpeechTimeout = TimeSpan.FromSeconds(8);

    [DllImport("winmm.dll")]
    private static extern uint waveInGetNumDevs();

    [DllImport("winmm.dll")]
    private static extern int waveInOpen(out nint device, uint deviceId, ref WaveFormat format, nint callback, nint instance, uint flags);

    [DllImport("winmm.dll")]
    private static extern int waveInPrepareHeader(nint device, nint header, int size);

    [DllImport("winmm.dll")]
    private static extern int waveInUnprepareHeader(nint device, nint header, int size);

    [DllImport("winmm.dll")]
    private static extern int waveInAddBuffer(nint device, nint header, int size);

    [DllImport("winmm.dll")]
    private static extern int waveInStart(nint device);

    [DllImport("winmm.dll")]
    private static extern int waveInReset(nint device);

    [DllImport("winmm.dll")]
    private static extern int waveInClose(nint device);

    public bool IsAvailable => waveInGetNumDevs() > 0;

    public async ValueTask<RecordedAudio> RecordAsync(
        MicrophoneRecordingOptions options,
        IProgress<double>? level,
        CancellationToken stopToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!IsAvailable)
            throw new PlatformCapabilityUnavailableException("No microphone is connected to this PC.");
        return await Task.Run(() => Record(options, level, stopToken, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private static RecordedAudio Record(MicrophoneRecordingOptions options, IProgress<double>? level, CancellationToken stopToken, CancellationToken cancellationToken)
    {
        var format = new WaveFormat
        {
            FormatTag = 1,
            Channels = 1,
            SamplesPerSecond = SampleRate,
            AverageBytesPerSecond = SampleRate * BytesPerSample,
            BlockAlign = BytesPerSample,
            BitsPerSample = 16
        };
        var opened = waveInOpen(out var device, WaveMapper, ref format, 0, 0, 0);
        if (opened != 0)
        {
            throw new PlatformCapabilityUnavailableException(opened == MmSysErrAllocated
                ? "The microphone is in use by another app."
                : "Autobots could not open the microphone. In Windows Settings > Privacy & security > Microphone, allow desktop apps to use it.");
        }

        var headerSize = Marshal.SizeOf<WaveHeader>();
        var headers = new nint[BufferCount];
        var buffers = new nint[BufferCount];
        var pcm = new MemoryStream(SampleRate * BytesPerSample * 10);
        var chunk = new byte[ChunkBytes];
        var detector = new SpeechDetector();
        var clock = Stopwatch.StartNew();
        try
        {
            for (var index = 0; index < BufferCount; index++)
            {
                buffers[index] = Marshal.AllocHGlobal(ChunkBytes);
                headers[index] = Marshal.AllocHGlobal(headerSize);
                Marshal.StructureToPtr(new WaveHeader { Data = buffers[index], BufferLength = ChunkBytes }, headers[index], false);
                ThrowIfFailed(waveInPrepareHeader(device, headers[index], headerSize));
                ThrowIfFailed(waveInAddBuffer(device, headers[index], headerSize));
            }
            ThrowIfFailed(waveInStart(device));

            var next = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var drained = false;
                // WinMM returns buffers in queue order; drain them in that order to keep audio sequential.
                while (true)
                {
                    var header = Marshal.PtrToStructure<WaveHeader>(headers[next]);
                    if ((header.Flags & WhdrDone) == 0)
                        break;
                    var recorded = (int)Math.Min(header.BytesRecorded, (uint)ChunkBytes);
                    Marshal.Copy(buffers[next], chunk, 0, recorded);
                    pcm.Write(chunk, 0, recorded);
                    var rms = detector.Add(chunk.AsSpan(0, recorded));
                    level?.Report(Math.Clamp(rms / 6000d, 0, 1));
                    header.Flags &= ~WhdrDone;
                    header.BytesRecorded = 0;
                    Marshal.StructureToPtr(header, headers[next], false);
                    ThrowIfFailed(waveInAddBuffer(device, headers[next], headerSize));
                    next = (next + 1) % BufferCount;
                    drained = true;
                }

                if (stopToken.IsCancellationRequested || clock.Elapsed >= options.MaxDuration)
                    break;
                if (options.StopOnSilence && detector.SpeechDetected && detector.TrailingSilence >= options.TrailingSilence)
                    break;
                if (options.StopOnSilence && !detector.SpeechDetected && clock.Elapsed >= NoSpeechTimeout)
                    break;
                if (!drained)
                    Thread.Sleep(15);
            }
        }
        finally
        {
            _ = waveInReset(device);
            for (var index = 0; index < BufferCount; index++)
            {
                if (headers[index] != 0)
                {
                    _ = waveInUnprepareHeader(device, headers[index], headerSize);
                    Marshal.FreeHGlobal(headers[index]);
                }
                if (buffers[index] != 0)
                    Marshal.FreeHGlobal(buffers[index]);
            }
            _ = waveInClose(device);
            level?.Report(0);
        }

        var samples = pcm.ToArray();
        return new RecordedAudio(
            WavBytes: ToWave(samples),
            Duration: TimeSpan.FromSeconds(samples.Length / (double)(SampleRate * BytesPerSample)),
            SpeechDetected: detector.SpeechDetected);
    }

    private static byte[] ToWave(byte[] samples)
    {
        var wave = new byte[44 + samples.Length];
        var span = wave.AsSpan();
        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)(36 + samples.Length));
        "WAVEfmt "u8.CopyTo(span[8..]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteUInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(span[22..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(span[24..], SampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(span[28..], SampleRate * BytesPerSample);
        BinaryPrimitives.WriteUInt16LittleEndian(span[32..], BytesPerSample);
        BinaryPrimitives.WriteUInt16LittleEndian(span[34..], 16);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[40..], (uint)samples.Length);
        samples.CopyTo(span[44..]);
        return wave;
    }

    private static void ThrowIfFailed(int result)
    {
        if (result != 0)
            throw new PlatformCapabilityUnavailableException($"The microphone stopped unexpectedly (WinMM error {result}).");
    }

    /// <summary>Simple energy-based speech detector with an adaptive noise floor.</summary>
    internal sealed class SpeechDetector
    {
        private double _noiseFloor = 250;
        private int _speechChunks;

        public bool SpeechDetected { get; private set; }
        public TimeSpan TrailingSilence { get; private set; }

        public double Add(ReadOnlySpan<byte> pcm)
        {
            var count = pcm.Length / 2;
            if (count == 0)
                return 0;
            double sum = 0;
            for (var index = 0; index < count; index++)
            {
                double sample = BinaryPrimitives.ReadInt16LittleEndian(pcm[(index * 2)..]);
                sum += sample * sample;
            }
            var rms = Math.Sqrt(sum / count);
            var threshold = Math.Max(550, _noiseFloor * 2.8);
            var duration = TimeSpan.FromMilliseconds(count * 1000d / SampleRate);
            if (rms > threshold)
            {
                _speechChunks++;
                if (_speechChunks >= 2)
                    SpeechDetected = true;
                TrailingSilence = TimeSpan.Zero;
            }
            else
            {
                _speechChunks = 0;
                _noiseFloor = (_noiseFloor * 0.9) + (rms * 0.1);
                if (SpeechDetected)
                    TrailingSilence += duration;
            }
            return rms;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSecond;
        public uint AverageBytesPerSecond;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public nint Data;
        public uint BufferLength;
        public uint BytesRecorded;
        public nint User;
        public uint Flags;
        public uint Loops;
        public nint Next;
        public nint Reserved;
    }
}
