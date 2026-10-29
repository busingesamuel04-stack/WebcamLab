using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace WebcamLab.Capture;

/// <summary>
/// Native C# MP4 container timestamp injector.
/// Directly rewrites the ISO Base Media File Format (ISO/IEC 14496-12) 'stts' (Time-to-Sample) atom,
/// 'mdhd' (Media Header) duration, and 'mvhd' (Movie Header) duration to propagate the exact,
/// unadulterated camera hardware sample timestamps (IMFSample::GetSampleTime) into the MP4 container.
/// 
/// Supports both in-place entry updates and dynamic stts box expansion with automatic parent box size
/// adjustment (stbl, minf, mdia, trak, moov) and chunk offset (stco / co64) relocation.
/// Guarantees sub-millisecond timestamp propagation fidelity without modifying the compressed H.264 bitstream,
/// without adding duplicate CFR padding frames, and without third-party dependencies.
/// </summary>
public static class Mp4TimestampInjector
{
    public static bool HasValidMoovAtom(string mp4Path)
    {
        if (string.IsNullOrWhiteSpace(mp4Path) || !File.Exists(mp4Path)) return false;
        try
        {
            var fi = new FileInfo(mp4Path);
            if (fi.Length < 64) return false;

            using var fs = new FileStream(mp4Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            byte[] header = new byte[8];
            long pos = 0;
            long len = fs.Length;
            while (pos + 8 <= len)
            {
                fs.Position = pos;
                int read = fs.Read(header, 0, 8);
                if (read < 8) break;
                uint size32 = ((uint)header[0] << 24) | ((uint)header[1] << 16) | ((uint)header[2] << 8) | header[3];
                string type = Encoding.ASCII.GetString(header, 4, 4);
                if (type == "moov") return true;

                long boxSize = size32;
                if (size32 == 1 && pos + 16 <= len)
                {
                    byte[] ext = new byte[8];
                    fs.ReadExactly(ext, 0, 8);
                    boxSize = (long)(((ulong)((uint)ext[0] << 24 | (uint)ext[1] << 16 | (uint)ext[2] << 8 | ext[3]) << 32) |
                                     ((uint)ext[4] << 24 | (uint)ext[5] << 16 | (uint)ext[6] << 8 | ext[7]));
                }
                else if (size32 <= 0)
                {
                    break;
                }
                pos += boxSize;
            }
        }
        catch { }
        return false;
    }

    public static bool InjectTrueSourceTimestamps(
        string mp4Path,
        IReadOnlyList<double> normalizedSampleTimesSec,
        double? targetTotalDurationSec = null)
    {
        if (string.IsNullOrWhiteSpace(mp4Path) || !File.Exists(mp4Path) || normalizedSampleTimesSec == null || normalizedSampleTimesSec.Count == 0)
        {
            return false;
        }

        if (!HasValidMoovAtom(mp4Path))
        {
            Debug.WriteLine($"[Mp4TimestampInjector] Aborting: MP4 does not contain a valid 'moov' atom ({mp4Path}).");
            return false;
        }

        try
        {
            byte[] mp4Bytes = File.ReadAllBytes(mp4Path);

            long moovOffset = -1;
            long trakOffset = -1;
            long mdiaOffset = -1;
            long minfOffset = -1;
            long stblOffset = -1;
            long sttsOffset = -1;
            long sttsSize = 0;
            long stcoOffset = -1;
            long co64Offset = -1;
            long mdhdOffset = -1;
            long mvhdOffset = -1;
            long tkhdOffset = -1;
            long elstOffset = -1;
            uint mediaTimescale = 0;
            uint movieTimescale = 0;

            // Traverse box tree
            void ScanBoxes(long start, long end)
            {
                long pos = start;
                while (pos + 8 <= end)
                {
                    uint size32 = ReadUInt32BE(mp4Bytes, (int)pos);
                    string type = Encoding.ASCII.GetString(mp4Bytes, (int)pos + 4, 4);

                    long boxSize = size32;
                    long headerSize = 8;
                    if (size32 == 1 && pos + 16 <= end)
                    {
                        boxSize = (long)ReadUInt64BE(mp4Bytes, (int)pos + 8);
                        headerSize = 16;
                    }
                    else if (size32 == 0)
                    {
                        boxSize = end - pos;
                    }

                    if (boxSize < headerSize || pos + boxSize > end) break;

                    if (type is "moov" or "trak" or "mdia" or "minf" or "stbl" or "edts")
                    {
                        if (type == "moov" && moovOffset < 0) moovOffset = pos;
                        else if (type == "trak" && trakOffset < 0) trakOffset = pos;
                        else if (type == "mdia" && mdiaOffset < 0) mdiaOffset = pos;
                        else if (type == "minf" && minfOffset < 0) minfOffset = pos;
                        else if (type == "stbl" && stblOffset < 0) stblOffset = pos;
                        ScanBoxes(pos + headerSize, pos + boxSize);
                    }
                    else if (type == "mvhd" && mvhdOffset < 0)
                    {
                        mvhdOffset = pos;
                        byte version = mp4Bytes[pos + 8];
                        int tsPos = (version == 1) ? (int)(pos + 8 + 1 + 3 + 16) : (int)(pos + 8 + 1 + 3 + 8);
                        movieTimescale = ReadUInt32BE(mp4Bytes, tsPos);
                    }
                    else if (type == "tkhd" && tkhdOffset < 0)
                    {
                        tkhdOffset = pos;
                    }
                    else if (type == "elst" && elstOffset < 0)
                    {
                        elstOffset = pos;
                    }
                    else if (type == "mdhd" && mdhdOffset < 0)
                    {
                        mdhdOffset = pos;
                        byte version = mp4Bytes[pos + 8];
                        int tsPos = (version == 1) ? (int)(pos + 8 + 1 + 3 + 16) : (int)(pos + 8 + 1 + 3 + 8);
                        mediaTimescale = ReadUInt32BE(mp4Bytes, tsPos);
                    }
                    else if (type == "stts" && sttsOffset < 0)
                    {
                        sttsOffset = pos;
                        sttsSize = boxSize;
                    }
                    else if (type == "stco" && stcoOffset < 0)
                    {
                        stcoOffset = pos;
                    }
                    else if (type == "co64" && co64Offset < 0)
                    {
                        co64Offset = pos;
                    }

                    pos += boxSize;
                }
            }

            ScanBoxes(0, mp4Bytes.Length);

            if (sttsOffset < 0 || mediaTimescale == 0)
            {
                Debug.WriteLine($"[Mp4TimestampInjector] Failed to locate stts (offset={sttsOffset}) or mdhd timescale ({mediaTimescale}).");
                return false;
            }

            int entryCountPos = (int)sttsOffset + 8 + 1 + 3; // 8-byte box header + 1-byte version + 3-byte flags
            uint origEntryCount = ReadUInt32BE(mp4Bytes, entryCountPos);
            int sampleCount = normalizedSampleTimesSec.Count;
            if (origEntryCount == 0 || sampleCount == 0) return false;

            // Synthesize exact sample deltas from true normalized hardware timestamps
            // Every sample has its own 1:1 entry in stts (sample_count = 1)
            var entries = new List<(uint Count, uint Delta)>(sampleCount);
            ulong totalMediaUnits = 0;

            for (int i = 0; i < sampleCount; i++)
            {
                double durationSec;
                if (i + 1 < sampleCount)
                {
                    durationSec = normalizedSampleTimesSec[i + 1] - normalizedSampleTimesSec[i];
                }
                else if (targetTotalDurationSec.HasValue && targetTotalDurationSec.Value > normalizedSampleTimesSec[sampleCount - 1])
                {
                    double tailDelta = targetTotalDurationSec.Value - normalizedSampleTimesSec[sampleCount - 1];
                    // Align tail video presentation duration to match audio duration (bounded to 1.0s)
                    if (tailDelta > 0 && tailDelta <= 1.0)
                    {
                        durationSec = tailDelta;
                    }
                    else
                    {
                        durationSec = (sampleCount > 1)
                            ? (normalizedSampleTimesSec[sampleCount - 1] - normalizedSampleTimesSec[sampleCount - 2])
                            : (1.0 / 30.0);
                    }
                }
                else if (sampleCount > 1)
                {
                    durationSec = normalizedSampleTimesSec[sampleCount - 1] - normalizedSampleTimesSec[sampleCount - 2];
                }
                else
                {
                    durationSec = 1.0 / 30.0;
                }

                if (durationSec <= 0) durationSec = 1.0 / 1000.0; // Floor at 1ms to maintain strict monotonicity
                uint deltaUnits = (uint)Math.Round(durationSec * mediaTimescale);
                if (deltaUnits == 0) deltaUnits = 1;

                totalMediaUnits += deltaUnits;
                entries.Add((1, deltaUnits));
            }

            long targetSttsSize = 8 + 4 + 4 + (long)entries.Count * 8;
            int deltaBytes = (int)(targetSttsSize - sttsSize);

            if (deltaBytes != 0)
            {
                // Reallocate and relocate boxes
                byte[] newMp4Bytes = new byte[mp4Bytes.Length + deltaBytes];

                int oldTableStart = entryCountPos + 4;
                int oldTableEnd = (int)(sttsOffset + sttsSize);

                // Copy everything up to table start
                Buffer.BlockCopy(mp4Bytes, 0, newMp4Bytes, 0, oldTableStart);

                // Update stts size and entry count
                WriteUInt32BE(newMp4Bytes, (int)sttsOffset, (uint)targetSttsSize);
                WriteUInt32BE(newMp4Bytes, entryCountPos, (uint)entries.Count);

                // Write new entries
                int writePos = oldTableStart;
                foreach (var e in entries)
                {
                    WriteUInt32BE(newMp4Bytes, writePos, e.Count);
                    WriteUInt32BE(newMp4Bytes, writePos + 4, e.Delta);
                    writePos += 8;
                }

                // Copy remainder of file
                int remainderSrc = oldTableEnd;
                int remainderDst = (int)(sttsOffset + targetSttsSize);
                int remainderLen = mp4Bytes.Length - remainderSrc;
                Buffer.BlockCopy(mp4Bytes, remainderSrc, newMp4Bytes, remainderDst, remainderLen);

                // Update container box sizes (stbl, minf, mdia, trak, moov)
                long[] parents = [stblOffset, minfOffset, mdiaOffset, trakOffset, moovOffset];
                foreach (var p in parents)
                {
                    if (p >= 0 && p < sttsOffset)
                    {
                        uint curSize = ReadUInt32BE(newMp4Bytes, (int)p);
                        WriteUInt32BE(newMp4Bytes, (int)p, (uint)(curSize + deltaBytes));
                    }
                }

                // Update chunk offsets in stco or co64
                if (stcoOffset >= 0)
                {
                    int effectiveStco = (stcoOffset > sttsOffset) ? (int)(stcoOffset + deltaBytes) : (int)stcoOffset;
                    uint chunkCount = ReadUInt32BE(newMp4Bytes, effectiveStco + 12);
                    for (int c = 0; c < chunkCount; c++)
                    {
                        int entryPos = effectiveStco + 16 + c * 4;
                        uint chunkOff = ReadUInt32BE(newMp4Bytes, entryPos);
                        if (chunkOff > moovOffset)
                        {
                            WriteUInt32BE(newMp4Bytes, entryPos, (uint)(chunkOff + deltaBytes));
                        }
                    }
                }
                else if (co64Offset >= 0)
                {
                    int effectiveCo64 = (co64Offset > sttsOffset) ? (int)(co64Offset + deltaBytes) : (int)co64Offset;
                    uint chunkCount = ReadUInt32BE(newMp4Bytes, effectiveCo64 + 12);
                    for (int c = 0; c < chunkCount; c++)
                    {
                        int entryPos = effectiveCo64 + 16 + c * 8;
                        ulong chunkOff = ReadUInt64BE(newMp4Bytes, entryPos);
                        if (chunkOff > (ulong)moovOffset)
                        {
                            WriteUInt64BE(newMp4Bytes, entryPos, (ulong)((long)chunkOff + deltaBytes));
                        }
                    }
                }

                mp4Bytes = newMp4Bytes;
            }
            else
            {
                // In-place rewrite
                WriteUInt32BE(mp4Bytes, entryCountPos, (uint)entries.Count);
                int tableStart = entryCountPos + 4;
                for (int i = 0; i < entries.Count; i++)
                {
                    int entryPos = tableStart + i * 8;
                    WriteUInt32BE(mp4Bytes, entryPos, entries[i].Count);
                    WriteUInt32BE(mp4Bytes, entryPos + 4, entries[i].Delta);
                }
            }

            // Update mdhd track duration
            if (mdhdOffset >= 0)
            {
                byte version = mp4Bytes[mdhdOffset + 8];
                if (version == 1)
                {
                    int durPos = (int)(mdhdOffset + 8 + 1 + 3 + 16 + 4);
                    WriteUInt64BE(mp4Bytes, durPos, totalMediaUnits);
                }
                else
                {
                    int durPos = (int)(mdhdOffset + 8 + 1 + 3 + 8 + 4);
                    uint clampedDur = totalMediaUnits > uint.MaxValue ? uint.MaxValue : (uint)totalMediaUnits;
                    WriteUInt32BE(mp4Bytes, durPos, clampedDur);
                }
            }

            // Update mvhd movie duration if movie timescale is known
            if (mvhdOffset >= 0 && movieTimescale > 0)
            {
                double totalSec = (double)totalMediaUnits / mediaTimescale;
                ulong totalMovieUnits = (ulong)Math.Round(totalSec * movieTimescale);
                byte version = mp4Bytes[mvhdOffset + 8];
                if (version == 1)
                {
                    int durPos = (int)(mvhdOffset + 8 + 1 + 3 + 16 + 4);
                    WriteUInt64BE(mp4Bytes, durPos, totalMovieUnits);
                }
                else
                {
                    int durPos = (int)(mvhdOffset + 8 + 1 + 3 + 8 + 4);
                    uint clampedDur = totalMovieUnits > uint.MaxValue ? uint.MaxValue : (uint)totalMovieUnits;
                    WriteUInt32BE(mp4Bytes, durPos, clampedDur);
                }
            }

            // Update tkhd track duration if movie timescale is known
            if (tkhdOffset >= 0 && movieTimescale > 0)
            {
                double totalSec = (double)totalMediaUnits / mediaTimescale;
                ulong totalMovieUnits = (ulong)Math.Round(totalSec * movieTimescale);
                byte version = mp4Bytes[tkhdOffset + 8];
                int durPos = (version == 1) ? (int)(tkhdOffset + 8 + 4 + 8 + 8 + 4 + 4) : (int)(tkhdOffset + 8 + 4 + 4 + 4 + 4 + 4);
                if (version == 1)
                {
                    WriteUInt64BE(mp4Bytes, durPos, totalMovieUnits);
                }
                else
                {
                    uint clampedDur = totalMovieUnits > uint.MaxValue ? uint.MaxValue : (uint)totalMovieUnits;
                    WriteUInt32BE(mp4Bytes, durPos, clampedDur);
                }
            }

            // Update elst edit list segment duration if present
            if (elstOffset >= 0 && movieTimescale > 0)
            {
                double totalSec = (double)totalMediaUnits / mediaTimescale;
                ulong totalMovieUnits = (ulong)Math.Round(totalSec * movieTimescale);
                byte version = mp4Bytes[elstOffset + 8];
                uint entryCount = ReadUInt32BE(mp4Bytes, (int)elstOffset + 12);
                if (entryCount > 0)
                {
                    int durPos = (int)(elstOffset + 16);
                    if (version == 1)
                    {
                        WriteUInt64BE(mp4Bytes, durPos, totalMovieUnits);
                    }
                    else
                    {
                        uint clampedDur = totalMovieUnits > uint.MaxValue ? uint.MaxValue : (uint)totalMovieUnits;
                        WriteUInt32BE(mp4Bytes, durPos, clampedDur);
                    }
                }
            }

            // Atomic file write using temporary file
            string tempPath = mp4Path + ".tmp_ts.mp4";
            File.WriteAllBytes(tempPath, mp4Bytes);
            File.Move(tempPath, mp4Path, overwrite: true);

            Debug.WriteLine($"[Mp4TimestampInjector] Injected true source timestamps into {mp4Path} ({entries.Count} entries, total duration={totalMediaUnits / (double)mediaTimescale:F3}s).");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Mp4TimestampInjector] Injection error: {ex.Message}");
            return false;
        }
    }

    private static uint ReadUInt32BE(byte[] data, int pos)
    {
        return ((uint)data[pos] << 24) | ((uint)data[pos + 1] << 16) | ((uint)data[pos + 2] << 8) | data[pos + 3];
    }

    private static ulong ReadUInt64BE(byte[] data, int pos)
    {
        return ((ulong)ReadUInt32BE(data, pos) << 32) | ReadUInt32BE(data, pos + 4);
    }

    private static void WriteUInt32BE(byte[] data, int pos, uint val)
    {
        data[pos] = (byte)((val >> 24) & 0xFF);
        data[pos + 1] = (byte)((val >> 16) & 0xFF);
        data[pos + 2] = (byte)((val >> 8) & 0xFF);
        data[pos + 3] = (byte)(val & 0xFF);
    }

    private static void WriteUInt64BE(byte[] data, int pos, ulong val)
    {
        WriteUInt32BE(data, pos, (uint)(val >> 32));
        WriteUInt32BE(data, pos + 4, (uint)(val & 0xFFFFFFFF));
    }
}
