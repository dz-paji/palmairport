using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;
#endif

namespace IslandAirport
{
    public enum VideoEncodingState { Starting, Recording, Finishing, Completed, Failed, Cancelled }

    /// <summary>Silent RGB24 → H.264 MP4. Poll from Unity's main thread; encoding runs on one worker.</summary>
    public sealed class SilentVideoEncoder : IDisposable
    {
        public const int MaxFrames = 1800;
        public const long MaxFileBytes = 128L * 1024 * 1024;
        private const int QueueCapacity = 3;
        private readonly object gate = new object();
        private readonly Queue<Frame> frames = new Queue<Frame>();
        private readonly Thread worker;
        private readonly int width, height, fps;
        private readonly string outputPath, temporaryPath;
        private VideoEncodingState state = VideoEncodingState.Starting;
        private string error = string.Empty;
        private bool finishRequested, cancelRequested;
        private int submittedFrames, encodedFrames;
        private Process process;
        private struct Frame { public byte[] Rgb; public bool BottomUp; }

        public VideoEncodingState State { get { lock (gate) return state; } }
        public string Error { get { lock (gate) return error; } }
        public int FrameCount { get { lock (gate) return encodedFrames; } }
        public string OutputPath { get { return outputPath; } }

        public static SilentVideoEncoder Create(string path, int width, int height, int fps, out string error)
        {
            error = string.Empty;
            if (width < 2 || height < 2 || width > 1920 || height > 1920 ||
                (long)width * height > 1280 * 720 || width % 2 != 0 || height % 2 != 0 || fps < 1 || fps > 30)
            {
                error = "视频尺寸须为偶数且不超过 720p，帧率须为 1–30。";
                return null;
            }
            try
            {
                if (string.IsNullOrEmpty(path) || !string.Equals(Path.GetExtension(path), ".mp4", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("导出路径须以 .mp4 结尾。");
                path = Path.GetFullPath(path);
                if (File.Exists(path)) throw new IOException("导出文件已存在，请使用新文件名。");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                return new SilentVideoEncoder(path, width, height, fps);
            }
            catch (Exception exception) { error = exception.Message; return null; }
        }

        private SilentVideoEncoder(string path, int width, int height, int fps)
        {
            this.width = width; this.height = height; this.fps = fps;
            outputPath = path;
            temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".encoding.mp4";
            worker = new Thread(Encode) { IsBackground = true, Name = "PalmBay silent video" };
            worker.Start();
        }

        /// <summary>Returns false on backpressure: retain the frame and retry without advancing replay time.</summary>
        public bool TryAddFrame(byte[] rgb24, bool bottomUp = true)
        {
            if (rgb24 == null || rgb24.Length != width * height * 3)
                throw new ArgumentException("Expected one complete RGB24 frame.", "rgb24");
            lock (gate)
            {
                if (state != VideoEncodingState.Recording || finishRequested || cancelRequested ||
                    frames.Count >= QueueCapacity || submittedFrames >= MaxFrames) return false;
                byte[] copy = new byte[rgb24.Length];
                Buffer.BlockCopy(rgb24, 0, copy, 0, copy.Length);
                frames.Enqueue(new Frame { Rgb = copy, BottomUp = bottomUp });
                submittedFrames++;
                Monitor.PulseAll(gate);
                return true;
            }
        }

        public void Finish()
        {
            lock (gate)
            {
                if (IsTerminal(state)) return;
                finishRequested = true;
                state = VideoEncodingState.Finishing;
                Monitor.PulseAll(gate);
            }
        }

        public void Cancel()
        {
            Process activeProcess;
            lock (gate)
            {
                if (IsTerminal(state)) return;
                cancelRequested = true;
                frames.Clear();
                activeProcess = process;
                Monitor.PulseAll(gate);
            }
            // Killing ffmpeg releases a worker blocked on stdin. Never join the worker on Unity's frame loop.
            if (activeProcess != null) { try { activeProcess.Kill(); } catch (Exception) { } }
        }

        public void Dispose() { Cancel(); }
        private static bool IsTerminal(VideoEncodingState value)
        { return value == VideoEncodingState.Completed || value == VideoEncodingState.Failed || value == VideoEncodingState.Cancelled; }

        private bool NextFrame(out Frame frame)
        {
            lock (gate)
            {
                while (frames.Count == 0 && !finishRequested && !cancelRequested) Monitor.Wait(gate, 100);
                if (cancelRequested) throw new OperationCanceledException();
                if (frames.Count == 0) { frame = default(Frame); return false; }
                frame = frames.Dequeue();
                return true;
            }
        }

        private void Encode()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJavaObject nativeEncoder = null;
            bool androidAttached = false;
#endif
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                androidAttached = AndroidJNI.AttachCurrentThread() == 0;
                if (!androidAttached) throw new InvalidOperationException("无法连接 Android 视频编码器。");
                nativeEncoder = new AndroidJavaObject("com.palmbay.sharing.SilentVideoEncoder", temporaryPath, width, height, fps);
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || PALMBAY_VIDEO_TEST
                StartFfmpeg();
#else
                throw new NotSupportedException("此平台没有可用的视频编码器。");
#endif
                lock (gate) { if (!finishRequested) state = VideoEncodingState.Recording; }
                Frame frame;
                while (NextFrame(out frame))
                {
#if UNITY_ANDROID && !UNITY_EDITOR
                    nativeEncoder.Call("addFrame", frame.Rgb, frame.BottomUp);
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || PALMBAY_VIDEO_TEST
                    WriteFfmpegFrame(frame);
#endif
                    lock (gate) encodedFrames++;
                    if (File.Exists(temporaryPath) && new FileInfo(temporaryPath).Length > MaxFileBytes)
                        throw new IOException("视频超过 128 MB，已停止导出。");
                }
                if (FrameCount == 0) throw new InvalidOperationException("没有可导出的视频画面。");
#if UNITY_ANDROID && !UNITY_EDITOR
                nativeEncoder.Call("finish");
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || PALMBAY_VIDEO_TEST
                process.StandardInput.Close();
                if (!process.WaitForExit(30000)) { process.Kill(); throw new IOException("视频编码器结束超时。"); }
                if (process.ExitCode != 0) throw new IOException("ffmpeg 编码失败。请检查安装及可用磁盘空间。");
#endif
                lock (gate)
                {
                    if (cancelRequested) throw new OperationCanceledException();
                    FileInfo result = new FileInfo(temporaryPath);
                    if (!result.Exists || result.Length < 32 || result.Length > MaxFileBytes)
                        throw new IOException("编码器未生成有效大小的视频文件。");
                    File.Move(temporaryPath, outputPath);
                    state = VideoEncodingState.Completed;
                }
            }
            catch (Exception exception)
            {
                lock (gate)
                {
                    state = cancelRequested || exception is OperationCanceledException ? VideoEncodingState.Cancelled : VideoEncodingState.Failed;
                    error = state == VideoEncodingState.Cancelled ? "视频导出已取消。" : exception.Message;
                    frames.Clear();
                }
            }
            finally
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                if (nativeEncoder != null) { try { nativeEncoder.Call("close"); } catch (Exception) { } nativeEncoder.Dispose(); }
                if (androidAttached) AndroidJNI.DetachCurrentThread();
#endif
                Process completedProcess;
                lock (gate) { completedProcess = process; process = null; }
                if (completedProcess != null)
                {
                    try { if (!completedProcess.HasExited) completedProcess.Kill(); } catch (Exception) { }
                    completedProcess.Dispose();
                }
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch (Exception) { }
            }
        }

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || PALMBAY_VIDEO_TEST
        private void StartFfmpeg()
        {
            string ffmpeg = FindFfmpeg();
            if (ffmpeg == null) throw new NotSupportedException("Mac 导出需要 ffmpeg：请安装后重试（brew install ffmpeg）。");
            Process candidate = new Process();
            candidate.StartInfo = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = "-hide_banner -loglevel error -nostats -y -f rawvideo -pixel_format rgb24 -video_size " +
                    width + "x" + height + " -framerate " + fps + " -i pipe:0 -an -c:v libx264 -preset veryfast " +
                    "-pix_fmt yuv420p -b:v 2500k -movflags +faststart " + QuoteArgument(temporaryPath),
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true, CreateNoWindow = true
            };
            candidate.ErrorDataReceived += delegate { }; // Drain stderr so the pipe never blocks encoding.
            lock (gate) { process = candidate; }
            if (!candidate.Start()) throw new IOException("无法启动 ffmpeg。");
            candidate.BeginErrorReadLine();
            lock (gate) { if (cancelRequested) { candidate.Kill(); throw new OperationCanceledException(); } }
        }

        private void WriteFfmpegFrame(Frame frame)
        {
            Stream stream = process.StandardInput.BaseStream;
            int rowBytes = width * 3;
            if (!frame.BottomUp) { stream.Write(frame.Rgb, 0, frame.Rgb.Length); return; }
            for (int y = height - 1; y >= 0; y--) stream.Write(frame.Rgb, y * rowBytes, rowBytes);
        }

        private static string QuoteArgument(string value)
        { return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }
        private static string FindFfmpeg()
        {
            string[] candidates = { "/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/usr/bin/ffmpeg" };
            foreach (string candidate in candidates) if (File.Exists(candidate)) return candidate;
            string searchPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string folder in searchPath.Split(Path.PathSeparator))
            {
                if (string.IsNullOrEmpty(folder)) continue;
                string candidate = Path.Combine(folder, "ffmpeg");
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }
#endif
    }
}
