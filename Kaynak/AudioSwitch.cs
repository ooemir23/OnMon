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
        public static volatile AudioDevice[] MicDevices = new AudioDevice[0];
        public static volatile string MicName = "";
        public static volatile bool MicAvailable, MicMuted;
        public static volatile float MicLevel;
        public static volatile bool VolAvailable, VolMuted;
        public static volatile float VolLevel;
        private static readonly object _lock = new object();
        private static string _micId = "";

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

        [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMeter
        {
            [PreserveSig] int GetPeakValue(out float peak);
        }

        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IEpVol
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr n);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr n);
            [PreserveSig] int GetChannelCount(out int c);
            [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid ctx);
            [PreserveSig] int SetMasterVolumeLevelScalar(float v, ref Guid ctx);
            [PreserveSig] int GetMasterVolumeLevel(out float db);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float v);
            [PreserveSig] int SetChannelVolumeLevel(uint ch, float db, ref Guid ctx);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint ch, float v, ref Guid ctx);
            [PreserveSig] int GetChannelVolumeLevel(uint ch, out float db);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint ch, out float v);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
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

        private static AudioDevice[] ListFlow(IEnum en, int flow, out string defName)
        {
            defName = "";
            string defId = null;
            IDev dd;
            if (en.GetDefaultAudioEndpoint(flow, 1, out dd) == 0 && dd != null)
            {
                dd.GetId(out defId);
                Marshal.ReleaseComObject(dd);
            }
            var list = new List<AudioDevice>();
            IColl coll;
            if (en.EnumAudioEndpoints(flow, 1, out coll) == 0 && coll != null)
            {
                int n; coll.GetCount(out n);
                for (int i = 0; i < n; i++)
                {
                    IDev d;
                    if (coll.Item(i, out d) != 0 || d == null) continue;
                    string id; d.GetId(out id);
                    var ad = new AudioDevice { Id = id, Name = FriendlyName(d), IsDefault = id == defId };
                    if (ad.IsDefault) defName = ad.Name;
                    list.Add(ad);
                    Marshal.ReleaseComObject(d);
                }
                Marshal.ReleaseComObject(coll);
            }
            return list.ToArray();
        }

        /// <summary>Aktif çıkış ve giriş cihazlarını okuyup önbelleğe alır.</summary>
        public static void Refresh()
        {
            lock (_lock)
            {
                try
                {
                    var en = (IEnum)(new MMDeviceEnumeratorCom());
                    string dn, mn;
                    Devices = ListFlow(en, 0, out dn);
                    DefaultName = dn;
                    MicDevices = ListFlow(en, 1, out mn);
                    if (mn.Length > 0) MicName = mn;
                    Marshal.ReleaseComObject(en);
                }
                catch { }
            }
        }

        private static IDev DefaultMic(IEnum en)
        {
            IDev d;
            return (en.GetDefaultAudioEndpoint(1, 0, out d) == 0) ? d : null;
        }

        /// <summary>Varsayılan mikrofonun anlık seviyesini ve sessiz durumunu okur (hafif; sık çağrılabilir).</summary>
        public static void PollMic()
        {
            try
            {
                var en = (IEnum)(new MMDeviceEnumeratorCom());
                IDev d = DefaultMic(en);
                if (d == null) { MicAvailable = false; Marshal.ReleaseComObject(en); return; }
                string mid; d.GetId(out mid);
                if (mid != _micId || MicName.Length == 0) { _micId = mid; MicName = FriendlyName(d); }
                object o;
                Guid gm = typeof(IMeter).GUID, gv = typeof(IEpVol).GUID;
                float peak = 0; bool muted = false;
                if (d.Activate(ref gm, 23, IntPtr.Zero, out o) == 0 && o != null)
                {
                    ((IMeter)o).GetPeakValue(out peak);
                    Marshal.ReleaseComObject(o);
                }
                if (d.Activate(ref gv, 23, IntPtr.Zero, out o) == 0 && o != null)
                {
                    ((IEpVol)o).GetMute(out muted);
                    Marshal.ReleaseComObject(o);
                }
                MicLevel = peak; MicMuted = muted; MicAvailable = true;
                Marshal.ReleaseComObject(d);
                Marshal.ReleaseComObject(en);
            }
            catch { MicAvailable = false; }
        }

        /// <summary>Varsayılan mikrofonu sessize alır / açar.</summary>
        public static bool ToggleMicMute()
        {
            try
            {
                var en = (IEnum)(new MMDeviceEnumeratorCom());
                IDev d = DefaultMic(en);
                if (d == null) return false;
                object o; Guid gv = typeof(IEpVol).GUID;
                bool ok = false;
                if (d.Activate(ref gv, 23, IntPtr.Zero, out o) == 0 && o != null)
                {
                    var ev = (IEpVol)o;
                    bool m; ev.GetMute(out m);
                    Guid ctx = Guid.Empty;
                    ok = ev.SetMute(!m, ref ctx) == 0;
                    MicMuted = ok ? !m : m;
                    Marshal.ReleaseComObject(o);
                }
                Marshal.ReleaseComObject(d);
                Marshal.ReleaseComObject(en);
                return ok;
            }
            catch { return false; }
        }

        private static IEpVol OutVol(out IDev dev, out IEnum en)
        {
            en = (IEnum)(new MMDeviceEnumeratorCom());
            dev = null;
            if (en.GetDefaultAudioEndpoint(0, 0, out dev) != 0 || dev == null) return null;
            object o; Guid gv = typeof(IEpVol).GUID;
            if (dev.Activate(ref gv, 23, IntPtr.Zero, out o) == 0 && o != null) return (IEpVol)o;
            return null;
        }

        /// <summary>Varsayılan çıkış cihazının ses seviyesini ve sessiz durumunu okur.</summary>
        public static void PollVolume()
        {
            try
            {
                IDev d; IEnum en;
                IEpVol v = OutVol(out d, out en);
                if (v == null) { VolAvailable = false; }
                else
                {
                    float lv; bool mu;
                    v.GetMasterVolumeLevelScalar(out lv); v.GetMute(out mu);
                    VolLevel = lv; VolMuted = mu; VolAvailable = true;
                    Marshal.ReleaseComObject(v);
                }
                if (d != null) Marshal.ReleaseComObject(d);
                Marshal.ReleaseComObject(en);
            }
            catch { VolAvailable = false; }
        }

        /// <summary>Çıkış ses seviyesini ayarlar (0..1).</summary>
        public static void SetVolume(float level)
        {
            try
            {
                level = Math.Max(0f, Math.Min(1f, level));
                IDev d; IEnum en;
                IEpVol v = OutVol(out d, out en);
                if (v != null)
                {
                    Guid ctx = Guid.Empty;
                    v.SetMasterVolumeLevelScalar(level, ref ctx);
                    if (level > 0) v.SetMute(false, ref ctx);
                    VolLevel = level;
                    Marshal.ReleaseComObject(v);
                }
                if (d != null) Marshal.ReleaseComObject(d);
                Marshal.ReleaseComObject(en);
            }
            catch { }
        }

        /// <summary>Çıkışı sessize alır / açar.</summary>
        public static void ToggleVolMute()
        {
            try
            {
                IDev d; IEnum en;
                IEpVol v = OutVol(out d, out en);
                if (v != null)
                {
                    bool m; v.GetMute(out m);
                    Guid ctx = Guid.Empty;
                    v.SetMute(!m, ref ctx);
                    VolMuted = !m;
                    Marshal.ReleaseComObject(v);
                }
                if (d != null) Marshal.ReleaseComObject(d);
                Marshal.ReleaseComObject(en);
            }
            catch { }
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
