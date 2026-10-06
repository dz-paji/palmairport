package com.palmbay.sharing;

import android.media.Image;
import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaCodecList;
import android.media.MediaFormat;
import android.media.MediaMuxer;
import android.os.SystemClock;
import java.io.File;
import java.io.IOException;
import java.nio.ByteBuffer;

/** Invoked exclusively by the Unity worker. No microphone, audio track, or storage permission. */
public final class SilentVideoEncoder {
    private MediaCodec codec;
    private MediaMuxer muxer;
    private boolean codecStarted, muxerStarted, closed, outputEos;
    private final int width, height, fps;
    private int frameCount, outputCount, track = -1;
    private final MediaCodec.BufferInfo bufferInfo = new MediaCodec.BufferInfo();

    public SilentVideoEncoder(String path, int width, int height, int fps) throws IOException {
        if (width < 2 || height < 2 || width % 2 != 0 || height % 2 != 0 ||
                (long)width * height > 1280 * 720 || fps < 1 || fps > 30)
            throw new IllegalArgumentException("Invalid video dimensions or frame rate");
        this.width = width; this.height = height; this.fps = fps;
        try {
            MediaFormat format = MediaFormat.createVideoFormat("video/avc", width, height);
            format.setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatYUV420Flexible);
            format.setInteger(MediaFormat.KEY_BIT_RATE, 2500000);
            format.setInteger(MediaFormat.KEY_FRAME_RATE, fps);
            format.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 1);
            // Baseline AVC is broadly accepted by mobile sharing apps and avoids frame reordering.
            format.setInteger(MediaFormat.KEY_PROFILE, MediaCodecInfo.CodecProfileLevel.AVCProfileBaseline);
            String name = new MediaCodecList(MediaCodecList.REGULAR_CODECS).findEncoderForFormat(format);
            if (name == null) throw new IOException("No H.264 encoder supports this video format");
            codec = MediaCodec.createByCodecName(name);
            codec.configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
            codec.start(); codecStarted = true;
            muxer = new MediaMuxer(path, MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4);
        } catch (IOException | RuntimeException exception) { close(); throw exception; }
    }

    public void addFrame(byte[] rgb, boolean bottomUp) throws IOException {
        if (closed || outputEos || frameCount >= 1800) throw new IOException("Video encoder is closed or full");
        int index = waitForInput();
        Image image = codec.getInputImage(index);
        if (image == null) throw new IOException("Video encoder does not expose a YUV input image");
        try {
            Image.Plane[] planes = image.getPlanes();
            if (planes.length != 3) throw new IOException("Unexpected encoder image planes");
            RgbYuvConverter.fill(rgb, width, height, bottomUp,
                    planes[0].getBuffer(), planes[0].getRowStride(), planes[0].getPixelStride(),
                    planes[1].getBuffer(), planes[1].getRowStride(), planes[1].getPixelStride(),
                    planes[2].getBuffer(), planes[2].getRowStride(), planes[2].getPixelStride());
        } finally { image.close(); }
        codec.queueInputBuffer(index, 0, width * height * 3 / 2, frameCount * 1000000L / fps, 0);
        frameCount++;
        drain(false);
    }

    private int waitForInput() throws IOException {
        long deadline = SystemClock.elapsedRealtime() + 10000;
        while (SystemClock.elapsedRealtime() < deadline) {
            int index = codec.dequeueInputBuffer(10000);
            if (index >= 0) return index;
            drain(false);
        }
        throw new IOException("Video encoder input timed out");
    }

    private void drain(boolean finishing) throws IOException {
        long deadline = SystemClock.elapsedRealtime() + 10000;
        do {
            int index = codec.dequeueOutputBuffer(bufferInfo, finishing ? 10000 : 0);
            if (index == MediaCodec.INFO_TRY_AGAIN_LATER) {
                if (!finishing) return;
            } else if (index == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                if (muxerStarted) throw new IOException("Encoder format changed twice");
                track = muxer.addTrack(codec.getOutputFormat());
                muxer.start(); muxerStarted = true;
            } else if (index >= 0) {
                try {
                    ByteBuffer buffer = codec.getOutputBuffer(index);
                    if ((bufferInfo.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) != 0) bufferInfo.size = 0;
                    if (bufferInfo.size > 0) {
                        if (!muxerStarted || buffer == null) throw new IOException("Encoder data arrived without a format");
                        buffer.position(bufferInfo.offset);
                        buffer.limit(bufferInfo.offset + bufferInfo.size);
                        muxer.writeSampleData(track, buffer, bufferInfo);
                        outputCount++;
                    }
                    if ((bufferInfo.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0) outputEos = true;
                } finally { codec.releaseOutputBuffer(index, false); }
            }
            if (outputEos) return;
        } while (SystemClock.elapsedRealtime() < deadline);
        throw new IOException("Video encoder output timed out");
    }

    public void finish() throws IOException {
        if (closed || frameCount == 0) throw new IOException("No video frames to finish");
        int index = waitForInput();
        codec.queueInputBuffer(index, 0, 0, frameCount * 1000000L / fps, MediaCodec.BUFFER_FLAG_END_OF_STREAM);
        drain(true);
        if (!muxerStarted || !outputEos || outputCount != frameCount)
            throw new IOException("Video encoder did not finish every frame");
        // stop() commits the MP4 indexes; a failed stop must propagate rather than report success.
        muxer.stop(); muxerStarted = false;
        close();
    }

    public void close() {
        if (closed) return;
        closed = true;
        if (codec != null) {
            if (codecStarted) { try { codec.stop(); } catch (RuntimeException ignored) { } }
            try { codec.release(); } catch (RuntimeException ignored) { }
            codec = null;
        }
        if (muxer != null) {
            if (muxerStarted) { try { muxer.stop(); } catch (RuntimeException ignored) { } }
            try { muxer.release(); } catch (RuntimeException ignored) { }
            muxer = null;
        }
    }
}
