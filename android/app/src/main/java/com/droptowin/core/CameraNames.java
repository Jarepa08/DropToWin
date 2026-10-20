package com.droptowin.core;

import java.time.Instant;
import java.time.LocalDateTime;
import java.time.ZoneId;
import java.time.ZoneOffset;
import java.time.format.DateTimeFormatter;
import java.time.format.DateTimeParseException;

/**
 * Google Fotos y el selector de fotos de Android comparten las fotos con su id interno como nombre
 * («1000012345.jpg»). Para esos casos se compone un nombre como el de la cámara a partir de la fecha
 * de captura: «IMG_20261020_154312.jpg» o, para videos, «VID_20261020_154312.mp4».
 */
public final class CameraNames {
    private CameraNames() {}

    private static final DateTimeFormatter NAME = DateTimeFormatter.ofPattern("uuuuMMdd_HHmmss");
    private static final DateTimeFormatter EXIF = DateTimeFormatter.ofPattern("uuuu:MM:dd HH:mm:ss");
    private static final DateTimeFormatter VIDEO = DateTimeFormatter.ofPattern("uuuuMMdd'T'HHmmss");

    /** Nombre que, sin la extensión, son solo dígitos: «1000012345.jpg», «1000012345». */
    public static boolean isNumeric(String name) {
        int dot = name.lastIndexOf('.');
        String stem = dot > 0 ? name.substring(0, dot) : name;
        if (stem.isEmpty()) return false;
        for (int i = 0; i < stem.length(); i++) {
            char ch = stem.charAt(i);
            if (ch < '0' || ch > '9') return false;
        }
        return true;
    }

    /** «IMG_aaaammdd_hhmmss» («VID_…» si es un video) con la extensión de {@code name}. */
    public static String fromDate(String name, boolean video, LocalDateTime taken) {
        int dot = name.lastIndexOf('.');
        return (video ? "VID_" : "IMG_") + taken.format(NAME) + (dot > 0 ? name.substring(dot) : "");
    }

    /** Fecha EXIF («2026:10:20 15:43:12», hora local de la cámara) o null si falta o no es válida. */
    public static LocalDateTime parseExif(String value) {
        if (value == null) return null;
        String v = value.trim();
        if (v.length() < 19) return null;
        v = v.substring(0, 10).replace('-', ':') + v.substring(10, 19); // Algunas cámaras usan guiones.
        try {
            return plausible(LocalDateTime.parse(v, EXIF));
        } catch (DateTimeParseException e) {
            return null;
        }
    }

    /** Fecha de un video («20261020T214312.000Z», en UTC) en la zona {@code zone}, o null. */
    public static LocalDateTime parseVideo(String value, ZoneId zone) {
        if (value == null || value.length() < 15) return null;
        try {
            LocalDateTime utc = LocalDateTime.parse(value.substring(0, 15), VIDEO);
            return plausible(utc.atOffset(ZoneOffset.UTC).atZoneSameInstant(zone).toLocalDateTime());
        } catch (DateTimeParseException e) {
            return null;
        }
    }

    /** Milisegundos Unix en la zona {@code zone}, o null si no hay fecha. */
    public static LocalDateTime fromMillis(long millis, ZoneId zone) {
        return millis > 0 ? plausible(LocalDateTime.ofInstant(Instant.ofEpochMilli(millis), zone)) : null;
    }

    /** Descarta las fechas vacías de algunos formatos (año 0, 1904 o 1970). */
    private static LocalDateTime plausible(LocalDateTime date) {
        return date.getYear() > 1970 ? date : null;
    }
}
