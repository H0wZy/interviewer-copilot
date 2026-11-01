import io
p = r"<scratch>\probe\src\Probe.cs"
s = io.open(p, encoding="utf-8").read()

# OpenExt: same as OpenCustom but with WAVEFORMATEXTENSIBLE (what wasapi::WaveFormat::new builds)
anchor = "        // Process loopback (VAD\\Process_Loopback)."
assert anchor in s
add = """        // Endpoint loopback with WAVEFORMATEXTENSIBLE PCM (exactly the layout wasapi::WaveFormat::new() produces).
        public void OpenExt(IMMDevice dev, int ch, int rate, int bits, uint mask, Stopwatch stopwatch)
        {
            sw = stopwatch; useEvent = true; bufferHnsRequested = 0;
            Native.QueryPerformanceFrequency(out qpcFreq);
            Guid iidClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
            object o; int hr = dev.Activate(ref iidClient, 23, IntPtr.Zero, out o);
            if (hr != 0) throw new Exception("Activate hr=0x" + hr.ToString("X8"));
            client = (IAudioClient)o;
            IntPtr pf = Marshal.AllocHGlobal(40);
            Marshal.WriteInt16(pf, 0, unchecked((short)0xFFFE)); Marshal.WriteInt16(pf, 2, (short)ch); Marshal.WriteInt32(pf, 4, rate);
            Marshal.WriteInt32(pf, 8, rate * ch * bits / 8); Marshal.WriteInt16(pf, 12, (short)(ch * bits / 8)); Marshal.WriteInt16(pf, 14, (short)bits);
            Marshal.WriteInt16(pf, 16, 22);                                 // cbSize
            Marshal.WriteInt16(pf, 18, (short)bits);                        // wValidBitsPerSample
            Marshal.WriteInt32(pf, 20, unchecked((int)mask));               // dwChannelMask
            byte[] g = new Guid("00000001-0000-0010-8000-00aa00389b71").ToByteArray(); Marshal.Copy(g, 0, IntPtr.Add(pf, 24), 16);   // KSDATAFORMAT_SUBTYPE_PCM
            fmt = Fmt.Parse(pf);
            uint flags = 0x00020000u | 0x00040000u | 0x80000000u | 0x08000000u;
            initHr = client.Initialize(0, unchecked((int)flags), 0, 0, pf, IntPtr.Zero);
            if (initHr != 0) throw new Exception("OpenExt Initialize hr=0x" + initHr.ToString("X8") + " (" + fmt + ")");
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

anchor2 = "        // Process loopback behaviour: own PID"
assert anchor2 in s
ext = """        static int ExtMode()
        {
            Console.WriteLine("== WAVEFORMATEXTENSIBLE 16k mono s16 + AUTOCONVERTPCM on ENDPOINT loopback ==");
            IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            IMMDevice dev; en.GetDefaultAudioEndpoint(0, 0, out dev); AssertRender(dev);
            Stopwatch sw = Stopwatch.StartNew();
            CaptureRun plain = new CaptureRun(); plain.OpenCustom(dev, 1, 1, 16000, 16, true, true, sw);
            uint[] masks = new uint[] { 0x4, 0x1, 0x0 };
            CaptureRun[] runs = new CaptureRun[masks.Length]; string[] errs = new string[masks.Length];
            for (int i = 0; i < masks.Length; i++) { runs[i] = new CaptureRun(); try { runs[i].OpenExt(dev, 1, 16000, 16, masks[i], sw); } catch (Exception ex) { errs[i] = ex.Message; } }
            plain.StartThread(); for (int i = 0; i < masks.Length; i++) if (errs[i] == null) runs[i].StartThread();
            double t0 = sw.Elapsed.TotalMilliseconds; Thread.Sleep(4000); double t1 = sw.Elapsed.TotalMilliseconds;
            plain.Stop(); for (int i = 0; i < masks.Length; i++) if (errs[i] == null) runs[i].Stop();
            Report("plain WAVEFORMATEX (tag=1,cbSize=0)", plain, t0, t1);
            for (int i = 0; i < masks.Length; i++)
            {
                string lab = "EXTENSIBLE mask=0x" + masks[i].ToString("X");
                if (errs[i] != null) Console.WriteLine("  [" + lab + "] FAILED: " + errs[i]); else Report(lab, runs[i], t0, t1);
            }
            return 0;
        }

        static void Report(string label, CaptureRun r, double t0, double t1)
        {
            double ss = 0; long n = 0; int pk = 0; long fr = 0; float peak = 0;
            foreach (Pkt p in r.pkts) { if (p.tMs < t0 || p.tMs >= t1) continue; ss += p.sumSq; n += p.nSamp; pk++; fr += p.frames; if (p.peak > peak) peak = p.peak; }
            Console.WriteLine("  [{0}] packets={1} frames={2} (avg {3:F1}/pkt) RMS={4:F5} peak={5:F4} buf={6}", label, pk, fr, pk > 0 ? (double)fr / pk : 0, Math.Sqrt(ss / Math.Max(1, n)), peak, r.bufferFrames);
        }

"""
s = s.replace(anchor2, ext + anchor2, 1)
key = 'if (args.Length > 0 && args[0] == "conv") return ConvMode();'
assert key in s
s = s.replace(key, 'if (args.Length > 0 && args[0] == "ext") return ExtMode();\n            ' + key)
io.open(p, "w", encoding="utf-8").write(s)
print("patched ext OK")
