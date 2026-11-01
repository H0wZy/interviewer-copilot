import io
p = r"<scratch>\probe\src\Probe.cs"
s = io.open(p, encoding="utf-8").read()

# record the timestamp of each loop iteration too
old = "public List<double> loopGaps = new List<double>(8192);"
assert old in s
s = s.replace(old, "public List<double> loopGaps = new List<double>(8192); public List<double> loopTimes = new List<double>(8192);", 1)
old = "loopGaps.Add(g); if (g > maxLoopGapMs) maxLoopGapMs = g;"
assert old in s
s = s.replace(old, "loopGaps.Add(g); loopTimes.Add(nowIter); if (g > maxLoopGapMs) maxLoopGapMs = g;", 1)

# new StressMode2
anchor2 = "        // Process loopback behaviour: own PID"
assert anchor2 in s
stress = """        static int StressMode2(int seconds, int spinners)
        {
            Console.WriteLine("== Stress2: {0} busy threads, first 600 ms excluded, {1}s per config ==", spinners, seconds);
            IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            IMMDevice dev; en.GetDefaultAudioEndpoint(0, 0, out dev); AssertRender(dev);
            object[][] cfgs = new object[][] {
                new object[] { "LOAD buffer=22ms  no-MMCSS", 0L, false },
                new object[] { "LOAD buffer=22ms  MMCSS-Audio", 0L, true },
                new object[] { "LOAD buffer=200ms no-MMCSS", 2000000L, false },
                new object[] { "LOAD buffer=1s    no-MMCSS", 10000000L, false },
            };
            foreach (object[] c in cfgs)
            {
                string label = (string)c[0]; long buf = (long)c[1]; bool mm = (bool)c[2];
                Stopwatch sw = Stopwatch.StartNew(); CaptureRun cr = new CaptureRun(); cr.mmcss = mm; cr.Open(dev, buf, true, sw);
                List<Thread> spin = new List<Thread>(); SpinStart(spinners, spin);
                cr.StartThread(); double t0 = sw.Elapsed.TotalMilliseconds; Thread.Sleep(seconds * 1000); double t1 = sw.Elapsed.TotalMilliseconds;
                cr.Stop(); SpinStop(spin);
                double cut = t0 + 600;
                int disc = 0, n = 0; long frames = 0; List<string> discAt = new List<string>();
                Pkt prev = null; long lostFrames = 0;
                foreach (Pkt p in cr.pkts)
                {
                    if (p.tMs < cut) { prev = p; continue; }
                    n++; frames += p.frames;
                    if (prev != null && (p.flags & 1) != 0) { disc++; long jump = (long)p.qpc100 - (long)prev.qpc100 - (long)(prev.frames * 10000000.0 / cr.fmt.rate); lostFrames += Math.Max(0, (long)(jump * cr.fmt.rate / 10000000.0)); discAt.Add(string.Format("+{0:F0}ms(qpcJump={1:F1}ms)", p.tMs - t0, jump / 10000.0)); }
                    prev = p;
                }
                List<double> gaps = new List<double>(); for (int i = 0; i < cr.loopGaps.Count; i++) if (cr.loopTimes[i] >= cut) gaps.Add(cr.loopGaps[i]);
                gaps.Sort(); double p99 = gaps.Count > 0 ? gaps[(int)(gaps.Count * 0.99)] : 0; double p50 = gaps.Count > 0 ? gaps[gaps.Count / 2] : 0; double mx = gaps.Count > 0 ? gaps[gaps.Count - 1] : 0;
                double span = (t1 - cut) / 1000.0;
                Console.WriteLine("  [{0}] bufFrames={1} mmcss={2} coverage={3:P2} DISCONTINUITY={4} lostByQpc~{5}ms {6} | loop gap ms p50={7:F1} p99={8:F1} max={9:F1}", label, cr.bufferFrames, mm ? cr.mmcssResult : "off", frames / (cr.fmt.rate * span), disc, lostFrames * 1000 / cr.fmt.rate, string.Join(" ", discAt.ToArray()), p50, p99, mx);
                Marshal.ReleaseComObject(cr.cap); Marshal.ReleaseComObject(cr.client);
                Thread.Sleep(800);
            }
            return 0;
        }

"""
s = s.replace(anchor2, stress + anchor2, 1)
key = 'if (args.Length > 0 && args[0] == "stress") return StressMode(args.Length > 1 ? int.Parse(args[1]) : 6);'
assert key in s
s = s.replace(key, 'if (args.Length > 0 && args[0] == "stress2") return StressMode2(args.Length > 1 ? int.Parse(args[1]) : 8, args.Length > 2 ? int.Parse(args[2]) : 4);\n            ' + key)
io.open(p, "w", encoding="utf-8").write(s)
print("patched stress2 OK")
