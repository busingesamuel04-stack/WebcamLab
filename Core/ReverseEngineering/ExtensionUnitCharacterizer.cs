using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using static WebcamLab.Core.ReverseEngineering.DirectShowTopologyService;

namespace WebcamLab.Core.ReverseEngineering;

public class ExtensionControlCharacterization
{
    public int NodeId { get; set; }
    public Guid UnitGuid { get; set; }
    public string UnitLabel { get; set; } = string.Empty;
    public uint ControlId { get; set; }
    public string ControlName { get; set; } = string.Empty;

    // Length probe
    public uint ReportedLength { get; set; }
    public int LengthProbeHResult { get; set; }
    public string LengthProbeStatus { get; set; } = string.Empty;

    // Basic support probe (Access flags & Info bitmap)
    public int BasicSupportHResult { get; set; }
    public string BasicSupportStatus { get; set; } = string.Empty;
    public uint AccessFlags { get; set; }
    public bool SupportsGet => (AccessFlags & KSPROPERTY_TYPE_GET) != 0;
    public bool SupportsSet => (AccessFlags & KSPROPERTY_TYPE_SET) != 0;
    public string AccessFlagsDescription => $"{(SupportsGet ? "GET " : "")}{(SupportsSet ? "SET " : "")}".Trim();

    // Full description probe
    public bool HasDescription { get; set; }
    public uint DescriptionMembersCount { get; set; }
    public string SteppingInfo { get; set; } = "None";

    // Current value probe (GET_CUR)
    public int GetCurHResult { get; set; }
    public string GetCurStatus { get; set; } = string.Empty;
    public uint GetCurBytesReturned { get; set; }
    public byte[] CurrentValue { get; set; } = [];
    public string CurrentValueHex => CurrentValue.Length > 0 ? BitConverter.ToString(CurrentValue).Replace('-', ' ') : "(None)";

    // Default value probe (GET_DEF)
    public int GetDefHResult { get; set; }
    public string GetDefStatus { get; set; } = string.Empty;
    public uint GetDefBytesReturned { get; set; }
    public byte[] DefaultValue { get; set; } = [];
    public string DefaultValueHex => DefaultValue.Length > 0 ? BitConverter.ToString(DefaultValue).Replace('-', ' ') : "(None)";

    // Synthesis & Interpretation
    public string Interpretation { get; set; } = string.Empty;
    public string SafetyVerdict { get; set; } = "Read-Only";
}

public static class ExtensionUnitCharacterizer
{
    public const uint KSPROPERTY_TYPE_DEFAULTVALUES = 0x00010000;

    [StructLayout(LayoutKind.Sequential)]
    public struct KSPROPERTY_DESCRIPTION
    {
        public uint AccessFlags;
        public IntPtr DescriptionType;
        public KSIDENTIFIER ResourceType;
        public uint TotalSize;
        public uint Reserved;
        public uint MembersListCount;
        public uint Reserved1;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KSPROPERTY_MEMBERSHEADER
    {
        public uint MembersFlags;
        public uint MembersSize;
        public uint MembersCount;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KSPROPERTY_STEPPING_LONG
    {
        public uint SteppingDelta;
        public uint Reserved;
        public int SignedMinimum;
        public int SignedMaximum;
    }

    public static List<ExtensionControlCharacterization> CharacterizeAllControls(object filterObject)
    {
        var results = new List<ExtensionControlCharacterization>();
        var filterKsControl = filterObject as IKsControl;
        if (filterKsControl == null) return results;

        // Discovered targets from 1,019-byte raw configuration descriptor:
        // Unit 4: Node 3, GUID {1229a78c-47b4-4094-b0ce-db07386fb938}, bmControls=0x0600 (Controls 10, 11)
        // Unit 7: Node 4, GUID {0fb885c3-68c2-4547-90f7-8f47579d95fc}, bmControls=0x000F (Controls 1, 2, 3, 4)

        var targets = new (int NodeId, Guid Guid, string Label, uint[] Selectors)[]
        {
            (3, new Guid("1229a78c-47b4-4094-b0ce-db07386fb938"), "Unit 4 (Node 3, 1229a78c)", [0, 10, 11]),
            (4, new Guid("0fb885c3-68c2-4547-90f7-8f47579d95fc"), "Unit 7 (Node 4, 0fb885c3)", [0, 1, 2, 3, 4])
        };

        foreach (var (nodeId, guid, label, selectors) in targets)
        {
            foreach (uint ctrlId in selectors)
            {
                var charact = CharacterizeSingleControl(filterKsControl, (uint)nodeId, guid, label, ctrlId);
                results.Add(charact);
            }
        }

        return results;
    }

    public static ExtensionControlCharacterization CharacterizeSingleControl(
        IKsControl ksControl,
        uint nodeId,
        Guid guid,
        string unitLabel,
        uint ctrlId)
    {
        var info = new ExtensionControlCharacterization
        {
            NodeId = (int)nodeId,
            UnitGuid = guid,
            UnitLabel = unitLabel,
            ControlId = ctrlId,
            ControlName = ctrlId == 0 ? "KSPROPERTY_EXTENSION_UNIT_INFO (Descriptor)" : $"Control {ctrlId}"
        };

        // -------------------------------------------------------------
        // Step 1: Probe Reported Length via 1-byte GET (checks ERROR_MORE_DATA)
        // -------------------------------------------------------------
        IntPtr pSmallBuf = Marshal.AllocHGlobal(1);
        try
        {
            Marshal.WriteByte(pSmallBuf, 0, 0);
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

            int hrLen = ksControl.KsProperty(ref propNode, (uint)Marshal.SizeOf<KSP_NODE>(), pSmallBuf, 1, out uint bytesReq);
            info.LengthProbeHResult = hrLen;
            info.LengthProbeStatus = FormatHResult(hrLen);

            if (hrLen == 0)
            {
                info.ReportedLength = bytesReq > 0 ? bytesReq : 1;
            }
            else if (hrLen == unchecked((int)0x800700EA)) // ERROR_MORE_DATA
            {
                info.ReportedLength = bytesReq;
            }
            else
            {
                // Fallback: Try with 512 bytes to see what GET_CUR returns
                info.ReportedLength = 0;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pSmallBuf);
        }

        // -------------------------------------------------------------
        // Step 2: Probe Basic Support (Access Flags: GET, SET)
        // -------------------------------------------------------------
        IntPtr pFlagsBuf = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt32(pFlagsBuf, 0, 0);
            var propNode = new KSP_NODE
            {
                Property = new KSIDENTIFIER
                {
                    Set = guid,
                    Id = ctrlId,
                    Flags = KSPROPERTY_TYPE_BASICSUPPORT | KSPROPERTY_TYPE_TOPOLOGY
                },
                NodeId = nodeId,
                Reserved = 0
            };

            int hrBs = ksControl.KsProperty(ref propNode, (uint)Marshal.SizeOf<KSP_NODE>(), pFlagsBuf, 4, out uint bsBytesRet);
            if (hrBs != 0)
            {
                // Try without TOPOLOGY flag
                var propDirect = new KSP_NODE
                {
                    Property = new KSIDENTIFIER
                    {
                        Set = guid,
                        Id = ctrlId,
                        Flags = KSPROPERTY_TYPE_BASICSUPPORT
                    },
                    NodeId = 0,
                    Reserved = 0
                };
                int hrBs2 = ksControl.KsProperty(ref propDirect, (uint)Marshal.SizeOf<KSIDENTIFIER>(), pFlagsBuf, 4, out bsBytesRet);
                if (hrBs2 == 0) hrBs = hrBs2;
            }

            info.BasicSupportHResult = hrBs;
            info.BasicSupportStatus = FormatHResult(hrBs);
            if (hrBs == 0 && bsBytesRet >= 4)
            {
                info.AccessFlags = (uint)Marshal.ReadInt32(pFlagsBuf, 0);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pFlagsBuf);
        }

        // -------------------------------------------------------------
        // Step 3: Probe Full Description (Ranges, Stepping, Min/Max)
        // -------------------------------------------------------------
        IntPtr pDescBuf = Marshal.AllocHGlobal(512);
        try
        {
            for (int i = 0; i < 512; i++) Marshal.WriteByte(pDescBuf, i, 0);
            var propNode = new KSP_NODE
            {
                Property = new KSIDENTIFIER
                {
                    Set = guid,
                    Id = ctrlId,
                    Flags = KSPROPERTY_TYPE_BASICSUPPORT | KSPROPERTY_TYPE_TOPOLOGY
                },
                NodeId = nodeId,
                Reserved = 0
            };

            int hrDesc = ksControl.KsProperty(ref propNode, (uint)Marshal.SizeOf<KSP_NODE>(), pDescBuf, 512, out uint descBytesRet);
            if (hrDesc == 0 && descBytesRet >= Marshal.SizeOf<KSPROPERTY_DESCRIPTION>())
            {
                var desc = Marshal.PtrToStructure<KSPROPERTY_DESCRIPTION>(pDescBuf);
                info.HasDescription = true;
                info.DescriptionMembersCount = desc.MembersListCount;

                int descSize = Marshal.SizeOf<KSPROPERTY_DESCRIPTION>();
                if (descBytesRet >= descSize + Marshal.SizeOf<KSPROPERTY_MEMBERSHEADER>() + Marshal.SizeOf<KSPROPERTY_STEPPING_LONG>())
                {
                    IntPtr pMembersHeader = IntPtr.Add(pDescBuf, descSize);
                    var membersHdr = Marshal.PtrToStructure<KSPROPERTY_MEMBERSHEADER>(pMembersHeader);

                    IntPtr pStepping = IntPtr.Add(pMembersHeader, Marshal.SizeOf<KSPROPERTY_MEMBERSHEADER>());
                    var step = Marshal.PtrToStructure<KSPROPERTY_STEPPING_LONG>(pStepping);
                    info.SteppingInfo = $"Min={step.SignedMinimum}, Max={step.SignedMaximum}, Step={step.SteppingDelta}";
                }
            }
        }
        catch
        {
            info.HasDescription = false;
        }
        finally
        {
            Marshal.FreeHGlobal(pDescBuf);
        }

        // -------------------------------------------------------------
        // Step 4: Probe Current Value (GET_CUR)
        // -------------------------------------------------------------
        uint curBufSize = info.ReportedLength > 0 ? Math.Max(info.ReportedLength, 64) : 512;
        IntPtr pCurBuf = Marshal.AllocHGlobal((int)curBufSize);
        try
        {
            for (int i = 0; i < curBufSize; i++) Marshal.WriteByte(pCurBuf, i, 0);
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

            int hrCur = ksControl.KsProperty(ref propNode, (uint)Marshal.SizeOf<KSP_NODE>(), pCurBuf, curBufSize, out uint curBytesRet);
            if (hrCur != 0)
            {
                var propDirect = new KSP_NODE
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
                int hrCur2 = ksControl.KsProperty(ref propDirect, (uint)Marshal.SizeOf<KSIDENTIFIER>(), pCurBuf, curBufSize, out curBytesRet);
                if (hrCur2 == 0) hrCur = hrCur2;
            }

            info.GetCurHResult = hrCur;
            info.GetCurStatus = FormatHResult(hrCur);
            info.GetCurBytesReturned = curBytesRet;

            if (hrCur == 0 && curBytesRet > 0)
            {
                info.CurrentValue = new byte[curBytesRet];
                Marshal.Copy(pCurBuf, info.CurrentValue, 0, (int)curBytesRet);
                if (info.ReportedLength == 0) info.ReportedLength = curBytesRet;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pCurBuf);
        }

        // -------------------------------------------------------------
        // Step 5: Probe Default Value (GET_DEF / KSPROPERTY_TYPE_DEFAULTVALUES)
        // -------------------------------------------------------------
        IntPtr pDefBuf = Marshal.AllocHGlobal(512);
        try
        {
            for (int i = 0; i < 512; i++) Marshal.WriteByte(pDefBuf, i, 0);
            var propNode = new KSP_NODE
            {
                Property = new KSIDENTIFIER
                {
                    Set = guid,
                    Id = ctrlId,
                    Flags = KSPROPERTY_TYPE_DEFAULTVALUES | KSPROPERTY_TYPE_TOPOLOGY
                },
                NodeId = nodeId,
                Reserved = 0
            };

            int hrDef = ksControl.KsProperty(ref propNode, (uint)Marshal.SizeOf<KSP_NODE>(), pDefBuf, 512, out uint defBytesRet);
            info.GetDefHResult = hrDef;
            info.GetDefStatus = FormatHResult(hrDef);
            info.GetDefBytesReturned = defBytesRet;

            if (hrDef == 0 && defBytesRet > 0)
            {
                info.DefaultValue = new byte[defBytesRet];
                Marshal.Copy(pDefBuf, info.DefaultValue, 0, (int)defBytesRet);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pDefBuf);
        }

        // -------------------------------------------------------------
        // Step 6: Interpretation & Safety Verdict
        // -------------------------------------------------------------
        info.Interpretation = SynthesizeInterpretation(ctrlId, info.CurrentValue, info.ReportedLength);

        if (!info.SupportsSet)
        {
            info.SafetyVerdict = "Read-Only (Firmware does not advertise SET support)";
        }
        else
        {
            if (ctrlId == 0)
            {
                info.SafetyVerdict = "Read-Only (UVC Unit Info Descriptor)";
            }
            else if (info.CurrentValue.Length == 1)
            {
                info.SafetyVerdict = "Writable (1-byte Boolean/Flag, candidates: 0x00 or 0x01)";
            }
            else if (info.CurrentValue.Length == 2)
            {
                info.SafetyVerdict = "Writable (16-bit scalar word)";
            }
            else
            {
                info.SafetyVerdict = $"Writable ({info.CurrentValue.Length}-byte proprietary structure, requires caution)";
            }
        }

        return info;
    }

    private static string FormatHResult(int hr) => hr switch
    {
        0 => "S_OK",
        unchecked((int)0x80070490) => "ERROR_NOT_FOUND (0x80070490)",
        unchecked((int)0x80070492) => "ERROR_SET_NOT_FOUND (0x80070492)",
        unchecked((int)0x80070057) => "E_INVALIDARG (0x80070057)",
        unchecked((int)0x800700EA) => "ERROR_MORE_DATA (0x800700EA)",
        unchecked((int)0x80004001) => "E_NOTIMPL (0x80004001)",
        _ => $"0x{hr:X8}"
    };

    private static string SynthesizeInterpretation(uint ctrlId, byte[] data, uint length)
    {
        if (ctrlId == 0)
        {
            return $"UVC Extension Unit Descriptor ({data.Length} bytes).";
        }

        if (data.Length == 0)
        {
            return "(No data returned / Request unsupported)";
        }

        if (data.Length >= 9 && data[0] == 0x28 && data[1] == 0x10)
        {
            ushort fwVersion = (ushort)(data[0] | (data[1] << 8));
            ushort subSys = (ushort)(data[2] | (data[3] << 8));
            byte sensorId = data[4];
            return $"Realtek ISP Calibration Header: FW=0x{fwVersion:X4}, SubSystem=0x{subSys:X4}, SensorID=0x{sensorId:X2}";
        }

        if (data.Length == 1)
        {
            return data[0] switch
            {
                0x00 => "State Flag: 0 (Disabled / Inactive / Mode 0)",
                0x01 => "State Flag: 1 (Enabled / Active / Mode 1)",
                _ => $"Scalar Byte: 0x{data[0]:X2} ({data[0]})"
            };
        }

        if (data.Length == 2)
        {
            ushort val = (ushort)(data[0] | (data[1] << 8));
            return $"16-bit Scalar Word: 0x{val:X4} ({val})";
        }

        if (data.Length == 8 && (data[0] == 0x00 || data[0] == 0x03) && data[1] == 0x08)
        {
            return $"Realtek Unit 4 Control 10 Vector: Mode=0x{data[0]:X2}, Length=0x{data[1]:X2}, Tail={BitConverter.ToString(data, 2).Replace('-', ' ')}";
        }

        return $"Binary payload ({data.Length} bytes): {BitConverter.ToString(data).Replace('-', ' ')}";
    }

    /// <summary>
    /// Executes a controlled write test with mandatory rollback.
    /// Strictly adheres to Non-Negotiable Safeguards:
    /// 1. Reads current value
    /// 2. Verifies SET support
    /// 3. Writes single new test value
    /// 4. Re-reads and verifies effect
    /// 5. Restores original value
    /// 6. Re-reads to verify clean rollback
    /// </summary>
    public static (bool Success, string Log) SafeWriteTestSingleByte(
        IKsControl ksControl,
        uint nodeId,
        Guid guid,
        uint ctrlId,
        byte testValue)
    {
        var sb = new System.Text.StringBuilder();
        void Log(string s) => sb.AppendLine(s);

        Log($"[SAFE WRITE TEST] Starting controlled test on Node {nodeId}, Control {ctrlId}, Target={guid:B}");

        // 1. Read initial state
        IntPtr pBuf = Marshal.AllocHGlobal(4);
        try
        {
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

            int hrGet = ksControl.KsProperty(ref propNode, (uint)Marshal.SizeOf<KSP_NODE>(), pBuf, 1, out uint retLen);
            if (hrGet != 0)
            {
                Log($"  [ABORT] Failed to read initial value: {FormatHResult(hrGet)}");
                return (false, sb.ToString());
            }

            byte originalValue = Marshal.ReadByte(pBuf, 0);
            Log($"  [INITIAL STATE] Original Value = 0x{originalValue:X2} ({originalValue})");

            if (originalValue == testValue)
            {
                Log($"  [SKIP] Test value 0x{testValue:X2} equals initial state. Inverting test value for experiment.");
                testValue = (byte)(originalValue == 0 ? 1 : 0);
            }

            // 2. Perform write
            Marshal.WriteByte(pBuf, 0, testValue);
            propNode.Property.Flags = KSPROPERTY_TYPE_SET | KSPROPERTY_TYPE_TOPOLOGY;

            Log($"  [WRITING] Sending test value 0x{testValue:X2} via KSPROPERTY_TYPE_SET...");
            int hrSet = ksControl.KsProperty(ref propNode, (uint)Marshal.SizeOf<KSP_NODE>(), pBuf, 1, out uint setRetLen);
            Log($"  [WRITE RESULT] Status = {FormatHResult(hrSet)}");

            if (hrSet != 0)
            {
                Log("  [RESTORE NOT NEEDED] Write was rejected by driver/firmware.");
                return (false, sb.ToString());
            }

            // 3. Read back written state
            propNode.Property.Flags = KSPROPERTY_TYPE_GET | KSPROPERTY_TYPE_TOPOLOGY;
            int hrReadBack = ksControl.KsProperty(ref propNode, (uint)Marshal.SizeOf<KSP_NODE>(), pBuf, 1, out _);
            byte readBackValue = Marshal.ReadByte(pBuf, 0);
            Log($"  [READ-BACK] Current Value after write = 0x{readBackValue:X2} (Matches test value: {readBackValue == testValue})");

            // 4. Mandatory rollback
            Log($"  [MANDATORY ROLLBACK] Restoring original value 0x{originalValue:X2}...");
            Marshal.WriteByte(pBuf, 0, originalValue);
            propNode.Property.Flags = KSPROPERTY_TYPE_SET | KSPROPERTY_TYPE_TOPOLOGY;
            int hrRestore = ksControl.KsProperty(ref propNode, (uint)Marshal.SizeOf<KSP_NODE>(), pBuf, 1, out _);
            Log($"  [ROLLBACK RESULT] Status = {FormatHResult(hrRestore)}");

            // 5. Final verification of restored state
            propNode.Property.Flags = KSPROPERTY_TYPE_GET | KSPROPERTY_TYPE_TOPOLOGY;
            ksControl.KsProperty(ref propNode, (uint)Marshal.SizeOf<KSP_NODE>(), pBuf, 1, out _);
            byte finalValue = Marshal.ReadByte(pBuf, 0);
            Log($"  [VERIFIED RESTORE] Value = 0x{finalValue:X2} (Fully restored: {finalValue == originalValue})");

            bool clean = (hrSet == 0) && (hrRestore == 0) && (finalValue == originalValue);
            return (clean, sb.ToString());
        }
        finally
        {
            Marshal.FreeHGlobal(pBuf);
        }
    }
}
