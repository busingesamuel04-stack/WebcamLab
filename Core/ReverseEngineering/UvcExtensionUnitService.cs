using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using static WebcamLab.Core.ReverseEngineering.DirectShowTopologyService;

namespace WebcamLab.Core.ReverseEngineering;

public class ExtensionUnitProbeResult
{
    public Guid TargetGuid { get; set; }
    public string GuidLabel { get; set; } = string.Empty;
    public int NodeId { get; set; }
    public uint ControlId { get; set; }
    public int HResult { get; set; }
    public bool Success => HResult == 0;
    public string ErrorName { get; set; } = string.Empty;
    public uint BytesReturned { get; set; }
    public byte[] Data { get; set; } = [];
    public string HexDump => Data.Length > 0 ? BitConverter.ToString(Data).Replace('-', ' ') : "(Empty)";
    public string Interpretation { get; set; } = string.Empty;
}

public static class UvcExtensionUnitService
{
    public static List<ExtensionUnitProbeResult> ProbeExtensionUnits(
        object filterObject,
        IEnumerable<int> nodeIds,
        IEnumerable<(string Label, Guid Guid)> candidateGuids)
    {
        var results = new List<ExtensionUnitProbeResult>();
        var topo = filterObject as IKsTopologyInfo;
        var filterKsControl = filterObject as IKsControl;

        IntPtr pBuf = Marshal.AllocHGlobal(512);

        try
        {
            var iidKsControl = typeof(IKsControl).GUID;

            foreach (int nodeId in nodeIds)
            {
                IKsControl? activeControl = null;

                // Priority 1: Create node instance via IKsTopologyInfo
                if (topo != null)
                {
                    try
                    {
                        int hrNode = topo.CreateNodeInstance(nodeId, ref iidKsControl, out object nodeObj);
                        if (hrNode == 0 && nodeObj is IKsControl nodeKs)
                        {
                            activeControl = nodeKs;
                        }
                    }
                    catch { }
                }

                // Priority 2: Fallback to filter-level IKsControl
                activeControl ??= filterKsControl;

                if (activeControl == null) continue;

                foreach (var (label, guid) in candidateGuids)
                {
                    // 1. Probe Control 0 (KSPROPERTY_EXTENSION_UNIT_INFO / Descriptor query)
                    var res0 = ExecutePropertyQuery(activeControl, (uint)nodeId, guid, label, 0, pBuf, 512);
                    results.Add(res0);

                    // 2. Probe Controls 1 through 12
                    for (uint ctrl = 1; ctrl <= 12; ctrl++)
                    {
                        var res = ExecutePropertyQuery(activeControl, (uint)nodeId, guid, label, ctrl, pBuf, 512);
                        if (res.Success || res.HResult != unchecked((int)0x80070490)) // Ignore ERROR_NOT_FOUND (not present)
                        {
                            results.Add(res);
                        }
                    }
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pBuf);
        }

        return results;
    }

    private static ExtensionUnitProbeResult ExecutePropertyQuery(
        IKsControl ksControl,
        uint nodeId,
        Guid guid,
        string label,
        uint ctrlId,
        IntPtr pBuf,
        uint bufSize)
    {
        for (int i = 0; i < bufSize; i++) Marshal.WriteByte(pBuf, i, 0);

        var propNode = new KSP_NODE
        {
            Property = new KSIDENTIFIER
            {
                Set = guid,
                Id = ctrlId,
                Flags = KSPROPERTY_TYPE_GET | KSPROPERTY_TYPE_TOPOLOGY
            },
            NodeId = nodeId,
            Reserved = 0
        };

        int hr = ksControl.KsProperty(ref propNode, (uint)Marshal.SizeOf<KSP_NODE>(), pBuf, bufSize, out uint bytesRet);

        if (hr != 0)
        {
            // Try standard KSPROPERTY header (for node instance objects)
            var propOnly = new KSP_NODE
            {
                Property = new KSIDENTIFIER
                {
                    Set = guid,
                    Id = ctrlId,
                    Flags = KSPROPERTY_TYPE_GET
                },
                NodeId = 0,
                Reserved = 0
            };
            int hr2 = ksControl.KsProperty(ref propOnly, (uint)Marshal.SizeOf<KSIDENTIFIER>(), pBuf, bufSize, out uint bytesRet2);
            if (hr2 == 0 || hr2 == unchecked((int)0x800700EA)) // S_OK or ERROR_MORE_DATA
            {
                hr = hr2;
                bytesRet = bytesRet2;
            }
        }

        var result = new ExtensionUnitProbeResult
        {
            TargetGuid = guid,
            GuidLabel = label,
            NodeId = (int)nodeId,
            ControlId = ctrlId,
            HResult = hr,
            BytesReturned = bytesRet,
            ErrorName = hr switch
            {
                0 => "S_OK",
                unchecked((int)0x80070490) => "ERROR_NOT_FOUND",
                unchecked((int)0x80070492) => "ERROR_SET_NOT_FOUND",
                unchecked((int)0x80070057) => "E_INVALIDARG",
                unchecked((int)0x800700EA) => "ERROR_MORE_DATA",
                unchecked((int)0x80004001) => "E_NOTIMPL",
                _ => $"0x{hr:X8}"
            }
        };

        if (hr == 0 && bytesRet > 0)
        {
            result.Data = new byte[bytesRet];
            Marshal.Copy(pBuf, result.Data, 0, (int)bytesRet);
            result.Interpretation = InterpretPayload(ctrlId, result.Data);
        }

        return result;
    }

    private static string InterpretPayload(uint ctrlId, byte[] data)
    {
        if (ctrlId == 0)
        {
            return $"UVC Unit Info Descriptor ({data.Length} bytes). Header/Bitmap match.";
        }

        if (data.Length == 1)
        {
            return data[0] switch
            {
                0x00 => "Boolean Flag: 0 (Inactive / Disabled)",
                0x01 => "Boolean Flag: 1 (Active / Enabled)",
                _ => $"Scalar 8-bit value: 0x{data[0]:X2} ({data[0]})"
            };
        }

        if (data.Length == 2)
        {
            ushort val = (ushort)(data[0] | (data[1] << 8));
            return $"16-bit Word: 0x{val:X4} ({val})";
        }

        if (data.Length >= 9 && data[0] == 0x28 && data[1] == 0x10)
        {
            return $"Realtek ISP Firmware Calibration Header (Version: 0x1028, Subsystem: 0x0001, Sensor ID: 0x{data[4]:X2})";
        }

        return $"Binary payload ({data.Length} bytes): {BitConverter.ToString(data).Replace('-', ' ')}";
    }
}
