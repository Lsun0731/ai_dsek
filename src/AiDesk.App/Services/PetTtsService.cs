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
    /// <summary>音色选项（按语言分组展示；edge-tts 神经网络语音，SAPI 离线兜底）。</summary>
    public sealed record VoiceOption(string Language, string Id, string Name);

    /// <summary>可选音色列表（Id 写入配置；全部 edge-tts 免费可用，无 key）。</summary>
    public static readonly VoiceOption[] Voices =
    {
        // ---- 中文 ----
        new("中文", "edge:zh-CN-XiaoxiaoNeural", "晓晓 · 女 温柔"),
        new("中文", "edge:zh-CN-XiaoyiNeural", "晓伊 · 女 活泼"),
        new("中文", "edge:zh-CN-XiaochenNeural", "晓辰 · 女 新闻"),
        new("中文", "edge:zh-CN-XiaoshuangNeural", "晓双 · 女 儿童"),
        new("中文", "edge:zh-CN-XiaohanNeural", "晓涵 · 女 温暖"),
        new("中文", "edge:zh-CN-XiaomengNeural", "晓萌 · 女 可爱"),
        new("中文", "edge:zh-CN-XiaomoNeural", "晓墨 · 女 解说"),
        new("中文", "edge:zh-CN-XiaoruiNeural", "晓睿 · 女 成熟"),
        new("中文", "edge:zh-CN-XiaoxuanNeural", "晓萱 · 女 知性"),
        new("中文", "edge:zh-CN-XiaoyouNeural", "晓悠 · 女 柔和"),
        new("中文", "edge:zh-CN-liaoning-XiaobeiNeural", "晓北 · 女 东北"),
        new("中文", "edge:zh-CN-shaanxi-XiaoniNeural", "晓妮 · 女 陕西"),
        new("中文", "edge:zh-CN-YunxiNeural", "云希 · 男 阳光"),
        new("中文", "edge:zh-CN-YunjianNeural", "云健 · 男 浑厚"),
        new("中文", "edge:zh-CN-YunyangNeural", "云扬 · 男 播音"),
        new("中文", "edge:zh-CN-YunxiaNeural", "云夏 · 男 少年"),
        new("中文", "edge:zh-CN-YunfengNeural", "云枫 · 男 沉稳"),
        new("中文", "edge:zh-CN-YunhaoNeural", "云皓 · 男 磁性"),
        new("中文", "edge:zh-CN-YunzeNeural", "云泽 · 男 青年"),
        new("中文", "edge:zh-CN-YunyeNeural", "云野 · 男 阳光"),
        new("中文", "edge:zh-CN-henan-YundengNeural", "云登 · 男 河南"),
        // ---- 台湾 ----
        new("台湾", "edge:zh-TW-HsiaoChenNeural", "曉臻 · 女"),
        new("台湾", "edge:zh-TW-HsiaoYuNeural", "曉雨 · 女 溫暖"),
        new("台湾", "edge:zh-TW-YunJheNeural", "雲哲 · 男"),
        // ---- 粤语 ----
        new("粤语", "edge:zh-HK-HiuGaaiNeural", "曉佳 · 女"),
        new("粤语", "edge:zh-HK-WanLungNeural", "雲龍 · 男"),
        new("粤语", "edge:zh-HK-HiuMaanNeural", "曉曼 · 女"),
        // ---- 英语 ----
        new("英语", "edge:en-US-AriaNeural", "Aria · 美 女"),
        new("英语", "edge:en-US-JennyNeural", "Jenny · 美 女"),
        new("英语", "edge:en-US-GuyNeural", "Guy · 美 男"),
        new("英语", "edge:en-US-ChristopherNeural", "Christopher · 美 男"),
        new("英语", "edge:en-GB-SoniaNeural", "Sonia · 英 女"),
        new("英语", "edge:en-GB-RyanNeural", "Ryan · 英 男"),
        new("英语", "edge:en-AU-NatashaNeural", "Natasha · 澳 女"),
        // ---- 日语 ----
        new("日语", "edge:ja-JP-NanamiNeural", "奈奈美 · 女"),
        new("日语", "edge:ja-JP-KeitaNeural", "启太 · 男"),
        // ---- 韩语 ----
        new("韩语", "edge:ko-KR-SunHiNeural", "선희 · 女"),
        new("韩语", "edge:ko-KR-InJoonNeural", "인준 · 男"),
        // ---- 法语 ----
        new("法语", "edge:fr-FR-DeniseNeural", "Denise · 女"),
        new("法语", "edge:fr-FR-HenriNeural", "Henri · 男"),
        // ---- 德语 ----
        new("德语", "edge:de-DE-KatjaNeural", "Katja · 女"),
        new("德语", "edge:de-DE-ConradNeural", "Conrad · 男"),
        // ---- 西班牙语 ----
        new("西班牙语", "edge:es-ES-ElviraNeural", "Elvira · 女"),
        new("西班牙语", "edge:es-MX-DaliaNeural", "Dalia · 墨 女"),
        // ---- 俄语 ----
        new("俄语", "edge:ru-RU-SvetlanaNeural", "Svetlana · 女"),
        // ---- 系统 ----
        new("系统", "sapi:zh-CN", "系统语音 · 离线"),
    };

    private static MediaPlayer? _player;
    private static SpeechSynthesizer? _synth;
    private static int _speakSeq;
    private static TaskCompletionSource<bool>? _interrupt;

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
        await SpeakSapiAsync(text);
        return false;
    }

    /// <summary>等待播放结束（MediaEnded / 失败 / 超时 / 打断）。</summary>
    private static Task WaitPlaybackEndAsync(MediaPlayer player, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _interrupt = tcs; // 注册为当前播放的打断信号
        EventHandler? ended = null;
        EventHandler<ExceptionEventArgs>? failed = null;
        ended = (_, _) =>
        {
            player.MediaEnded -= ended;
            player.MediaFailed -= failed;
            tcs.TrySetResult(true);
        };
        failed = (_, _) =>
        {
            player.MediaEnded -= ended;
            player.MediaFailed -= failed;
            tcs.TrySetResult(true);
        };
        player.MediaEnded += ended;
        player.MediaFailed += failed;
        return Task.WhenAny(tcs.Task, Task.Delay(timeout));
    }

    /// <summary>打断当前朗读（停止播放/合成并立即结束等待）。</summary>
    public static void Stop()
    {
        try { _player?.Stop(); _player?.Close(); } catch { /* 忽略 */ }
        try { _synth?.SpeakAsyncCancelAll(); } catch { /* 忽略 */ }
        _interrupt?.TrySetResult(true);
    }

    /// <summary>edge-tts 生成并播放，播放完成后返回；成功返回 true。</summary>
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

            // 播放（停止上一次），并等待播放完成（调用方据此恢复语音监听，防回声自触发）
            _player?.Close();
            _player = new MediaPlayer();
            _player.Open(new Uri(mediaFile));
            _player.Play();
            Telemetry.Function("Pet.TtsEdge", true, 0, $"voice={voiceId} len={text.Length}");
            await WaitPlaybackEndAsync(_player, TimeSpan.FromSeconds(60));
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

    /// <summary>SAPI 系统语音（离线兜底，中文优先）；等待朗读完成（取消也会触发 SpeakCompleted）。</summary>
    private static async Task SpeakSapiAsync(string text)
    {
        try
        {
            _synth ??= CreateChineseSynth();
            _synth.SpeakAsyncCancelAll();
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _interrupt = tcs; // 注册为当前朗读的打断信号
            EventHandler<SpeakCompletedEventArgs>? handler = null;
            handler = (_, _) =>
            {
                if (_synth is not null)
                    _synth.SpeakCompleted -= handler;
                tcs.TrySetResult(true);
            };
            _synth.SpeakCompleted += handler;
            _synth.SpeakAsync(text);
            await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(120)));
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
