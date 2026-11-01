import io
p = r"<scratch>\probe\src\Probe.cs"
s = io.open(p, encoding="utf-8").read()

# task name selectable
old = "public bool mmcss; public string mmcssResult;"
assert old in s
s = s.replace(old, "public bool mmcss; public string mmcssTask = \"Audio\"; public string mmcssResult;", 1)
old = 'task = Avrt.AvSetMmThreadCharacteristicsW("Audio", ref idx);'
assert old in s
s = s.replace(old, 'task = Avrt.AvSetMmThreadCharacteristicsW(mmcssTask, ref idx);', 1)
old = '''mmcssResult = task == IntPtr.Zero ? ("FAILED err=" + Marshal.GetLastWin32Error()) : "joined task 'Audio'";'''
assert old in s
s = s.replace(old, '''mmcssResult = task == IntPtr.Zero ? ("FAILED err=" + Marshal.GetLastWin32Error()) : ("joined task '" + mmcssTask + "'");''', 1)

anchor2 = "        // Process loopback behaviour: own PID"
stress = """        static int StressMode3(int seconds, int spinners)
        {
            Console.WriteLine("== Stress3 (MMCSS task comparison): {0} busy threads, first 600 ms excluded, {1}s per config ==", spinners, seconds);
            IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            IMMDevice dev; en.GetDefaultAudioEndpoint(0, 0, out dev); AssertRender(dev);
            object[][] cfgs = new object[][] {
                new object[] { "LOAD buffer=22ms  MMCSS-Pro Audio", 0L, "Pro Audio" },
                new object[] { "LOAD buffer=22ms  MMCSS-Capture", 0L, "Capture" },
                new object[] { "LOAD buffer=1s    MMCSS-Pro Audio", 10000000L, "Pro Audio" },
                new object[] { "LOAD buffer=1s    no-MMCSS (repeat)", 10000000L, null },
            };
            foreach (object[] c in cfgs)
            {
                string label = (string)c[0]; long buf = (long)c[1]; string task = (string)c[2];
                Stopwatch sw = Stopwatch.StartNew(); CaptureRun cr = new CaptureRun(); cr.mmcss = task != null; if (task != null) cr.mmcssTask = task; cr.Open(dev, buf, true, sw);
                List<Thread> spin = new List<Thread>(); SpinStart(spinners, spin);
                cr.StartThread(); double t0 = sw.Elapsed.TotalMilliseconds; Thread.Sleep(seconds * 1000); double t1 = sw.Elapsed.TotalMilliseconds;
                cr.Stop(); SpinStop(spin);
                double cut = t0 + 600; int disc = 0; long frames = 0; Pkt prev = null; long lost = 0;
                foreach (Pkt p in cr.pkts)
                {
                    if (p.tMs < cut) { prev = p; continue; }
                    frames += p.frames;
                    if (prev != null && (p.flags & 1) != 0) { disc++; long jump = (long)p.qpc100 - (long)prev.qpc100 - (long)(prev.frames * 10000000.0 / cr.fmt.rate); if (jump > 0) lost += jump; }
                    prev = p;
                }
                List<double> gaps = new List<double>(); for (int i = 0; i < cr.loopGaps.Count; i++) if (cr.loopTimes[i] >= cut) gaps.Add(cr.loopGaps[i]);
                gaps.Sort(); double p99 = gaps.Count > 0 ? gaps[(int)(gaps.Count * 0.99)] : 0; double mx = gaps.Count > 0 ? gaps[gaps.Count - 1] : 0;
                Console.WriteLine("  [{0}] mmcss={1} coverage={2:P2} DISCONTINUITY={3} lostByQpc~{4:F0}ms | loop gap ms p99={5:F1} max={6:F1}", label, task == null ? "off" : cr.mmcssResult, frames / (cr.fmt.rate * ((t1 - cut) / 1000.0)), disc, lost / 10000.0, p99, mx);
                Marshal.ReleaseComObject(cr.cap); Marshal.ReleaseComObject(cr.client);
                Thread.Sleep(800);
            }
            return 0;
        }

"""
s = s.replace(anchor2, stress + anchor2, 1)
key = 'if (args.Length > 0 && args[0] == "stress2")'
assert key in s
s = s.replace(key, 'if (args.Length > 0 && args[0] == "stress3") return StressMode3(args.Length > 1 ? int.Parse(args[1]) : 8, args.Length > 2 ? int.Parse(args[2]) : 4);\n            ' + key, 1)
io.open(p, "w", encoding="utf-8").write(s)
print("patched stress3 OK")
