import com.droptowin.core.Duplex;
import com.droptowin.core.DtwException;
import com.droptowin.core.FileSource;
import com.droptowin.core.Json;
import com.droptowin.core.Session;
import com.droptowin.core.Tls;
import com.droptowin.core.Transfer;

import java.io.BufferedInputStream;
import java.io.BufferedOutputStream;
import java.io.EOFException;
import java.io.FilterOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.io.PrintStream;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.nio.file.attribute.FileTime;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Map;
import java.util.Random;

import javax.net.SocketFactory;

/**
 * Pruebas de extremo a extremo del núcleo de la app Android (com.droptowin.core) contra la app de
 * Windows arrancada con --loopback --test-transport. El transporte de prueba hace de Bluetooth.
 *
 * Uso: java ProtocolTest <pair|main|reject> <carpeta Recibidos del PC> <carpeta de trabajo>
 */
public final class ProtocolTest {
    static final int TEST_PORT = 47819;
    static final long MB = 1024 * 1024;
    static int failures;
    static PrintStream out;

    public static void main(String[] args) throws Exception {
        out = new PrintStream(System.out, true, "UTF-8");
        String step = args[0];
        Path received = Paths.get(args[1]);
        Path work = Paths.get(args[2]);
        Files.createDirectories(work);
        out.println("== " + step + " ==");
        try {
            switch (step) {
                case "pair": pair(); break;
                case "main": mainSuite(received, work); break;
                case "reject": reject(); break;
                default: throw new IllegalArgumentException(step);
            }
        } catch (Exception e) {
            failures++;
            out.println("  ✗ excepción inesperada: " + e);
            e.printStackTrace(out);
        }
        out.println(failures == 0 ? "RESULTADO: OK" : "RESULTADO: " + failures + " FALLO(S)");
        System.exit(failures == 0 ? 0 : 1);
    }

    // ───────────────────────── Pasos ─────────────────────────

    static void pair() throws Exception {
        PhaseLog log = new PhaseLog();
        Transfer.TestResult r = new Transfer(testTransport(), SocketFactory.getDefault(), hello("test-device-A", "Teléfono de prueba"), log).test();
        check(log.phases.contains(Transfer.Phase.WAITING_APPROVAL), "el PC pidió aprobación para un dispositivo nuevo");
        check(r.pcName != null && !r.pcName.isEmpty(), "bienvenida recibida del PC «" + r.pcName + "»");
        check(r.wifiOffered, "el PC ofreció Wi-Fi turbo por el canal Bluetooth");
        check(r.wifiReachable, "el Wi-Fi turbo es alcanzable (TLS con huella fijada + llave)");
    }

    static void reject() throws Exception {
        try {
            new Transfer(testTransport(), null, hello("test-device-B", "Intruso"), new PhaseLog()).test();
            check(false, "un dispositivo rechazado no debería conectar");
        } catch (DtwException e) {
            check("rejected".equals(e.code), "conexión rechazada desde el PC (" + e.code + "): " + e.getMessage());
        }
    }

    static void mainSuite(Path received, Path work) throws Exception {
        Map<String, Object> hello = hello("test-device-A", "Teléfono de prueba");

        // 1. Dispositivo ya autorizado: no debe volver a pedir aprobación.
        PhaseLog log = new PhaseLog();
        Transfer.TestResult tr = new Transfer(testTransport(), SocketFactory.getDefault(), hello, log).test();
        check(!log.phases.contains(Transfer.Phase.WAITING_APPROVAL), "dispositivo recordado: sin nueva aprobación");
        check(tr.wifiReachable, "prueba de conexión con Wi-Fi turbo");

        // 2. Texto.
        String pc = new Transfer(testTransport(), null, hello, new PhaseLog()).sendText("Hola desde la prueba ✓ https://example.com/ñ");
        check(pc != null, "texto enviado a «" + pc + "»");

        // 3. Varios archivos por Wi-Fi turbo, con nombres conflictivos.
        Path empty = write(work, "vacio.bin", new byte[0]);
        Path utf = write(work, "utf.txt", "¡Hola, mundo! ñandú 👋\n".getBytes(StandardCharsets.UTF_8));
        Path video = write(work, "video.bin", random(40 * MB, 1));
        Path bad = write(work, "bad.bin", random(1000, 2));
        Path evil = write(work, "evil.bin", "x".getBytes(StandardCharsets.UTF_8));
        long mtime = 1_700_000_000_000L;
        List<FileSource> files = new ArrayList<>();
        files.add(new PathSource(empty, "vacío.txt", "text/plain", 0));
        files.add(new PathSource(utf, "canción ñ «prueba».txt", "text/plain", 0));
        files.add(new PathSource(video, "video.mp4", "video/mp4", mtime));
        files.add(new PathSource(bad, "a:b*c?.jpg", "image/jpeg", 0));
        files.add(new PathSource(evil, "..\\..\\evil.txt", null, 0));
        PhaseLog filesLog = new PhaseLog();
        long t0 = System.nanoTime();
        Transfer.Result r = new Transfer(testTransport(), SocketFactory.getDefault(), hello, filesLog).sendFiles(files);
        double secs = (System.nanoTime() - t0) / 1e9;
        out.printf("  · %d archivos, %.1f MB en %.2f s (%.0f MB/s) por %s%n", r.files, r.bytes / (double) MB, secs, r.bytes / (double) MB / secs, r.transport);
        check("wifi".equals(r.transport), "los datos viajaron por Wi-Fi turbo");
        checkFile(received, "vacío.txt", empty);
        checkFile(received, "canción ñ «prueba».txt", utf);
        checkFile(received, "video.mp4", video);
        checkFile(received, "a_b_c_.jpg", bad);
        checkFile(received, "evil.txt", evil);
        check(!Files.exists(received.getParent().resolve("evil.txt")) && !Files.exists(received.getParent().getParent().resolve("evil.txt")),
                "un nombre con «..\\» no escapa de la carpeta de recepción");
        FileTime ft = Files.getLastModifiedTime(received.resolve("video.mp4"));
        check(ft.toMillis() == mtime, "se conserva la fecha de modificación original (" + ft + ")");

        // 4. Corte de Wi-Fi a mitad de un archivo: debe reconectar (Bluetooth → Wi-Fi) y reanudar.
        Path wifiResume = write(work, "wifi.bin", random(30 * MB, 3));
        RecordingSocketFactory sockets = new RecordingSocketFactory();
        PhaseLog cutLog = new PhaseLog() {
            @Override public void onProgress(Transfer.ProgressInfo p) {
                super.onProgress(p);
                if (!cut && "wifi".equals(p.transport) && p.sent > 12 * MB) {
                    cut = true;
                    sockets.closeFirst();
                }
            }
        };
        Transfer.Result r4 = new Transfer(testTransport(), sockets, hello, cutLog)
                .sendFiles(Collections.singletonList(new PathSource(wifiResume, "reanudar-wifi.bin", null, 0)));
        check(cutLog.cut && cutLog.phases.contains(Transfer.Phase.RECONNECTING), "corte de Wi-Fi a mitad de archivo detectado y reconectado");
        check("wifi".equals(r4.transport) && sockets.created >= 2, "volvió a Wi-Fi turbo tras reconectar (" + sockets.created + " conexiones TLS)");
        checkFile(received, "reanudar-wifi.bin", wifiResume);

        // 5. Solo Bluetooth, con un corte del canal a los 3 MB.
        Path btResume = write(work, "bt.bin", random(8 * MB, 4));
        CuttingConnector cutting = new CuttingConnector(3 * MB);
        Transfer.Result r5 = new Transfer(cutting, null, hello, new PhaseLog())
                .sendFiles(Collections.singletonList(new PathSource(btResume, "reanudar-bt.bin", null, 0)));
        check("bluetooth".equals(r5.transport), "envío completo por el canal Bluetooth (sin Wi-Fi)");
        check(cutting.connections >= 2, "corte del canal Bluetooth reanudado (" + cutting.connections + " conexiones)");
        checkFile(received, "reanudar-bt.bin", btResume);

        // 6. Nombres repetidos: nunca se sobrescribe.
        Path notes1 = write(work, "n1.txt", "primera".getBytes(StandardCharsets.UTF_8));
        Path notes2 = write(work, "n2.txt", "segunda".getBytes(StandardCharsets.UTF_8));
        new Transfer(testTransport(), null, hello, new PhaseLog()).sendFiles(Collections.singletonList(new PathSource(notes1, "notas.txt", null, 0)));
        new Transfer(testTransport(), null, hello, new PhaseLog()).sendFiles(Collections.singletonList(new PathSource(notes2, "notas.txt", null, 0)));
        checkFile(received, "notas.txt", notes1);
        checkFile(received, "notas (1).txt", notes2);

        // 7. Seguridad del canal Wi-Fi.
        Session bt = Session.open(testTransport().connect(), helloMessage(hello, null), null);
        Session.WifiOffer offer = bt.wifiOffer();
        bt.bye();
        bt.close();
        check(offer != null, "oferta Wi-Fi: puerto " + (offer == null ? "?" : offer.port + ", huella " + offer.fingerprint.substring(0, 16) + "…"));
        Duplex w = Tls.connect(SocketFactory.getDefault(), "127.0.0.1", offer.port, offer.fingerprint, 2000);
        try {
            Session.open(w, helloMessage(hello, "llave-falsa"), null);
            check(false, "una llave Wi-Fi falsa no debería aceptarse");
        } catch (DtwException e) {
            check("token".equals(e.code), "llave Wi-Fi falsa rechazada");
        } finally {
            w.close();
        }
        Duplex w2 = Tls.connect(SocketFactory.getDefault(), "127.0.0.1", offer.port, offer.fingerprint, 2000);
        try {
            Session.open(w2, helloMessage(hello("otro-dispositivo", "Otro"), offer.token), null);
            check(false, "la llave de un dispositivo no debería servir para otro");
        } catch (DtwException e) {
            check("token".equals(e.code), "la llave está ligada al dispositivo que la recibió");
        } finally {
            w2.close();
        }
        try {
            Tls.connect(SocketFactory.getDefault(), "127.0.0.1", offer.port, "00000000000000000000000000000000000000000000000000000000000000ff", 2000).close();
            check(false, "una huella distinta no debería aceptarse");
        } catch (IOException e) {
            check(true, "certificado con huella distinta rechazado (" + e.getClass().getSimpleName() + ")");
        }
    }

    // ───────────────────────── Utilidades ─────────────────────────

    static Map<String, Object> hello(String id, String name) {
        return Json.obj("deviceId", id, "deviceName", name, "app", "test", "appVersion", "1.0.0");
    }

    static Map<String, Object> helloMessage(Map<String, Object> base, String token) {
        Map<String, Object> m = Json.obj("t", "hello", "v", 1);
        m.putAll(base);
        if (token != null) m.put("token", token);
        return m;
    }

    static Transfer.Connector testTransport() {
        return () -> {
            Socket s = new Socket();
            s.connect(new InetSocketAddress(InetAddress.getLoopbackAddress(), TEST_PORT), 3000);
            s.setTcpNoDelay(true);
            return new Duplex(new BufferedInputStream(s.getInputStream(), 16 * 1024),
                    new BufferedOutputStream(s.getOutputStream(), 32 * 1024), s, "bluetooth");
        };
    }

    static void check(boolean ok, String what) {
        if (!ok) failures++;
        out.println((ok ? "  ✓ " : "  ✗ ") + what);
    }

    static void checkFile(Path folder, String name, Path original) throws Exception {
        Path saved = folder.resolve(name);
        boolean ok = Files.exists(saved) && Tls.sha256Hex(Files.readAllBytes(saved)).equals(Tls.sha256Hex(Files.readAllBytes(original)));
        check(ok, "«" + name + "» guardado e íntegro (SHA-256)" + (Files.exists(saved) ? ", " + Files.size(saved) + " bytes" : " — NO EXISTE"));
    }

    static Path write(Path dir, String name, byte[] data) throws IOException {
        Path p = dir.resolve(name);
        Files.write(p, data);
        return p;
    }

    static byte[] random(long size, long seed) {
        byte[] data = new byte[(int) size];
        new Random(seed).nextBytes(data);
        return data;
    }

    static class PhaseLog implements Transfer.Listener {
        final List<Transfer.Phase> phases = Collections.synchronizedList(new ArrayList<>());
        volatile boolean cut;

        @Override public void onPhase(Transfer.Phase phase) {
            phases.add(phase);
            out.println("    fase: " + phase);
        }

        @Override public void onProgress(Transfer.ProgressInfo p) { }
    }

    static final class PathSource implements FileSource {
        final Path path;
        final String name;
        final String mime;
        final long modified;

        PathSource(Path path, String name, String mime, long modified) {
            this.path = path;
            this.name = name;
            this.mime = mime;
            this.modified = modified;
        }

        @Override public String name() { return name; }
        @Override public long size() {
            try { return Files.size(path); } catch (IOException e) { throw new IllegalStateException(e); }
        }
        @Override public String mime() { return mime; }
        @Override public long modified() { return modified; }
        @Override public InputStream open(long offset) throws IOException {
            InputStream in = Files.newInputStream(path);
            long skipped = 0;
            while (skipped < offset) {
                long n = in.skip(offset - skipped);
                if (n <= 0) throw new EOFException();
                skipped += n;
            }
            return new BufferedInputStream(in, 64 * 1024);
        }
    }

    /** Fábrica de sockets que permite cortar la primera conexión Wi-Fi. */
    static final class RecordingSocketFactory extends SocketFactory {
        final List<Socket> sockets = Collections.synchronizedList(new ArrayList<>());
        volatile int created;

        @Override public Socket createSocket() {
            Socket s = new Socket();
            sockets.add(s);
            created++;
            return s;
        }

        void closeFirst() {
            try { sockets.get(0).close(); } catch (IOException ignored) { }
        }

        @Override public Socket createSocket(String host, int port) { throw new UnsupportedOperationException(); }
        @Override public Socket createSocket(String host, int port, InetAddress local, int localPort) { throw new UnsupportedOperationException(); }
        @Override public Socket createSocket(InetAddress host, int port) { throw new UnsupportedOperationException(); }
        @Override public Socket createSocket(InetAddress host, int port, InetAddress local, int localPort) { throw new UnsupportedOperationException(); }
    }

    /** Simula que el enlace "Bluetooth" se cae tras N bytes en la primera conexión. */
    static final class CuttingConnector implements Transfer.Connector {
        final long cutAfter;
        int connections;

        CuttingConnector(long cutAfter) { this.cutAfter = cutAfter; }

        @Override public Duplex connect() throws IOException {
            connections++;
            Socket s = new Socket();
            s.connect(new InetSocketAddress(InetAddress.getLoopbackAddress(), TEST_PORT), 3000);
            OutputStream raw = s.getOutputStream();
            OutputStream o = connections == 1 ? new CutStream(raw, s, cutAfter) : raw;
            return new Duplex(new BufferedInputStream(s.getInputStream()), new BufferedOutputStream(o, 32 * 1024), s, "bluetooth");
        }
    }

    static final class CutStream extends FilterOutputStream {
        final Socket socket;
        final long limit;
        long written;

        CutStream(OutputStream out, Socket socket, long limit) {
            super(out);
            this.socket = socket;
            this.limit = limit;
        }

        @Override public void write(int b) throws IOException {
            write(new byte[] {(byte) b}, 0, 1);
        }

        @Override public void write(byte[] b, int off, int len) throws IOException {
            if (written + len > limit) {
                socket.close();
                throw new IOException("corte simulado del enlace");
            }
            written += len;
            out.write(b, off, len);
        }
    }
}
