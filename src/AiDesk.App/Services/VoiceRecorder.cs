using System.IO;
using NAudio.Wave;

namespace AiDesk.App.Services;

/// <summary>
/// 麦克风录音（16kHz 单声道 16bit，whisper 标准输入格式）。
/// 静音超过 <see cref="SilenceTimeout"/> 自动停止并保存 wav，通过 <see cref="Completed"/> 回调文件路径。
/// 也可手动 Stop（比如用户打断）。
/// </summary>
public sealed class VoiceRecorder : IDisposable
{
    public static readonly TimeSpan SilenceTimeout = TimeSpan.FromSeconds(1.2);
    private static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(30);

    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _outputPath;
    private DateTime _lastSoundAt;
    private System.Threading.Timer? _silenceTimer;
    private bool _stopped;

    /// <summary>录音完成（自动静音/手动停止）后触发，参数为 wav 文件路径；失败传 null。</summary>
    public event Action<string?>? Completed;

    public bool IsRecording => _waveIn is not null && !_stopped;

    /// <summary>开始录音；输出到指定路径（默认临时目录随机名）。</summary>
    public void Start(string? outputPath = null)
    {
        if (IsRecording)
            return;
        _stopped = false;
        _outputPath = outputPath ?? Path.Combine(Path.GetTempPath(), $"AiDeskMic_{Guid.NewGuid():N}.wav");
        _lastSoundAt = DateTime.UtcNow;

        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16000, 16, 1),
            BufferMilliseconds = 100,
        };
        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.RecordingStopped += OnRecordingStopped;
        _waveIn.StartRecording();

        // 静音检测：定期检查最后有声时间，超时自动停止
        _silenceTimer = new System.Threading.Timer(_ =>
        {
            if (_stopped)
                return;
            if (DateTime.UtcNow - _lastSoundAt > SilenceTimeout ||
                DateTime.UtcNow - _lastSoundAt > MaxDuration)
                Stop();
        }, null, 500, 500);
    }

    /// <summary>停止录音并保存文件（若已停止则忽略）。</summary>
    public void Stop()
    {
        if (_stopped)
            return;
        _stopped = true;
        try
        {
            _waveIn?.StopRecording();
        }
        catch
        {
            // StopRecording 可能因设备问题抛异常，走强制关闭
            Finish(null);
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_stopped || _writer is null && _outputPath is not null)
        {
            // 首次数据时创建文件写入器
            _writer ??= new WaveFileWriter(_outputPath!, _waveIn!.WaveFormat);
        }
        _writer?.Write(e.Buffer, 0, e.BytesRecorded);

        // 能量检测（RMS 近似）：静音门限
        var sum = 0L;
        for (var i = 0; i < e.BytesRecorded; i += 2)
        {
            var sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
            sum += sample * sample;
        }
        var rms = Math.Sqrt(sum / (double)(e.BytesRecorded / 2 + 1));
        if (rms > 800)
            _lastSoundAt = DateTime.UtcNow;
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        var path = _outputPath;
        var ok = e.Exception is null && path is not null;
        if (ok)
        {
            try
            {
                _writer?.Dispose();
                _writer = null;
                // wav 头由 WaveFileWriter 写出，直接可用
                if (new FileInfo(path!).Length < 1000) // 太短 = 没录到内容
                    ok = false;
            }
            catch
            {
                ok = false;
            }
        }
        Finish(ok ? path : null);
    }

    private void Finish(string? path)
    {
        try
        {
            _silenceTimer?.Dispose();
            _silenceTimer = null;
            if (_writer is not null)
            {
                _writer.Dispose();
                _writer = null;
            }
            _waveIn?.Dispose();
            _waveIn = null;
        }
        catch
        {
            // 忽略
        }
        _stopped = true;
        if (path is null && _outputPath is not null)
        {
            try { File.Delete(_outputPath); } catch { /* 忽略 */ }
        }
        Completed?.Invoke(path);
    }

    public void Dispose() => Finish(null);
}
