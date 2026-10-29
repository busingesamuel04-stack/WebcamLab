using System;
using System.IO;
using System.Text;

namespace WebcamLab.Capture.Audio;

/// <summary>
/// Stream-based WAV (RIFF/WAVE) file writer for standard 16-bit uncompressed PCM audio.
/// Automatically updates the RIFF header chunk sizes upon disposal.
/// </summary>
public class WaveFileWriter : IDisposable
{
    private readonly FileStream _fileStream;
    private readonly BinaryWriter _writer;
    private readonly int _channels;
    private readonly int _sampleRate;
    private readonly int _bitsPerSample;
    private long _dataChunkPos = -1;
    private long _totalDataBytes = 0;
    private bool _isDisposed;

    public WaveFileWriter(string filePath, int channels, int sampleRate, int bitsPerSample = 16)
    {
        _channels = channels;
        _sampleRate = sampleRate;
        _bitsPerSample = bitsPerSample;

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        _writer = new BinaryWriter(_fileStream);

        WriteHeaderPlaceholder();
    }

    private void WriteHeaderPlaceholder()
    {
        // 1. RIFF chunk descriptor
        _writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        _writer.Write(0); // placeholder for RIFF size
        _writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        // 2. "fmt " sub-chunk
        _writer.Write(Encoding.ASCII.GetBytes("fmt "));
        _writer.Write(16); // subchunk1 size (16 for standard PCM)
        _writer.Write((short)1); // AudioFormat = 1 (PCM)
        _writer.Write((short)_channels);
        _writer.Write(_sampleRate);
        int blockAlign = _channels * (_bitsPerSample / 8);
        int byteRate = _sampleRate * blockAlign;
        _writer.Write(byteRate);
        _writer.Write((short)blockAlign);
        _writer.Write((short)_bitsPerSample);

        // 3. "data" sub-chunk
        _writer.Write(Encoding.ASCII.GetBytes("data"));
        _dataChunkPos = _fileStream.Position;
        _writer.Write(0); // placeholder for data size
    }

    public void WriteSamples(byte[] pcmData, int offset, int count)
    {
        if (_isDisposed || count <= 0) return;
        _writer.Write(pcmData, offset, count);
        _totalDataBytes += count;
    }

    public long TotalBytesWritten => _totalDataBytes;

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            _writer.Flush();

            // Patch RIFF chunk size: 36 + data size
            _fileStream.Seek(4, SeekOrigin.Begin);
            _writer.Write((int)(36 + _totalDataBytes));

            // Patch data chunk size
            if (_dataChunkPos >= 0)
            {
                _fileStream.Seek(_dataChunkPos, SeekOrigin.Begin);
                _writer.Write((int)_totalDataBytes);
            }

            _writer.Flush();
        }
        catch
        {
            // Ignore on close
        }
        finally
        {
            _writer.Dispose();
            _fileStream.Dispose();
        }
    }
}
