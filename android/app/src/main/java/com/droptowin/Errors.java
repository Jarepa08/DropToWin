package com.droptowin;

import com.droptowin.core.DtwException;

import java.io.IOException;

/** Mensajes de error comprensibles para el usuario. */
final class Errors {
    static final String BT_CONNECT_FAILED = "No se pudo conectar por Bluetooth con el PC.";

    private Errors() {}

    static String message(Exception e) {
        if (e instanceof DtwException) return e.getMessage();
        if (e instanceof IOException && BT_CONNECT_FAILED.equals(e.getMessage())) {
            return BT_CONNECT_FAILED + "\n\nComprueba que DropToWin esté abierto en el PC, que el Bluetooth de "
                    + "ambos esté activado y que el teléfono esté emparejado con el PC.";
        }
        if (e instanceof IOException) {
            return "Se perdió la conexión con el PC" + (e.getMessage() != null ? ": " + e.getMessage() : ".");
        }
        return e.getClass().getSimpleName() + (e.getMessage() != null ? ": " + e.getMessage() : "");
    }
}
