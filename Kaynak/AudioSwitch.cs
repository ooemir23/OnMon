using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ProSystemMonitor
{
    public sealed class AudioDevice
    {
        public string Id, Name;
        public bool IsDefault;
    }

    /// <summary>Ses çıkış cihazlarını listeler ve varsayılanı değiştirir (SoundSwitch benzeri, ek program gerektirmez).</summary>
    public static class AudioSwitch
    {
        public static volatile AudioDevice[] Devices = new AudioDevice[0];
        public static volatile string DefaultName = "";
        private static readonly object _lock = new object();

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorCom { }

        [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
        private class PolicyConfigClient { }

        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IEnum
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IColl devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IDev endpoint);
        }

        [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IColl
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int Item(int index, out IDev device);
        }

        [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDev
        {
            [PreserveSig] int Activate(ref Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object itf);
            [PreserveSig] int OpenPropertyStore(int access, out IProps props);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out int state);
        }

        [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IProps
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetAt(int i, out PropKey key);
            [PreserveSig] int GetValue(ref PropKey key, out PropVariant value);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropKey { public Guid fmtid; public int pid; }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropVariant { public ushort vt, r1, r2, r3; public IntPtr p; public IntPtr p2; }

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant pv);

        [Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPolicyConfig
        {
            [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string n, IntPtr f);
            [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string n, int def, IntPtr f);
            [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string n);
            [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string n, IntPtr a, IntPtr b);
            [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string n, int def, IntPtr a, IntPtr b);
            [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string n, IntPtr a);
            [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string n, IntPtr m);
            [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string n, IntPtr m);
            [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string n, int fx, IntPtr key, IntPtr pv);
            [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string n, int fx, IntPtr key, IntPtr pv);
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string n, int role);
            [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string n, int vis);
        }

        private static string FriendlyName(IDev d)
        {
            IProps ps = null;
            try
            {
                if (d.OpenPropertyStore(0, out ps) != 0 || ps == null) return "?";
                PropKey k = new PropKey { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
                PropVariant v;
                if (ps.GetValue(ref k, out v) == 0)
                {
                    string s = v.vt == 31 ? Marshal.PtrToStringUni(v.p) : null;
                    PropVariantClear(ref v);
                    return string.IsNullOrEmpty(s) ? "?" : s;
                }
            }
            catch { }
            finally { try { if (ps != null) Marshal.ReleaseComObject(ps); } catch { } }
            return "?";
        }

        /// <summary>Aktif çıkış cihazlarını okuyup önbelleğe alır.</summary>
        public static void Refresh()
        {
            lock (_lock)
            {
                try
                {
                    var en = (IEnum)(new MMDeviceEnumeratorCom());
                    string defId = null;
                    IDev dd;
                    if (en.GetDefaultAudioEndpoint(0, 1, out dd) == 0 && dd != null)
                    {
                        dd.GetId(out defId);
                        Marshal.ReleaseComObject(dd);
                    }
                    IColl coll;
                    var list = new List<AudioDevice>();
                    if (en.EnumAudioEndpoints(0, 1, out coll) == 0 && coll != null)
                    {
                        int n; coll.GetCount(out n);
                        for (int i = 0; i < n; i++)
                        {
                            IDev d;
                            if (coll.Item(i, out d) != 0 || d == null) continue;
                            string id; d.GetId(out id);
                            list.Add(new AudioDevice { Id = id, Name = FriendlyName(d), IsDefault = id == defId });
                            Marshal.ReleaseComObject(d);
                        }
                        Marshal.ReleaseComObject(coll);
                    }
                    Marshal.ReleaseComObject(en);
                    Devices = list.ToArray();
                    var cur = list.Find(x => x.IsDefault);
                    DefaultName = cur != null ? cur.Name : "";
                }
                catch { }
            }
        }

        /// <summary>Varsayılan ses çıkışını değiştirir (oyun, iletişim ve çoklu ortam rolleri).</summary>
        public static bool SetDefault(string id)
        {
            try
            {
                var pc = (IPolicyConfig)(new PolicyConfigClient());
                int hr = 0;
                for (int role = 0; role < 3; role++) hr |= pc.SetDefaultEndpoint(id, role);
                Marshal.ReleaseComObject(pc);
                Refresh();
                return hr == 0;
            }
            catch { return false; }
        }
    }
}
