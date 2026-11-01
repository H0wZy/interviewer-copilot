import io
p = r"<scratch>\probe\src\Probe.cs"
s = io.open(p, encoding="utf-8").read()

# 1) Pkt: add energy fields
old = "        public float peak;         // peak abs of data (float32 only)"
assert old in s
s = s.replace(old, "        public float peak;         // peak abs of data (float32/int16)\n        public double sumSq; public int nSamp;")

# 2) Drain: support int16 as well as float32, accumulate energy
old = """                if (fmt.IsFloat32 && data != IntPtr.Zero)
                {
                    int count = (int)frames * fmt.ch;
                    float[] tmp = new float[count];
                    Marshal.Copy(data, tmp, 0, count);
                    float pk = 0;
                    for (int i = 0; i < count; i++) { float a = Math.Abs(tmp[i]); if (a > pk) pk = a; }
                    p.peak = pk;
                }"""
new = """                if (fmt.IsFloat32 && data != IntPtr.Zero)
                {
                    int count = (int)frames * fmt.ch;
                    float[] tmp = new float[count];
                    Marshal.Copy(data, tmp, 0, count);
                    float pk = 0; double ss = 0;
                    for (int i = 0; i < count; i++) { float a = Math.Abs(tmp[i]); if (a > pk) pk = a; ss += (double)tmp[i] * tmp[i]; }
                    p.peak = pk; p.sumSq = ss; p.nSamp = count;
                }
                else if (fmt.bits == 16 && data != IntPtr.Zero)
                {
                    int count = (int)frames * fmt.ch;
                    short[] tmp = new short[count];
                    Marshal.Copy(data, tmp, 0, count);
                    float pk = 0; double ss = 0;
                    for (int i = 0; i < count; i++) { float v = tmp[i] / 32768f; float a = Math.Abs(v); if (a > pk) pk = a; ss += (double)v * v; }
                    p.peak = pk; p.sumSq = ss; p.nSamp = count;
                }"""
assert old in s
s = s.replace(old, new)

# 3) OpenCustom for endpoint loopback with a caller supplied PCM format + flags
anchor = "        // Process loopback (VAD\\Process_Loopback)."
assert anchor in s, "anchor missing"
add = """        // Endpoint loopback with a caller-supplied PCM format (tests AUTOCONVERTPCM on loopback).
        public void OpenCustom(IMMDevice dev, int tag, int ch, int rate, int bits, bool autoconvert, bool srcQuality, Stopwatch stopwatch)
        {
            sw = stopwatch; useEvent = true; bufferHnsRequested = 0;
            Native.QueryPerformanceFrequency(out qpcFreq);
            Guid iidClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
            object o; int hr = dev.Activate(ref iidClient, 23, IntPtr.Zero, out o);
            if (hr != 0) throw new Exception("Activate hr=0x" + hr.ToString("X8"));
            client = (IAudioClient)o;
            IntPtr pf = Marshal.AllocHGlobal(18);
            Marshal.WriteInt16(pf, 0, (short)tag); Marshal.WriteInt16(pf, 2, (short)ch); Marshal.WriteInt32(pf, 4, rate);
            Marshal.WriteInt32(pf, 8, rate * ch * bits / 8); Marshal.WriteInt16(pf, 12, (short)(ch * bits / 8)); Marshal.WriteInt16(pf, 14, (short)bits); Marshal.WriteInt16(pf, 16, 0);
            fmt = Fmt.Parse(pf);
            uint flags = 0x00020000u | 0x00040000u;                       // LOOPBACK | EVENTCALLBACK
            if (autoconvert) flags |= 0x80000000u;                        // AUTOCONVERTPCM
            if (srcQuality) flags |= 0x08000000u;                         // SRC_DEFAULT_QUALITY
            initHr = client.Initialize(0, unchecked((int)flags), 0, 0, pf, IntPtr.Zero);
            if (initHr != 0) throw new Exception("OpenCustom Initialize hr=0x" + initHr.ToString("X8") + " (" + fmt + ", autoconvert=" + autoconvert + ")");
            client.GetBufferSize(out bufferFrames);
            evt = Native.CreateEventW(IntPtr.Zero, false, false, null);
            client.SetEventHandle(evt);
            Guid iidCap = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
            hr = client.GetService(ref iidCap, out o);
            if (hr != 0) throw new Exception("GetService hr=0x" + hr.ToString("X8"));
            cap = (IAudioCaptureClient)o;
        }

"""
s = s.replace(anchor, add + anchor, 1)

# 4) ConvMode
anchor2 = "        // Process loopback behaviour: own PID"
assert anchor2 in s
conv = """        static int ConvMode()
        {
            Console.WriteLine("== AUTOCONVERTPCM on ENDPOINT loopback (render endpoint only) ==");
            IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            IMMDevice dev; en.GetDefaultAudioEndpoint(0, 0, out dev); AssertRender(dev);
            Stopwatch sw = Stopwatch.StartNew();
            CaptureRun a = new CaptureRun(); a.Open(dev, 0, true, sw);
            CaptureRun b = new CaptureRun(); string bErr = null;
            try { b.OpenCustom(dev, 1, 1, 16000, 16, true, true, sw); } catch (Exception ex) { bErr = ex.Message; }
            CaptureRun c = new CaptureRun(); string cErr = null;
            try { c.OpenCustom(dev, 1, 1, 16000, 16, false, false, sw); } catch (Exception ex) { cErr = ex.Message; }
            Console.WriteLine("control mix fmt: " + a.fmt + "  buf=" + a.bufferFrames);
            Console.WriteLine("16k/mono/s16 + AUTOCONVERTPCM|SRC_DEFAULT_QUALITY: " + (bErr == null ? "Initialize OK, buf=" + b.bufferFrames + " frames" : "FAILED " + bErr));
            Console.WriteLine("16k/mono/s16 WITHOUT AUTOCONVERTPCM: " + (cErr == null ? "Initialize OK (unexpected)" : "FAILED " + cErr));
            a.StartThread(); if (bErr == null) b.StartThread();
            double t0 = sw.Elapsed.TotalMilliseconds; Thread.Sleep(4000); double t1 = sw.Elapsed.TotalMilliseconds;
            a.Stop(); if (bErr == null) b.Stop();
            Summarize("control f32/48k/2ch", a, t0, t1, a.qpcFreq);
            double ssA = 0; long nA = 0; foreach (Pkt p in a.pkts) { if (p.tMs < t0 || p.tMs >= t1) continue; ssA += p.sumSq; nA += p.nSamp; }
            Console.WriteLine("  control RMS (all channels) = {0:F5}", Math.Sqrt(ssA / Math.Max(1, nA)));
            if (bErr == null)
            {
                Summarize("converted s16/16k/mono", b, t0, t1, b.qpcFreq);
                double ssB = 0; long nB = 0; int nPk = 0; long fr = 0; foreach (Pkt p in b.pkts) { if (p.tMs < t0 || p.tMs >= t1) continue; ssB += p.sumSq; nB += p.nSamp; nPk++; fr += p.frames; }
                Console.WriteLine("  converted RMS = {0:F5}; packets={1}; avg frames/packet={2:F1}; expected 160 frames per 10 ms", Math.Sqrt(ssB / Math.Max(1, nB)), nPk, nPk > 0 ? (double)fr / nPk : 0);
                if (b.error != null) Console.WriteLine("  converted thread error: " + b.error);
            }
            return 0;
        }

"""
s = s.replace(anchor2, conv + anchor2, 1)
key = 'if (args.Length > 0 && args[0] == "proc") return ProcMode();'
assert key in s
s = s.replace(key, 'if (args.Length > 0 && args[0] == "conv") return ConvMode();\n            ' + key)
io.open(p, "w", encoding="utf-8").write(s)
print("patched OK")
