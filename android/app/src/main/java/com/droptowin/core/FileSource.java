package com.droptowin.core;

import java.io.IOException;
import java.io.InputStream;

/** Un archivo a enviar, sea cual sea su origen (content:// en Android, File en el escritorio). */
public interface FileSource {
    String name();

    long size();

    /** Tipo MIME o null. */
    String mime();

    /** Fecha de modificación en milisegundos Unix, o 0 si no se conoce. */
    long modified();

    /** Abre el contenido a partir de {@code offset} (para reanudar envíos cortados). */
    InputStream open(long offset) throws IOException;
}
