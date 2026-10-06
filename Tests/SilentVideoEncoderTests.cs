using System;
using System.IO;
using System.Threading;
using IslandAirport;

public static class SilentVideoEncoderTests
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void WaitUntil(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(40);
        while (!condition()) { if (DateTime.UtcNow >= deadline) throw new Exception("Encoder timed out"); Thread.Sleep(10); }
    }
    public static int Main(string[] args)
    {
        string directory = args[0];
        string error;
        Require(SilentVideoEncoder.Create(Path.Combine(directory, "odd.mp4"), 63, 48, 24, out error) == null, "Odd dimensions must fail");
        Require(SilentVideoEncoder.Create(Path.Combine(directory, "large.mp4"), 1920, 1080, 24, out error) == null, "Large dimensions must fail");
        Require(SilentVideoEncoder.Create(Path.Combine(directory, "wrong.txt"), 64, 48, 24, out error) == null, "Non-MP4 must fail");
        string path = Path.Combine(directory, "silent 回放 with spaces.mp4");
        using (SilentVideoEncoder encoder = SilentVideoEncoder.Create(path, 64, 48, 24, out error))
        {
            Require(encoder != null, error);
            WaitUntil(() => encoder.State != VideoEncodingState.Starting);
            Require(encoder.State == VideoEncodingState.Recording, encoder.Error);
            byte[] rgb = new byte[64 * 48 * 3];
            // Unity-style bottom-up pixels: lower half blue, upper half red.
            for (int y = 0; y < 48; y++) for (int x = 0; x < 64; x++) rgb[(y * 64 + x) * 3 + (y < 24 ? 2 : 0)] = 255;
            for (int i = 0; i < 30; i++) WaitUntil(() => encoder.TryAddFrame(rgb, true));
            encoder.Finish();
            WaitUntil(() => encoder.State == VideoEncodingState.Completed || encoder.State == VideoEncodingState.Failed);
            Require(encoder.State == VideoEncodingState.Completed, encoder.Error);
            Require(encoder.FrameCount == 30 && File.Exists(path), "All frames must commit");
            Require(!encoder.TryAddFrame(rgb), "Completed encoder must reject frames");
        }
        Require(SilentVideoEncoder.Create(path, 64, 48, 24, out error) == null, "Do not overwrite completed files");
        string empty = Path.Combine(directory, "empty.mp4");
        using (SilentVideoEncoder encoder = SilentVideoEncoder.Create(empty, 64, 48, 24, out error))
        {
            encoder.Finish();
            WaitUntil(() => encoder.State == VideoEncodingState.Failed);
            Require(!File.Exists(empty), "An empty export must never report a file");
        }
        string cancelled = Path.Combine(directory, "cancelled.mp4");
        using (SilentVideoEncoder encoder = SilentVideoEncoder.Create(cancelled, 64, 48, 24, out error))
        {
            WaitUntil(() => encoder.State != VideoEncodingState.Starting);
            byte[] rgb = new byte[64 * 48 * 3];
            Require(encoder.TryAddFrame(rgb), "Cancelled test frame");
            encoder.Cancel();
            WaitUntil(() => encoder.State == VideoEncodingState.Cancelled);
            Require(!File.Exists(cancelled), "Cancelled exports must not publish a file");
        }
        Thread.Sleep(300); // Terminal status is visible just before worker finally releases native resources.
        Require(Directory.GetFiles(directory, "*.encoding.mp4").Length == 0, "Partial files must be removed");
        Console.WriteLine("SilentVideoEncoder: MP4, validation, finish, cancellation and cleanup passed");
        return 0;
    }
}
