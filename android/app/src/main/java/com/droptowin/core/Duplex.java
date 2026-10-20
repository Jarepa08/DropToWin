package com.droptowin.core;

import java.io.Closeable;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;

/** Un flujo bidireccional (socket Bluetooth RFCOMM o TLS por Wi-Fi). */
public final class Duplex implements Closeable {
    public final InputStream in;
    public final OutputStream out;
    /** "bluetooth" o "wifi". */
    public final String transport;
    private final Closeable resource;

    public Duplex(InputStream in, OutputStream out, Closeable resource, String transport) {
        this.in = in;
        this.out = out;
        this.resource = resource;
        this.transport = transport;
    }

    @Override
    public void close() {
        try {
            resource.close();
        } catch (IOException ignored) {
            // Cerrar es lo último que se hace; no hay nada que recuperar.
        }
    }
}
