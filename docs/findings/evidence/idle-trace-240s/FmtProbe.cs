// Scratchpad-only, READ-ONLY: shared-mode LOOPBACK|EVENTCALLBACK|AUTOCONVERTPCM acceptance test on the default RENDER
// endpoint (eRender/eConsole hard-coded). Checks which 16 kHz mono formats/channel masks Initialize accepts.
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace FmtProbe
{
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IntPtr ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
    }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCo { }
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
    interface IAudioMeterInformation { [PreserveSig] int GetPeakValue(out float pfPeak); }

    public static class F
    {
        [DllImport("kernel32.dll")] static extern IntPtr CreateEventW(IntPtr a, bool manual, bool initial, string name);
        [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        public static string Run()
        {
            string r = null; Exception err = null;
            Thread t = new Thread(new ThreadStart(delegate () { try { r = Inner(); } catch (Exception ex) { err = ex; } }));
            t.SetApartmentState(ApartmentState.MTA); t.Start(); t.Join();
            return err != null ? "EXC " + err : r;
        }

        static IntPtr MakeFmt(bool extensible, int rate, int ch, int bits, uint mask)
        {
            int size = extensible ? 40 : 18;
            IntPtr p = Marshal.AllocCoTaskMem(size);
            for (int i = 0; i < size; i++) Marshal.WriteByte(p, i, 0);
            Marshal.WriteInt16(p, 0, extensible ? unchecked((short)0xFFFE) : (short)1);
            Marshal.WriteInt16(p, 2, (short)ch);
            Marshal.WriteInt32(p, 4, rate);
            int align = ch * bits / 8;
            Marshal.WriteInt32(p, 8, rate * align);
            Marshal.WriteInt16(p, 12, (short)align);
            Marshal.WriteInt16(p, 14, (short)bits);
            Marshal.WriteInt16(p, 16, (short)(extensible ? 22 : 0));
            if (extensible)
            {
                Marshal.WriteInt16(p, 18, (short)bits);
                Marshal.WriteInt32(p, 20, unchecked((int)mask));
                byte[] g = new Guid("00000001-0000-0010-8000-00aa00389b71").ToByteArray();
                Marshal.Copy(g, 0, new IntPtr(p.ToInt64() + 24), 16);
            }
            return p;
        }

        static string One(IMMDevice dev, string label, IntPtr fmt, int rate, int ch, int bits, bool autoconv)
        {
            Guid iidClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
            Guid iidCapture = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
            object o; dev.Activate(ref iidClient, 23, IntPtr.Zero, out o); IAudioClient ac = (IAudioClient)o;
            int flags = 0x00020000 | 0x00040000 | (autoconv ? (unchecked((int)0x80000000) | 0x08000000) : 0);
            int hr = ac.Initialize(0, flags, 10000000, 0, fmt, IntPtr.Zero);
            if (hr != 0) { Marshal.ReleaseComObject(ac); return String.Format("{0,-46} Initialize -> 0x{1:X8}", label, hr); }
            uint bf; ac.GetBufferSize(out bf);
            IntPtr evt = CreateEventW(IntPtr.Zero, false, false, null); ac.SetEventHandle(evt);
            object oc; ac.GetService(ref iidCapture, out oc); IAudioCaptureClient cc = (IAudioCaptureClient)oc;
            ac.Start();
            int packets = 0; long frames = 0; uint first = 0; Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 1000)
            {
                WaitForSingleObject(evt, 20);
                uint n; cc.GetNextPacketSize(out n);
                while (n > 0)
                {
                    IntPtr d; uint nf, fl; ulong dp, q; cc.GetBuffer(out d, out nf, out fl, out dp, out q); cc.ReleaseBuffer(nf);
                    packets++; frames += nf; if (first == 0) first = nf; cc.GetNextPacketSize(out n);
                }
            }
            ac.Stop(); CloseHandle(evt); Marshal.ReleaseComObject(cc); Marshal.ReleaseComObject(ac);
            return String.Format("{0,-46} Initialize OK, buffer={1} frames; 1 s -> {2} packets, {3} frames (expect {4}), first packet {5} frames", label, bf, packets, frames, rate, first);
        }

        static string Inner()
        {
            StringBuilder sb = new StringBuilder();
            IMMDeviceEnumerator en = (IMMDeviceEnumerator)(new MMDeviceEnumeratorCo());
            IMMDevice dev; int hr = en.GetDefaultAudioEndpoint(0 /*eRender ONLY*/, 0, out dev);
            if (hr != 0) return "no default render " + hr.ToString("X8");
            sb.AppendLine(One(dev, "16k mono s16 EXTENSIBLE mask=0x0 +AUTOCONVERT", MakeFmt(true, 16000, 1, 16, 0), 16000, 1, 16, true));
            sb.AppendLine(One(dev, "16k mono s16 EXTENSIBLE mask=0x1 +AUTOCONVERT", MakeFmt(true, 16000, 1, 16, 1), 16000, 1, 16, true));
            sb.AppendLine(One(dev, "16k mono s16 EXTENSIBLE mask=0x4 (FC) +AUTOCONVERT", MakeFmt(true, 16000, 1, 16, 4), 16000, 1, 16, true));
            sb.AppendLine(One(dev, "16k mono s16 plain WAVEFORMATEX +AUTOCONVERT", MakeFmt(false, 16000, 1, 16, 0), 16000, 1, 16, true));
            sb.AppendLine(One(dev, "16k mono s16 EXTENSIBLE mask=0x0, NO autoconvert", MakeFmt(true, 16000, 1, 16, 0), 16000, 1, 16, false));
            sb.AppendLine(One(dev, "48k stereo s16 EXTENSIBLE mask=0x3, NO autoconvert", MakeFmt(true, 48000, 2, 16, 3), 48000, 2, 16, false));
            return sb.ToString();
        }
    }
}
