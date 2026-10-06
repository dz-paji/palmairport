package com.palmbay.sharing;

import java.nio.ByteBuffer;

/** BT.601 limited range, honoring both planar and interleaved Image plane strides. */
public final class RgbYuvConverter {
    private RgbYuvConverter() { }

    public static void fill(byte[] rgb, int width, int height, boolean bottomUp,
            ByteBuffer yBuffer, int yRow, int yPixel, ByteBuffer uBuffer, int uRow, int uPixel,
            ByteBuffer vBuffer, int vRow, int vPixel) {
        if (rgb == null || rgb.length != width * height * 3 || width % 2 != 0 || height % 2 != 0)
            throw new IllegalArgumentException("Expected an even-sized RGB24 frame");
        int yBase = yBuffer.position(), uBase = uBuffer.position(), vBase = vBuffer.position();
        for (int y = 0; y < height; y += 2) {
            for (int x = 0; x < width; x += 2) {
                int red = 0, green = 0, blue = 0;
                for (int dy = 0; dy < 2; dy++) {
                    int sourceY = bottomUp ? height - 1 - y - dy : y + dy;
                    for (int dx = 0; dx < 2; dx++) {
                        int input = (sourceY * width + x + dx) * 3;
                        int r = rgb[input] & 255, g = rgb[input + 1] & 255, b = rgb[input + 2] & 255;
                        yBuffer.put(yBase + (y + dy) * yRow + (x + dx) * yPixel,
                                (byte)clamp(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16));
                        red += r; green += g; blue += b;
                    }
                }
                red /= 4; green /= 4; blue /= 4;
                uBuffer.put(uBase + (y / 2) * uRow + (x / 2) * uPixel,
                        (byte)clamp(((-38 * red - 74 * green + 112 * blue + 128) >> 8) + 128));
                vBuffer.put(vBase + (y / 2) * vRow + (x / 2) * vPixel,
                        (byte)clamp(((112 * red - 94 * green - 18 * blue + 128) >> 8) + 128));
            }
        }
    }

    private static int clamp(int value) { return Math.max(0, Math.min(255, value)); }
}
