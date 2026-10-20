package com.droptowin.core;

import java.io.IOException;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

import javax.net.SocketFactory;

/**
 * Orquesta un envío completo: conecta por Bluetooth, salta a Wi-Fi si el PC lo ofrece y es
 * alcanzable, y si la conexión se corta vuelve a conectar y reanuda donde se quedó.
 */
public final class Transfer {

    /** Abre el canal Bluetooth (o el transporte de prueba en el escritorio). */
    public interface Connector {
        Duplex connect() throws IOException, DtwException;
    }

    public enum Phase { CONNECTING, WAITING_APPROVAL, SWITCHING_TO_WIFI, SENDING, RECONNECTING }

    public interface Listener {
        void onPhase(Phase phase);

        void onProgress(ProgressInfo info);
    }

    public static final class ProgressInfo {
        public final long sent;
        public final long total;
        public final int fileIndex;
        public final int fileCount;
        public final String fileName;
        public final String transport;
        public final double bytesPerSecond;

        ProgressInfo(long sent, long total, int fileIndex, int fileCount, String fileName, String transport,
                     double bytesPerSecond) {
            this.sent = sent;
            this.total = total;
            this.fileIndex = fileIndex;
            this.fileCount = fileCount;
            this.fileName = fileName;
            this.transport = transport;
            this.bytesPerSecond = bytesPerSecond;
        }
    }

    public static final class Result {
        public final int files;
        public final long bytes;
        public final String pcName;
        /** Transporte usado al final: "bluetooth" o "wifi". */
        public final String transport;

        Result(int files, long bytes, String pcName, String transport) {
            this.files = files;
            this.bytes = bytes;
            this.pcName = pcName;
            this.transport = transport;
        }
    }

    public static final class TestResult {
        public final String pcId;
        public final String pcName;
        public final boolean wifiOffered;
        public final boolean wifiReachable;

        TestResult(String pcId, String pcName, boolean wifiOffered, boolean wifiReachable) {
            this.pcId = pcId;
            this.pcName = pcName;
            this.wifiOffered = wifiOffered;
            this.wifiReachable = wifiReachable;
        }
    }

    static final int BLUETOOTH_CHUNK = 16 * 1024;
    static final int WIFI_CHUNK = 256 * 1024;
    static final int CONNECT_ATTEMPTS = 2;
    static final int MAX_RECONNECTS = 6;
    static final int WIFI_CONNECT_TIMEOUT_MS = 1500;

    private final Connector bluetooth;
    private final SocketFactory wifiSockets;
    private final Map<String, Object> helloBase;
    private final Listener listener;

    private volatile boolean cancelled;
    private volatile Duplex current;

    private long sampleTime;
    private long sampleBytes;
    private double speed;

    /**
     * @param wifiSockets fábrica de sockets ligada a la red Wi-Fi, o null para no usar el Wi-Fi turbo
     * @param helloBase   deviceId, deviceName… (se añaden "t", "v" y "token")
     */
    public Transfer(Connector bluetooth, SocketFactory wifiSockets, Map<String, Object> helloBase, Listener listener) {
        this.bluetooth = bluetooth;
        this.wifiSockets = wifiSockets;
        this.helloBase = helloBase;
        this.listener = listener;
    }

    /** Cancela desde cualquier hilo: cierra la conexión activa para desbloquear lecturas y escrituras. */
    public void cancel() {
        cancelled = true;
        Duplex d = current;
        if (d != null) d.close();
    }

    public boolean isCancelled() {
        return cancelled;
    }

    /** Conecta, se identifica (el PC puede pedir aprobación) y comprueba si el Wi-Fi turbo funcionaría. */
    public TestResult test() throws IOException, DtwException {
        Session bt = openBluetooth();
        try {
            Session.WifiOffer offer = bt.wifiOffer();
            boolean reachable = false;
            if (offer != null && wifiSockets != null) {
                listener.onPhase(Phase.SWITCHING_TO_WIFI);
                Duplex wifi = tryWifi(offer);
                if (wifi != null) {
                    try {
                        Session ws = Session.open(wifi, hello(offer.token), null);
                        reachable = true;
                        ws.bye();
                    } catch (IOException | DtwException ignored) {
                        // El Wi-Fi no sirve; se informa como "solo Bluetooth".
                    } finally {
                        wifi.close();
                    }
                }
            }
            TestResult result = new TestResult(bt.pcId(), bt.pcName(), offer != null, reachable);
            bt.bye();
            return result;
        } finally {
            bt.close();
            current = null;
        }
    }

    /** Envía texto (siempre por Bluetooth: es pequeño y no compensa abrir el Wi-Fi). */
    public String sendText(String text) throws IOException, DtwException {
        Session s = openBluetooth();
        try {
            listener.onPhase(Phase.SENDING);
            s.sendText(text);
            String pcName = s.pcName();
            s.bye();
            return pcName;
        } finally {
            s.close();
            current = null;
        }
    }

    public Result sendFiles(List<? extends FileSource> files) throws IOException, DtwException {
        long total = 0;
        for (FileSource f : files) total += f.size();
        final long totalBytes = total;
        final int count = files.size();

        int index = 0;
        long doneBytes = 0;
        String batchId = null;
        List<String> fileIds = null;
        int batchStart = 0;
        int failures = 0;
        boolean connectedOnce = false;
        String pcName = null;
        String transport = null;
        sampleTime = 0;
        speed = 0;

        while (index < count) {
            checkCancelled();
            Session s = null;
            try {
                s = openDataSession();
                connectedOnce = true;
                pcName = s.pcName();
                transport = s.transport();
                if (batchId == null) {
                    Session.Batch batch = s.createBatch(files.subList(index, count));
                    batchId = batch.id;
                    fileIds = batch.fileIds;
                    batchStart = index;
                }
                listener.onPhase(Phase.SENDING);
                int chunk = "wifi".equals(transport) ? WIFI_CHUNK : BLUETOOTH_CHUNK;
                while (index < count) {
                    checkCancelled();
                    final FileSource file = files.get(index);
                    final long base = doneBytes;
                    final int fileIndex = index;
                    final String via = transport;
                    s.sendFile(batchId, fileIds.get(index - batchStart), file, chunk,
                            position -> reportProgress(base + position, totalBytes, fileIndex, count, file.name(), via),
                            this::isCancelled);
                    doneBytes += file.size();
                    index++;
                    failures = 0;
                }
                s.bye();
            } catch (DtwException e) {
                // El PC se reinició y perdió el lote: se crea uno nuevo con lo que falta.
                if ("unknown-batch".equals(e.code) && ++failures <= MAX_RECONNECTS) {
                    batchId = null;
                    continue;
                }
                throw e;
            } catch (IOException e) {
                checkCancelled();
                if (!connectedOnce || ++failures > MAX_RECONNECTS) throw e;
                listener.onPhase(Phase.RECONNECTING);
                sleep(Math.min(8000L, 1000L << (failures - 1)));
            } finally {
                if (s != null) s.close();
                current = null;
            }
        }
        return new Result(count, totalBytes, pcName, transport);
    }

    private Session openDataSession() throws IOException, DtwException {
        Session bt = openBluetooth();
        Session.WifiOffer offer = wifiSockets != null ? bt.wifiOffer() : null;
        if (offer == null) return bt;

        listener.onPhase(Phase.SWITCHING_TO_WIFI);
        Duplex wifi = tryWifi(offer);
        if (wifi == null) return bt;
        try {
            current = wifi;
            Session ws = Session.open(wifi, hello(offer.token), null);
            bt.bye();
            bt.close();
            return ws;
        } catch (IOException | DtwException e) {
            wifi.close();
            current = bt.link();
            return bt;
        }
    }

    private Session openBluetooth() throws IOException, DtwException {
        IOException last = null;
        for (int attempt = 1; attempt <= CONNECT_ATTEMPTS; attempt++) {
            checkCancelled();
            listener.onPhase(Phase.CONNECTING);
            Duplex d;
            try {
                d = bluetooth.connect();
            } catch (IOException e) {
                last = e;
                if (attempt < CONNECT_ATTEMPTS) sleep(1000);
                continue;
            }
            current = d;
            try {
                return Session.open(d, hello(null), () -> listener.onPhase(Phase.WAITING_APPROVAL));
            } catch (IOException | DtwException e) {
                d.close();
                current = null;
                throw e;
            }
        }
        checkCancelled();
        throw last;
    }

    private Duplex tryWifi(Session.WifiOffer offer) {
        for (String ip : Subnets.reachable(offer.addresses)) {
            if (cancelled) return null;
            try {
                return Tls.connect(wifiSockets, ip, offer.port, offer.fingerprint, WIFI_CONNECT_TIMEOUT_MS);
            } catch (IOException ignored) {
                // Se prueba la siguiente dirección; si ninguna responde, se sigue por Bluetooth.
            }
        }
        return null;
    }

    private Map<String, Object> hello(String token) {
        Map<String, Object> m = new LinkedHashMap<>();
        m.put("t", "hello");
        m.put("v", 1);
        m.putAll(helloBase);
        if (token != null) m.put("token", token);
        return m;
    }

    private void reportProgress(long sent, long total, int index, int count, String name, String transport) {
        long now = System.nanoTime();
        if (sampleTime == 0) {
            sampleTime = now;
            sampleBytes = sent;
        }
        double elapsed = (now - sampleTime) / 1e9;
        if (elapsed >= 0.5) {
            double instant = Math.max(0, sent - sampleBytes) / elapsed;
            speed = speed == 0 ? instant : speed * 0.6 + instant * 0.4;
            sampleTime = now;
            sampleBytes = sent;
        }
        listener.onProgress(new ProgressInfo(sent, total, index, count, name, transport, speed));
    }

    private void checkCancelled() throws DtwException {
        if (cancelled) throw new DtwException("cancelled", "Envío cancelado.");
    }

    private void sleep(long ms) throws DtwException {
        try {
            Thread.sleep(ms);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            throw new DtwException("cancelled", "Envío cancelado.");
        }
        checkCancelled();
    }
}
