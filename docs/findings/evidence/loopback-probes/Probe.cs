// Probe.cs -- WASAPI loopback semantics probe. RENDER ENDPOINTS ONLY.
// Safety: every endpoint is checked with IMMEndpoint.GetDataFlow == eRender (0) before Activate;
// no eCapture / eAll enumeration and no GetDefaultAudioEndpoint(eCapture) call exists in this file.
// C# 5 compatible (csc.exe from .NET Framework 4.x).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace LoopProbe
{
    [StructLayout(LayoutKind.Sequential)]
    struct PROPERTYKEY { public Guid fmtid; public uint pid; }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr ptr;
        [FieldOffset(8)] public int cbSize;
        [FieldOffset(16)] public IntPtr blobData;
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

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int pcDevices);
        [PreserveSig] int Item(int nDevice, out IMMDevice ppDevice);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        [PreserveSig] int OpenPropertyStore(int stgmAccess, out IPropertyStore ppProperties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        [PreserveSig] int GetState(out int pdwState);
    }

    [ComImport, Guid("1BE09788-6894-4089-8586-9A2A6C265AC5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMEndpoint
    {
        [PreserveSig] int GetDataFlow(out int pDataFlow);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int cProps);
        [PreserveSig] int GetAt(int iProp, out PROPERTYKEY pkey);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
        [PreserveSig] int Commit();
    }

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
        [PreserveSig] int ReleaseBuffer(uint numFramesRead);
        [PreserveSig] int GetNextPacketSize(out uint pNumFramesInNextPacket);
    }

    [ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioRenderClient
    {
        [PreserveSig] int GetBuffer(uint numFramesRequested, out IntPtr ppData);
        [PreserveSig] int ReleaseBuffer(uint numFramesWritten, uint dwFlags);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float pfPeak);
    }

    // Only GetMasterVolumeLevelScalar / GetMute are ever called (read-only). Order matters for the vtable.
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr p);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr p);
        [PreserveSig] int GetChannelCount(out uint n);
        [PreserveSig] int SetMasterVolumeLevel(float f, ref Guid ctx);
        [PreserveSig] int SetMasterVolumeLevelScalar(float f, ref Guid ctx);
        [PreserveSig] int GetMasterVolumeLevel(out float f);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float f);
        [PreserveSig] int SetChannelVolumeLevel(uint ch, float f, ref Guid ctx);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint ch, float f, ref Guid ctx);
        [PreserveSig] int GetChannelVolumeLevel(uint ch, out float f);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint ch, out float f);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool b, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool b);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr g, uint flags, out IntPtr p);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr g, uint flags, out IntPtr p);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator e);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int n);
        [PreserveSig] int GetSession(int i, out IAudioSessionControl2 c);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        // IAudioSessionControl part (order matters)
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string s);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string s, ref Guid ctx);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string s);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string s, ref Guid ctx);
        [PreserveSig] int GetGroupingParam(out Guid g);
        [PreserveSig] int SetGroupingParam(ref Guid g, ref Guid ctx);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr p);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr p);
        // IAudioSessionControl2 part
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string s);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string s);
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool b);
    }

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IActivateAudioInterfaceAsyncOperation
    {
        [PreserveSig] int GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
    }

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IActivateAudioInterfaceCompletionHandler
    {
        [PreserveSig] int ActivateCompleted(IActivateAudioInterfaceAsyncOperation op);
    }

    [ComImport, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAgileObject { }

    class ActivationHandler : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        public ManualResetEvent done = new ManualResetEvent(false);
        public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation op) { done.Set(); return 0; }
    }

    static class MmDevApi
    {
        [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
        public static extern void ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath, ref Guid riid, IntPtr activationParams, IActivateAudioInterfaceCompletionHandler completionHandler, out IActivateAudioInterfaceAsyncOperation activationOperation);
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorComObject { }

    static class Avrt
    {
        [DllImport("avrt.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr AvSetMmThreadCharacteristicsW(string task, ref uint idx);
        [DllImport("avrt.dll", SetLastError = true)] public static extern bool AvRevertMmThreadCharacteristics(IntPtr h);
    }

    static class Native
    {
        [DllImport("kernel32.dll")] public static extern bool QueryPerformanceCounter(out long v);
        [DllImport("kernel32.dll")] public static extern bool QueryPerformanceFrequency(out long v);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateEventW(IntPtr attrs, bool manual, bool initial, string name);
        [DllImport("kernel32.dll")] public static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("ole32.dll")] public static extern int PropVariantClear(ref PROPVARIANT pv);
        [DllImport("ole32.dll")] public static extern void CoTaskMemFree(IntPtr p);
    }

    class Pkt
    {
        public double tMs;         // stopwatch ms at read
        public uint frames;
        public uint flags;
        public ulong devPos;
        public ulong qpc100;       // packet qpc position, 100 ns units
        public long wall100;       // QPC at read, 100 ns units
        public float peak;         // peak abs of data (float32/int16)
        public double sumSq; public int nSamp;
        public bool signaled;      // read was triggered by event signal (vs timeout/poll)
    }

    class WaitRec { public double tMs; public bool signaled; }

    class Fmt
    {
        public int tag, ch, rate, bits, validBits, blockAlign; public uint mask; public Guid sub; public bool ext;
        public override string ToString()
        {
            return string.Format("tag=0x{0:X} ch={1} rate={2} bits={3} valid={4} blockAlign={5} mask=0x{6:X} sub={7}", tag, ch, rate, bits, validBits, blockAlign, mask, ext ? sub.ToString() : "-");
        }
        public static Fmt Parse(IntPtr p)
        {
            Fmt f = new Fmt();
            f.tag = (ushort)Marshal.ReadInt16(p, 0);
            f.ch = Marshal.ReadInt16(p, 2);
            f.rate = Marshal.ReadInt32(p, 4);
            f.blockAlign = Marshal.ReadInt16(p, 12);
            f.bits = Marshal.ReadInt16(p, 14);
            int cb = Marshal.ReadInt16(p, 16);
            f.validBits = f.bits;
            if (f.tag == 0xFFFE && cb >= 22)
            {
                f.ext = true;
                f.validBits = Marshal.ReadInt16(p, 18);
                f.mask = (uint)Marshal.ReadInt32(p, 20);
                byte[] g = new byte[16];
                Marshal.Copy(IntPtr.Add(p, 24), g, 0, 16);
                f.sub = new Guid(g);
            }
            return f;
        }
        public bool IsFloat32 { get { return bits == 32 && ((tag == 3) || (ext && sub == new Guid("00000003-0000-0010-8000-00aa00389b71"))); } }
    }

    class CaptureRun
    {
        public IAudioClient client;
        public IAudioCaptureClient cap;
        public IntPtr evt = IntPtr.Zero;
        public Fmt fmt;
        public uint bufferFrames;
        public long bufferHnsRequested;
        public bool useEvent;
        public List<Pkt> pkts = new List<Pkt>(8192);
        public List<WaitRec> waits = new List<WaitRec>(8192);
        public volatile bool stop;
        public Thread th;
        public Stopwatch sw;
        public long qpcFreq;
        public int lastHr;
        public bool mmcss; public string mmcssTask = "Audio"; public string mmcssResult; public double maxLoopGapMs; public List<double> loopGaps = new List<double>(8192); public List<double> loopTimes = new List<double>(8192);
        public string error;
        public int initHr;
        public long defPeriod, minPeriod;

        public void Open(IMMDevice dev, long bufferHns, bool evtMode, Stopwatch stopwatch)
        {
            sw = stopwatch; useEvent = evtMode; bufferHnsRequested = bufferHns;
            Native.QueryPerformanceFrequency(out qpcFreq);
            Guid iidClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
            object o; int hr = dev.Activate(ref iidClient, 23, IntPtr.Zero, out o);
            if (hr != 0) throw new Exception("Activate IAudioClient hr=0x" + hr.ToString("X8"));
            client = (IAudioClient)o;
            IntPtr pf; hr = client.GetMixFormat(out pf);
            if (hr != 0) throw new Exception("GetMixFormat hr=0x" + hr.ToString("X8"));
            fmt = Fmt.Parse(pf);
            client.GetDevicePeriod(out defPeriod, out minPeriod);
            const int LOOPBACK = 0x00020000, EVENTCALLBACK = 0x00040000;
            int flags = LOOPBACK | (evtMode ? EVENTCALLBACK : 0);
            // AUDCLNT_SHAREMODE_SHARED = 0
            initHr = client.Initialize(0, flags, bufferHns, 0, pf, IntPtr.Zero);
            Native.CoTaskMemFree(pf);
            if (initHr != 0) throw new Exception("Initialize(loopback" + (evtMode ? "+event" : "") + ", buf=" + bufferHns + ") hr=0x" + initHr.ToString("X8"));
            client.GetBufferSize(out bufferFrames);
            if (evtMode)
            {
                evt = Native.CreateEventW(IntPtr.Zero, false, false, null);
                hr = client.SetEventHandle(evt);
                if (hr != 0) throw new Exception("SetEventHandle hr=0x" + hr.ToString("X8"));
            }
            Guid iidCap = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
            hr = client.GetService(ref iidCap, out o);
            if (hr != 0) throw new Exception("GetService(IAudioCaptureClient) hr=0x" + hr.ToString("X8"));
            cap = (IAudioCaptureClient)o;
        }

        // Endpoint loopback with a caller-supplied PCM format (tests AUTOCONVERTPCM on loopback).
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

        // Endpoint loopback with WAVEFORMATEXTENSIBLE PCM (exactly the layout wasapi::WaveFormat::new() produces).
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

        // Process loopback (VAD\Process_Loopback). includeTree: true=INCLUDE_TARGET_PROCESS_TREE(0), false=EXCLUDE(1).
        public void OpenProcess(uint pid, bool includeTree, Stopwatch stopwatch)
        {
            sw = stopwatch; useEvent = true; bufferHnsRequested = 0;
            Native.QueryPerformanceFrequency(out qpcFreq);
            IntPtr blob = Marshal.AllocHGlobal(12);
            Marshal.WriteInt32(blob, 0, 1);                       // AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK
            Marshal.WriteInt32(blob, 4, (int)pid);                // TargetProcessId
            Marshal.WriteInt32(blob, 8, includeTree ? 0 : 1);     // PROCESS_LOOPBACK_MODE
            IntPtr pv = Marshal.AllocHGlobal(24);
            for (int i = 0; i < 24; i++) Marshal.WriteByte(pv, i, 0);
            Marshal.WriteInt16(pv, 0, 65);                        // VT_BLOB
            Marshal.WriteInt32(pv, 8, 12);                        // cbSize
            Marshal.WriteIntPtr(pv, 16, blob);                    // pBlobData
            ActivationHandler h = new ActivationHandler();
            Guid iid = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
            IActivateAudioInterfaceAsyncOperation op;
            MmDevApi.ActivateAudioInterfaceAsync("VAD\\Process_Loopback", ref iid, pv, h, out op);
            if (!h.done.WaitOne(5000)) throw new Exception("process-loopback activation timed out");
            int actHr; object o; int hr = op.GetActivateResult(out actHr, out o);
            if (hr != 0 || actHr != 0) throw new Exception("GetActivateResult hr=0x" + hr.ToString("X8") + " activate=0x" + actHr.ToString("X8"));
            client = (IAudioClient)o;
            // GetMixFormat is not implemented on this client (Not implemented), so ask for float32 48k stereo ourselves.
            IntPtr pf = Marshal.AllocHGlobal(18);
            Marshal.WriteInt16(pf, 0, 3); Marshal.WriteInt16(pf, 2, 2); Marshal.WriteInt32(pf, 4, 48000); Marshal.WriteInt32(pf, 8, 48000 * 8);
            Marshal.WriteInt16(pf, 12, 8); Marshal.WriteInt16(pf, 14, 32); Marshal.WriteInt16(pf, 16, 0);
            fmt = Fmt.Parse(pf);
            const int LOOPBACK = 0x00020000, EVENTCALLBACK = 0x00040000; uint AUTOCONVERTPCM = 0x80000000;
            initHr = client.Initialize(0, unchecked((int)(LOOPBACK | EVENTCALLBACK | AUTOCONVERTPCM)), 0, 0, pf, IntPtr.Zero);
            if (initHr != 0) throw new Exception("process-loopback Initialize hr=0x" + initHr.ToString("X8"));
            client.GetBufferSize(out bufferFrames);
            evt = Native.CreateEventW(IntPtr.Zero, false, false, null);
            hr = client.SetEventHandle(evt);
            if (hr != 0) throw new Exception("SetEventHandle hr=0x" + hr.ToString("X8"));
            Guid iidCap = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
            hr = client.GetService(ref iidCap, out o);
            if (hr != 0) throw new Exception("GetService hr=0x" + hr.ToString("X8"));
            cap = (IAudioCaptureClient)o;
        }

        public void StartThread()
        {
            int hr = client.Start();
            if (hr != 0) throw new Exception("Start hr=0x" + hr.ToString("X8"));
            th = new Thread(Loop);
            th.IsBackground = true;
            th.Start();
        }

        void Loop()
        {
            IntPtr task = IntPtr.Zero;
            try
            {
                if (mmcss)
                {
                    uint idx = 0; task = Avrt.AvSetMmThreadCharacteristicsW(mmcssTask, ref idx);
                    mmcssResult = task == IntPtr.Zero ? ("FAILED err=" + Marshal.GetLastWin32Error()) : ("joined task '" + mmcssTask + "'");
                }
                double lastIter = sw.Elapsed.TotalMilliseconds;
                while (!stop)
                {
                    double nowIter = sw.Elapsed.TotalMilliseconds; double g = nowIter - lastIter; lastIter = nowIter; loopGaps.Add(g); loopTimes.Add(nowIter); if (g > maxLoopGapMs) maxLoopGapMs = g;
                    bool signaled = false;
                    if (useEvent)
                    {
                        uint w = Native.WaitForSingleObject(evt, 20);
                        signaled = (w == 0);
                        WaitRec wr = new WaitRec(); wr.tMs = sw.Elapsed.TotalMilliseconds; wr.signaled = signaled; waits.Add(wr);
                    }
                    else
                    {
                        Thread.Sleep(10);
                    }
                    Drain(signaled);
                }
            }
            catch (Exception ex) { error = ex.ToString(); }
            finally { if (task != IntPtr.Zero) Avrt.AvRevertMmThreadCharacteristics(task); }
        }

        void Drain(bool signaled)
        {
            while (true)
            {
                uint n;
                int hr = cap.GetNextPacketSize(out n);
                lastHr = hr;
                if (hr != 0) { error = "GetNextPacketSize hr=0x" + hr.ToString("X8"); stop = true; return; }
                if (n == 0) return;
                IntPtr data; uint frames, flags; ulong dpos, qpc;
                hr = cap.GetBuffer(out data, out frames, out flags, out dpos, out qpc);
                lastHr = hr;
                if (hr != 0 && hr != 0x08890001) { error = "GetBuffer hr=0x" + hr.ToString("X8"); stop = true; return; }
                if (frames == 0) return;
                Pkt p = new Pkt();
                p.tMs = sw.Elapsed.TotalMilliseconds; p.frames = frames; p.flags = flags; p.devPos = dpos; p.qpc100 = qpc;
                long w; Native.QueryPerformanceCounter(out w);
                p.wall100 = (long)((double)w * 10000000.0 / qpcFreq);
                p.signaled = signaled;
                if (fmt.IsFloat32 && data != IntPtr.Zero)
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
                }
                pkts.Add(p);
                cap.ReleaseBuffer(frames);
            }
        }

        public void Stop()
        {
            stop = true;
            if (th != null) th.Join(2000);
            if (client != null) client.Stop();
            if (evt != IntPtr.Zero) Native.CloseHandle(evt);
        }
    }

    // Render client used only to create deterministic render activity on the default render endpoint.
    class RenderRun
    {
        public IAudioClient client; public IAudioRenderClient rc; public Fmt fmt; public uint bufFrames;
        public volatile int mode;   // 0 idle(no write), 1 silent-flag zeros, 2 zeros no flag, 3 quiet sine
        public volatile bool stop; public Thread th; public double phase; public float sineAmp = 0.01f;

        public void Open(IMMDevice dev)
        {
            Guid iid = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
            object o; int hr = dev.Activate(ref iid, 23, IntPtr.Zero, out o);
            if (hr != 0) throw new Exception("render Activate hr=0x" + hr.ToString("X8"));
            client = (IAudioClient)o;
            IntPtr pf; client.GetMixFormat(out pf);
            fmt = Fmt.Parse(pf);
            hr = client.Initialize(0, 0, 1000000, 0, pf, IntPtr.Zero);   // 100 ms shared, polling
            Native.CoTaskMemFree(pf);
            if (hr != 0) throw new Exception("render Initialize hr=0x" + hr.ToString("X8"));
            client.GetBufferSize(out bufFrames);
            Guid iidR = new Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");
            hr = client.GetService(ref iidR, out o);
            if (hr != 0) throw new Exception("render GetService hr=0x" + hr.ToString("X8"));
            rc = (IAudioRenderClient)o;
        }

        public void Start()
        {
            Pump();  // prefill
            int hr = client.Start();
            if (hr != 0) throw new Exception("render Start hr=0x" + hr.ToString("X8"));
            th = new Thread(delegate () { while (!stop) { Pump(); Thread.Sleep(10); } });
            th.IsBackground = true; th.Start();
        }

        void Pump()
        {
            if (mode == 0) return;
            uint pad; if (client.GetCurrentPadding(out pad) != 0) return;
            uint n = bufFrames - pad; if (n == 0) return;
            IntPtr p; if (rc.GetBuffer(n, out p) != 0) return;
            uint flags = 0;
            int count = (int)n * fmt.ch;
            float[] tmp = new float[count];
            if (mode == 1) flags = 2;        // AUDCLNT_BUFFERFLAGS_SILENT
            else if (mode == 3 && fmt.IsFloat32)
            {
                double step = 2.0 * Math.PI * 440.0 / fmt.rate;
                for (int i = 0; i < n; i++)
                {
                    float v = (float)(Math.Sin(phase) * sineAmp); phase += step; if (phase > 2.0 * Math.PI) phase -= 2.0 * Math.PI;
                    for (int c = 0; c < fmt.ch; c++) tmp[i * fmt.ch + c] = v;
                }
            }
            if (fmt.IsFloat32) Marshal.Copy(tmp, 0, p, count);
            else { byte[] z = new byte[n * fmt.blockAlign]; Marshal.Copy(z, 0, p, z.Length); }
            rc.ReleaseBuffer(n, flags);
        }

        public void Stop() { stop = true; if (th != null) th.Join(1000); if (client != null) client.Stop(); }
    }

    static class Program
    {
        static string FriendlyName(IMMDevice d)
        {
            IPropertyStore ps; int hr0 = d.OpenPropertyStore(0, out ps); if (hr0 != 0) return "?(open hr=" + Hex(hr0) + ")";
            PROPERTYKEY k = new PROPERTYKEY(); k.fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"); k.pid = 14;
            PROPVARIANT pv; string s = "?";
            int hr1 = ps.GetValue(ref k, out pv);
            if (hr1 == 0) { if (pv.vt == 31 && pv.ptr != IntPtr.Zero) s = Marshal.PtrToStringUni(pv.ptr); else s = "?(vt=" + pv.vt + ")"; Native.PropVariantClear(ref pv); }
            else s = "?(getvalue hr=" + Hex(hr1) + ")";
            return s;
        }

        static int DevicesMode()
        {
            IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            IMMDeviceCollection col; en.EnumAudioEndpoints(0 /*eRender only*/, 0xF, out col);
            int cnt; col.GetCount(out cnt);
            IMMDevice dC, dM, dCom; en.GetDefaultAudioEndpoint(0, 0, out dC); en.GetDefaultAudioEndpoint(0, 1, out dM); en.GetDefaultAudioEndpoint(0, 2, out dCom);
            string iC, iM, iCom; dC.GetId(out iC); dM.GetId(out iM); dCom.GetId(out iCom);
            for (int i = 0; i < cnt; i++)
            {
                IMMDevice d; col.Item(i, out d); string id; d.GetId(out id); int st; d.GetState(out st);
                Console.WriteLine("#{0} state=0x{1:X} console={2} multimedia={3} comms={4} name='{5}' id={6}", i, st, id == iC, id == iM, id == iCom, FriendlyName(d), id);
            }
            return 0;
        }

        static string DeviceFormatString(IMMDevice d)
        {
            IPropertyStore ps; if (d.OpenPropertyStore(0, out ps) != 0) return "?";
            PROPERTYKEY k = new PROPERTYKEY(); k.fmtid = new Guid("f19f064d-082c-4e27-bc73-6882a1bb8e4c"); k.pid = 0;
            PROPVARIANT pv; string s = "?";
            if (ps.GetValue(ref k, out pv) == 0) { if (pv.vt == 65 && pv.blobData != IntPtr.Zero) s = Fmt.Parse(pv.blobData).ToString(); Native.PropVariantClear(ref pv); }
            return s;
        }

        // The structural guard: only eRender endpoints may be activated.
        static void AssertRender(IMMDevice d)
        {
            IMMEndpoint ep = (IMMEndpoint)d;
            int flow; int hr = ep.GetDataFlow(out flow);
            if (hr != 0 || flow != 0) throw new InvalidOperationException("REFUSING: endpoint data flow is not eRender (flow=" + flow + ", hr=0x" + hr.ToString("X8") + ")");
        }

        static string Hex(int v) { return "0x" + v.ToString("X8"); }

        static void Summarize(string label, CaptureRun c, double t0, double t1, long qpcFreq)
        {
            int n = 0, silent = 0, disc = 0, tserr = 0, sigWaits = 0, toWaits = 0, signaledReads = 0;
            long frames = 0; float peakNonSilent = 0, peakSilentData = 0;
            foreach (Pkt p in c.pkts)
            {
                if (p.tMs < t0 || p.tMs >= t1) continue;
                n++; frames += p.frames;
                bool s = (p.flags & 2) != 0;
                if (s) { silent++; if (p.peak > peakSilentData) peakSilentData = p.peak; } else if (p.peak > peakNonSilent) peakNonSilent = p.peak;
                if ((p.flags & 1) != 0) disc++;
                if ((p.flags & 4) != 0) tserr++;
                if (p.signaled) signaledReads++;
            }
            foreach (WaitRec w in c.waits) { if (w.tMs < t0 || w.tMs >= t1) continue; if (w.signaled) sigWaits++; else toWaits++; }
            double span = (t1 - t0) / 1000.0;
            double cover = frames / (c.fmt.rate * span);
            Console.WriteLine("  [{0}] window={1:F1}s packets={2} frames={3} coverage={4:P1} silentFlagged={5} discontinuity={6} tsError={7} eventSignaled={8} eventTimeout={9} peakNonSilentData={10:F4} peakDataInSilentPkts={11:F4}",
                label, span, n, frames, cover, silent, disc, tserr, sigWaits, toWaits, peakNonSilent, peakSilentData);
        }

        static void Gaps(string label, CaptureRun c, double t0, double t1)
        {
            Pkt prev = null; int shown = 0;
            foreach (Pkt p in c.pkts)
            {
                if (p.tMs < t0 || p.tMs >= t1) continue;
                if (prev != null)
                {
                    long expectDev = (long)prev.devPos + prev.frames;
                    long devJump = (long)p.devPos - expectDev;                       // frames
                    double expectQpcMs = prev.qpc100 / 10000.0 + prev.frames * 1000.0 / c.fmt.rate;
                    double qpcJumpMs = p.qpc100 / 10000.0 - expectQpcMs;             // ms
                    double wallGapMs = p.tMs - prev.tMs;
                    if (Math.Abs(devJump) > 0 || Math.Abs(qpcJumpMs) > 5.0 || wallGapMs > 60.0)
                    {
                        if (shown++ < 12)
                            Console.WriteLine("    gap@{0:F0}ms: wallGap={1:F1}ms devPosJump={2} frames ({3:F1} ms) qpcJump={4:F1}ms flags={5} prevFlags={6} frames={7}", p.tMs, wallGapMs, devJump, devJump * 1000.0 / c.fmt.rate, qpcJumpMs, p.flags, prev.flags, p.frames);
                    }
                }
                prev = p;
            }
            if (shown == 0) Console.WriteLine("    [{0}] no device-position / qpc jumps > thresholds", label);
        }

        // Wait until the default render endpoint is truly idle (meter == 0 for 2 s), then record loopback
        // in event mode and in polling mode and report what the engine delivers when NOTHING is rendering.
        static int IdleMode(int maxWaitSec)
        {
            Console.WriteLine("== LoopProbe IDLE mode (render endpoints only), maxWait=" + maxWaitSec + "s ==");
            IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            IMMDevice dev; en.GetDefaultAudioEndpoint(0, 0, out dev); AssertRender(dev);
            Console.WriteLine("endpoint: " + FriendlyName(dev));
            Guid iidMeter = new Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"); object ov; IAudioMeterInformation meter = null;
            if (dev.Activate(ref iidMeter, 23, IntPtr.Zero, out ov) == 0) meter = (IAudioMeterInformation)ov;
            if (meter == null) { Console.WriteLine("no meter"); return 2; }
            Stopwatch wait = Stopwatch.StartNew(); double quietSince = -1; bool idle = false; int polls = 0; float maxSeen = 0;
            while (wait.Elapsed.TotalSeconds < maxWaitSec)
            {
                float pk; meter.GetPeakValue(out pk); polls++; if (pk > maxSeen) maxSeen = pk;
                double now = wait.Elapsed.TotalSeconds;
                if (pk <= 0.0f) { if (quietSince < 0) quietSince = now; if (now - quietSince >= 2.0) { idle = true; break; } }
                else quietSince = -1;
                Thread.Sleep(100);
            }
            Console.WriteLine("waited {0:F1}s, polls={1}, maxMeterSeen={2:F4}, idleReached={3}", wait.Elapsed.TotalSeconds, polls, maxSeen, idle);
            if (!idle) { Console.WriteLine("RESULT: endpoint never became idle; cannot observe idle behaviour on this run."); return 3; }

            for (int cfg = 0; cfg < 2; cfg++)
            {
                bool evt = (cfg == 0);
                Console.WriteLine("\n=== IDLE capture: " + (evt ? "event-driven buffer=0" : "polling 10ms buffer=1s") + " ===");
                Stopwatch sw = Stopwatch.StartNew();
                CaptureRun cr = new CaptureRun(); cr.Open(dev, evt ? 0 : 10000000, evt, sw);
                cr.StartThread();
                double t0 = sw.Elapsed.TotalMilliseconds;
                float meterMax = 0; int meterPolls = 0;
                while (sw.Elapsed.TotalMilliseconds - t0 < 8000) { float pk; meter.GetPeakValue(out pk); meterPolls++; if (pk > meterMax) meterMax = pk; Thread.Sleep(50); }
                double t1 = sw.Elapsed.TotalMilliseconds;
                cr.Stop();
                Console.WriteLine("  endpoint meter max during window = {0:F6} (polls={1})", meterMax, meterPolls);
                Summarize("idle window", cr, t0, t1, cr.qpcFreq);
                Console.WriteLine("  wait records: total={0} (signaled+timeouts, 20ms timeout each)", cr.waits.Count);
                if (cr.pkts.Count > 0)
                {
                    Pkt f = cr.pkts[0]; Console.WriteLine("  first packet at +{0:F0}ms frames={1} flags={2} devPos={3}", f.tMs - t0, f.frames, f.flags, f.devPos);
                    int shown = 0; foreach (Pkt p in cr.pkts) { if (shown++ < 6) Console.WriteLine("    pkt +{0:F0}ms frames={1} flags={2} devPos={3} qpc={4} peak={5:F6}", p.tMs - t0, p.frames, p.flags, p.devPos, p.qpc100, p.peak); }
                }
                else Console.WriteLine("  NO PACKETS delivered at all while idle");
                Gaps("idle", cr, t0, t1);
                if (cr.error != null) Console.WriteLine("  ERROR: " + cr.error);
                Marshal.ReleaseComObject(cr.cap); Marshal.ReleaseComObject(cr.client);
            }
            return 0;
        }

        // Read-only: list audio sessions on the default render endpoint (who is producing sound).
        static int SessionsMode()
        {
            IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            IMMDevice dev; en.GetDefaultAudioEndpoint(0, 0, out dev); AssertRender(dev);
            Guid iid = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"); object o;
            int hr = dev.Activate(ref iid, 23, IntPtr.Zero, out o);
            if (hr != 0) { Console.WriteLine("Activate(IAudioSessionManager2) hr=" + Hex(hr)); return 1; }
            IAudioSessionManager2 mgr = (IAudioSessionManager2)o; IAudioSessionEnumerator se; mgr.GetSessionEnumerator(out se);
            int n; se.GetCount(out n); Console.WriteLine("sessions on default render endpoint: " + n);
            for (int i = 0; i < n; i++)
            {
                IAudioSessionControl2 c; if (se.GetSession(i, out c) != 0) continue;
                int st; c.GetState(out st); uint pid; c.GetProcessId(out pid);
                string pname = "?"; try { pname = Process.GetProcessById((int)pid).ProcessName; } catch { }
                Console.WriteLine("  session#{0} state={1} (0=inactive,1=active,2=expired) pid={2} process={3} systemSounds={4}", i, st, pid, pname, c.IsSystemSoundsSession() == 0);
            }
            return 0;
        }

        static int ConvMode()
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

        static int ExtMode()
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

        static volatile bool spinStop;
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

        static int StressMode2(int seconds, int spinners)
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

        static int StressMode3(int seconds, int spinners)
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

        static int AgeMode(int seconds)
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

        // Process loopback behaviour: own PID (silent target), Spotify tree INCLUDE, Spotify tree EXCLUDE.
        static int ProcMode()
        {
            Console.WriteLine("== Process loopback probe (VAD\\Process_Loopback; never opens a capture endpoint) ==");
            uint myPid = (uint)Process.GetCurrentProcess().Id;
            uint spPid = 0;
            // pick the process that owns the active audio session on the default render endpoint
            IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            IMMDevice dev; en.GetDefaultAudioEndpoint(0, 0, out dev); AssertRender(dev);
            Guid iid = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"); object o;
            if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) == 0)
            {
                IAudioSessionEnumerator se; ((IAudioSessionManager2)o).GetSessionEnumerator(out se);
                int n; se.GetCount(out n);
                for (int i = 0; i < n; i++)
                {
                    IAudioSessionControl2 c; if (se.GetSession(i, out c) != 0) continue;
                    int st; c.GetState(out st); uint pid; c.GetProcessId(out pid);
                    if (st == 1 && pid != 0) { spPid = pid; }
                }
            }
            Console.WriteLine("own pid={0}, active-session pid={1}", myPid, spPid);
            object[][] runs = new object[][] {
                new object[] { "INCLUDE own pid (silent target)", myPid, true },
                new object[] { "INCLUDE active-session pid tree", spPid, true },
                new object[] { "EXCLUDE active-session pid tree (system mix minus that app)", spPid, false },
            };
            foreach (object[] r in runs)
            {
                string label = (string)r[0]; uint pid = (uint)r[1]; bool inc = (bool)r[2];
                if (pid == 0) { Console.WriteLine("[" + label + "] skipped (no pid)"); continue; }
                Console.WriteLine("\n=== " + label + " ===");
                Stopwatch sw = Stopwatch.StartNew(); CaptureRun cr = new CaptureRun();
                try { cr.OpenProcess(pid, inc, sw); }
                catch (Exception ex) { Console.WriteLine("  open failed: " + ex.Message); continue; }
                Console.WriteLine("  OK bufferFrames=" + cr.bufferFrames + " fmt: " + cr.fmt);
                cr.StartThread(); double t0 = sw.Elapsed.TotalMilliseconds; Thread.Sleep(5000); double t1 = sw.Elapsed.TotalMilliseconds; cr.Stop();
                Summarize("window", cr, t0, t1, cr.qpcFreq);
                if (cr.pkts.Count > 0)
                {
                    int shown = 0; foreach (Pkt p in cr.pkts) { if (shown++ < 4) Console.WriteLine("    pkt +{0:F0}ms frames={1} flags={2} devPos={3} peak={4:F6}", p.tMs - t0, p.frames, p.flags, p.devPos, p.peak); }
                }
                else Console.WriteLine("  NO PACKETS in 5 s");
                if (cr.error != null) Console.WriteLine("  ERROR: " + cr.error);
                Gaps("proc", cr, t0, t1);
            }
            return 0;
        }

        [MTAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "age") return AgeMode(args.Length > 1 ? int.Parse(args[1]) : 6);
            if (args.Length > 0 && args[0] == "stress3") return StressMode3(args.Length > 1 ? int.Parse(args[1]) : 8, args.Length > 2 ? int.Parse(args[2]) : 4);
            if (args.Length > 0 && args[0] == "stress2") return StressMode2(args.Length > 1 ? int.Parse(args[1]) : 8, args.Length > 2 ? int.Parse(args[2]) : 4);
            if (args.Length > 0 && args[0] == "stress") return StressMode(args.Length > 1 ? int.Parse(args[1]) : 6);
            if (args.Length > 0 && args[0] == "ext") return ExtMode();
            if (args.Length > 0 && args[0] == "conv") return ConvMode();
            if (args.Length > 0 && args[0] == "proc") return ProcMode();
            if (args.Length > 0 && args[0] == "devices") return DevicesMode();
            if (args.Length > 0 && args[0] == "sessions") return SessionsMode();
            if (args.Length > 0 && args[0] == "idle") return IdleMode(args.Length > 1 ? int.Parse(args[1]) : 60);
            Console.WriteLine("== LoopProbe (render endpoints only) ==");
            Console.WriteLine("OS: " + Environment.OSVersion + "  64bit=" + Environment.Is64BitProcess);
            IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();

            // 1) Enumerate eRender endpoints in ALL states (0xF) -- eRender only, never eCapture/eAll.
            IMMDeviceCollection col; int hr = en.EnumAudioEndpoints(0 /*eRender*/, 0xF /*DEVICE_STATEMASK_ALL*/, out col);
            if (hr != 0) { Console.WriteLine("EnumAudioEndpoints failed " + Hex(hr)); return 1; }
            int cnt; col.GetCount(out cnt);
            IMMDevice defC, defM, defCom;
            en.GetDefaultAudioEndpoint(0, 0, out defC); en.GetDefaultAudioEndpoint(0, 1, out defM); en.GetDefaultAudioEndpoint(0, 2, out defCom);
            string idC, idM, idCom; defC.GetId(out idC); defM.GetId(out idM); defCom.GetId(out idCom);
            Console.WriteLine("eRender endpoints (all states): " + cnt);
            for (int i = 0; i < cnt; i++)
            {
                IMMDevice d; col.Item(i, out d);
                string id; d.GetId(out id); int st; d.GetState(out st);
                IMMEndpoint ep = (IMMEndpoint)d; int flow; ep.GetDataFlow(out flow);
                Console.WriteLine("  #{0} state=0x{1:X} flow={2} console={3} multimedia={4} comms={5}\n      name='{6}'\n      id={7}", i, st, flow, id == idC, id == idM, id == idCom, FriendlyName(d), id);
            }

            // 2) Default render endpoint (eConsole) -> mix format vs device format, volume/mute.
            IMMDevice dev = defC; AssertRender(dev);
            string devId; dev.GetId(out devId);
            Console.WriteLine("\nUsing default eRender/eConsole endpoint: " + FriendlyName(dev));
            Console.WriteLine("  DeviceFormat (PKEY_AudioEngine_DeviceFormat): " + DeviceFormatString(dev));
            Guid iidEv = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A"); object ov;
            if (dev.Activate(ref iidEv, 23, IntPtr.Zero, out ov) == 0)
            {
                IAudioEndpointVolume ev = (IAudioEndpointVolume)ov; float mv; bool mute;
                ev.GetMasterVolumeLevelScalar(out mv); ev.GetMute(out mute);
                Console.WriteLine("  endpoint master volume (scalar)={0:F3} mute={1}", mv, mute);
            }
            Guid iidMeter = new Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"); IAudioMeterInformation meter = null;
            if (dev.Activate(ref iidMeter, 23, IntPtr.Zero, out ov) == 0) meter = (IAudioMeterInformation)ov;
            float mp0 = 0; if (meter != null) meter.GetPeakValue(out mp0);
            Console.WriteLine("  endpoint meter peak right now (other apps' output) = {0:F4}", mp0);

            // 3) Buffer-size table for loopback+event init (no streaming).
            Console.WriteLine("\n-- Initialize(loopback|eventcallback) buffer-duration table --");
            long[] durs = new long[] { 0, 100000, 200000, 500000, 2000000, 10000000 };
            foreach (long dur in durs)
            {
                CaptureRun t = new CaptureRun();
                try
                {
                    t.Open(dev, dur, true, Stopwatch.StartNew());
                    Console.WriteLine("  hnsBufferDuration={0,9} -> OK  GetBufferSize={1} frames ({2:F1} ms)  mix: {3}  defPeriod={4:F2}ms minPeriod={5:F2}ms",
                        dur, t.bufferFrames, t.bufferFrames * 1000.0 / t.fmt.rate, t.fmt.ToString(), t.defPeriod / 10000.0, t.minPeriod / 10000.0);
                }
                catch (Exception ex) { Console.WriteLine("  hnsBufferDuration={0,9} -> FAIL {1}", dur, ex.Message); }
                finally { try { if (t.client != null) { Marshal.ReleaseComObject(t.client); } if (t.evt != IntPtr.Zero) Native.CloseHandle(t.evt); } catch { } }
            }

            // 4) Scenario run: idle -> render SILENT-flag zeros -> render zeros (no flag) -> quiet sine -> render stopped.
            string[] cfgNames = new string[] { "event-driven, buffer=0 (engine default)", "event-driven, buffer=1s", "polling 10ms, buffer=1s" };
            for (int cfg = 0; cfg < 3; cfg++)
            {
                Console.WriteLine("\n=== SCENARIO RUN: " + cfgNames[cfg] + " ===");
                Stopwatch sw = Stopwatch.StartNew();
                CaptureRun cr = new CaptureRun();
                cr.Open(dev, cfg == 0 ? 0 : 10000000, cfg != 2, sw);
                Console.WriteLine("  loopback init OK; bufferFrames=" + cr.bufferFrames + " mix: " + cr.fmt);
                long qf = cr.qpcFreq;
                float meterBefore = 0; if (meter != null) meter.GetPeakValue(out meterBefore);
                cr.StartThread();
                double tA0 = sw.Elapsed.TotalMilliseconds;
                Thread.Sleep(4000);                                         // Phase A: probe renders nothing
                double tA1 = sw.Elapsed.TotalMilliseconds;
                float meterA = 0; if (meter != null) meter.GetPeakValue(out meterA);

                RenderRun rr = new RenderRun(); rr.Open(dev); rr.mode = 1; rr.Start();
                double tB0 = sw.Elapsed.TotalMilliseconds; Thread.Sleep(3000); double tB1 = sw.Elapsed.TotalMilliseconds;   // B: silent-flag zeros
                rr.mode = 2; double tC0 = tB1; Thread.Sleep(3000); double tC1 = sw.Elapsed.TotalMilliseconds;               // C: zeros no flag
                rr.mode = 3; double tD0 = tC1; Thread.Sleep(600); double tD1 = sw.Elapsed.TotalMilliseconds;                  // D: quiet sine
                float meterD = 0; if (meter != null) meter.GetPeakValue(out meterD);
                rr.mode = 0; rr.Stop(); Marshal.ReleaseComObject(rr.rc); Marshal.ReleaseComObject(rr.client); rr = null; GC.Collect(); GC.WaitForPendingFinalizers();
                double tE0 = sw.Elapsed.TotalMilliseconds; Thread.Sleep(4000); double tE1 = sw.Elapsed.TotalMilliseconds;     // E: render stream closed
                cr.Stop();

                Console.WriteLine("  meter(before)={0:F4} meter(after phase A)={1:F4} meter(end of sine)={2:F4}", meterBefore, meterA, meterD);
                Summarize("A idle (probe renders nothing)", cr, tA0, tA1, qf);
                Summarize("B render zeros FLAGGED SILENT ", cr, tB0, tB1, qf);
                Summarize("C render zeros (no flag)      ", cr, tC0, tC1, qf);
                Summarize("D render quiet 440Hz sine     ", cr, tD0, tD1, qf);
                Summarize("E render stream closed        ", cr, tE0, tE1, qf);
                if (cr.error != null) Console.WriteLine("  CAPTURE THREAD ERROR: " + cr.error);
                // Transition detail: first/last packet timing around phase boundaries
                Console.WriteLine("  -- gap analysis (devPos/QPC continuity) over whole run --");
                Gaps("whole", cr, tA0, tE1);
                // Sine peak vs written amplitude (pre/post volume hint)
                float pkD = 0; foreach (Pkt p in cr.pkts) { if (p.tMs >= tD0 && p.tMs < tE0 + 300 && p.peak > pkD) pkD = p.peak; }
                Console.WriteLine("  sine written amp=0.0100, loopback peak seen={0:F4}", pkD);
                // time to first packet after Start and last packet time after render closed
                if (cr.pkts.Count > 0) Console.WriteLine("  first packet at +{0:F0} ms from run start; last packet at +{1:F0} ms", cr.pkts[0].tMs, cr.pkts[cr.pkts.Count - 1].tMs);
                double lastPktInE = -1; foreach (Pkt p in cr.pkts) { if (p.tMs >= tE0) lastPktInE = p.tMs - tE0; }
                Console.WriteLine("  after render stream closed, last packet arrived +{0:F0} ms (-1 = none)", lastPktInE);
                Marshal.ReleaseComObject(cr.cap); Marshal.ReleaseComObject(cr.client);
            }
            Console.WriteLine("\n== done ==");
            return 0;
        }
    }
}
