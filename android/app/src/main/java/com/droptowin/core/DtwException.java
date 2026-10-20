package com.droptowin.core;

/**
 * Error que no se arregla reintentando: el PC rechazó la conexión, falta espacio, el archivo
 * de origen no se puede leer, el usuario canceló… El mensaje ya está listo para mostrarse.
 */
public class DtwException extends Exception {
    /** Código del protocolo ("rejected", "no-space", "unknown-batch"…) o local ("cancelled", "source"…). */
    public final String code;

    public DtwException(String code, String message) {
        super(message);
        this.code = code;
    }
}
