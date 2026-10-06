package com.palmbay.sharing;

import android.app.Activity;
import android.content.ClipData;
import android.content.Intent;
import android.net.Uri;
import android.os.Looper;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.util.Arrays;
import java.util.Comparator;
import java.util.UUID;
import java.util.concurrent.FutureTask;
import java.util.concurrent.TimeUnit;

public final class ReplayVideoShare {
    private ReplayVideoShare() { }
    /** Copies inside the app cache before granting a read-only content URI. Returns only chooser-open status. */
    public static boolean share(Activity activity, String path) throws Exception {
        File source = new File(path).getCanonicalFile();
        File internalFiles = activity.getFilesDir().getCanonicalFile();
        File externalFiles = activity.getExternalFilesDir(null);
        File internalCache = activity.getCacheDir().getCanonicalFile();
        File externalCache = activity.getExternalCacheDir();
        boolean privateSource = source.getPath().startsWith(internalFiles.getPath() + File.separator) ||
                source.getPath().startsWith(internalCache.getPath() + File.separator) ||
                (externalFiles != null && source.getPath().startsWith(externalFiles.getCanonicalPath() + File.separator)) ||
                (externalCache != null && source.getPath().startsWith(externalCache.getCanonicalPath() + File.separator));
        if (!privateSource || !source.isFile() ||
                !source.getName().endsWith(".mp4") || source.length() < 32 || source.length() > 128L * 1024 * 1024)
            throw new IOException("Replay file is unavailable or outside app storage");
        File directory = new File(activity.getCacheDir(), "replay-video-share");
        if (!directory.isDirectory() && !directory.mkdirs()) throw new IOException("Cannot create share cache");
        File[] old = directory.listFiles();
        if (old != null) {
            Arrays.sort(old, Comparator.comparingLong(File::lastModified));
            for (int i = 0; i < old.length - 2; i++) {
                if (old[i].getName().matches("replay-[a-f0-9]{32}\\.mp4") && !old[i].delete())
                    throw new IOException("Cannot trim share cache");
            }
        }
        File destination = new File(directory, "replay-" + UUID.randomUUID().toString().replace("-", "") + ".mp4");
        try (FileInputStream input = new FileInputStream(source); FileOutputStream output = new FileOutputStream(destination)) {
            byte[] buffer = new byte[65536];
            int count;
            while ((count = input.read(buffer)) != -1) output.write(buffer, 0, count);
        } catch (Exception exception) { destination.delete(); throw exception; }
        final Uri uri = new Uri.Builder().scheme("content").authority(activity.getPackageName() + ".replayvideo")
                .appendPath(destination.getName()).build();
        FutureTask<Boolean> openChooser = new FutureTask<>(() -> {
            Intent send = new Intent(Intent.ACTION_SEND);
            send.setType("video/mp4");
            send.putExtra(Intent.EXTRA_STREAM, uri);
            send.setClipData(ClipData.newUri(activity.getContentResolver(), "PALM BAY replay", uri));
            send.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
            Intent chooser = Intent.createChooser(send, "分享机场回放");
            chooser.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
            activity.startActivity(chooser);
            return true;
        });
        if (Looper.myLooper() == Looper.getMainLooper()) openChooser.run();
        else activity.runOnUiThread(openChooser);
        try { return openChooser.get(10, TimeUnit.SECONDS); }
        catch (Exception exception) { openChooser.cancel(false); destination.delete(); throw exception; }
    }
}
