using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WebcamLab.Core.Native;

public static class MediaFoundationNative
{
    public const uint MF_SDK_VERSION = 0x0002;
    public const uint MF_API_VERSION = 0x0070;
    public const uint MF_VERSION = (MF_SDK_VERSION << 16) | MF_API_VERSION;
    public const uint MFSOURCE_READER_FIRST_VIDEO_STREAM = 0xFFFFFFFC;

    public static readonly Guid MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE = new("c60ac5fe-252a-478f-a0ef-bc8fa5f7cad3");
    public static readonly Guid MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID = new("8ac3587a-4ae7-42d8-99e0-0a6013eef90f");
    public static readonly Guid MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME = new("60d0e559-52f8-4fa2-bbce-acdb34a8ec01");
    public static readonly Guid MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK = new("58f0aad8-22bf-4f8a-bb3d-d2c4978c6e2f");
    public static readonly Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00AA00389B71");
    public static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
    public static readonly Guid MF_MT_FRAME_RATE = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    public static readonly Guid MF_MT_FRAME_RATE_RANGE_MAX = new("e3371d41-b4cf-4a05-bd4e-20b88bb2c4d6");
    public static readonly Guid MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING = new("fb394f3d-cc51-4232-acac-7937e5683277");

    public static readonly Guid MFVideoFormat_RGB32 = new("00000016-0000-0010-8000-00aa00389b71");
    public static readonly Guid MFVideoFormat_YUY2 = new("32595559-0000-0010-8000-00aa00389b71");
    public static readonly Guid MFVideoFormat_MJPG = new("47504a4d-0000-0010-8000-00aa00389b71");
    public static readonly Guid MFVideoFormat_NV12 = new("3231564e-0000-0010-8000-00aa00389b71");
    public static readonly Guid MFVideoFormat_H264 = new("34363248-0000-0010-8000-00aa00389b71");

    public static readonly Guid MF_MT_AVG_BITRATE = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    public static readonly Guid MF_MT_INTERLACE_MODE = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    public static readonly Guid MF_MT_PIXEL_ASPECT_RATIO = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    public static readonly Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new("a634a91c-822b-41b9-a494-4de4643612b0");
    public static readonly Guid MF_SINK_WRITER_DISABLE_THROTTLING = new("08b845d8-2b74-4afe-9d53-be16d2d5ae8c");
    public static readonly Guid MF_LOW_LATENCY = new("9c27891a-ed7a-40e1-88e8-b22727a024ee");

    public const uint MFVideoInterlace_Progressive = 2;

    public static readonly Guid MF_MT_VIDEO_NOMINAL_RANGE = new("c21b8ee5-b956-4071-8daf-325edf5cab11");
    public static readonly Guid MF_MT_YUV_MATRIX = new("3e23d450-2c75-4d25-a00e-b91670d12327");
    public static readonly Guid MF_MT_TRANSFER_FUNCTION = new("5fb0fce9-be5c-4935-a811-ec838f8eed93");

    // Media Foundation sample extension attributes
    public static readonly Guid MFSampleExtension_DeviceTimestamp = new("8f3e35e7-2dcd-4887-8622-2a58baa652b0");

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFStartup(uint Version, uint dwFlags = 0);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateAttributes(out IMFAttributes ppMFAttributes, uint cInitialSize);

    [DllImport("mf.dll", ExactSpelling = true)]
    public static extern int MFEnumDeviceSources(IMFAttributes pAttributes, out IntPtr pppSourceActivate, out uint pcSourceActivate);

    [DllImport("mfreadwrite.dll", ExactSpelling = true)]
    public static extern int MFCreateSourceReaderFromMediaSource(IMFMediaSource pMediaSource, IMFAttributes? pAttributes, out IMFSourceReader ppSourceReader);

    [DllImport("ole32.dll")]
    public static extern void CoTaskMemFree(IntPtr pv);

    [ComImport]
    [Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFAttributes
    {
        [PreserveSig] int GetItem([In] ref Guid guidKey, [In, Out] IntPtr pValue);
        [PreserveSig] int GetItemType([In] ref Guid guidKey, out int pType);
        [PreserveSig] int CompareItem([In] ref Guid guidKey, [In] IntPtr Value, [Out] out bool pbResult);
        [PreserveSig] int Compare([In, MarshalAs(UnmanagedType.Interface)] IMFAttributes pTheirs, [In] int MatchType, [Out] out bool pbResult);
        [PreserveSig] int GetUINT32([In] ref Guid guidKey, out uint punValue);
        [PreserveSig] int GetUINT64([In] ref Guid guidKey, out ulong punValue);
        [PreserveSig] int GetDouble([In] ref Guid guidKey, out double pfValue);
        [PreserveSig] int GetGUID([In] ref Guid guidKey, out Guid pguidValue);
        [PreserveSig] int GetStringLength([In] ref Guid guidKey, out uint pcchLength);
        [PreserveSig] int GetString([In] ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszValue, [In] uint cchBufSize, [In, Out] ref uint pcchLength);
        [PreserveSig] int GetAllocatedString([In] ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPWStr)] out string ppwszValue, [In, Out] ref uint pcchLength);
        [PreserveSig] int GetBlobSize([In] ref Guid guidKey, out uint pcbBlobSize);
        [PreserveSig] int GetBlob([In] ref Guid guidKey, [Out] byte[] pBuf, [In] uint cbBufSize, out uint pcbBlobSize);
        [PreserveSig] int GetAllocatedBlob([In] ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        [PreserveSig] int GetUnknown([In] ref Guid guidKey, [In] ref Guid riid, [Out, MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        [PreserveSig] int SetItem([In] ref Guid guidKey, [In] IntPtr Value);
        [PreserveSig] int DeleteItem([In] ref Guid guidKey);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32([In] ref Guid guidKey, [In] uint unValue);
        [PreserveSig] int SetUINT64([In] ref Guid guidKey, [In] ulong unValue);
        [PreserveSig] int SetDouble([In] ref Guid guidKey, [In] double fValue);
        [PreserveSig] int SetGUID([In] ref Guid guidKey, [In] ref Guid guidValue);
        [PreserveSig] int SetString([In] ref Guid guidKey, [In, MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] int SetBlob([In] ref Guid guidKey, [In] byte[] pBuf, [In] uint cbBufSize);
        [PreserveSig] int SetUnknown([In] ref Guid guidKey, [In, MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out uint pcItems);
        [PreserveSig] int GetItemByIndex([In] uint unIndex, out Guid pguidKey, [In, Out] IntPtr pValue);
        [PreserveSig] int CopyAllItems([In, MarshalAs(UnmanagedType.Interface)] IMFAttributes pDest);
    }

    [ComImport]
    [Guid("7FEE9E9A-4A89-47a6-899C-B6A53A70FB67")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFActivate : IMFAttributes
    {
        #region IMFAttributes redeclarations
        [PreserveSig] new int GetItem([In] ref Guid guidKey, [In, Out] IntPtr pValue);
        [PreserveSig] new int GetItemType([In] ref Guid guidKey, out int pType);
        [PreserveSig] new int CompareItem([In] ref Guid guidKey, [In] IntPtr Value, [Out] out bool pbResult);
        [PreserveSig] new int Compare([In, MarshalAs(UnmanagedType.Interface)] IMFAttributes pTheirs, [In] int MatchType, [Out] out bool pbResult);
        [PreserveSig] new int GetUINT32([In] ref Guid guidKey, out uint punValue);
        [PreserveSig] new int GetUINT64([In] ref Guid guidKey, out ulong punValue);
        [PreserveSig] new int GetDouble([In] ref Guid guidKey, out double pfValue);
        [PreserveSig] new int GetGUID([In] ref Guid guidKey, out Guid pguidValue);
        [PreserveSig] new int GetStringLength([In] ref Guid guidKey, out uint pcchLength);
        [PreserveSig] new int GetString([In] ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszValue, [In] uint cchBufSize, [In, Out] ref uint pcchLength);
        [PreserveSig] new int GetAllocatedString([In] ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPWStr)] out string ppwszValue, [In, Out] ref uint pcchLength);
        [PreserveSig] new int GetBlobSize([In] ref Guid guidKey, out uint pcbBlobSize);
        [PreserveSig] new int GetBlob([In] ref Guid guidKey, [Out] byte[] pBuf, [In] uint cbBufSize, out uint pcbBlobSize);
        [PreserveSig] new int GetAllocatedBlob([In] ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        [PreserveSig] new int GetUnknown([In] ref Guid guidKey, [In] ref Guid riid, [Out, MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        [PreserveSig] new int SetItem([In] ref Guid guidKey, [In] IntPtr Value);
        [PreserveSig] new int DeleteItem([In] ref Guid guidKey);
        [PreserveSig] new int DeleteAllItems();
        [PreserveSig] new int SetUINT32([In] ref Guid guidKey, [In] uint unValue);
        [PreserveSig] new int SetUINT64([In] ref Guid guidKey, [In] ulong unValue);
        [PreserveSig] new int SetDouble([In] ref Guid guidKey, [In] double fValue);
        [PreserveSig] new int SetGUID([In] ref Guid guidKey, [In] ref Guid guidValue);
        [PreserveSig] new int SetString([In] ref Guid guidKey, [In, MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] new int SetBlob([In] ref Guid guidKey, [In] byte[] pBuf, [In] uint cbBufSize);
        [PreserveSig] new int SetUnknown([In] ref Guid guidKey, [In, MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig] new int LockStore();
        [PreserveSig] new int UnlockStore();
        [PreserveSig] new int GetCount(out uint pcItems);
        [PreserveSig] new int GetItemByIndex([In] uint unIndex, out Guid pguidKey, [In, Out] IntPtr pValue);
        [PreserveSig] new int CopyAllItems([In, MarshalAs(UnmanagedType.Interface)] IMFAttributes pDest);
        #endregion

        [PreserveSig] int ActivateObject([In] ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        [PreserveSig] int ShutdownObject();
        [PreserveSig] int DetachObject();
    }

    [ComImport]
    [Guid("279A808D-AEC7-40C8-9C6B-A6B492C78A66")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFMediaSource
    {
        [PreserveSig] int GetCharacteristics(out uint pdwCharacteristics);
        [PreserveSig] int CreatePresentationDescriptor(out IntPtr ppPresentationDescriptor);
        [PreserveSig] int Start([In] IntPtr pPresentationDescriptor, [In] IntPtr pguidTimeFormat, [In] IntPtr pvarStartPosition);
        [PreserveSig] int Stop();
        [PreserveSig] int Pause();
        [PreserveSig] int Shutdown();
    }

    [ComImport]
    [Guid("70AE66F2-C809-4E4F-8915-BDCB406B7993")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSourceReader
    {
        [PreserveSig] int GetStreamSelection(uint dwStreamIndex, [MarshalAs(UnmanagedType.Bool)] out bool pfSelected);
        [PreserveSig] int SetStreamSelection(uint dwStreamIndex, [MarshalAs(UnmanagedType.Bool)] bool fSelected);
        [PreserveSig] int GetNativeMediaType(uint dwStreamIndex, uint dwMediaTypeIndex, out IMFMediaType ppMediaType);
        [PreserveSig] int GetCurrentMediaType(uint dwStreamIndex, out IMFMediaType ppMediaType);
        [PreserveSig] int SetCurrentMediaType(uint dwStreamIndex, [In, Out] IntPtr pdwReserved, [In] IMFMediaType pMediaType);
        [PreserveSig] int SetStreamPosition([In] ref Guid guidTimeFormat, [In] IntPtr varPosition);
        [PreserveSig] int ReadSample(uint dwStreamIndex, uint dwControlFlags, out uint pdwActualStreamIndex, out uint pdwStreamFlags, out ulong pllTimestamp, out IMFSample ppSample);
        [PreserveSig] int Flush(uint dwStreamIndex);
        [PreserveSig] int GetServiceForStream(uint dwStreamIndex, [In] ref Guid guidService, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);
        [PreserveSig] int GetPresentationAttribute(uint dwStreamIndex, [In] ref Guid guidAttribute, [In, Out] IntPtr pvarAttribute);
    }

    [ComImport]
    [Guid("44AE0FA8-EA31-4109-8D2E-4CAE4997C555")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFMediaType : IMFAttributes
    {
        #region IMFAttributes redeclarations
        [PreserveSig] new int GetItem([In] ref Guid guidKey, [In, Out] IntPtr pValue);
        [PreserveSig] new int GetItemType([In] ref Guid guidKey, out int pType);
        [PreserveSig] new int CompareItem([In] ref Guid guidKey, [In] IntPtr Value, [Out] out bool pbResult);
        [PreserveSig] new int Compare([In, MarshalAs(UnmanagedType.Interface)] IMFAttributes pTheirs, [In] int MatchType, [Out] out bool pbResult);
        [PreserveSig] new int GetUINT32([In] ref Guid guidKey, out uint punValue);
        [PreserveSig] new int GetUINT64([In] ref Guid guidKey, out ulong punValue);
        [PreserveSig] new int GetDouble([In] ref Guid guidKey, out double pfValue);
        [PreserveSig] new int GetGUID([In] ref Guid guidKey, out Guid pguidValue);
        [PreserveSig] new int GetStringLength([In] ref Guid guidKey, out uint pcchLength);
        [PreserveSig] new int GetString([In] ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszValue, [In] uint cchBufSize, [In, Out] ref uint pcchLength);
        [PreserveSig] new int GetAllocatedString([In] ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPWStr)] out string ppwszValue, [In, Out] ref uint pcchLength);
        [PreserveSig] new int GetBlobSize([In] ref Guid guidKey, out uint pcbBlobSize);
        [PreserveSig] new int GetBlob([In] ref Guid guidKey, [Out] byte[] pBuf, [In] uint cbBufSize, out uint pcbBlobSize);
        [PreserveSig] new int GetAllocatedBlob([In] ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        [PreserveSig] new int GetUnknown([In] ref Guid guidKey, [In] ref Guid riid, [Out, MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        [PreserveSig] new int SetItem([In] ref Guid guidKey, [In] IntPtr Value);
        [PreserveSig] new int DeleteItem([In] ref Guid guidKey);
        [PreserveSig] new int DeleteAllItems();
        [PreserveSig] new int SetUINT32([In] ref Guid guidKey, [In] uint unValue);
        [PreserveSig] new int SetUINT64([In] ref Guid guidKey, [In] ulong unValue);
        [PreserveSig] new int SetDouble([In] ref Guid guidKey, [In] double fValue);
        [PreserveSig] new int SetGUID([In] ref Guid guidKey, [In] ref Guid guidValue);
        [PreserveSig] new int SetString([In] ref Guid guidKey, [In, MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] new int SetBlob([In] ref Guid guidKey, [In] byte[] pBuf, [In] uint cbBufSize);
        [PreserveSig] new int SetUnknown([In] ref Guid guidKey, [In, MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig] new int LockStore();
        [PreserveSig] new int UnlockStore();
        [PreserveSig] new int GetCount(out uint pcItems);
        [PreserveSig] new int GetItemByIndex([In] uint unIndex, out Guid pguidKey, [In, Out] IntPtr pValue);
        [PreserveSig] new int CopyAllItems([In, MarshalAs(UnmanagedType.Interface)] IMFAttributes pDest);
        #endregion

        [PreserveSig] int GetMajorType(out Guid pguidMajorType);
        [PreserveSig] int IsFullBoundsFormat([MarshalAs(UnmanagedType.Bool)] out bool pfFullBounds);
        [PreserveSig] int IsCompressedFormat([MarshalAs(UnmanagedType.Bool)] out bool pfCompressed);
        [PreserveSig] int IsEqual([MarshalAs(UnmanagedType.Interface)] IMFMediaType pIMediaType, out uint pdwFlags);
        [PreserveSig] int GetRepresentation([In] ref Guid guidRepresentation, out IntPtr ppvRepresentation);
        [PreserveSig] int FreeRepresentation([In] ref Guid guidRepresentation, [In] IntPtr pvRepresentation);
    }

    [ComImport]
    [Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSample : IMFAttributes
    {
        #region IMFAttributes redeclarations
        [PreserveSig] new int GetItem([In] ref Guid guidKey, [In, Out] IntPtr pValue);
        [PreserveSig] new int GetItemType([In] ref Guid guidKey, out int pType);
        [PreserveSig] new int CompareItem([In] ref Guid guidKey, [In] IntPtr Value, [Out] out bool pbResult);
        [PreserveSig] new int Compare([In, MarshalAs(UnmanagedType.Interface)] IMFAttributes pTheirs, [In] int MatchType, [Out] out bool pbResult);
        [PreserveSig] new int GetUINT32([In] ref Guid guidKey, out uint punValue);
        [PreserveSig] new int GetUINT64([In] ref Guid guidKey, out ulong punValue);
        [PreserveSig] new int GetDouble([In] ref Guid guidKey, out double pfValue);
        [PreserveSig] new int GetGUID([In] ref Guid guidKey, out Guid pguidValue);
        [PreserveSig] new int GetStringLength([In] ref Guid guidKey, out uint pcchLength);
        [PreserveSig] new int GetString([In] ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszValue, [In] uint cchBufSize, [In, Out] ref uint pcchLength);
        [PreserveSig] new int GetAllocatedString([In] ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPWStr)] out string ppwszValue, [In, Out] ref uint pcchLength);
        [PreserveSig] new int GetBlobSize([In] ref Guid guidKey, out uint pcbBlobSize);
        [PreserveSig] new int GetBlob([In] ref Guid guidKey, [Out] byte[] pBuf, [In] uint cbBufSize, out uint pcbBlobSize);
        [PreserveSig] new int GetAllocatedBlob([In] ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        [PreserveSig] new int GetUnknown([In] ref Guid guidKey, [In] ref Guid riid, [Out, MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        [PreserveSig] new int SetItem([In] ref Guid guidKey, [In] IntPtr Value);
        [PreserveSig] new int DeleteItem([In] ref Guid guidKey);
        [PreserveSig] new int DeleteAllItems();
        [PreserveSig] new int SetUINT32([In] ref Guid guidKey, [In] uint unValue);
        [PreserveSig] new int SetUINT64([In] ref Guid guidKey, [In] ulong unValue);
        [PreserveSig] new int SetDouble([In] ref Guid guidKey, [In] double fValue);
        [PreserveSig] new int SetGUID([In] ref Guid guidKey, [In] ref Guid guidValue);
        [PreserveSig] new int SetString([In] ref Guid guidKey, [In, MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] new int SetBlob([In] ref Guid guidKey, [In] byte[] pBuf, [In] uint cbBufSize);
        [PreserveSig] new int SetUnknown([In] ref Guid guidKey, [In, MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig] new int LockStore();
        [PreserveSig] new int UnlockStore();
        [PreserveSig] new int GetCount(out uint pcItems);
        [PreserveSig] new int GetItemByIndex([In] uint unIndex, out Guid pguidKey, [In, Out] IntPtr pValue);
        [PreserveSig] new int CopyAllItems([In, MarshalAs(UnmanagedType.Interface)] IMFAttributes pDest);
        #endregion

        [PreserveSig] int GetSampleFlags(out uint pdwSampleFlags);
        [PreserveSig] int SetSampleFlags(uint dwSampleFlags);
        [PreserveSig] int GetSampleTime(out long pllSampleTime);
        [PreserveSig] int SetSampleTime(long llSampleTime);
        [PreserveSig] int GetSampleDuration(out long pllSampleDuration);
        [PreserveSig] int SetSampleDuration(long llSampleDuration);
        [PreserveSig] int GetBufferCount(out uint pdwBufferCount);
        [PreserveSig] int GetBufferByIndex(uint dwIndex, out IMFMediaBuffer ppBuffer);
        [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer ppBuffer);
        [PreserveSig] int AddBuffer(IMFMediaBuffer pBuffer);
        [PreserveSig] int RemoveBufferByIndex(uint dwIndex);
        [PreserveSig] int RemoveAllBuffers();
        [PreserveSig] int GetTotalLength(out uint pcbTotalLength);
        [PreserveSig] int CopyToBuffer(IMFMediaBuffer pBuffer);
    }

    [ComImport]
    [Guid("045FA593-8799-42B8-BC8D-8968C6453507")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr ppbBuffer, out uint pcbMaxLength, out uint pcbCurrentLength);
        [PreserveSig] int Unlock();
        [PreserveSig] int GetCurrentLength(out uint pcbCurrentLength);
        [PreserveSig] int SetCurrentLength(uint cbCurrentLength);
        [PreserveSig] int GetMaxLength(out uint pcbMaxLength);
    }

    public static string GetFourCCFromGuid(Guid subtype)
    {
        byte[] bytes = subtype.ToByteArray();
        char c0 = (char)bytes[0];
        char c1 = (char)bytes[1];
        char c2 = (char)bytes[2];
        char c3 = (char)bytes[3];
        if (char.IsLetterOrDigit(c0) && char.IsLetterOrDigit(c1) && char.IsLetterOrDigit(c2) && char.IsLetterOrDigit(c3))
        {
            return $"{c0}{c1}{c2}{c3}";
        }
        return subtype.ToString();
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod", SetLastError = true)]
    public static extern uint TimeBeginPeriod(uint uMilliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod", SetLastError = true)]
    public static extern uint TimeEndPeriod(uint uMilliseconds);

    [Flags]
    public enum EXECUTION_STATE : uint
    {
        ES_SYSTEM_REQUIRED = 0x00000001,
        ES_DISPLAY_REQUIRED = 0x00000002,
        ES_USER_PRESENT = 0x00000004,
        ES_AWAYMODE_REQUIRED = 0x00000040,
        ES_CONTINUOUS = 0x80000000,
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

    [ComImport]
    [Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSinkWriter
    {
        [PreserveSig] int AddStream([In] IMFMediaType pTargetMediaType, out uint pdwStreamIndex);
        [PreserveSig] int SetInputMediaType(uint dwStreamIndex, [In] IMFMediaType pInputMediaType, [In] IMFAttributes? pEncodingParameters);
        [PreserveSig] int BeginWriting();
        [PreserveSig] int WriteSample(uint dwStreamIndex, [In] IMFSample pSample);
        [PreserveSig] int SendStreamTick(uint dwStreamIndex, long llTimestamp);
        [PreserveSig] int PlaceMarker(uint dwStreamIndex, IntPtr pvContext);
        [PreserveSig] int NotifyEndOfSegment(uint dwStreamIndex);
        [PreserveSig] int Flush(uint dwStreamIndex);
        [PreserveSig] int Finalize();
        [PreserveSig] int GetServiceForStream(uint dwStreamIndex, [In] ref Guid guidService, [In] ref Guid riid, out object ppvObject);
        [PreserveSig] int GetStatistics(uint dwStreamIndex, out MF_SINK_WRITER_STATISTICS pStats);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MF_SINK_WRITER_STATISTICS
    {
        public uint cb;
        public long llLastTimestampReceived;
        public long llLastTimestampEncoded;
        public long llLastTimestampProcessed;
        public long llLastStreamTickReceived;
        public long cFramesReceived;
        public long cFramesEncoded;
        public long cFramesProcessed;
        public long cFramesStream;
        public uint dwByteCountReceived;
        public uint dwByteCountProcessed;
        public uint dwByteCountStream;
        public uint dwAverageBitRateReceived;
        public uint dwAverageBitRateProcessed;
    }

    [DllImport("mfreadwrite.dll", ExactSpelling = true)]
    public static extern int MFCreateSinkWriterFromURL(
        [MarshalAs(UnmanagedType.LPWStr)] string pwszOutputURL,
        IntPtr pByteStream,
        IMFAttributes? pAttributes,
        out IMFSinkWriter ppSinkWriter);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateMediaType(out IMFMediaType ppMFType);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateSample(out IMFSample ppSample);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateMemoryBuffer(uint cbMaxLength, out IMFMediaBuffer ppBuffer);

    public static ulong PackSize(uint width, uint height) => ((ulong)width << 32) | height;
    public static ulong PackRatio(uint numerator, uint denominator) => ((ulong)numerator << 32) | denominator;
}
