// Phase 1 diagnostic: enumerate Core Audio endpoints via IMMDeviceEnumerator and
// test WASAPI shared-mode LOOPBACK initialization + short capture on each ACTIVE
// render endpoint. C# 5 only (compiled by Windows PowerShell 5.1 Add-Type).
// Never opens a capture (microphone) endpoint.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace WasapiProbe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct PROPERTYKEY
    {
        public Guid fmtid;
        public int pid;
        public PROPERTYKEY(Guid f, int p) { fmtid = f; pid = p; }
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr p;
        [FieldOffset(8)] public uint ulVal;
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int cProps);
        [PreserveSig] int GetAt(int iProp, out PROPERTYKEY pkey);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT propvar);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        [PreserveSig] int OpenPropertyStore(int stgmAccess, out IPropertyStore ppProperties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        [PreserveSig] int GetState(out int pdwState);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int pcDevices);
        [PreserveSig] int Item(int nDevice, out IMMDevice ppDevice);
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IMMDeviceCollection ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr pClient);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCo { }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, int streamFlags, long hnsBufferDuration, long hnsPeriodicity, IntPtr pFormat, IntPtr audioSessionGuid);
        [PreserveSig] int GetBufferSize(out uint pNumBufferFrames);
        [PreserveSig] int GetStreamLatency(out long phnsLatency);
        [PreserveSig] int GetCurrentPadding(out uint pNumPaddingFrames);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr pFormat, out IntPtr ppClosestMatch);
        [PreserveSig] int GetMixFormat(out IntPtr ppDeviceFormat);
        [PreserveSig] int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr eventHandle);
        [PreserveSig] int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr ppData, out uint pNumFramesToRead, out uint pdwFlags, out ulong pu64DevicePosition, out ulong pu64QPCPosition);
        [PreserveSig] int ReleaseBuffer(uint NumFramesRead);
        [PreserveSig] int GetNextPacketSize(out uint pNumFramesInNextPacket);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float pfPeak);
        [PreserveSig] int GetMeteringChannelCount(out int pnChannelCount);
        [PreserveSig] int GetChannelsPeakValues(int u32ChannelCount, [Out, MarshalAs(UnmanagedType.LPArray)] float[] afPeakValues);
        [PreserveSig] int QueryHardwareSupport(out int pdwHardwareSupportMask);
    }

    public static class Probe
    {
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT pvar);

        static PROPERTYKEY Key(string g, int pid) { return new PROPERTYKEY(new Guid(g), pid); }

        static string GetString(IPropertyStore ps, PROPERTYKEY key)
        {
            PROPVARIANT pv;
            if (ps.GetValue(ref key, out pv) != 0) return null;
            string s = null;
            if (pv.vt == 31 && pv.p != IntPtr.Zero) s = Marshal.PtrToStringUni(pv.p);
            PropVariantClear(ref pv);
            return s;
        }

        static int GetUInt(IPropertyStore ps, PROPERTYKEY key)
        {
            PROPVARIANT pv;
            if (ps.GetValue(ref key, out pv) != 0) return -1;
            int v = (pv.vt == 19) ? (int)pv.ulVal : -1;
            PropVariantClear(ref pv);
            return v;
        }

        static string StateName(int s)
        {
            switch (s)
            {
                case 1: return "ACTIVE";
                case 2: return "DISABLED";
                case 4: return "NOTPRESENT";
                case 8: return "UNPLUGGED";
                default: return "STATE_" + s;
            }
        }

        static readonly string[] FormFactors = {
            "RemoteNetworkDevice", "Speakers", "LineLevel", "Headphones", "Microphone", "Headset",
            "Handset", "UnknownDigitalPassthrough", "SPDIF", "DigitalAudioDisplay(HDMI/DP)", "UnknownFormFactor" };

        static string FF(int f) { return (f >= 0 && f < FormFactors.Length) ? FormFactors[f] : "n/a"; }

        static string DescribeFormat(IntPtr pwfx, out int kind, out int ch, out int rate, out int bits, out int align)
        {
            int tag = (ushort)Marshal.ReadInt16(pwfx, 0);
            ch = (ushort)Marshal.ReadInt16(pwfx, 2);
            rate = Marshal.ReadInt32(pwfx, 4);
            align = (ushort)Marshal.ReadInt16(pwfx, 12);
            bits = (ushort)Marshal.ReadInt16(pwfx, 14);
            int cb = (ushort)Marshal.ReadInt16(pwfx, 16);
            kind = 0;
            string tagName;
            if (tag == 1) { kind = 1; tagName = "PCM"; }
            else if (tag == 3) { kind = 3; tagName = "IEEE_FLOAT"; }
            else if (tag == 0xFFFE && cb >= 22)
            {
                int valid = (ushort)Marshal.ReadInt16(pwfx, 18);
                uint mask = (uint)Marshal.ReadInt32(pwfx, 20);
                byte[] g = new byte[16];
                Marshal.Copy(new IntPtr(pwfx.ToInt64() + 24), g, 0, 16);
                Guid sub = new Guid(g);
                if (sub == new Guid("00000003-0000-0010-8000-00aa00389b71")) { kind = 3; tagName = "EXTENSIBLE/IEEE_FLOAT"; }
                else if (sub == new Guid("00000001-0000-0010-8000-00aa00389b71")) { kind = 1; tagName = "EXTENSIBLE/PCM"; }
                else tagName = "EXTENSIBLE/" + sub;
                tagName += String.Format(" (validBits={0}, channelMask=0x{1:X})", valid, mask);
            }
            else tagName = String.Format("tag=0x{0:X}", tag);
            return String.Format("{0} Hz, {1} ch, {2}-bit container, {3}, blockAlign={4}", rate, ch, bits, tagName, align);
        }

        public static string Run(int captureMsPerDevice)
        {
            string result = null;
            Exception err = null;
            Thread t = new Thread(new ThreadStart(delegate ()
            {
                try { result = RunInner(captureMsPerDevice); }
                catch (Exception ex) { err = ex; }
            }));
            t.SetApartmentState(ApartmentState.MTA);
            t.Start();
            t.Join();
            return err != null ? ("PROBE EXCEPTION: " + err) : result;
        }

        class Ep
        {
            public string Id;
            public string Name;
            public IMMDevice Dev;
            public string Defaults = "";
        }

        static string RunInner(int captureMs)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Thread apartment: " + Thread.CurrentThread.GetApartmentState());
            IMMDeviceEnumerator en = (IMMDeviceEnumerator)(new MMDeviceEnumeratorCo());
            string[] roles = { "Console", "Multimedia", "Communications" };
            string[] flows = { "Render", "Capture" };
            Dictionary<string, string> defaults = new Dictionary<string, string>();
            for (int f = 0; f < 2; f++)
            {
                for (int r = 0; r < 3; r++)
                {
                    IMMDevice d;
                    int hr = en.GetDefaultAudioEndpoint(f, r, out d);
                    if (hr == 0 && d != null)
                    {
                        string id;
                        d.GetId(out id);
                        string tag = flows[f] + "/" + roles[r];
                        defaults[id] = defaults.ContainsKey(id) ? defaults[id] + "," + tag : tag;
                    }
                    else sb.AppendLine(String.Format("!! No default {0}/{1} endpoint (hr=0x{2:X8})", flows[f], roles[r], hr));
                }
            }

            PROPERTYKEY pkFriendly = Key("a45c254e-df1c-4efd-8020-67d146a850e0", 14);
            PROPERTYKEY pkIface = Key("026e516e-b814-414b-83cd-856d6fef4822", 2);
            PROPERTYKEY pkFormFactor = Key("1da5d803-d492-4edd-8c23-e0c0ffee7f0e", 0);

            List<Ep> activeRender = new List<Ep>();
            for (int f = 0; f < 2; f++)
            {
                IMMDeviceCollection col;
                int hr = en.EnumAudioEndpoints(f, 0xF, out col);
                if (hr != 0) { sb.AppendLine(String.Format("!! EnumAudioEndpoints({0}) hr=0x{1:X8}", flows[f], hr)); continue; }
                int n;
                col.GetCount(out n);
                sb.AppendLine(String.Format("== {0} endpoints ({1}) ==", flows[f], n));
                for (int i = 0; i < n; i++)
                {
                    IMMDevice d;
                    if (col.Item(i, out d) != 0 || d == null) continue;
                    string id;
                    d.GetId(out id);
                    int st;
                    d.GetState(out st);
                    string name = "?", iface = "?";
                    int ff = -1;
                    IPropertyStore ps;
                    if (d.OpenPropertyStore(0, out ps) == 0 && ps != null)
                    {
                        name = GetString(ps, pkFriendly) ?? "?";
                        iface = GetString(ps, pkIface) ?? "?";
                        ff = GetUInt(ps, pkFormFactor);
                    }
                    string def = defaults.ContainsKey(id) ? "   <== DEFAULT(" + defaults[id] + ")" : "";
                    sb.AppendLine(String.Format("  [{0,-10}] {1} | form={2} | adapter={3}{4}", StateName(st), name, FF(ff), iface, def));
                    if (f == 0 && st == 1)
                    {
                        Ep e = new Ep();
                        e.Id = id;
                        e.Name = name;
                        e.Dev = d;
                        e.Defaults = defaults.ContainsKey(id) ? defaults[id] : "";
                        activeRender.Add(e);
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine("== WASAPI loopback probe on ACTIVE render endpoints (shared mode, AUDCLNT_STREAMFLAGS_LOOPBACK) ==");
            if (activeRender.Count == 0)
                sb.AppendLine("!! No ACTIVE render endpoint -> loopback impossible (app must raise explicit error; NO mic fallback).");
            foreach (Ep e in activeRender) ProbeLoopback(e, captureMs, sb);
            return sb.ToString();
        }

        static void ProbeLoopback(Ep e, int captureMs, StringBuilder sb)
        {
            sb.AppendLine(String.Format("-- {0}{1}", e.Name, e.Defaults.Length > 0 ? "   [default: " + e.Defaults + "]" : ""));
            Guid iidClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
            Guid iidCapture = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
            Guid iidMeter = new Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064");

            object o;
            int hr = e.Dev.Activate(ref iidClient, 23, IntPtr.Zero, out o);
            if (hr != 0) { sb.AppendLine(String.Format("   !! Activate(IAudioClient) hr=0x{0:X8}", hr)); return; }
            IAudioClient ac = (IAudioClient)o;

            IntPtr pwfx;
            hr = ac.GetMixFormat(out pwfx);
            if (hr != 0) { sb.AppendLine(String.Format("   !! GetMixFormat hr=0x{0:X8}", hr)); return; }
            int kind, ch, rate, bits, align;
            sb.AppendLine("   mix format: " + DescribeFormat(pwfx, out kind, out ch, out rate, out bits, out align));
            long defP, minP;
            ac.GetDevicePeriod(out defP, out minP);
            sb.AppendLine(String.Format("   device period: default={0:F2} ms, min={1:F2} ms", defP / 10000.0, minP / 10000.0));

            IAudioMeterInformation meter = null;
            object om;
            if (e.Dev.Activate(ref iidMeter, 23, IntPtr.Zero, out om) == 0) meter = (IAudioMeterInformation)om;

            hr = ac.Initialize(0 /*SHARED*/, 0x00020000 /*LOOPBACK*/, 2000000 /*200 ms*/, 0, pwfx, IntPtr.Zero);
            if (hr != 0)
            {
                sb.AppendLine(String.Format("   !! IAudioClient::Initialize(LOOPBACK) FAILED hr=0x{0:X8}", hr));
                Marshal.FreeCoTaskMem(pwfx);
                return;
            }
            uint bufFrames;
            ac.GetBufferSize(out bufFrames);
            object oc;
            hr = ac.GetService(ref iidCapture, out oc);
            if (hr != 0)
            {
                sb.AppendLine(String.Format("   !! GetService(IAudioCaptureClient) hr=0x{0:X8}", hr));
                Marshal.FreeCoTaskMem(pwfx);
                return;
            }
            IAudioCaptureClient cc = (IAudioCaptureClient)oc;
            sb.AppendLine(String.Format("   loopback Initialize: OK (buffer {0} frames = {1:F1} ms)", bufFrames, bufFrames * 1000.0 / rate));

            hr = ac.Start();
            if (hr != 0) { sb.AppendLine(String.Format("   !! Start hr=0x{0:X8}", hr)); Marshal.FreeCoTaskMem(pwfx); return; }

            long frames = 0, packets = 0, silentPk = 0, disc = 0, firstMs = -1, samples = 0;
            double sumSq = 0, peak = 0;
            float meterMax = 0;
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < captureMs)
            {
                Thread.Sleep(10);
                if (meter != null)
                {
                    float pk;
                    if (meter.GetPeakValue(out pk) == 0 && pk > meterMax) meterMax = pk;
                }
                uint next;
                if (cc.GetNextPacketSize(out next) != 0) break;
                while (next > 0)
                {
                    IntPtr data;
                    uint nf, flags;
                    ulong dp, qpc;
                    if (cc.GetBuffer(out data, out nf, out flags, out dp, out qpc) != 0) break;
                    packets++;
                    frames += nf;
                    if (firstMs < 0) firstMs = sw.ElapsedMilliseconds;
                    if ((flags & 1) != 0) disc++;
                    int total = (int)nf * ch;
                    if ((flags & 2) != 0) { silentPk++; }
                    else if (kind == 3 && bits == 32)
                    {
                        float[] b = new float[total];
                        Marshal.Copy(data, b, 0, total);
                        for (int k = 0; k < total; k++) { double v = b[k]; double a = Math.Abs(v); if (a > peak) peak = a; sumSq += v * v; }
                    }
                    else if (kind == 1 && bits == 16)
                    {
                        short[] b = new short[total];
                        Marshal.Copy(data, b, 0, total);
                        for (int k = 0; k < total; k++) { double v = b[k] / 32768.0; double a = Math.Abs(v); if (a > peak) peak = a; sumSq += v * v; }
                    }
                    else if (kind == 1 && bits == 32)
                    {
                        int[] b = new int[total];
                        Marshal.Copy(data, b, 0, total);
                        for (int k = 0; k < total; k++) { double v = b[k] / 2147483648.0; double a = Math.Abs(v); if (a > peak) peak = a; sumSq += v * v; }
                    }
                    samples += total;
                    cc.ReleaseBuffer(nf);
                    if (cc.GetNextPacketSize(out next) != 0) break;
                }
            }
            long elapsed = sw.ElapsedMilliseconds;
            ac.Stop();
            double expected = rate * elapsed / 1000.0;
            sb.AppendLine(String.Format(
                "   captured {0} ms: packets={1}, frames={2} ({3:P0} of wall-clock), silentFlagPackets={4}, discontinuities={5}, firstPacketAt={6} ms",
                elapsed, packets, frames, expected > 0 ? frames / expected : 0, silentPk, disc, firstMs));
            if (samples > 0)
            {
                double rms = Math.Sqrt(sumSq / samples);
                sb.AppendLine(String.Format("   level: peak={0} dBFS, rms={1} dBFS, endpointMeterPeak={2:F3}",
                    peak > 0 ? (20 * Math.Log10(peak)).ToString("F1") : "-inf",
                    rms > 0 ? (20 * Math.Log10(rms)).ToString("F1") : "-inf",
                    meterMax));
            }
            else
            {
                sb.AppendLine(String.Format(
                    "   level: no samples delivered (endpoint idle -> loopback sends NO packets while nothing renders). endpointMeterPeak={0:F3}",
                    meterMax));
            }
            Marshal.FreeCoTaskMem(pwfx);
            Marshal.ReleaseComObject(cc);
            Marshal.ReleaseComObject(ac);
            if (meter != null) Marshal.ReleaseComObject(meter);
        }
    }
}
