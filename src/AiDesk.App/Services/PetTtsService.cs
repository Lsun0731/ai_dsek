using System.Diagnostics;
using System.IO;
using System.Speech.Synthesis;
using System.Windows.Media;
using AiDesk.Core.Diagnostics;

namespace AiDesk.App.Services;

/// <summary>
/// 语音朗读服务：优先 edge-tts（微软 Edge 同款免费神经网络语音，多中文音色），
/// 不可用时回退 SAPI 系统语音。音色通过 AppConfig.AI.Voice 配置。
/// </summary>
public static class PetTtsService
{
    /// <summary>可选音色列表（Id 写入配置；edge-tts 神经网络语音，SAPI 离线兜底）。</summary>
    public static readonly (string Id, string Name)[] Voices =
    {
        ("edge:zh-CN-XiaoxiaoNeural", "晓晓 · 女声 温柔"),
        ("edge:zh-CN-XiaoyiNeural", "晓伊 · 女声 活泼"),
        ("edge:zh-CN-XiaochenNeural", "晓辰 · 女声 新闻"),
        ("edge:zh-CN-XiaoshuangNeural", "晓双 · 女声 儿童"),
        ("edge:zh-CN-liaoning-XiaobeiNeural", "晓北 · 女声 东北"),
        ("edge:zh-CN-shaanxi-XiaoniNeural", "晓妮 · 女声 陕西"),
        ("edge:zh-CN-YunxiNeural", "云希 · 男声 阳光"),
        ("edge:zh-CN-YunjianNeural", "云健 · 男声 浑厚"),
        ("edge:zh-CN-YunyangNeural", "云扬 · 男声 播音"),
        ("edge:zh-CN-YunxiaNeural", "云夏 · 男声 少年"),
        ("edge:zh-CN-henan-YundengNeural", "云登 · 男声 河南"),
        ("edge:zh-TW-HsiaoChenNeural", "曉臻 · 女声 台湾"),
        ("edge:zh-TW-YunJheNeural", "雲哲 · 男声 台湾"),
        ("edge:zh-HK-HiuGaaiNeural", "曉佳 · 女声 粤语"),
        ("edge:zh-HK-WanLungNeural", "雲龍 · 男声 粤语"),
        ("edge:en-US-AriaNeural", "Aria · 英语 女声"),
        ("sapi:zh-CN", "系统语音 · 离线"),
    };

    private static MediaPlayer? _player;
    private static SpeechSynthesizer? _synth;
    private static int _speakSeq;

    /// <summary>
    /// 朗读文本（异步，连续调用取消上一次）。返回 true 表示 edge-tts 成功，false 表示回退系统语音或失败。
    /// </summary>
    public static async Task<bool> SpeakAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var voice = AppConfig.Load().AI.Voice;
        if (voice.StartsWith("edge:"))
        {
            if (await TrySpeakEdgeAsync(text, voice))
                return true;
            // edge 不可用（无 python/断网/失败）→ 回退系统语音
        }
        SpeakSapi(text);
        return false;
    }

    /// <summary>edge-tts 生成并播放；成功返回 true。</summary>
    private static async Task<bool> TrySpeakEdgeAsync(string text, string voiceId)
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "AiDeskTts");
        Directory.CreateDirectory(tmpDir);
        var seq = Interlocked.Increment(ref _speakSeq);
        var textFile = Path.Combine(tmpDir, $"input_{seq}.txt");
        var mediaFile = Path.Combine(tmpDir, $"tts_{Environment.ProcessId}_{seq}.mp3");

        // 清理本进程早前的临时 mp3（MediaPlayer 已 Close 的旧文件可删；正在播放的被锁自动跳过）
        try
        {
            foreach (var stale in Directory.EnumerateFiles(tmpDir, $"tts_{Environment.ProcessId}_*.mp3"))
            {
                try { File.Delete(stale); } catch { /* 播放中，跳过 */ }
            }
        }
        catch
        {
            // 忽略清理失败
        }

        try
        {
            // 文本写入文件避免命令行转义问题
            await File.WriteAllTextAsync(textFile, text);
            var psi = new ProcessStartInfo("python",
                $"-m edge_tts --voice {voiceId} --file \"{textFile}\" --write-media \"{mediaFile}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(psi);
            if (process is null)
                return false;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await process.WaitForExitAsync(cts.Token);
            if (process.ExitCode != 0 || !File.Exists(mediaFile))
            {
                // 失败分支清理本次残留的 mp3（生成中被打断可能留下半文件）
                try { if (File.Exists(mediaFile)) File.Delete(mediaFile); } catch { /* 忽略 */ }
                Telemetry.Function("Pet.TtsEdge", false, 0, $"exit={process.ExitCode}");
                return false;
            }

            // 播放（停止上一次）
            _player?.Close();
            _player = new MediaPlayer();
            _player.Open(new Uri(mediaFile));
            _player.Play();
            Telemetry.Function("Pet.TtsEdge", true, 0, $"voice={voiceId} len={text.Length}");
            return true;
        }
        catch (Exception ex)
        {
            Telemetry.Error("Pet.TtsEdge", ex);
            return false;
        }
        finally
        {
            // 播放中删除文件会失败，留到下次生成时覆盖/系统清理
            try
            {
                if (File.Exists(textFile))
                    File.Delete(textFile);
            }
            catch
            {
                // 忽略
            }
        }
    }

    /// <summary>SAPI 系统语音（离线兜底，中文优先）。</summary>
    private static void SpeakSapi(string text)
    {
        try
        {
            _synth ??= CreateChineseSynth();
            _synth.SpeakAsyncCancelAll();
            _synth.SpeakAsync(text);
        }
        catch (Exception ex)
        {
            Telemetry.Error("Pet.TtsSapi", ex);
        }
    }

    private static SpeechSynthesizer CreateChineseSynth()
    {
        var synth = new SpeechSynthesizer();
        try
        {
            var zh = synth.GetInstalledVoices()
                .FirstOrDefault(v => v.VoiceInfo.Culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
            if (zh is not null)
                synth.SelectVoice(zh.VoiceInfo.Name);
        }
        catch
        {
            // 无中文语音则用默认
        }
        return synth;
    }

    /// <summary>释放资源（应用退出时调用）。</summary>
    public static void Shutdown()
    {
        try
        {
            _player?.Close();
            _player = null;
            _synth?.SpeakAsyncCancelAll();
            _synth?.Dispose();
            _synth = null;
        }
        catch
        {
            // 忽略
        }
    }
}
