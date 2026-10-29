using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace WebcamLab.Core.ReverseEngineering;

public class DirectShowTopologyReport
{
    public bool FilterFound { get; set; }
    public string FilterName { get; set; } = string.Empty;
    public int TotalNodes { get; set; }
    public List<DirectShowNodeInfo> Nodes { get; set; } = [];
    public List<string> Diagnostics { get; set; } = [];
    public object? KsControlObject { get; set; }
}

public class DirectShowNodeInfo
{
    public int NodeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid NodeTypeGuid { get; set; }
    public string Classification { get; set; } = string.Empty;
    public bool IsExtensionUnit { get; set; }
}

public static class DirectShowTopologyService
{
    [StructLayout(LayoutKind.Sequential)]
    public struct KSIDENTIFIER
    {
        public Guid Set;
        public uint Id;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KSP_NODE
    {
        public KSIDENTIFIER Property;
        public uint NodeId;
        public uint Reserved;
    }

    public const uint KSPROPERTY_TYPE_GET = 0x00000001;
    public const uint KSPROPERTY_TYPE_SET = 0x00000002;
    public const uint KSPROPERTY_TYPE_SETSUPPORT = 0x00000100;
    public const uint KSPROPERTY_TYPE_BASICSUPPORT = 0x00000200;
    public const uint KSPROPERTY_TYPE_TOPOLOGY = 0x10000000;

    public static readonly Guid KSNODETYPE_DEV_SPECIFIC = new("94164667-04F8-11D1-A129-004058039F3F");
    public static readonly Guid KSNODETYPE_VIDEO_CAMERA_TERMINAL = new("DFF22CE6-F70F-11D0-B917-00A0C9223196");
    public static readonly Guid KSNODETYPE_VIDEO_PROCESSING = new("DFF22CE7-F70F-11D0-B917-00A0C9223196");

    [ComImport, Guid("56a86895-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IBaseFilter
    {
        [PreserveSig] int GetClassID(out Guid pClassID);
        [PreserveSig] int Stop();
        [PreserveSig] int Pause();
        [PreserveSig] int Run(long tStart);
        [PreserveSig] int GetState(int dwMilliSecsTimeout, out int filtState);
        [PreserveSig] int SetSyncSource(IntPtr pClock);
        [PreserveSig] int GetSyncSource(out IntPtr pClock);
        [PreserveSig] int EnumPins(out IEnumPins ppEnum);
        [PreserveSig] int FindPin([In, MarshalAs(UnmanagedType.LPWStr)] string Id, out IPin ppPin);
        [PreserveSig] int QueryFilterInfo(IntPtr pInfo);
        [PreserveSig] int JoinFilterGraph(IntPtr pGraph, [In, MarshalAs(UnmanagedType.LPWStr)] string pName);
        [PreserveSig] int QueryVendorInfo([Out, MarshalAs(UnmanagedType.LPWStr)] out string pVendorInfo);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PIN_INFO
    {
        public IntPtr pFilter;
        public int dir; // 0 = PINDIR_INPUT, 1 = PINDIR_OUTPUT
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string achName;
    }

    [ComImport, Guid("56a86899-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPin
    {
        [PreserveSig] int Connect(IntPtr pReceivePin, IntPtr pmt);
        [PreserveSig] int ReceiveConnection(IntPtr pConnector, IntPtr pmt);
        [PreserveSig] int Disconnect();
        [PreserveSig] int ConnectedTo(out IntPtr pPin);
        [PreserveSig] int ConnectionMediaType(IntPtr pmt);
        [PreserveSig] int QueryPinInfo([Out] out PIN_INFO pInfo);
        [PreserveSig] int QueryDirection(out int pPinDir);
        [PreserveSig] int QueryId([Out, MarshalAs(UnmanagedType.LPWStr)] out string Id);
        [PreserveSig] int QueryAccept(IntPtr pmt);
        [PreserveSig] int EnumMediaTypes(out IntPtr ppEnum);
        [PreserveSig] int QueryInternalConnections([Out, MarshalAs(UnmanagedType.LPArray)] IntPtr[] apPin, [In, Out] ref uint nPin);
        [PreserveSig] int EndOfStream();
        [PreserveSig] int BeginFlush();
        [PreserveSig] int EndFlush();
        [PreserveSig] int NewSegment(long tStart, long tStop, double dRate);
    }

    [ComImport, Guid("56a86892-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IEnumPins
    {
        [PreserveSig] int Next([In] uint celt, [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.IUnknown)] object[] rgelt, [Out] IntPtr pceltFetched);
        [PreserveSig] int Skip([In] uint celt);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone(out IEnumPins ppEnum);
    }

    [ComImport, Guid("720D4AC0-7533-11D0-A5D6-28DB04C10000"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IKsTopologyInfo
    {
        [PreserveSig] int get_NumCategories(out int pdwNumCategories);
        [PreserveSig] int get_Category(int dwIndex, out Guid pCategory);
        [PreserveSig] int get_NumConnections(out int pdwNumConnections);
        [PreserveSig] int get_ConnectionInfo(int dwIndex, IntPtr pConnectionInfo);
        [PreserveSig] int get_NumNodes(out int pdwNumNodes);
        [PreserveSig] int get_NodeType(int dwNodeIndex, out Guid pNodeType);
        [PreserveSig] int get_NodeName(int dwNodeId, IntPtr pwchNodeName, uint dwBufSize, out uint pdwNameLen);
        [PreserveSig] int CreateNodeInstance(int dwNodeId, [In] ref Guid iid, [Out, MarshalAs(UnmanagedType.Interface)] out object ppvObject);
    }

    [ComImport, Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IKsControl
    {
        [PreserveSig]
        int KsProperty(
            [In] ref KSP_NODE Property,
            [In] uint PropertyLength,
            [In, Out] IntPtr PropertyData,
            [In] uint DataLength,
            [Out] out uint BytesReturned);

        [PreserveSig] int KsMethod(IntPtr Method, uint MethodLength, IntPtr MethodData, uint DataLength, out uint BytesReturned);
        [PreserveSig] int KsEvent(IntPtr Event, uint EventLength, IntPtr EventData, uint DataLength, out uint BytesReturned);
    }

    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICreateDevEnum
    {
        [PreserveSig] int CreateClassEnumerator([In] ref Guid pType, out IEnumMoniker ppEnumMoniker, [In] uint dwFlags);
    }

    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyBag
    {
        [PreserveSig] int Read([In, MarshalAs(UnmanagedType.LPWStr)] string pszPropName, [Out] out object pVar, [In] object? pErrorLog);
    }

    public static (DirectShowTopologyReport Report, object? FilterObject) InspectDellWebcamTopology()
    {
        var report = new DirectShowTopologyReport();

        var devType = Type.GetTypeFromCLSID(new Guid("62BE5D10-60EB-11d0-BD3B-00A0C911CE86")); // CLSID_SystemDeviceEnum
        if (devType == null)
        {
            report.Diagnostics.Add("CLSID_SystemDeviceEnum type lookup returned null.");
            return (report, null);
        }

        object? devEnum;
        try
        {
            devEnum = Activator.CreateInstance(devType);
        }
        catch (Exception ex)
        {
            report.Diagnostics.Add($"Activator.CreateInstance failed: {ex.Message}");
            return (report, null);
        }

        if (devEnum is not ICreateDevEnum createDevEnum)
        {
            report.Diagnostics.Add($"devEnum does not implement ICreateDevEnum (Type: {devEnum?.GetType().FullName})");
            return (report, null);
        }

        var videoCapGuid = new Guid("860BB310-5D01-11d0-BD3B-00A0C911CE86"); // CLSID_VideoInputDeviceCategory
        int hr = createDevEnum.CreateClassEnumerator(ref videoCapGuid, out var enumMoniker, 0);
        report.Diagnostics.Add($"CreateClassEnumerator hr=0x{hr:X8}, enumMoniker is null: {enumMoniker == null}");
        if (hr != 0 || enumMoniker == null) return (report, null);

        var monikers = new IMoniker[1];
        var iidBaseFilter = typeof(IBaseFilter).GUID;
        var iidPropBag = typeof(IPropertyBag).GUID;

        while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
        {
            var moniker = monikers[0];
            string friendlyName = string.Empty;
            try
            {
                moniker.BindToStorage(null!, null, ref iidPropBag, out object propBagObj);
                if (propBagObj is IPropertyBag propBag)
                {
                    if (propBag.Read("FriendlyName", out object val, null) == 0 && val is string s)
                    {
                        friendlyName = s;
                    }
                }
            }
            catch (Exception ex)
            {
                report.Diagnostics.Add($"BindToStorage exception: {ex.Message}");
            }

            report.Diagnostics.Add($"Found VideoInputDevice: '{friendlyName}'");

            if (friendlyName.Contains("Dell", StringComparison.OrdinalIgnoreCase) ||
                friendlyName.Contains("Webcam", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    moniker.BindToObject(null!, null, ref iidBaseFilter, out object filterObj);
                    report.Diagnostics.Add($"BindToObject succeeded, filterObj type: {filterObj?.GetType().FullName}");
                    report.Diagnostics.Add($"  is IKsTopologyInfo: {filterObj is IKsTopologyInfo}");
                    report.Diagnostics.Add($"  is IKsControl: {filterObj is IKsControl}");

                    report.KsControlObject = filterObj is IKsControl ? filterObj : null;
                    report.FilterFound = true;
                    report.FilterName = friendlyName;

                    if (filterObj is IKsTopologyInfo topo)
                    {
                        topo.get_NumNodes(out int numNodes);
                        report.TotalNodes = numNodes;

                        for (int i = 0; i < numNodes; i++)
                        {
                            topo.get_NodeType(i, out Guid nodeType);

                            string classif = "Unknown Node";
                            string nodeName = $"Node_{i}";
                            bool isXu = false;
                            if (nodeType == KSNODETYPE_DEV_SPECIFIC)
                            {
                                classif = "DEV_SPECIFIC (UVC Extension Unit)";
                                nodeName = $"UVC Extension Unit (Node {i})";
                                isXu = true;
                            }
                            else if (nodeType == KSNODETYPE_VIDEO_CAMERA_TERMINAL)
                            {
                                classif = "VIDEO_CAMERA_TERMINAL (Sensor / Lens Input)";
                                nodeName = $"Camera Terminal (Node {i})";
                            }
                            else if (nodeType == KSNODETYPE_VIDEO_PROCESSING)
                            {
                                classif = "VIDEO_PROCESSING (Picture Controls)";
                                nodeName = $"Video Processing Unit (Node {i})";
                            }

                            report.Nodes.Add(new DirectShowNodeInfo
                            {
                                NodeId = i,
                                Name = nodeName,
                                NodeTypeGuid = nodeType,
                                Classification = classif,
                                IsExtensionUnit = isXu
                            });
                        }
                    }

                    return (report, filterObj);
                }
                catch (Exception ex)
                {
                    report.Diagnostics.Add($"BindToObject exception: {ex.Message}");
                }
            }
        }

        return (report, null);
    }
}
