import io
p = r"<scratch>\probe\src\Probe.cs"
s = io.open(p, encoding="utf-8").read()
anchor2 = "        // Process loopback behaviour: own PID"
assert anchor2 in s
age = """        static int AgeMode(int seconds)
        {
            Console.WriteLine("== Packet age: (read time) - (packet QPC start) and - (packet QPC end), event-driven loopback, buffer=1s ==");
            IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            IMMDevice dev; en.GetDefaultAudioEndpoint(0, 0, out dev); AssertRender(dev);
            Stopwatch sw = Stopwatch.StartNew(); CaptureRun cr = new CaptureRun(); cr.Open(dev, 10000000, true, sw);
            cr.StartThread(); double t0 = sw.Elapsed.TotalMilliseconds; Thread.Sleep(seconds * 1000); cr.Stop();
            List<double> ageStart = new List<double>(), ageEnd = new List<double>(); int firstFlag = 0;
            foreach (Pkt p in cr.pkts)
            {
                if (p.tMs < t0 + 500) continue;
                double a0 = (p.wall100 - (long)p.qpc100) / 10000.0; double dur = p.frames * 1000.0 / cr.fmt.rate; ageStart.Add(a0); ageEnd.Add(a0 - dur);
            }
            ageStart.Sort(); ageEnd.Sort();
            Func<List<double>, double, double> pct = delegate (List<double> l, double q) { return l.Count == 0 ? 0 : l[Math.Min(l.Count - 1, (int)(l.Count * q))]; };
            Console.WriteLine("  n={0}  age of packet START (ms): min={1:F1} p50={2:F1} p99={3:F1} max={4:F1}", ageStart.Count, ageStart[0], pct(ageStart, 0.5), pct(ageStart, 0.99), ageStart[ageStart.Count - 1]);
            Console.WriteLine("  n={0}  age of packet END   (ms): min={1:F1} p50={2:F1} p99={3:F1} max={4:F1}", ageEnd.Count, ageEnd[0], pct(ageEnd, 0.5), pct(ageEnd, 0.99), ageEnd[ageEnd.Count - 1]);
            if (cr.pkts.Count > 0) Console.WriteLine("  first packet arrived {0:F0} ms after Start() returned; first packet flags={1} devPos={2}", cr.pkts[0].tMs - t0, cr.pkts[0].flags, cr.pkts[0].devPos);
            return 0;
        }

"""
s = s.replace(anchor2, age + anchor2, 1)
key = 'if (args.Length > 0 && args[0] == "stress3")'
assert key in s
s = s.replace(key, 'if (args.Length > 0 && args[0] == "age") return AgeMode(args.Length > 1 ? int.Parse(args[1]) : 6);\n            ' + key, 1)
io.open(p, "w", encoding="utf-8").write(s)
print("patched age OK")
