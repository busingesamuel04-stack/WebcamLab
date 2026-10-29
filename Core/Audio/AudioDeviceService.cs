using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WebcamLab.Core.Native;

namespace WebcamLab.Core.Audio;

public class AudioDeviceService
{
    public static Task<List<AudioDeviceInfo>> EnumerateCaptureDevicesAsync()
    {
        return Task.Run(() =>
        {
            var devices = new List<AudioDeviceInfo>();

            AudioNative.IMMDeviceEnumerator? enumerator = null;
            AudioNative.IMMDeviceCollection? collection = null;
            AudioNative.IMMDevice? defaultDevice = null;
            string defaultDeviceId = string.Empty;

            try
            {
                enumerator = (AudioNative.IMMDeviceEnumerator)new AudioNative.MMDeviceEnumeratorCom();

                // Get default capture device
                int hrDef = enumerator.GetDefaultAudioEndpoint(
                    AudioNative.EDataFlow.eCapture,
                    AudioNative.ERole.eConsole,
                    out defaultDevice);

                if (hrDef == 0 && defaultDevice != null)
                {
                    defaultDevice.GetId(out defaultDeviceId);
                }

                // Enumerate active capture devices
                int hr = enumerator.EnumAudioEndpoints(
                    AudioNative.EDataFlow.eCapture,
                    AudioNative.DEVICE_STATE_ACTIVE,
                    out collection);

                if (hr != 0 || collection == null)
                {
                    return devices;
                }

                collection.GetCount(out int count);

                for (int i = 0; i < count; i++)
                {
                    AudioNative.IMMDevice? dev = null;
                    try
                    {
                        collection.Item(i, out dev);
                        if (dev == null) continue;

                        dev.GetId(out string devId);
                        string friendlyName = GetDeviceFriendlyName(dev);

                        var info = new AudioDeviceInfo
                        {
                            Id = devId,
                            FriendlyName = friendlyName,
                            IsDefault = !string.IsNullOrEmpty(defaultDeviceId) &&
                                        string.Equals(devId, defaultDeviceId, StringComparison.OrdinalIgnoreCase),
                            IsDellUZ2315H = friendlyName.Contains("UZ2315H", StringComparison.OrdinalIgnoreCase) ||
                                            devId.Contains("0572&PID_141B", StringComparison.OrdinalIgnoreCase) ||
                                            devId.Contains("0451&PID_8044", StringComparison.OrdinalIgnoreCase)
                        };

                        // Query audio format capabilities from AudioClient
                        InspectDeviceAudioFormat(dev, info);

                        devices.Add(info);
                    }
                    catch
                    {
                        // Resilient enumeration: continue with next device if one fails
                    }
                    finally
                    {
                        if (dev != null && Marshal.IsComObject(dev))
                        {
                            Marshal.ReleaseComObject(dev);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AudioDeviceService] Enumeration error: {ex.Message}");
            }
            finally
            {
                if (defaultDevice != null && Marshal.IsComObject(defaultDevice))
                {
                    Marshal.ReleaseComObject(defaultDevice);
                }
                if (collection != null && Marshal.IsComObject(collection))
                {
                    Marshal.ReleaseComObject(collection);
                }
                if (enumerator != null && Marshal.IsComObject(enumerator))
                {
                    Marshal.ReleaseComObject(enumerator);
                }
            }

            return devices;
        });
    }

    private static string GetDeviceFriendlyName(AudioNative.IMMDevice dev)
    {
        string? nameFromStore = null;
        AudioNative.IPropertyStore? store = null;
        try
        {
            int hr = dev.OpenPropertyStore(AudioNative.STGM_READ, out store);
            if (hr == 0 && store != null)
            {
                var key = AudioNative.PKEY_Device_FriendlyName;
                var pv = new AudioNative.PROPVARIANT();
                try
                {
                    int hrVal = store.GetValue(ref key, out pv);
                    if (hrVal == 0 && pv.vt == 31 && pv.ptrVal != IntPtr.Zero)
                    {
                        nameFromStore = Marshal.PtrToStringUni(pv.ptrVal);
                    }
                }
                finally
                {
                    AudioNative.PropVariantClear(ref pv);
                }

                if (string.IsNullOrWhiteSpace(nameFromStore))
                {
                    var keyDesc = AudioNative.PKEY_Device_DeviceDesc;
                    var keyExt = AudioNative.PKEY_AudioEndpoint_Ext_FriendlyName;
                    var pvDesc = new AudioNative.PROPVARIANT();
                    var pvExt = new AudioNative.PROPVARIANT();
                    try
                    {
                        store.GetValue(ref keyDesc, out pvDesc);
                        store.GetValue(ref keyExt, out pvExt);
                        string? d = (pvDesc.vt == 31 && pvDesc.ptrVal != IntPtr.Zero) ? Marshal.PtrToStringUni(pvDesc.ptrVal) : null;
                        string? e = (pvExt.vt == 31 && pvExt.ptrVal != IntPtr.Zero) ? Marshal.PtrToStringUni(pvExt.ptrVal) : null;
                        if (!string.IsNullOrWhiteSpace(d) && !string.IsNullOrWhiteSpace(e))
                        {
                            nameFromStore = $"{d} ({e})";
                        }
                        else if (!string.IsNullOrWhiteSpace(d))
                        {
                            nameFromStore = d;
                        }
                    }
                    finally
                    {
                        AudioNative.PropVariantClear(ref pvDesc);
                        AudioNative.PropVariantClear(ref pvExt);
                    }
                }
            }
        }
        catch
        {
            // Fall through to registry fallback
        }
        finally
        {
            if (store != null && Marshal.IsComObject(store))
            {
                Marshal.ReleaseComObject(store);
            }
        }

        if (!string.IsNullOrWhiteSpace(nameFromStore))
        {
            return nameFromStore;
        }

        // Fallback: Query Windows MMDevices registry
        try
        {
            dev.GetId(out string devId);
            string? regName = GetFriendlyNameFromRegistry(devId);
            if (!string.IsNullOrWhiteSpace(regName))
            {
                return regName;
            }
        }
        catch { }

        return "Audio Capture Device";
    }

    private static string? GetFriendlyNameFromRegistry(string devId)
    {
        try
        {
            // Extract endpoint GUID from devId format: "{0.0.1.00000000}.{guid}"
            int lastDot = devId.LastIndexOf('.');
            string guidPart = lastDot >= 0 ? devId[(lastDot + 1)..] : devId;
            if (!guidPart.StartsWith('{')) guidPart = "{" + guidPart;
            if (!guidPart.EndsWith('}')) guidPart += "}";

            string regKeyPath = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture\{guidPart}\Properties";
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(regKeyPath);
            if (key != null)
            {
                // Try PKEY_Device_FriendlyName ({a45c254e-df1c-4efd-8020-67d146a850e0},14)
                var fn = key.GetValue("{a45c254e-df1c-4efd-8020-67d146a850e0},14") as string;
                if (!string.IsNullOrWhiteSpace(fn)) return fn;

                // Try {a45c254e-df1c-4efd-8020-67d146a850e0},2 (Desc) and {b3f8fa53-0004-438e-9003-51a46e139bfc},6 (Ext)
                var desc = key.GetValue("{a45c254e-df1c-4efd-8020-67d146a850e0},2") as string;
                var ext = key.GetValue("{b3f8fa53-0004-438e-9003-51a46e139bfc},6") as string;

                if (!string.IsNullOrWhiteSpace(desc) && !string.IsNullOrWhiteSpace(ext))
                {
                    return $"{desc} ({ext})";
                }
                if (!string.IsNullOrWhiteSpace(desc)) return desc;
            }
        }
        catch { }

        return null;
    }

    private static void InspectDeviceAudioFormat(AudioNative.IMMDevice dev, AudioDeviceInfo info)
    {
        object? audioClientObj = null;
        IntPtr pMixFormat = IntPtr.Zero;

        try
        {
            var iidAudioClient = AudioNative.IID_IAudioClient;
            int hr = dev.Activate(ref iidAudioClient, AudioNative.CLSCTX_ALL, IntPtr.Zero, out audioClientObj);
            if (hr != 0 || audioClientObj is not AudioNative.IAudioClient client) return;

            hr = client.GetMixFormat(out pMixFormat);
            if (hr != 0 || pMixFormat == IntPtr.Zero) return;

            var waveFormat = Marshal.PtrToStructure<AudioNative.WAVEFORMATEX>(pMixFormat);
            info.Channels = waveFormat.nChannels;
            info.SampleRate = (int)waveFormat.nSamplesPerSec;
            info.BitsPerSample = waveFormat.wBitsPerSample;

            if (waveFormat.wFormatTag == AudioNative.WAVE_FORMAT_EXTENSIBLE)
            {
                var ext = Marshal.PtrToStructure<AudioNative.WAVEFORMATEXTENSIBLE>(pMixFormat);
                info.IsFloat = ext.SubFormat == AudioNative.KSDATAFORMAT_SUBTYPE_IEEE_FLOAT;
                if (ext.Format.wBitsPerSample > 0)
                {
                    info.BitsPerSample = ext.Format.wBitsPerSample;
                }
            }
            else if (waveFormat.wFormatTag == AudioNative.WAVE_FORMAT_IEEE_FLOAT)
            {
                info.IsFloat = true;
            }
            else
            {
                info.IsFloat = false;
            }
        }
        catch
        {
            // Keep default values
        }
        finally
        {
            if (pMixFormat != IntPtr.Zero)
            {
                AudioNative.CoTaskMemFree(pMixFormat);
            }
            if (audioClientObj != null && Marshal.IsComObject(audioClientObj))
            {
                Marshal.ReleaseComObject(audioClientObj);
            }
        }
    }
}
