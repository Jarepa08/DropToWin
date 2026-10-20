package com.droptowin;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.ClipData;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.graphics.drawable.Icon;
import android.net.Uri;
import android.os.Build;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.os.PowerManager;
import android.os.SystemClock;
import android.widget.Toast;

import com.droptowin.core.DtwException;
import com.droptowin.core.FileSource;
import com.droptowin.core.Transfer;

import java.io.File;
import java.io.IOException;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * Envía en segundo plano (servicio en primer plano con notificación de progreso), de modo que el
 * envío sigue aunque se cierre la app de origen o se apague la pantalla. Los envíos se encolan.
 */
public class TransferService extends Service {
    static final String ACTION_FILES = "com.droptowin.action.SEND_FILES";
    static final String ACTION_TEXT = "com.droptowin.action.SEND_TEXT";
    static final String ACTION_CANCEL = "com.droptowin.action.CANCEL";
    static final String EXTRA_PC = "pc";
    static final String EXTRA_URIS = "uris";
    static final String EXTRA_TEXT = "text";

    private static final String CHANNEL_PROGRESS = "progreso";
    private static final String CHANNEL_RESULT = "resultado";
    private static final int ID_PROGRESS = 1;

    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private final Handler main = new Handler(Looper.getMainLooper());
    private final AtomicInteger pending = new AtomicInteger();
    private final AtomicInteger generation = new AtomicInteger();
    private final AtomicInteger resultIds = new AtomicInteger(100);
    private volatile Transfer current;
    private NotificationManager notifications;

    static void sendUris(Context c, String pcAddress, ArrayList<Uri> uris) {
        Intent intent = new Intent(c, TransferService.class)
                .setAction(ACTION_FILES)
                .putExtra(EXTRA_PC, pcAddress)
                .putParcelableArrayListExtra(EXTRA_URIS, uris);
        // El permiso de lectura que recibió la app de origen pasa también al servicio.
        ClipData clip = null;
        for (Uri uri : uris) {
            if (clip == null) clip = ClipData.newRawUri("", uri);
            else clip.addItem(new ClipData.Item(uri));
        }
        intent.setClipData(clip);
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        c.startForegroundService(intent);
    }

    static void sendText(Context c, String pcAddress, String text) {
        c.startForegroundService(new Intent(c, TransferService.class)
                .setAction(ACTION_TEXT)
                .putExtra(EXTRA_PC, pcAddress)
                .putExtra(EXTRA_TEXT, text));
    }

    @Override
    public void onCreate() {
        super.onCreate();
        notifications = getSystemService(NotificationManager.class);
        notifications.createNotificationChannel(new NotificationChannel(
                CHANNEL_PROGRESS, "Envíos en curso", NotificationManager.IMPORTANCE_LOW));
        notifications.createNotificationChannel(new NotificationChannel(
                CHANNEL_RESULT, "Resultado de los envíos", NotificationManager.IMPORTANCE_DEFAULT));
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        String action = intent != null ? intent.getAction() : null;
        if (ACTION_CANCEL.equals(action)) {
            generation.incrementAndGet();
            Transfer t = current;
            if (t != null) t.cancel();
            if (pending.get() == 0) stopSelf();
            return START_NOT_STICKY;
        }
        if (!ACTION_FILES.equals(action) && !ACTION_TEXT.equals(action)) {
            if (pending.get() == 0) stopSelf();
            return START_NOT_STICKY;
        }

        Notification preparing = progressNotification("Preparando el envío…", null, 0, 0, true);
        if (Build.VERSION.SDK_INT >= 29) {
            startForeground(ID_PROGRESS, preparing, ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE);
        } else {
            startForeground(ID_PROGRESS, preparing);
        }

        final Job job = Job.from(intent);
        final int gen = generation.get();
        pending.incrementAndGet();
        worker.execute(() -> {
            try {
                if (gen == generation.get()) run(job);
            } finally {
                if (pending.decrementAndGet() == 0) main.post(this::stopIfIdle);
            }
        });
        return START_NOT_STICKY;
    }

    private void stopIfIdle() {
        if (pending.get() != 0) return;
        stopForeground(STOP_FOREGROUND_REMOVE);
        stopSelf();
    }

    @Override
    public void onDestroy() {
        Transfer t = current;
        if (t != null) t.cancel();
        worker.shutdownNow();
        super.onDestroy();
    }

    private void run(Job job) {
        Store.Pc pc = Store.find(this, job.pcAddress);
        String label = pc != null ? pc.label() : job.pcAddress;
        PowerManager.WakeLock wake = ((PowerManager) getSystemService(POWER_SERVICE))
                .newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "DropToWin:envio");
        wake.acquire(4 * 60 * 60 * 1000L);
        List<File> tempFiles = new ArrayList<>();
        Transfer transfer = new Transfer(Bt.connector(this, job.pcAddress), Bt.wifiSocketFactory(this),
                Store.hello(this), new NotificationListener(label));
        current = transfer;
        try {
            if (job.text != null) {
                String pcName = transfer.sendText(job.text);
                Store.touch(this, job.pcAddress, pcName);
                showResult("Texto enviado a " + pcName, preview(job.text), false);
            } else {
                showProgress(progressNotification("Enviando a " + label, "Preparando archivos…", 0, 0, true));
                List<FileSource> sources = AndroidSources.resolve(this, job.uris, tempFiles);
                if (sources.isEmpty()) {
                    showResult("Nada que enviar", null, true);
                    return;
                }
                Transfer.Result r = transfer.sendFiles(sources);
                Store.touch(this, job.pcAddress, r.pcName);
                String title = r.files == 1
                        ? "«" + sources.get(0).name() + "» enviado a " + r.pcName
                        : r.files + " archivos enviados a " + r.pcName;
                showResult(title, Fmt.size(r.bytes) + " por " + Fmt.transport(r.transport), false);
            }
        } catch (DtwException e) {
            if ("cancelled".equals(e.code)) showResult("Envío cancelado", null, true);
            else showResult("No se pudo enviar a " + label, e.getMessage(), true);
        } catch (IOException | RuntimeException e) {
            showResult("No se pudo enviar a " + label, Errors.message(e), true);
        } finally {
            current = null;
            for (File f : tempFiles) deleteRecursively(f);
            if (wake.isHeld()) wake.release();
        }
    }

    private final class NotificationListener implements Transfer.Listener {
        private final String label;
        private long lastUpdate;

        NotificationListener(String label) {
            this.label = label;
        }

        @Override
        public void onPhase(Transfer.Phase phase) {
            String text;
            switch (phase) {
                case CONNECTING: text = "Conectando por Bluetooth…"; break;
                case WAITING_APPROVAL: text = "Acepta la conexión en el PC"; break;
                case SWITCHING_TO_WIFI: text = "Buscando el PC en la red Wi-Fi…"; break;
                case RECONNECTING: text = "Se perdió la conexión; reintentando…"; break;
                default: return;
            }
            showProgress(progressNotification("Enviando a " + label, text, 0, 0, true));
        }

        @Override
        public void onProgress(Transfer.ProgressInfo p) {
            long now = SystemClock.elapsedRealtime();
            if (now - lastUpdate < 500 && p.sent < p.total) return;
            lastUpdate = now;
            int permille = p.total > 0 ? (int) Math.min(1000, p.sent * 1000 / p.total) : 1000;
            String title = p.fileCount == 1
                    ? "Enviando a " + label
                    : "Enviando a " + label + " (" + (p.fileIndex + 1) + " de " + p.fileCount + ")";
            StringBuilder text = new StringBuilder();
            text.append(permille / 10).append(" % · ").append(Fmt.transport(p.transport));
            String speed = Fmt.speed(p.bytesPerSecond);
            if (!speed.isEmpty()) text.append(" · ").append(speed);
            if (p.bytesPerSecond > 1) {
                String eta = Fmt.duration((p.total - p.sent) / p.bytesPerSecond);
                if (!eta.isEmpty()) text.append(" · quedan ").append(eta);
            }
            showProgress(progressNotification(title, p.fileName + "\n" + text, permille, 1000, false));
        }
    }

    /** {@code text} puede tener dos líneas: la primera se ve plegada; ambas, al desplegar. */
    private Notification progressNotification(String title, String text, int progress, int max, boolean indeterminate) {
        PendingIntent cancel = PendingIntent.getService(this, 1,
                new Intent(this, TransferService.class).setAction(ACTION_CANCEL),
                PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
        String collapsed = text;
        if (text != null && text.indexOf('\n') > 0) {
            String[] lines = text.split("\n", 2);
            collapsed = lines[1] + " · " + lines[0];
        }
        Notification.Builder b = new Notification.Builder(this, CHANNEL_PROGRESS)
                .setSmallIcon(R.drawable.ic_notification)
                .setContentTitle(title)
                .setContentText(collapsed)
                .setStyle(new Notification.BigTextStyle().bigText(text))
                .setOngoing(true)
                .setOnlyAlertOnce(true)
                .setShowWhen(false)
                .setCategory(Notification.CATEGORY_PROGRESS)
                .setContentIntent(openAppIntent())
                .setProgress(max, progress, indeterminate)
                .addAction(new Notification.Action.Builder(
                        Icon.createWithResource(this, R.drawable.ic_close), "Cancelar", cancel).build());
        if (Build.VERSION.SDK_INT >= 31) b.setForegroundServiceBehavior(Notification.FOREGROUND_SERVICE_IMMEDIATE);
        return b.build();
    }

    private void showProgress(Notification n) {
        notifications.notify(ID_PROGRESS, n);
    }

    private void showResult(String title, String text, boolean error) {
        Notification n = new Notification.Builder(this, CHANNEL_RESULT)
                .setSmallIcon(R.drawable.ic_notification)
                .setContentTitle(title)
                .setContentText(text)
                .setStyle(new Notification.BigTextStyle().bigText(text))
                .setAutoCancel(true)
                .setContentIntent(openAppIntent())
                .build();
        notifications.notify(resultIds.incrementAndGet(), n);
        // Sin permiso de notificaciones, al menos un aviso breve en pantalla.
        if (!notifications.areNotificationsEnabled()) {
            final String message = text != null && error ? title + ": " + text : title;
            main.post(() -> Toast.makeText(this, message, Toast.LENGTH_LONG).show());
        }
    }

    private PendingIntent openAppIntent() {
        return PendingIntent.getActivity(this, 0, new Intent(this, MainActivity.class), PendingIntent.FLAG_IMMUTABLE);
    }

    private static String preview(String text) {
        String oneLine = text.replace('\n', ' ').trim();
        return oneLine.length() > 80 ? oneLine.substring(0, 80) + "…" : oneLine;
    }

    private static void deleteRecursively(File f) {
        File[] children = f.listFiles();
        if (children != null) for (File child : children) deleteRecursively(child);
        //noinspection ResultOfMethodCallIgnored
        f.delete();
    }

    static final class Job {
        String pcAddress;
        ArrayList<Uri> uris;
        String text;

        @SuppressWarnings("deprecation")
        static Job from(Intent intent) {
            Job job = new Job();
            job.pcAddress = intent.getStringExtra(EXTRA_PC);
            job.text = intent.getStringExtra(EXTRA_TEXT);
            job.uris = intent.getParcelableArrayListExtra(EXTRA_URIS);
            if (job.uris == null) job.uris = new ArrayList<>();
            return job;
        }
    }
}
