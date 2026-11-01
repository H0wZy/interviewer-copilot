import io
p = r"<scratch>\probe\src\Probe.cs"
s = io.open(p, encoding="utf-8").read()

# 1) Avrt interop
old = "    static class Native\n    {"
assert old in s
s = s.replace(old, """    static class Avrt
    {
        [DllImport("avrt.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr AvSetMmThreadCharacteristicsW(string task, ref uint idx);
        [DllImport("avrt.dll", SetLastError = true)] public static extern bool AvRevertMmThreadCharacteristics(IntPtr h);
    }

    static class Native
    {""", 1)

# 2) CaptureRun: mmcss flag + loop gap tracking
old = "        public int lastHr;\n        public string error;"
assert old in s
s = s.replace(old, "        public int lastHr;\n        public bool mmcss; public string mmcssResult; public double maxLoopGapMs; public List<double> loopGaps = new List<double>(8192);\n        public string error;", 1)

old = """        void Loop()
        {
            try
            {
                while (!stop)
                {"""
assert old in s
s = s.replace(old, """        void Loop()
        {
            IntPtr task = IntPtr.Zero;
            try
            {
                if (mmcss)
                {
                    uint idx = 0; task = Avrt.AvSetMmThreadCharacteristicsW("Audio", ref idx);
                    mmcssResult = task == IntPtr.Zero ? ("FAILED err=" + Marshal.GetLastWin32Error()) : "joined task 'Audio'";
                }
                double lastIter = sw.Elapsed.TotalMilliseconds;
                while (!stop)
                {
                    double nowIter = sw.Elapsed.TotalMilliseconds; double g = nowIter - lastIter; lastIter = nowIter; loopGaps.Add(g); if (g > maxLoopGapMs) maxLoopGapMs = g;""", 1)

old = "            catch (Exception ex) { error = ex.ToString(); }\n        }\n\n        void Drain(bool signaled)"
assert old in s
s = s.replace(old, "            catch (Exception ex) { error = ex.ToString(); }\n            finally { if (task != IntPtr.Zero) Avrt.AvRevertMmThreadCharacteristics(task); }\n        }\n\n        void Drain(bool signaled)", 1)

# 3) StressMode
anchor2 = "        // Process loopback behaviour: own PID"
assert anchor2 in s
stress = """        static volatile bool spinStop;
        static void SpinStart(int n, List<Thread> list)
        {
            spinStop = false;
            for (int i = 0; i < n; i++)
            {
                Thread t = new Thread(delegate () { double x = 1.0; while (!spinStop) { for (int k = 0; k < 20000; k++) x = x * 1.0000001 + 1e-9; if (x > 1e300) x = 1.0; } });
                t.IsBackground = true; t.Start(); list.Add(t);
            }
        }
        static void SpinStop(List<Thread> list) { spinStop = true; foreach (Thread t in list) t.Join(2000); list.Clear(); }

        static int StressMode(int seconds)
        {
            Console.WriteLine("== Stress: capture robustness with {0} busy threads (normal priority), 2 logical CPUs ==", Environment.ProcessorCount);
            IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            IMMDevice dev; en.GetDefaultAudioEndpoint(0, 0, out dev); AssertRender(dev);
            object[][] cfgs = new object[][] {
                new object[] { "no load, buffer=0 (22ms)", 0L, false, 0 },
                new object[] { "LOAD, buffer=0 (22ms), no MMCSS", 0L, false, 2 },
                new object[] { "LOAD, buffer=0 (22ms), MMCSS Audio", 0L, true, 2 },
                new object[] { "LOAD, buffer=1s, no MMCSS", 10000000L, false, 2 },
                new object[] { "LOAD, buffer=1s, MMCSS Audio", 10000000L, true, 2 },
            };
            foreach (object[] c in cfgs)
            {
                string label = (string)c[0]; long buf = (long)c[1]; bool mm = (bool)c[2]; int spinners = (int)c[3];
                Stopwatch sw = Stopwatch.StartNew(); CaptureRun cr = new CaptureRun(); cr.mmcss = mm;
                cr.Open(dev, buf, true, sw);
                List<Thread> spin = new List<Thread>(); if (spinners > 0) SpinStart(spinners, spin);
                cr.StartThread(); double t0 = sw.Elapsed.TotalMilliseconds; Thread.Sleep(seconds * 1000); double t1 = sw.Elapsed.TotalMilliseconds;
                cr.Stop(); SpinStop(spin);
                int disc = 0, n = 0; long frames = 0; foreach (Pkt p in cr.pkts) { if (p.tMs < t0 + 300) continue; n++; frames += p.frames; if ((p.flags & 1) != 0) disc++; }
                List<double> gaps = new List<double>(cr.loopGaps); gaps.Sort(); double p99 = gaps.Count > 0 ? gaps[(int)(gaps.Count * 0.99)] : 0; double p50 = gaps.Count > 0 ? gaps[gaps.Count / 2] : 0;
                double span = (t1 - t0 - 300) / 1000.0;
                Console.WriteLine("  [{0}] bufferFrames={1} mmcss={2} packets={3} coverage={4:P1} DISCONTINUITY={5} loopGap(ms): p50={6:F1} p99={7:F1} max={8:F1}", label, cr.bufferFrames, mm ? cr.mmcssResult : "off", n, frames / (cr.fmt.rate * span), disc, p50, p99, cr.maxLoopGapMs);
                Marshal.ReleaseComObject(cr.cap); Marshal.ReleaseComObject(cr.client);
                Thread.Sleep(800);
            }
            return 0;
        }

"""
s = s.replace(anchor2, stress + anchor2, 1)
key = 'if (args.Length > 0 && args[0] == "ext") return ExtMode();'
assert key in s
s = s.replace(key, 'if (args.Length > 0 && args[0] == "stress") return StressMode(args.Length > 1 ? int.Parse(args[1]) : 6);\n            ' + key)
io.open(p, "w", encoding="utf-8").write(s)
print("patched stress OK")
