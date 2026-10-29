using System;
using System.IO;
using System.Text;

namespace WebcamLab.Scratch;

public static class Mp4BoxInspector
{
    public static void Inspect(string filePath)
    {
        Console.WriteLine($"=== Inspecting MP4 Boxes for {Path.GetFileName(filePath)} ===");
        using var fs = File.OpenRead(filePath);
        using var br = new BinaryReader(fs);

        ReadBoxes(br, 0, fs.Length, 0);
    }

    private static void ReadBoxes(BinaryReader br, long startOffset, long endOffset, int depth)
    {
        br.BaseStream.Position = startOffset;
        string indent = new string(' ', depth * 2);

        while (br.BaseStream.Position < endOffset)
        {
            long currentPos = br.BaseStream.Position;
            if (currentPos + 8 > endOffset) break;

            byte[] sizeBytes = br.ReadBytes(4);
            if (BitConverter.IsLittleEndian) Array.Reverse(sizeBytes);
            uint size32 = BitConverter.ToUInt32(sizeBytes, 0);

            byte[] typeBytes = br.ReadBytes(4);
            string type = Encoding.ASCII.GetString(typeBytes);

            long boxSize = size32;
            int headerSize = 8;
            if (size32 == 1) // 64-bit size
            {
                byte[] largeSizeBytes = br.ReadBytes(8);
                if (BitConverter.IsLittleEndian) Array.Reverse(largeSizeBytes);
                boxSize = BitConverter.ToInt64(largeSizeBytes, 0);
                headerSize = 16;
            }
            else if (size32 == 0) // Extends to end of file
            {
                boxSize = endOffset - currentPos;
            }

            Console.WriteLine($"{indent}Box '{type}' at 0x{currentPos:X6}, Size={boxSize}");

            if (type is "moov" or "trak" or "mdia" or "minf" or "stbl")
            {
                // Container box: descend
                ReadBoxes(br, currentPos + headerSize, currentPos + boxSize, depth + 1);
            }
            else if (type == "stts")
            {
                // Parse stts
                byte version = br.ReadByte();
                byte[] flags = br.ReadBytes(3);
                byte[] countBytes = br.ReadBytes(4);
                if (BitConverter.IsLittleEndian) Array.Reverse(countBytes);
                uint entryCount = BitConverter.ToUInt32(countBytes, 0);
                Console.WriteLine($"{indent}  [STTS] Version={version}, EntryCount={entryCount}");
                for (int i = 0; i < Math.Min(entryCount, 10); i++)
                {
                    byte[] sCountBytes = br.ReadBytes(4);
                    byte[] sDeltaBytes = br.ReadBytes(4);
                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(sCountBytes);
                        Array.Reverse(sDeltaBytes);
                    }
                    uint sCount = BitConverter.ToUInt32(sCountBytes, 0);
                    uint sDelta = BitConverter.ToUInt32(sDeltaBytes, 0);
                    Console.WriteLine($"{indent}    Entry[{i}]: SampleCount={sCount}, SampleDelta={sDelta}");
                }
            }
            else if (type == "mdhd")
            {
                byte version = br.ReadByte();
                byte[] flags = br.ReadBytes(3);
                uint timescale;
                ulong duration;
                if (version == 1)
                {
                    br.ReadBytes(16); // creation + mod
                    byte[] tsBytes = br.ReadBytes(4);
                    if (BitConverter.IsLittleEndian) Array.Reverse(tsBytes);
                    timescale = BitConverter.ToUInt32(tsBytes, 0);
                    byte[] durBytes = br.ReadBytes(8);
                    if (BitConverter.IsLittleEndian) Array.Reverse(durBytes);
                    duration = BitConverter.ToUInt64(durBytes, 0);
                }
                else
                {
                    br.ReadBytes(8); // creation + mod
                    byte[] tsBytes = br.ReadBytes(4);
                    if (BitConverter.IsLittleEndian) Array.Reverse(tsBytes);
                    timescale = BitConverter.ToUInt32(tsBytes, 0);
                    byte[] durBytes = br.ReadBytes(4);
                    if (BitConverter.IsLittleEndian) Array.Reverse(durBytes);
                    duration = BitConverter.ToUInt32(durBytes, 0);
                }
                Console.WriteLine($"{indent}  [MDHD] Timescale={timescale}, Duration={duration} ({duration / (double)timescale:F3}s)");
            }

            br.BaseStream.Position = currentPos + boxSize;
        }
    }
}
