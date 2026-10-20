package com.droptowin.core;

import java.io.DataInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.ProtocolException;
import java.nio.charset.StandardCharsets;
import java.util.Map;

/**
 * Tramas del protocolo: [u32 big-endian longitud][1 byte tipo][carga].
 * Tipo 'J' = objeto JSON UTF-8, tipo 'D' = bytes del archivo en curso. Ver PROTOCOLO.md.
 */
public final class Frames {
    public static final int MAX_PAYLOAD = 1 << 20;
    public static final byte JSON = 'J';
    public static final byte DATA = 'D';

    private final DataInputStream in;
    private final OutputStream out;
    private final byte[] readHeader = new byte[5];
    private final byte[] writeHeader = new byte[5];
    private byte[] buffer = new byte[8 * 1024];

    public Frames(InputStream in, OutputStream out) {
        this.in = new DataInputStream(in);
        this.out = out;
    }

    public synchronized void writeJson(Map<String, Object> message) throws IOException {
        byte[] bytes = Json.stringify(message).getBytes(StandardCharsets.UTF_8);
        writeFrame(JSON, bytes, 0, bytes.length);
        out.flush();
    }

    public synchronized void writeData(byte[] data, int offset, int length) throws IOException {
        writeFrame(DATA, data, offset, length);
    }

    public void flush() throws IOException {
        out.flush();
    }

    private void writeFrame(byte kind, byte[] data, int offset, int length) throws IOException {
        if (length > MAX_PAYLOAD) throw new IOException("Trama demasiado grande");
        writeHeader[0] = (byte) (length >>> 24);
        writeHeader[1] = (byte) (length >>> 16);
        writeHeader[2] = (byte) (length >>> 8);
        writeHeader[3] = (byte) length;
        writeHeader[4] = kind;
        out.write(writeHeader, 0, 5);
        out.write(data, offset, length);
    }

    /** Lee el siguiente mensaje JSON (el PC nunca envía tramas de datos al teléfono). */
    public Map<String, Object> readJson() throws IOException {
        in.readFully(readHeader);
        int length = ((readHeader[0] & 0xFF) << 24) | ((readHeader[1] & 0xFF) << 16)
                | ((readHeader[2] & 0xFF) << 8) | (readHeader[3] & 0xFF);
        if (length < 0 || length > MAX_PAYLOAD) throw new ProtocolException("Trama demasiado grande");
        if (readHeader[4] != JSON) throw new ProtocolException("Se esperaba un mensaje JSON");
        if (buffer.length < length) buffer = new byte[Math.max(length, buffer.length * 2)];
        in.readFully(buffer, 0, length);
        try {
            return Json.parseObject(new String(buffer, 0, length, StandardCharsets.UTF_8));
        } catch (IllegalArgumentException e) {
            throw new ProtocolException(e.getMessage());
        }
    }
}
