package com.droptowin.core;

import java.io.Closeable;
import java.io.IOException;
import java.io.InputStream;
import java.net.ProtocolException;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Map;

/** Una sesión del protocolo DropToWin abierta sobre un {@link Duplex}. */
public final class Session implements Closeable {

    public interface ApprovalListener { void onApprovalPending(); }

    public interface Progress { void onPosition(long bytesOfFile); }

    public interface Cancel { boolean isCancelled(); }

    public static final class Batch {
        public final String id;
        public final List<String> fileIds;

        Batch(String id, List<String> fileIds) {
            this.id = id;
            this.fileIds = fileIds;
        }
    }

    /** Datos para el Wi-Fi turbo, recibidos por el canal Bluetooth ya autenticado. */
    public static final class WifiOffer {
        public final int port;
        public final String fingerprint;
        public final String token;
        public final List<String> addresses;

        WifiOffer(int port, String fingerprint, String token, List<String> addresses) {
            this.port = port;
            this.fingerprint = fingerprint;
            this.token = token;
            this.addresses = addresses;
        }
    }

    private final Duplex link;
    private final Frames io;
    private final Map<String, Object> welcome;

    private Session(Duplex link, Frames io, Map<String, Object> welcome) {
        this.link = link;
        this.io = io;
        this.welcome = welcome;
    }

    /** Envía el saludo y espera la bienvenida (puede tardar si el PC pide aprobación al usuario). */
    public static Session open(Duplex link, Map<String, Object> hello, ApprovalListener listener)
            throws IOException, DtwException {
        Frames io = new Frames(link.in, link.out);
        io.writeJson(hello);
        while (true) {
            Map<String, Object> m = io.readJson();
            String t = Json.str(m, "t");
            if ("pending".equals(t)) {
                if (listener != null) listener.onApprovalPending();
                continue;
            }
            if ("denied".equals(t)) {
                throw new DtwException(orElse(Json.str(m, "reason"), "denied"),
                        orElse(Json.str(m, "message"), "El PC rechazó la conexión."));
            }
            if (!"welcome".equals(t)) throw new ProtocolException("Respuesta inesperada del PC");
            return new Session(link, io, m);
        }
    }

    Duplex link() { return link; }

    public String transport() { return link.transport; }

    public String pcId() { return Json.str(welcome, "pcId"); }

    public String pcName() { return orElse(Json.str(welcome, "pcName"), "PC"); }

    public WifiOffer wifiOffer() {
        Map<String, Object> w = Json.map(welcome, "wifi");
        if (w == null) return null;
        String fp = Json.str(w, "fp");
        String token = Json.str(w, "token");
        List<Object> addrs = Json.list(w, "addrs");
        long port = Json.lng(w, "port", -1);
        if (fp == null || token == null || addrs == null || port <= 0 || port > 65535) return null;
        List<String> list = new ArrayList<>();
        for (Object a : addrs) if (a instanceof String) list.add((String) a);
        return new WifiOffer((int) port, fp, token, Collections.unmodifiableList(list));
    }

    public void sendText(String text) throws IOException, DtwException {
        io.writeJson(Json.obj("t", "text", "text", text));
        expect("ok");
    }

    public Batch createBatch(List<? extends FileSource> files) throws IOException, DtwException {
        List<Object> specs = new ArrayList<>();
        for (FileSource f : files) {
            Map<String, Object> spec = Json.obj("name", f.name(), "size", f.size());
            if (f.mime() != null) spec.put("mime", f.mime());
            if (f.modified() > 0) spec.put("modified", f.modified());
            specs.add(spec);
        }
        io.writeJson(Json.obj("t", "batch", "files", specs));
        Map<String, Object> reply = expect("batch-ok");
        String id = Json.str(reply, "batchId");
        List<Object> list = Json.list(reply, "files");
        if (id == null || list == null || list.size() != files.size()) {
            throw new ProtocolException("Respuesta de lote inválida");
        }
        List<String> ids = new ArrayList<>();
        for (Object o : list) {
            @SuppressWarnings("unchecked")
            String fileId = o instanceof Map ? Json.str((Map<String, Object>) o, "id") : null;
            if (fileId == null) throw new ProtocolException("Respuesta de lote inválida");
            ids.add(fileId);
        }
        return new Batch(id, ids);
    }

    /**
     * Envía un archivo. El PC indica desde qué byte continuar (reanudación) y responde con el
     * nombre con el que lo guardó.
     */
    public String sendFile(String batchId, String fileId, FileSource source, int chunkSize,
                           Progress progress, Cancel cancel) throws IOException, DtwException {
        io.writeJson(Json.obj("t", "file", "batchId", batchId, "fileId", fileId));
        Map<String, Object> reply = readReply();
        String t = Json.str(reply, "t");
        if ("file-done".equals(t)) {
            progress.onPosition(source.size());
            return Json.str(reply, "savedAs");
        }
        if (!"file-ready".equals(t)) throw new ProtocolException("Respuesta inesperada del PC");

        long size = source.size();
        long position = Json.lng(reply, "offset", -1);
        if (position < 0 || position > size) throw new ProtocolException("Posición de reanudación inválida");
        progress.onPosition(position);

        if (position < size) {
            InputStream in;
            try {
                in = source.open(position);
            } catch (IOException | SecurityException e) {
                throw new DtwException("source", "No se pudo leer «" + source.name() + "»: " + e.getMessage());
            }
            try {
                byte[] buffer = new byte[chunkSize];
                while (position < size) {
                    if (cancel.isCancelled()) throw new DtwException("cancelled", "Envío cancelado.");
                    int want = (int) Math.min(buffer.length, size - position);
                    int n;
                    try {
                        n = in.read(buffer, 0, want);
                    } catch (IOException e) {
                        throw new DtwException("source", "Error al leer «" + source.name() + "»: " + e.getMessage());
                    }
                    if (n < 0) {
                        throw new DtwException("source", "«" + source.name() + "» cambió de tamaño mientras se enviaba.");
                    }
                    io.writeData(buffer, 0, n);
                    position += n;
                    progress.onPosition(position);
                }
                io.flush();
            } finally {
                try {
                    in.close();
                } catch (IOException ignored) {
                    // Solo se estaba leyendo.
                }
            }
        }

        Map<String, Object> done = readReply();
        if (!"file-done".equals(Json.str(done, "t"))) throw new ProtocolException("Respuesta inesperada del PC");
        return Json.str(done, "savedAs");
    }

    public void bye() {
        try {
            io.writeJson(Json.obj("t", "bye"));
        } catch (IOException ignored) {
            // La sesión termina de todos modos.
        }
    }

    @Override
    public void close() {
        link.close();
    }

    private Map<String, Object> readReply() throws IOException, DtwException {
        Map<String, Object> m = io.readJson();
        if ("error".equals(Json.str(m, "t"))) {
            throw new DtwException(orElse(Json.str(m, "code"), "error"),
                    orElse(Json.str(m, "message"), "El PC devolvió un error."));
        }
        return m;
    }

    private Map<String, Object> expect(String type) throws IOException, DtwException {
        Map<String, Object> m = readReply();
        if (!type.equals(Json.str(m, "t"))) throw new ProtocolException("Respuesta inesperada del PC");
        return m;
    }

    private static String orElse(String value, String fallback) {
        return value != null && !value.isEmpty() ? value : fallback;
    }
}
