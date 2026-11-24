using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ProSystemMonitor
{
    /// <summary>Native wlanapi.dll ile Wi-Fi SSID ve sinyal yüzdesini okur (alt süreç açmaz).</summary>
    internal static class NetWifi
    {
        private static readonly object _lk = new object();
        private static DateTime _lastRead = DateTime.MinValue;
        private static string _cachedSsid = "";
        private static int _cachedSignal = -1;

        [DllImport("wlanapi.dll")]
        private static extern uint WlanOpenHandle(uint dwClientVersion, IntPtr pReserved, out uint pdwNegotiatedVersion, out IntPtr phClientHandle);

        [DllImport("wlanapi.dll")]
        private static extern uint WlanCloseHandle(IntPtr hClientHandle, IntPtr pReserved);

        [DllImport("wlanapi.dll")]
        private static extern void WlanFreeMemory(IntPtr pMemory);

        [DllImport("wlanapi.dll")]
        private static extern uint WlanEnumInterfaces(IntPtr hClientHandle, IntPtr pReserved, out IntPtr ppInterfaceList);

        [DllImport("wlanapi.dll")]
        private static extern uint WlanQueryInterface(IntPtr hClientHandle, ref Guid pInterfaceGuid, int OpCode, IntPtr pReserved, out uint pdwDataSize, out IntPtr ppData, IntPtr pWlanOpcodeValueType);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WLAN_INTERFACE_INFO
        {
            public Guid InterfaceGuid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strInterfaceDescription;
            public int isState;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DOT11_SSID
        {
            public uint uSSIDLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] ucSSID;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WLAN_ASSOCIATION_ATTRIBUTES
        {
            public DOT11_SSID dot11Ssid;
            public int dot11BssType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public byte[] dot11Bssid;
            public int dot11PhyType;
            public uint uDot11PhyIndex;
            public uint wlanSignalQuality;
            public uint ulRxRate;
            public uint ulTxRate;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WLAN_SECURITY_ATTRIBUTES
        {
            [MarshalAs(UnmanagedType.Bool)]
            public bool bSecurityEnabled;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bOneXEnabled;
            public int dot11AuthAlgorithm;
            public int dot11CipherAlgorithm;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WLAN_CONNECTION_ATTRIBUTES
        {
            public int isState;
            public int wlanConnectionMode;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strProfileName;
            public WLAN_ASSOCIATION_ATTRIBUTES wlanAssociationAttributes;
            public WLAN_SECURITY_ATTRIBUTES wlanSecurityAttributes;
        }

        public static void Read(out string ssid, out int signalPct)
        {
            lock (_lk)
            {
                if ((DateTime.UtcNow - _lastRead).TotalSeconds < 20.0)
                {
                    ssid = _cachedSsid;
                    signalPct = _cachedSignal;
                    return;
                }
                _lastRead = DateTime.UtcNow;
                string foundSsid = "";
                int foundSig = -1;
                IntPtr client = IntPtr.Zero;
                IntPtr ifList = IntPtr.Zero;
                try
                {
                    uint neg;
                    if (WlanOpenHandle(2, IntPtr.Zero, out neg, out client) == 0 && client != IntPtr.Zero)
                    {
                        if (WlanEnumInterfaces(client, IntPtr.Zero, out ifList) == 0 && ifList != IntPtr.Zero)
                        {
                            int count = Marshal.ReadInt32(ifList, 0);
                            int infoSize = Marshal.SizeOf(typeof(WLAN_INTERFACE_INFO));
                            long baseAddr = ifList.ToInt64() + 8;
                            for (int i = 0; i < count && i < 16; i++)
                            {
                                var info = (WLAN_INTERFACE_INFO)Marshal.PtrToStructure(new IntPtr(baseAddr + (long)i * infoSize), typeof(WLAN_INTERFACE_INFO));
                                if (info.isState != 1) continue; // wlan_interface_state_connected = 1

                                uint sz;
                                IntPtr pData = IntPtr.Zero;
                                try
                                {
                                    Guid g = info.InterfaceGuid;
                                    if (WlanQueryInterface(client, ref g, 7, IntPtr.Zero, out sz, out pData, IntPtr.Zero) == 0 && pData != IntPtr.Zero)
                                    {
                                        var attr = (WLAN_CONNECTION_ATTRIBUTES)Marshal.PtrToStructure(pData, typeof(WLAN_CONNECTION_ATTRIBUTES));
                                        int len = (int)Math.Min(32, attr.wlanAssociationAttributes.dot11Ssid.uSSIDLength);
                                        if (len > 0 && attr.wlanAssociationAttributes.dot11Ssid.ucSSID != null)
                                            foundSsid = Encoding.UTF8.GetString(attr.wlanAssociationAttributes.dot11Ssid.ucSSID, 0, len);
                                        else if (!string.IsNullOrEmpty(attr.strProfileName))
                                            foundSsid = attr.strProfileName;
                                        foundSig = Math.Max(0, Math.Min(100, (int)attr.wlanAssociationAttributes.wlanSignalQuality));
                                        break;
                                    }
                                }
                                finally
                                {
                                    if (pData != IntPtr.Zero) WlanFreeMemory(pData);
                                }
                            }
                        }
                    }
                }
                catch { }
                finally
                {
                    if (ifList != IntPtr.Zero) try { WlanFreeMemory(ifList); } catch { }
                    if (client != IntPtr.Zero) try { WlanCloseHandle(client, IntPtr.Zero); } catch { }
                }
                _cachedSsid = foundSsid ?? "";
                _cachedSignal = foundSig;
                ssid = _cachedSsid;
                signalPct = _cachedSignal;
            }
        }
    }
}
