import java.nio.ByteBuffer;
import com.palmbay.sharing.RgbYuvConverter;

public final class RgbYuvConverterTests {
    private static void check(boolean value, String message) { if (!value) throw new AssertionError(message); }
    public static void main(String[] args) {
        byte[] rgb = new byte[4 * 2 * 3];
        for (int i = 0; i < rgb.length; i += 3) rgb[i] = (byte)255;
        ByteBuffer y = ByteBuffer.allocate(12), u = ByteBuffer.allocate(4), v = ByteBuffer.allocate(4);
        RgbYuvConverter.fill(rgb, 4, 2, false, y, 6, 1, u, 4, 1, v, 4, 1);
        check((y.get(0) & 255) == 82 && (y.get(9) & 255) == 82, "red Y range");
        check((u.get(0) & 255) == 90 && (v.get(0) & 255) == 240, "red chroma");
        check(y.get(4) == 0 && y.get(5) == 0, "row padding preserved");
        // Interleaved NV12 image planes share storage with independent starting positions.
        ByteBuffer uv = ByteBuffer.allocate(4), uPlane = uv.duplicate(), vPlane = uv.duplicate();
        vPlane.position(1);
        RgbYuvConverter.fill(rgb, 4, 2, false, y, 6, 1, uPlane, 4, 2, vPlane, 4, 2);
        check((uv.get(0) & 255) == 90 && (uv.get(1) & 255) == 240 && (uv.get(2) & 255) == 90 && (uv.get(3) & 255) == 240,
                "interleaved pixel stride and plane offset");
        for (int i = 12; i < rgb.length; i += 3) { rgb[i] = 0; rgb[i + 2] = (byte)255; }
        RgbYuvConverter.fill(rgb, 4, 2, true, y, 6, 1, u, 4, 1, v, 4, 1);
        check((y.get(0) & 255) == 41 && (y.get(6) & 255) == 82, "Unity bottom-up orientation");
        System.out.println("RgbYuvConverter: planar/interleaved strides, range, flip and padding passed");
    }
}
