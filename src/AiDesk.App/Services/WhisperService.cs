using System.Diagnostics;
using System.IO;
using AiDesk.Core.Diagnostics;

namespace AiDesk.App.Services;

/// <summary>
/// 本地语音识别（faster-whisper，离线高准确率）。
/// 常驻 python 进程：stdin 收 wav 路径 → stdout 返回识别文本（模型只加载一次，后续秒回）。
/// 不可用（python/包/模型缺失）时由调用方回退 SAPI。
/// </summary>
public sealed class WhisperService : IDisposable
{
    private static readonly string ModelDir = Path.Combine(AppConfig.DataDirectory, "models", "whisper-small");
    private static readonly string WorkerScript = Path.Combine(AppConfig.DataDirectory, "whisper_worker.py");

    private Process? _proc;
    private StreamWriter? _writer;
    private StreamReader? _reader;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _available;

    /// <summary>可用性（python + faster_whisper + 模型齐全）。</summary>
    public bool IsAvailable => _available;

    /// <summary>检测环境：python 可执行、faster_whisper 已安装、模型文件存在。</summary>
    public bool CheckAvailability()
    {
        try
        {
            if (!File.Exists(Path.Combine(ModelDir, "model.bin")))
                return false;
            var psi = new ProcessStartInfo("python", "-c \"import faster_whisper\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            using var p = Process.Start(psi);
            if (p is null)
                return false;
            p.WaitForExit(15000);
            _available = p.HasExited && p.ExitCode == 0;
        }
        catch
        {
            _available = false;
        }
        return _available;
    }

    /// <summary>转写结果：文本 + 置信度（avg_logprob，0 附近最准，越负越不可信）。</summary>
    public sealed record WhisperResult(string? Text, float Confidence);

    /// <summary>转写 wav 音频为中文文本；失败返回 null。低置信由调用方判定（防杂音误发）。</summary>
    public async Task<WhisperResult?> TranscribeAsync(string wavPath, CancellationToken ct = default)
    {
        if (!_available)
            return null;
        await _gate.WaitAsync(ct);
        try
        {
            EnsureProcess();
            if (_writer is null || _reader is null)
                return null;

            await _writer.WriteLineAsync(wavPath);
            await _writer.FlushAsync(ct);

            var line = await _reader.ReadLineAsync(ct);
            if (line is null)
                return null;
            if (line.StartsWith("__ERR__", StringComparison.Ordinal))
            {
                Telemetry.Function("Whisper.Transcribe", false, 0, line);
                return null;
            }
            // 协议：文本 \t avg_logprob
            var tab = line.LastIndexOf('\t');
            var text = tab > 0 ? line[..tab] : line;
            var prob = tab > 0 && float.TryParse(line[(tab + 1)..], out var p) ? p : 0f;
            Telemetry.Function("Whisper.Transcribe", true, 0, $"len={text.Length} prob={prob:F2}");
            return new WhisperResult(text, prob);
        }
        catch (Exception ex)
        {
            Telemetry.Function("Whisper.Transcribe", false, 0, ex.Message);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>确保常驻 python 进程已启动（模型在进程内只加载一次）。</summary>
    private void EnsureProcess()
    {
        if (_proc is { HasExited: false })
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(WorkerScript)!);
            File.WriteAllText(WorkerScript, BuildWorkerScript());

            var psi = new ProcessStartInfo("python", $"\"{WorkerScript}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
            };
            _proc = Process.Start(psi);
            if (_proc is null)
                return;
            _writer = _proc.StandardInput;
            _reader = _proc.StandardOutput;
            // 丢弃 stderr（模型加载日志）
            _ = _proc.StandardError.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            Telemetry.Function("Whisper.Start", false, 0, ex.Message);
            _proc = null;
        }
    }

    /// <summary>生成 worker 脚本（模型路径内嵌）。</summary>
    private static string BuildWorkerScript()
    {
        var model = ModelDir.Replace("\\", "/");
        return $$"""
            # -*- coding: utf-8 -*-
            import sys
            from faster_whisper import WhisperModel
            model = WhisperModel(r"{{model}}", device="cpu", compute_type="int8")
            for line in sys.stdin:
                path = line.strip()
                if not path:
                    continue
                try:
                    segments, info = model.transcribe(path, language="zh", beam_size=1)
                    text = "".join(s.text for s in segments).strip()
                    prob = getattr(info, "avg_logprob", 0.0) or 0.0
                    print(text + "\t" + str(prob), flush=True)
                except Exception as e:
                    print("__ERR__" + str(e), flush=True)
            """;
    }

    public void Dispose()
    {
        try
        {
            _writer?.WriteLine("__EXIT__");
            _writer?.Flush();
        }
        catch
        {
            // 忽略
        }
        try
        {
            if (_proc is { HasExited: false })
            {
                _proc.Kill(entireProcessTree: true);
                _proc.Dispose();
            }
        }
        catch
        {
            // 忽略
        }
        _gate.Dispose();
    }
}
