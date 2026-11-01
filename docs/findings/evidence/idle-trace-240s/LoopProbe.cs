// Adversarial-review probe (scratchpad only). READ-ONLY: opens ONE shared-mode LOOPBACK stream on the
// default RENDER endpoint (eRender/eConsole is hard-coded; there is no code path to a capture endpoint).
// Plays nothing, changes no volume. C# 5 (Windows PowerShell 5.1 Add-Type).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace LoopProbe
{
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        [PreserveSig] int OpenPropertyStore(int stgmAccess, out IntPtr ppProperties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        [PreserveSig] int GetState(out int pdwState);
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IntPtr ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
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
    }


    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr g, int f, out IntPtr c);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr g, int f, out IntPtr c);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator e);
        [PreserveSig] int RegisterSessionNotification(IntPtr n);
        [PreserveSig] int UnregisterSessionNotification(IntPtr n);
        [PreserveSig] int RegisterDuckNotification(IntPtr id, IntPtr n);
        [PreserveSig] int UnregisterDuckNotification(IntPtr n);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int n);
        [PreserveSig] int GetSession(int i, out IAudioSessionControl2 s);
    }
    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string n);
        [PreserveSig] int SetDisplayName(IntPtr v, IntPtr c);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string n);
        [PreserveSig] int SetIconPath(IntPtr v, IntPtr c);
        [PreserveSig] int GetGroupingParam(out Guid g);
        [PreserveSig] int SetGroupingParam(IntPtr g, IntPtr c);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr n);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr n);
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string n);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string n);
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference(bool b);
    }

    // Faithful port of the draft's Timeline (section 3.3), in device frames.
    class Timeline
    {
        public ulong rate, anchor, emitted;
        const ulong MARGIN = 500000UL; // 50 ms in 100-ns units
        public Timeline(ulong rate, ulong anchor) { this.rate = rate; this.anchor = anchor; this.emitted = 0; }
        ulong Frames(ulong dq) { return (ulong)((decimal)dq * rate / 10000000m); }
        ulong Slack() { return rate / 500; }
        public void OnPacket(ulong q, uint n, out ulong pad, out uint drop)
        {
            ulong pos = Frames(q > anchor ? q - anchor : 0);
            ulong s = Slack();
            if (pos > emitted + s) { pad = pos - emitted; drop = 0; }
            else if (pos + s < emitted) { pad = 0; drop = (uint)Math.Min(emitted - pos, (ulong)n); }
            else { pad = 0; drop = 0; }
            emitted += pad + (n - drop);
        }
        public ulong OnIdle(ulong nowQ)
        {
            ulong a = anchor + MARGIN;
            ulong target = Frames(nowQ > a ? nowQ - a : 0);
            if (target > emitted + Slack()) { ulong p = target - emitted; emitted = target; return p; }
            return 0;
        }
    }

    class Pkt { public long readQ; public ulong stamp; public ulong dev; public uint frames; public uint flags; public float peak; }

    public static class P
    {
        [DllImport("kernel32.dll")] static extern IntPtr CreateEventW(IntPtr a, bool manual, bool initial, string name);
        [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        static ulong Q100() { long c = Stopwatch.GetTimestamp(); return (ulong)((decimal)c * 10000000m / Stopwatch.Frequency); }

        public static string Run(int seconds, long hns, bool useEvent)
        {
            string result = null; Exception err = null;
            Thread t = new Thread(new ThreadStart(delegate () { try { result = Inner(seconds, hns, useEvent); } catch (Exception ex) { err = ex; } }));
            t.SetApartmentState(ApartmentState.MTA);
            t.Start(); t.Join();
            return err != null ? "PROBE EXCEPTION: " + err : result;
        }

        static string Inner(int seconds, long hns, bool useEvent)
        {
            StringBuilder sb = new StringBuilder();
            long f = Stopwatch.Frequency; long c0 = Stopwatch.GetTimestamp();
            sb.AppendLine(String.Format("QPC frequency = {0} Hz, raw counter now = {1} ({2:F1} h since QPC epoch)", f, c0, c0 / (double)f / 3600.0));
            decimal maxRaw = 18446744073709551615m / 10000000m; // largest raw counter whose x1e7 fits u64
            double hoursLeft = (double)((maxRaw - c0) / f) / 3600.0;
            sb.AppendLine(String.Format("naive u64 'raw*10_000_000/freq' overflows when raw > {0:E3}; hours left on this machine at this QPC rate: {1:F1}", (double)maxRaw, hoursLeft));

            IMMDeviceEnumerator en = (IMMDeviceEnumerator)(new MMDeviceEnumeratorCo());
            IMMDevice dev; int hr = en.GetDefaultAudioEndpoint(0 /*eRender ONLY*/, 0 /*eConsole*/, out dev);
            if (hr != 0) return sb + "no default render endpoint hr=" + hr.ToString("X8");
            string id; dev.GetId(out id);
            Guid iidClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
            Guid iidCapture = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
            Guid iidMeter = new Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064");
            object o; hr = dev.Activate(ref iidClient, 23, IntPtr.Zero, out o);
            if (hr != 0) return sb + "Activate hr=" + hr.ToString("X8");
            IAudioClient ac = (IAudioClient)o;
            IntPtr pwfx; ac.GetMixFormat(out pwfx);
            int tag = (ushort)Marshal.ReadInt16(pwfx, 0), ch = (ushort)Marshal.ReadInt16(pwfx, 2), rate = Marshal.ReadInt32(pwfx, 4);
            int align = (ushort)Marshal.ReadInt16(pwfx, 12), bits = (ushort)Marshal.ReadInt16(pwfx, 14);
            bool isFloat = (tag == 3) || (tag == 0xFFFE && bits == 32);
            long defP, minP; ac.GetDevicePeriod(out defP, out minP);
            sb.AppendLine(String.Format("endpoint={0}", id));
            sb.AppendLine(String.Format("mix: {0} Hz {1} ch {2}-bit tag=0x{3:X} align={4}; period default={5:F2} ms min={6:F2} ms", rate, ch, bits, tag, align, defP / 1e4, minP / 1e4));
            IAudioMeterInformation meter = null; object om;
            if (dev.Activate(ref iidMeter, 23, IntPtr.Zero, out om) == 0) meter = (IAudioMeterInformation)om;


            try
            {
                object osm; Guid iidSm = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
                if (dev.Activate(ref iidSm, 23, IntPtr.Zero, out osm) == 0)
                {
                    IAudioSessionManager2 sm = (IAudioSessionManager2)osm; IAudioSessionEnumerator se; sm.GetSessionEnumerator(out se);
                    int cnt; se.GetCount(out cnt); sb.AppendLine("render sessions on endpoint: " + cnt);
                    for (int si = 0; si < cnt; si++)
                    {
                        IAudioSessionControl2 sc; if (se.GetSession(si, out sc) != 0) continue;
                        int st; sc.GetState(out st); uint pid; sc.GetProcessId(out pid); int sys = sc.IsSystemSoundsSession();
                        string pn = "?"; try { pn = Process.GetProcessById((int)pid).ProcessName; } catch (Exception) { }
                        sb.AppendLine(String.Format("   session pid={0} ({1}) state={2} (0=inactive,1=ACTIVE,2=expired) systemSounds={3}", pid, pn, st, sys == 0));
                    }
                }
            }
            catch (Exception ex) { sb.AppendLine("session enumeration failed: " + ex.Message); }

            int flags = 0x00020000 /*LOOPBACK*/ | (useEvent ? 0x00040000 /*EVENTCALLBACK*/ : 0);
            hr = ac.Initialize(0 /*SHARED*/, flags, hns, 0, pwfx, IntPtr.Zero);
            sb.AppendLine(String.Format("Initialize(SHARED, LOOPBACK{0}, hns={1}) -> hr=0x{2:X8}", useEvent ? "|EVENTCALLBACK" : "", hns, hr));
            if (hr != 0) return sb.ToString();
            uint bufFrames; ac.GetBufferSize(out bufFrames);
            sb.AppendLine(String.Format("GetBufferSize = {0} frames = {1:F1} ms (requested {2:F1} ms)", bufFrames, bufFrames * 1000.0 / rate, hns / 1e4));
            IntPtr evt = IntPtr.Zero;
            if (useEvent) { evt = CreateEventW(IntPtr.Zero, false, false, null); ac.SetEventHandle(evt); }
            object oc; ac.GetService(ref iidCapture, out oc); IAudioCaptureClient cc = (IAudioCaptureClient)oc;
            ulong startQ = Q100();
            ulong anchor = startQ;
            hr = ac.Start();
            if (hr != 0) { sb.AppendLine("Start hr=" + hr.ToString("X8")); return sb.ToString(); }

            List<Pkt> pk = new List<Pkt>(8192);
            int[] secSignals = new int[seconds + 2], secTimeouts = new int[seconds + 2], secPackets = new int[seconds + 2], secSilent = new int[seconds + 2], secDisc = new int[seconds + 2], secTsErr = new int[seconds + 2];
            float[] secPeak = new float[seconds + 2], secMeter = new float[seconds + 2];
            long[] secFrames = new long[seconds + 2];
            Timeline tl = new Timeline((ulong)rate, anchor);
            ulong simPad = 0, simDrop = 0; int simPadEv = 0, simDropEv = 0, simIdlePadEv = 0; ulong simIdlePad = 0, maxPad = 0;
            List<string> simEvents = new List<string>();
            int wakes = 0; int sigWakes = 0, toWakes = 0;
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < seconds * 1000L)
            {
                bool signaled;
                if (useEvent) { signaled = WaitForSingleObject(evt, 20) == 0; } else { Thread.Sleep(10); signaled = false; }
                int sec = (int)(sw.ElapsedMilliseconds / 1000); if (sec > seconds) sec = seconds;
                wakes++; if (signaled) { sigWakes++; secSignals[sec]++; } else { toWakes++; secTimeouts[sec]++; }
                if (meter != null) { float mp; if (meter.GetPeakValue(out mp) == 0 && mp > secMeter[sec]) secMeter[sec] = mp; }
                uint next; if (cc.GetNextPacketSize(out next) != 0) { sb.AppendLine("GetNextPacketSize failed"); break; }
                while (next > 0)
                {
                    IntPtr data; uint nf, fl; ulong dp, qpc;
                    if (cc.GetBuffer(out data, out nf, out fl, out dp, out qpc) != 0) break;
                    Pkt p = new Pkt(); p.readQ = (long)Q100(); p.stamp = qpc; p.dev = dp; p.frames = nf; p.flags = fl;
                    float peak = 0;
                    if ((fl & 2) == 0 && isFloat && bits == 32)
                    {
                        int total = (int)nf * ch; float[] b = new float[total]; Marshal.Copy(data, b, 0, total);
                        for (int k = 0; k < total; k++) { float a = Math.Abs(b[k]); if (a > peak) peak = a; }
                    }
                    p.peak = peak; pk.Add(p);
                    cc.ReleaseBuffer(nf);
                    secPackets[sec]++; secFrames[sec] += nf; if ((fl & 2) != 0) secSilent[sec]++; if ((fl & 1) != 0) secDisc[sec]++; if ((fl & 4) != 0) secTsErr[sec]++;
                    if (peak > secPeak[sec]) secPeak[sec] = peak;
                    // simulate the draft's capture loop
                    ulong q = ((fl & 4) != 0) ? Q100() : qpc; ulong pad; uint drop;
                    tl.OnPacket(q, nf, out pad, out drop);
                    if (pad > 0) { simPad += pad; simPadEv++; if (pad > maxPad) maxPad = pad; if (simEvents.Count < 12) simEvents.Add(String.Format("t={0:F2}s PAD {1} fr ({2:F2} ms) flags={3} devPos={4}", sw.ElapsedMilliseconds / 1000.0, pad, pad * 1000.0 / rate, fl, dp)); }
                    if (drop > 0) { simDrop += drop; simDropEv++; if (simEvents.Count < 12) simEvents.Add(String.Format("t={0:F2}s DROP {1} fr ({2:F2} ms) flags={3} devPos={4}", sw.ElapsedMilliseconds / 1000.0, drop, drop * 1000.0 / rate, fl, dp)); }
                    if (cc.GetNextPacketSize(out next) != 0) break;
                }
                ulong ip = tl.OnIdle(Q100());
                if (ip > 0) { simIdlePad += ip; simIdlePadEv++; }
            }
            double wallFrames = (double)rate * (double)(Q100() - startQ) / 1e7;
            ac.Stop();
            sb.AppendLine(String.Format("wakes={0} (signalled={1}, timed-out={2}); total packets={3}", wakes, sigWakes, toWakes, pk.Count));
            sb.AppendLine("sec | signals timeouts | packets frames silentPk discPk tsErrPk | maxSamplePeak | endpointMeter(GetPeakValue)");
            for (int s = 0; s < seconds; s++)
                sb.AppendLine(String.Format("{0,3} | {1,6} {2,8} | {3,7} {4,6} {5,8} {6,6} {7,7} | {8,10:F4} | {9:F4}", s, secSignals[s], secTimeouts[s], secPackets[s], secFrames[s], secSilent[s], secDisc[s], secTsErr[s], secPeak[s], secMeter[s]));

            if (pk.Count > 5)
            {
                // age = readQ - stamp
                List<double> ages = new List<double>();
                for (int i = 0; i < pk.Count; i++) ages.Add((pk[i].readQ - (long)pk[i].stamp) / 1e4);
                ages.Sort();
                sb.AppendLine(String.Format("packet age (read - stamp) ms: p50={0:F2} p90={1:F2} p99={2:F2} max={3:F2} min={4:F2}", ages[ages.Count / 2], ages[(int)(ages.Count * 0.9)], ages[(int)(ages.Count * 0.99)], ages[ages.Count - 1], ages[0]));
                // packet sizes
                Dictionary<uint, int> sizes = new Dictionary<uint, int>(); foreach (Pkt p in pk) { int n; sizes.TryGetValue(p.frames, out n); sizes[p.frames] = n + 1; }
                StringBuilder ss = new StringBuilder(); int shown = 0; foreach (KeyValuePair<uint, int> kv in sizes) { if (shown++ < 8) ss.AppendFormat("{0}x{1} ", kv.Key, kv.Value); }
                sb.AppendLine("packet sizes (frames x count): " + ss);
                // device position contiguity
                int devGaps = 0; long devGapFrames = 0; int contig = 0;
                for (int i = 1; i < pk.Count; i++)
                {
                    long exp = (long)pk[i - 1].dev + pk[i - 1].frames; long d = (long)pk[i].dev - exp;
                    if (d != 0) { devGaps++; devGapFrames += d; } else contig++;
                }
                sb.AppendLine(String.Format("device-position continuity: contiguous={0}, non-contiguous={1} (sum of gaps={2} frames); first devPos={3}", contig, devGaps, devGapFrames, pk[0].dev));
                // QPC-stamp vs cumulative frames (what the draft's Timeline compares)
                double q0 = pk[0].stamp; double cum = 0; List<double> res = new List<double>(); List<double> tf = new List<double>();
                for (int i = 0; i < pk.Count; i++) { double ts = (pk[i].stamp - q0) / 1e7; double tfr = cum / rate; res.Add((ts - tfr) * 1000.0); tf.Add(tfr); cum += pk[i].frames; }
                // linear fit res = a + b*t
                double n2 = res.Count, sx = 0, sy = 0, sxx = 0, sxy = 0;
                for (int i = 0; i < res.Count; i++) { sx += tf[i]; sy += res[i]; sxx += tf[i] * tf[i]; sxy += tf[i] * res[i]; }
                double bslope = (n2 * sxy - sx * sy) / (n2 * sxx - sx * sx); double aint = (sy - bslope * sx) / n2;
                double maxRes = 0, sumsq = 0; int over2 = 0;
                for (int i = 0; i < res.Count; i++) { double r = res[i] - (aint + bslope * tf[i]); sumsq += r * r; if (Math.Abs(r) > maxRes) maxRes = Math.Abs(r); if (Math.Abs(res[i] - res[0]) > 2.0) over2++; }
                sb.AppendLine(String.Format("QPC-stamp minus frame-count time: slope = {0:F1} ppm (ms per s = {1:F4}); detrended jitter std={2:F3} ms max|dev|={3:F3} ms; packets whose raw offset vs first packet exceeds the 2 ms slack: {4}/{5}",
                    bslope * 1000.0, bslope, Math.Sqrt(sumsq / res.Count), maxRes, over2, res.Count));
                sb.AppendLine(String.Format("   raw offset first={0:F3} ms, last={1:F3} ms over {2:F1} s of stream", res[0], res[res.Count - 1], tf[tf.Count - 1]));
            }
            sb.AppendLine(String.Format("DRAFT Timeline simulation (anchor=Start, slack 2 ms, idle margin 50 ms): packet-pad events={0} ({1} frames = {2:F1} ms, largest {3:F2} ms), packet-drop events={4} ({5} frames = {6:F1} ms), idle-pad events={7} ({8} frames = {9:F1} ms)",
                simPadEv, simPad, simPad * 1000.0 / rate, maxPad * 1000.0 / rate, simDropEv, simDrop, simDrop * 1000.0 / rate, simIdlePadEv, simIdlePad, simIdlePad * 1000.0 / rate));
            foreach (string s in simEvents) sb.AppendLine("   " + s);
            sb.AppendLine(String.Format("   final: emitted={0} frames vs wall-clock frames={1:F0} (delta {2:F1} ms)", tl.emitted, wallFrames, ((double)tl.emitted - wallFrames) * 1000.0 / rate));
            Marshal.FreeCoTaskMem(pwfx);
            if (evt != IntPtr.Zero) CloseHandle(evt);
            return sb.ToString();
        }
    }
}
