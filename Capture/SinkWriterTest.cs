using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using WebcamLab.Capture;
using WebcamLab.Core.Native;

namespace WebcamLab.Scratch;

public class LoggingMediaType : MediaFoundationNative.IMFMediaType
{
    private readonly MediaFoundationNative.IMFMediaType _inner;
    public LoggingMediaType(MediaFoundationNative.IMFMediaType inner) => _inner = inner;

    public int GetItem(ref Guid guidKey, IntPtr pValue) { Console.WriteLine($"[QUERY] GetItem: {guidKey}"); return _inner.GetItem(ref guidKey, pValue); }
    public int GetItemType(ref Guid guidKey, out int pType) { Console.WriteLine($"[QUERY] GetItemType: {guidKey}"); return _inner.GetItemType(ref guidKey, out pType); }
    public int CompareItem(ref Guid guidKey, IntPtr Value, out bool pbResult) => _inner.CompareItem(ref guidKey, Value, out pbResult);
    public int Compare(MediaFoundationNative.IMFAttributes pTheirs, int MatchType, out bool pbResult) => _inner.Compare(pTheirs, MatchType, out pbResult);
    public int GetUINT32(ref Guid guidKey, out uint punValue)
    {
        int hr = _inner.GetUINT32(ref guidKey, out punValue);
        Console.WriteLine($"[QUERY] GetUINT32: {guidKey} -> val={punValue}, hr=0x{hr:X8}");
        return hr;
    }
    public int GetUINT64(ref Guid guidKey, out ulong punValue)
    {
        int hr = _inner.GetUINT64(ref guidKey, out punValue);
        Console.WriteLine($"[QUERY] GetUINT64: {guidKey} -> val={punValue:X16}, hr=0x{hr:X8}");
        return hr;
    }
    public int GetDouble(ref Guid guidKey, out double pfValue)
    {
        int hr = _inner.GetDouble(ref guidKey, out pfValue);
        Console.WriteLine($"[QUERY] GetDouble: {guidKey} -> val={pfValue}, hr=0x{hr:X8}");
        return hr;
    }
    public int GetGUID(ref Guid guidKey, out Guid pguidValue)
    {
        int hr = _inner.GetGUID(ref guidKey, out pguidValue);
        Console.WriteLine($"[QUERY] GetGUID: {guidKey} -> val={pguidValue}, hr=0x{hr:X8}");
        return hr;
    }
    public int GetStringLength(ref Guid guidKey, out uint pcchLength) { Console.WriteLine($"[QUERY] GetStringLength: {guidKey}"); return _inner.GetStringLength(ref guidKey, out pcchLength); }
    public int GetString(ref Guid guidKey, StringBuilder pwszValue, uint cchBufSize, ref uint pcchLength) => _inner.GetString(ref guidKey, pwszValue, cchBufSize, ref pcchLength);
    public int GetAllocatedString(ref Guid guidKey, out string ppwszValue, ref uint pcchLength) { Console.WriteLine($"[QUERY] GetAllocatedString: {guidKey}"); return _inner.GetAllocatedString(ref guidKey, out ppwszValue, ref pcchLength); }
    public int GetBlobSize(ref Guid guidKey, out uint pcbBlobSize)
    {
        int hr = _inner.GetBlobSize(ref guidKey, out pcbBlobSize);
        Console.WriteLine($"[QUERY] GetBlobSize: {guidKey} -> hr=0x{hr:X8}");
        return hr;
    }
    public int GetBlob(ref Guid guidKey, byte[] pBuf, uint cbBufSize, out uint pcbBlobSize)
    {
        int hr = _inner.GetBlob(ref guidKey, pBuf, cbBufSize, out pcbBlobSize);
        Console.WriteLine($"[QUERY] GetBlob: {guidKey} -> hr=0x{hr:X8}");
        return hr;
    }
    public int GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize)
    {
        int hr = _inner.GetAllocatedBlob(ref guidKey, out ppBuf, out pcbSize);
        Console.WriteLine($"[QUERY] GetAllocatedBlob: {guidKey} -> hr=0x{hr:X8}");
        return hr;
    }
    public int GetUnknown(ref Guid guidKey, ref Guid riid, out object ppv)
    {
        int hr = _inner.GetUnknown(ref guidKey, ref riid, out ppv);
        Console.WriteLine($"[QUERY] GetUnknown: {guidKey} -> hr=0x{hr:X8}");
        return hr;
    }
    public int SetItem(ref Guid guidKey, IntPtr Value) => _inner.SetItem(ref guidKey, Value);
    public int DeleteItem(ref Guid guidKey) => _inner.DeleteItem(ref guidKey);
    public int DeleteAllItems() => _inner.DeleteAllItems();
    public int SetUINT32(ref Guid guidKey, uint unValue) => _inner.SetUINT32(ref guidKey, unValue);
    public int SetUINT64(ref Guid guidKey, ulong unValue) => _inner.SetUINT64(ref guidKey, unValue);
    public int SetDouble(ref Guid guidKey, double fValue) => _inner.SetDouble(ref guidKey, fValue);
    public int SetGUID(ref Guid guidKey, ref Guid guidValue) => _inner.SetGUID(ref guidKey, ref guidValue);
    public int SetString(ref Guid guidKey, string wszValue) => _inner.SetString(ref guidKey, wszValue);
    public int SetBlob(ref Guid guidKey, byte[] pBuf, uint cbBufSize) => _inner.SetBlob(ref guidKey, pBuf, cbBufSize);
    public int SetUnknown(ref Guid guidKey, object pUnknown) => _inner.SetUnknown(ref guidKey, pUnknown);
    public int LockStore() => _inner.LockStore();
    public int UnlockStore() => _inner.UnlockStore();
    public int GetCount(out uint pcItems) => _inner.GetCount(out pcItems);
    public int GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue) => _inner.GetItemByIndex(unIndex, out pguidKey, pValue);
    public int CopyAllItems(MediaFoundationNative.IMFAttributes pDest) => _inner.CopyAllItems(pDest);

    public int GetMajorType(out Guid pguidMajorType) { Console.WriteLine("[QUERY] GetMajorType"); return _inner.GetMajorType(out pguidMajorType); }
    public int IsFullBoundsFormat(out bool pfFullBounds) => _inner.IsFullBoundsFormat(out pfFullBounds);
    public int IsCompressedFormat(out bool pfCompressed) => _inner.IsCompressedFormat(out pfCompressed);
    public int IsEqual(MediaFoundationNative.IMFMediaType pIMediaType, out uint pdwFlags) => _inner.IsEqual(pIMediaType, out pdwFlags);
    public int GetRepresentation(ref Guid guidRepresentation, out IntPtr ppvRepresentation) => _inner.GetRepresentation(ref guidRepresentation, out ppvRepresentation);
    public int FreeRepresentation(ref Guid guidRepresentation, IntPtr pvRepresentation) => _inner.FreeRepresentation(ref guidRepresentation, pvRepresentation);
}

public static class SinkWriterTest
{
    public static void Test()
    {
        string sampleFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "WebcamLab", "Phase13_1", "WebcamLab_Phase13_1_MJPEG_VFR_20261025_213010.mp4");
        string testedOutput = Path.Combine(AppContext.BaseDirectory, "injected_test.mp4");

        // Simulate 52 realistic camera sample timestamps: 0, 64ms, 128ms, 192ms, 272ms, 336ms, ...
        var sampleTimes = new List<double>();
        double t = 0.0;
        sampleTimes.Add(t);
        // Using the exact numbers from the Phase 13.1 report for the first 6 frames:
        // 0.0000, 0.0640, 0.1282, 0.1924, 0.2720, 0.3361...
        double[] knownDeltas = [0.0640185, 0.0642004, 0.0641376, 0.0795963, 0.0641632];
        for (int i = 0; i < 51; i++)
        {
            double d = (i < knownDeltas.Length) ? knownDeltas[i] : 0.0666667;
            t += d;
            sampleTimes.Add(t);
        }

        Console.WriteLine($"Injecting into {sampleFile} -> {testedOutput}");
        if (File.Exists(sampleFile))
        {
            File.Copy(sampleFile, testedOutput, true);
            bool ok = Mp4TimestampInjector.InjectTrueSourceTimestamps(testedOutput, sampleTimes);
            Console.WriteLine($"Injection success: {ok}");

            if (ok)
            {
                Mp4BoxInspector.Inspect(testedOutput);
            }
        }
    }
}
