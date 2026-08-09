using System.Runtime.InteropServices;

namespace AiDesk.App.Services;

/// <summary>
/// 系统音量控制（Windows Core Audio COM：IAudioEndpointVolume）。
/// </summary>
public static class CoreAudioVolume
{
    // ---- COM 接口定义 ----

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDevice ppDevices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, out IAudioEndpointVolume ppInterface);
        // 其余方法不需要
        int OpenPropertyStore(int stgmAccess, out IntPtr ppProperties);
        int GetId(out string? ppstrId);
        int GetState(out int pdwState);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr pNotify);
        int UnregisterControlChangeNotify(IntPtr pNotify);
        int GetChannelCount(out int pnChannelCount);
        int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        int GetMasterVolumeLevel(out float pfLevelDB);
        int GetMasterVolumeLevelScalar(out float pfLevel);
        int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
        int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
        int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        int SetMute(bool bMute, ref Guid pguidEventContext);
        int GetMute(out bool pbMute);
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator
    {
    }

    private const int EDataFlowRender = 0;   // 输出设备
    private const int ERoleMultimedia = 1;   // 多媒体角色
    private const int CLSCTX_INPROC_SERVER = 1;
    private static readonly Guid IID_IAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    /// <summary>读取当前音量（0-100），失败返回 -1。</summary>
    public static double GetVolume()
    {
        try
        {
            using var volume = GetEndpointVolume();
            if (volume is null)
                return -1;
            volume.GetMasterVolumeLevelScalar(out var level);
            return Math.Round(level * 100);
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>设置音量（0-100）。</summary>
    public static bool SetVolume(double percent)
    {
        try
        {
            using var volume = GetEndpointVolume();
            if (volume is null)
                return false;
            var level = (float)Math.Clamp(percent / 100.0, 0, 1);
            var guid = Guid.Empty;
            return volume.SetMasterVolumeLevelScalar(level, ref guid) == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>静音切换，返回当前是否静音（失败返回 false）。</summary>
    public static bool ToggleMute()
    {
        try
        {
            using var volume = GetEndpointVolume();
            if (volume is null)
                return false;
            volume.GetMute(out var muted);
            var guid = Guid.Empty;
            volume.SetMute(!muted, ref guid);
            return !muted;
        }
        catch
        {
            return false;
        }
    }

    private static ComVolume? GetEndpointVolume()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            if (enumerator.GetDefaultAudioEndpoint(EDataFlowRender, ERoleMultimedia, out var device) != 0)
                return null;
            var iid = IID_IAudioEndpointVolume;
            if (device.Activate(ref iid, CLSCTX_INPROC_SERVER, IntPtr.Zero, out var volume) != 0)
            {
                Marshal.ReleaseComObject(device);
                return null;
            }
            return new ComVolume(volume, device);
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    /// <summary>COM 包装：释放 IAudioEndpointVolume 与 IMMDevice 两个 RCW。</summary>
    private sealed class ComVolume : IDisposable
    {
        private readonly IAudioEndpointVolume _volume;
        private readonly IMMDevice _device;

        public ComVolume(IAudioEndpointVolume volume, IMMDevice device)
        {
            _volume = volume;
            _device = device;
        }

        public int GetMasterVolumeLevelScalar(out float level) => _volume.GetMasterVolumeLevelScalar(out level);
        public int SetMasterVolumeLevelScalar(float level, ref Guid ctx) => _volume.SetMasterVolumeLevelScalar(level, ref ctx);
        public int GetMute(out bool muted) => _volume.GetMute(out muted);
        public int SetMute(bool mute, ref Guid ctx) => _volume.SetMute(mute, ref ctx);

        public void Dispose()
        {
            Marshal.ReleaseComObject(_volume);
            Marshal.ReleaseComObject(_device);
        }
    }
}
