using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace WebcamLab.Core.ReverseEngineering;

public class UsbDeviceReport
{
    public ushort VendorId { get; set; }
    public ushort ProductId { get; set; }
    public ushort BcdDevice { get; set; }
    public ushort BcdUsb { get; set; }
    public string Speed { get; set; } = "Unknown";
    public string HubPath { get; set; } = string.Empty;
    public uint PortNumber { get; set; }
    public byte[] RawConfigDescriptor { get; set; } = [];
    public List<string> Interfaces { get; set; } = [];
    public List<string> Endpoints { get; set; } = [];
    public List<UvcExtensionUnitDescriptor> ExtensionUnits { get; set; } = [];
    public List<UvcFormatDescriptor> Formats { get; set; } = [];
    public string CameraTerminalControls { get; set; } = string.Empty;
    public string ProcessingUnitControls { get; set; } = string.Empty;
    public string StillCaptureSupport { get; set; } = "Not Specified";
    public List<string> StillImageFrames { get; set; } = [];
}

public class UvcExtensionUnitDescriptor
{
    public byte UnitId { get; set; }
    public Guid VendorGuid { get; set; }
    public byte NumControls { get; set; }
    public byte NrInPins { get; set; }
    public byte SourceId { get; set; }
    public byte ControlSize { get; set; }
    public uint BmControls { get; set; }
    public List<int> AdvertisedControls { get; set; } = [];
    public byte[] RawBytes { get; set; } = [];
}

public class UvcFormatDescriptor
{
    public string FormatType { get; set; } = string.Empty;
    public byte FormatIndex { get; set; }
    public Guid FormatGuid { get; set; }
    public List<UvcFrameDescriptor> Frames { get; set; } = [];
}

public class UvcFrameDescriptor
{
    public byte FrameIndex { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double DefaultFps { get; set; }
    public uint MinBitRateMbps { get; set; }
    public uint MaxBitRateMbps { get; set; }
}

public static class UsbDescriptorService
{
    private static readonly Guid[] HUB_GUIDS =
    [
        new("f18a0e88-c30c-11d0-8815-00a0c906bed8"), // Modern Windows USB Hub interface (USBHUB3 / Generic Hub)
        new("f18a0e88-c30c-11d0-8815-00a0c906be1d"), // Classic USB Hub interface
        new("a5dcbf10-6530-11d2-901f-00c04fb951ed")  // USB devices
    ];

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;

    private const uint IOCTL_USB_GET_NODE_INFORMATION = 0x00220408;
    private const uint IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX = 0x00220448;
    private const uint IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION = 0x00220410;

    private const byte USB_CONFIGURATION_DESCRIPTOR_TYPE = 0x02;

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, uint MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, uint DeviceInterfaceDetailDataSize, out uint RequiredSize, IntPtr DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle hDevice, uint dwIoControlCode, IntPtr lpInBuffer, uint nInBufferSize, IntPtr lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct USB_DEVICE_DESCRIPTOR
    {
        public byte bLength;
        public byte bDescriptorType;
        public ushort bcdUSB;
        public byte bDeviceClass;
        public byte bDeviceSubClass;
        public byte bDeviceProtocol;
        public byte bMaxPacketSize0;
        public ushort idVendor;
        public ushort idProduct;
        public ushort bcdDevice;
        public byte iManufacturer;
        public byte iProduct;
        public byte iSerialNumber;
        public byte bNumConfigurations;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct USB_NODE_CONNECTION_INFORMATION_EX
    {
        public uint ConnectionIndex;
        public USB_DEVICE_DESCRIPTOR DeviceDescriptor;
        public byte CurrentConfigurationValue;
        public byte Speed;
        public byte DeviceIsHub;
        public ushort DeviceAddress;
        public uint NumberOfOpenPipes;
        public uint ConnectionStatus;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct USB_DESCRIPTOR_REQUEST
    {
        public uint ConnectionIndex;
        public byte bmRequestType;
        public byte bRequest;
        public ushort wValue;
        public ushort wIndex;
        public ushort wLength;
    }

    public static UsbDeviceReport? ExtractDellWebcamDescriptors(ushort targetVid = 0x0BDA, ushort targetPid = 0x58BA)
    {
        var hubPaths = GetHubPaths();
        foreach (var hubPath in hubPaths)
        {
            var report = ScanHubForDevice(hubPath, targetVid, targetPid);
            if (report != null) return report;
        }

        return null;
    }

    private static List<string> GetHubPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Enumerate via SetupDi across known hub GUIDs
        foreach (var guid in HUB_GUIDS)
        {
            var g = guid;
            var hDevInfo = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);
            if (hDevInfo == IntPtr.Zero || hDevInfo == new IntPtr(-1)) continue;

            try
            {
                uint memberIndex = 0;
                var ifData = new SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>();

                while (SetupDiEnumDeviceInterfaces(hDevInfo, IntPtr.Zero, ref g, memberIndex++, ref ifData))
                {
                    SetupDiGetDeviceInterfaceDetail(hDevInfo, ref ifData, IntPtr.Zero, 0, out uint requiredSize, IntPtr.Zero);
                    IntPtr detailBuffer = Marshal.AllocHGlobal((int)requiredSize);
                    try
                    {
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);
                        if (SetupDiGetDeviceInterfaceDetail(hDevInfo, ref ifData, detailBuffer, requiredSize, out _, IntPtr.Zero))
                        {
                            string? path = Marshal.PtrToStringAuto(IntPtr.Add(detailBuffer, 4));
                            if (!string.IsNullOrEmpty(path)) paths.Add(path);
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detailBuffer);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(hDevInfo);
            }
        }

        // 2. Query registry device classes directly as a reliable fallback
        try
        {
            using var hubKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceClasses\{f18a0e88-c30c-11d0-8815-00a0c906bed8}");
            if (hubKey != null)
            {
                foreach (var subName in hubKey.GetSubKeyNames())
                {
                    if (subName.StartsWith("##?#", StringComparison.OrdinalIgnoreCase))
                    {
                        string path = @"\\?\" + subName[4..];
                        paths.Add(path);
                    }
                }
            }
        }
        catch { }

        return [.. paths];
    }

    private static SafeFileHandle OpenHubHandle(string hubPath)
    {
        var hHub = CreateFile(hubPath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (!hHub.IsInvalid) return hHub;

        hHub = CreateFile(hubPath, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (!hHub.IsInvalid) return hHub;

        return CreateFile(hubPath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
    }

    private static UsbDeviceReport? ScanHubForDevice(string hubPath, ushort targetVid, ushort targetPid)
    {
        using var hHub = OpenHubHandle(hubPath);
        if (hHub.IsInvalid) return null;

        int nodeInfoSize = 40;
        IntPtr nodeInfoBuf = Marshal.AllocHGlobal(nodeInfoSize);
        uint ports = 30;
        try
        {
            if (DeviceIoControl(hHub, IOCTL_USB_GET_NODE_INFORMATION, nodeInfoBuf, (uint)nodeInfoSize, nodeInfoBuf, (uint)nodeInfoSize, out _, IntPtr.Zero))
            {
                byte numPorts = Marshal.ReadByte(nodeInfoBuf, 6);
                if (numPorts > 0 && numPorts < 64) ports = numPorts;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(nodeInfoBuf);
        }

        int connInfoSize = 2048;
        IntPtr connBuf = Marshal.AllocHGlobal(connInfoSize);

        try
        {
            for (uint port = 1; port <= ports; port++)
            {
                for (int i = 0; i < connInfoSize; i++) Marshal.WriteByte(connBuf, i, 0);
                Marshal.WriteInt32(connBuf, 0, (int)port);

                if (DeviceIoControl(hHub, IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX, connBuf, (uint)connInfoSize, connBuf, (uint)connInfoSize, out uint bytesReturned, IntPtr.Zero))
                {
                    if (bytesReturned >= 35)
                    {
                        var connInfo = Marshal.PtrToStructure<USB_NODE_CONNECTION_INFORMATION_EX>(connBuf);
                        if (connInfo.ConnectionStatus == 1) // DeviceConnected
                        {
                            var desc = connInfo.DeviceDescriptor;
                            if (desc.idVendor == targetVid && desc.idProduct == targetPid)
                            {
                                var report = new UsbDeviceReport
                                {
                                    VendorId = desc.idVendor,
                                    ProductId = desc.idProduct,
                                    BcdDevice = desc.bcdDevice,
                                    BcdUsb = desc.bcdUSB,
                                    Speed = connInfo.Speed switch
                                    {
                                        1 => "Full-Speed (USB 1.1 12 Mbps)",
                                        2 => "High-Speed (USB 2.0 480 Mbps)",
                                        3 => "SuperSpeed (USB 3.0 5 Gbps)",
                                        _ => $"Code {connInfo.Speed}"
                                    },
                                    HubPath = hubPath,
                                    PortNumber = port
                                };

                                report.RawConfigDescriptor = ReadFullConfigurationDescriptor(hHub, port);
                                ParseConfigDescriptor(report);
                                return report;
                            }
                        }
                    }
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(connBuf);
        }

        return null;
    }

    private static byte[] ReadFullConfigurationDescriptor(SafeFileHandle hHub, uint port)
    {
        int reqHeaderSize = Marshal.SizeOf<USB_DESCRIPTOR_REQUEST>();
        int firstBufSize = reqHeaderSize + 9;
        IntPtr firstBuf = Marshal.AllocHGlobal(firstBufSize);

        try
        {
            for (int i = 0; i < firstBufSize; i++) Marshal.WriteByte(firstBuf, i, 0);
            Marshal.WriteInt32(firstBuf, 0, (int)port);
            Marshal.WriteByte(firstBuf, 4, 0x80);
            Marshal.WriteByte(firstBuf, 5, 0x06); // GET_DESCRIPTOR
            Marshal.WriteInt16(firstBuf, 6, (short)((USB_CONFIGURATION_DESCRIPTOR_TYPE << 8) | 0));
            Marshal.WriteInt16(firstBuf, 8, 0);
            Marshal.WriteInt16(firstBuf, 10, 9);

            if (!DeviceIoControl(hHub, IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION, firstBuf, (uint)firstBufSize, firstBuf, (uint)firstBufSize, out _, IntPtr.Zero))
            {
                return [];
            }

            ushort wTotalLength = (ushort)Marshal.ReadInt16(firstBuf, reqHeaderSize + 2);
            int fullBufSize = reqHeaderSize + wTotalLength;
            IntPtr fullBuf = Marshal.AllocHGlobal(fullBufSize);
            try
            {
                for (int i = 0; i < fullBufSize; i++) Marshal.WriteByte(fullBuf, i, 0);
                Marshal.WriteInt32(fullBuf, 0, (int)port);
                Marshal.WriteByte(fullBuf, 4, 0x80);
                Marshal.WriteByte(fullBuf, 5, 0x06);
                Marshal.WriteInt16(fullBuf, 6, (short)((USB_CONFIGURATION_DESCRIPTOR_TYPE << 8) | 0));
                Marshal.WriteInt16(fullBuf, 8, 0);
                Marshal.WriteInt16(fullBuf, 10, (short)wTotalLength);

                if (!DeviceIoControl(hHub, IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION, fullBuf, (uint)fullBufSize, fullBuf, (uint)fullBufSize, out _, IntPtr.Zero))
                {
                    return [];
                }

                byte[] result = new byte[wTotalLength];
                Marshal.Copy(IntPtr.Add(fullBuf, reqHeaderSize), result, 0, wTotalLength);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(fullBuf);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(firstBuf);
        }
    }

    private static void ParseConfigDescriptor(UsbDeviceReport r)
    {
        byte[] desc = r.RawConfigDescriptor;
        int offset = 0;
        byte currentIfSubClass = 0;
        UvcFormatDescriptor? currentFormat = null;

        while (offset < desc.Length)
        {
            if (offset + 1 >= desc.Length) break;
            byte len = desc[offset];
            byte type = desc[offset + 1];
            if (len == 0 || offset + len > desc.Length) break;

            byte[] c = new byte[len];
            Array.Copy(desc, offset, c, 0, len);

            if (type == 0x04 && len >= 9) // Standard Interface Descriptor
            {
                byte ifNum = c[2];
                byte alt = c[3];
                byte epCount = c[4];
                byte cls = c[5];
                byte subCls = c[6];
                currentIfSubClass = subCls;
                string name = subCls == 1 ? "VideoControl" : subCls == 2 ? "VideoStreaming" : $"SubClass 0x{subCls:X2}";
                r.Interfaces.Add($"Interface {ifNum}, AltSetting {alt}: {name} (Endpoints: {epCount})");
            }
            else if (type == 0x05 && len >= 7) // Standard Endpoint Descriptor
            {
                byte epAddr = c[2];
                byte attr = c[3];
                ushort maxPacket = (ushort)(c[4] | (c[5] << 8));
                byte interval = c[6];
                string xfer = (attr & 0x03) switch { 0 => "Control", 1 => "Isochronous", 2 => "Bulk", 3 => "Interrupt", _ => "Unknown" };
                int mult = ((maxPacket >> 11) & 0x03) + 1;
                int packetSize = maxPacket & 0x07FF;
                r.Endpoints.Add($"EP 0x{epAddr:X2} ({(epAddr >= 0x80 ? "IN" : "OUT")}): {xfer}, PacketSize={packetSize} (x{mult}/uF = {packetSize * mult} bytes/uF), Interval={interval}");
            }
            else if (type == 0x24 && len >= 3) // Class-Specific Interface Descriptor
            {
                byte subType = c[2];
                if (currentIfSubClass == 1) // VideoControl Interface
                {
                    switch (subType)
                    {
                        case 0x02: // VC_INPUT_TERMINAL
                            if (c.Length >= 15)
                            {
                                byte bControlSize = c[14];
                                uint bm = 0;
                                for (int i = 0; i < bControlSize && (15 + i) < c.Length; i++) bm |= ((uint)c[15 + i]) << (i * 8);
                                r.CameraTerminalControls = $"UnitID={c[3]}, bmControls=0x{bm:X6}";
                            }
                            break;

                        case 0x05: // VC_PROCESSING_UNIT
                            if (c.Length >= 8)
                            {
                                byte bControlSize = c[7];
                                uint bm = 0;
                                for (int i = 0; i < bControlSize && (8 + i) < c.Length; i++) bm |= ((uint)c[8 + i]) << (i * 8);
                                r.ProcessingUnitControls = $"UnitID={c[3]}, SourceID={c[4]}, bmControls=0x{bm:X6}";
                            }
                            break;

                        case 0x06: // VC_EXTENSION_UNIT
                            if (c.Length >= 23)
                            {
                                byte unitId = c[3];
                                byte[] gBytes = new byte[16];
                                Array.Copy(c, 4, gBytes, 0, 16);
                                var guid = new Guid(gBytes);
                                byte numControls = c[20];
                                byte nrInPins = c[21];
                                byte srcId = c[22];
                                int ctrlSizeOffset = 22 + nrInPins;
                                byte ctrlSize = ctrlSizeOffset < c.Length ? c[ctrlSizeOffset] : (byte)0;

                                uint bm = 0;
                                int ctrlOffset = ctrlSizeOffset + 1;
                                for (int i = 0; i < ctrlSize && (ctrlOffset + i) < c.Length; i++)
                                {
                                    bm |= ((uint)c[ctrlOffset + i]) << (i * 8);
                                }

                                var xu = new UvcExtensionUnitDescriptor
                                {
                                    UnitId = unitId,
                                    VendorGuid = guid,
                                    NumControls = numControls,
                                    NrInPins = nrInPins,
                                    SourceId = srcId,
                                    ControlSize = ctrlSize,
                                    BmControls = bm,
                                    RawBytes = c
                                };

                                for (int bit = 0; bit < ctrlSize * 8; bit++)
                                {
                                    if ((bm & (1u << bit)) != 0)
                                    {
                                        xu.AdvertisedControls.Add(bit + 1);
                                    }
                                }

                                r.ExtensionUnits.Add(xu);
                            }
                            break;
                    }
                }
                else if (currentIfSubClass == 2) // VideoStreaming Interface
                {
                    switch (subType)
                    {
                        case 0x01: // VS_INPUT_HEADER
                            if (c.Length >= 11)
                            {
                                byte stillMethod = c[9];
                                byte trigger = c[10];
                                string methodDesc = stillMethod switch
                                {
                                    0 => "Method 0 (None / Unsupported)",
                                    1 => "Method 1 (Host software extracts uncompressed/compressed frame from stream)",
                                    2 => "Method 2 (Hardware still frame trigger on dedicated isoc alt-setting)",
                                    3 => "Method 3 (Dedicated still image stream pin)",
                                    _ => $"Custom (0x{stillMethod:X2})"
                                };
                                r.StillCaptureSupport = $"{methodDesc}, Hardware Trigger Button={trigger != 0}";
                            }
                            break;

                        case 0x03: // VS_STILL_IMAGE_FRAME
                            if (c.Length >= 5)
                            {
                                byte bNumImageSizePatterns = c[4];
                                r.StillImageFrames.Add($"Still Frame Pattern Count: {bNumImageSizePatterns} (Length: {c.Length} bytes)");
                            }
                            break;

                        case 0x04: // VS_FORMAT_UNCOMPRESSED
                            if (c.Length >= 27)
                            {
                                byte[] gBytes = new byte[16];
                                Array.Copy(c, 5, gBytes, 0, 16);
                                currentFormat = new UvcFormatDescriptor
                                {
                                    FormatType = "Uncompressed YUV (YUY2)",
                                    FormatIndex = c[3],
                                    FormatGuid = new Guid(gBytes)
                                };
                                r.Formats.Add(currentFormat);
                            }
                            break;

                        case 0x05: // VS_FRAME_UNCOMPRESSED
                            if (c.Length >= 26 && currentFormat != null)
                            {
                                ushort w = (ushort)(c[5] | (c[6] << 8));
                                ushort h = (ushort)(c[7] | (c[8] << 8));
                                uint defInt = (uint)(c[21] | (c[22] << 8) | (c[23] << 16) | (c[24] << 24));
                                currentFormat.Frames.Add(new UvcFrameDescriptor
                                {
                                    FrameIndex = c[3],
                                    Width = w,
                                    Height = h,
                                    DefaultFps = defInt > 0 ? 10_000_000.0 / defInt : 0,
                                    MinBitRateMbps = (uint)(c[9] | (c[10] << 8) | (c[11] << 16) | (c[12] << 24)) / 1_000_000,
                                    MaxBitRateMbps = (uint)(c[13] | (c[14] << 8) | (c[15] << 16) | (c[16] << 24)) / 1_000_000
                                });
                            }
                            break;

                        case 0x06: // VS_FORMAT_MJPEG
                            if (c.Length >= 11)
                            {
                                currentFormat = new UvcFormatDescriptor
                                {
                                    FormatType = "Compressed MJPEG",
                                    FormatIndex = c[3]
                                };
                                r.Formats.Add(currentFormat);
                            }
                            break;

                        case 0x07: // VS_FRAME_MJPEG
                            if (c.Length >= 26 && currentFormat != null)
                            {
                                ushort w = (ushort)(c[5] | (c[6] << 8));
                                ushort h = (ushort)(c[7] | (c[8] << 8));
                                uint defInt = (uint)(c[21] | (c[22] << 8) | (c[23] << 16) | (c[24] << 24));
                                currentFormat.Frames.Add(new UvcFrameDescriptor
                                {
                                    FrameIndex = c[3],
                                    Width = w,
                                    Height = h,
                                    DefaultFps = defInt > 0 ? 10_000_000.0 / defInt : 0
                                });
                            }
                            break;
                    }
                }
            }

            offset += len;
        }
    }
}
