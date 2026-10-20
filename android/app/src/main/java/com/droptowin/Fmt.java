package com.droptowin;

import java.util.Locale;

/** Formatos en español: tamaños, velocidades y tiempos. */
final class Fmt {
    private static final Locale ES = new Locale("es", "ES");

    private Fmt() {}

    static String size(long bytes) {
        if (bytes < 1024) return bytes + " B";
        String[] units = {"KB", "MB", "GB", "TB"};
        double value = bytes;
        int unit = -1;
        while (value >= 1024 && unit < units.length - 1) {
            value /= 1024;
            unit++;
        }
        return String.format(ES, value < 10 ? "%.1f %s" : "%.0f %s", value, units[unit]);
    }

    static String speed(double bytesPerSecond) {
        return bytesPerSecond < 1 ? "" : size((long) bytesPerSecond) + "/s";
    }

    static String duration(double seconds) {
        if (Double.isNaN(seconds) || Double.isInfinite(seconds) || seconds < 0) return "";
        if (seconds < 60) return (int) Math.ceil(seconds) + " s";
        if (seconds < 3600) return (int) Math.ceil(seconds / 60) + " min";
        return (int) (seconds / 3600) + " h " + (int) Math.ceil(seconds % 3600 / 60) + " min";
    }

    static String ago(long timeMs) {
        if (timeMs <= 0) return "nunca";
        long minutes = (System.currentTimeMillis() - timeMs) / 60_000;
        if (minutes < 1) return "hace un momento";
        if (minutes < 60) return "hace " + minutes + " min";
        long hours = minutes / 60;
        if (hours < 24) return "hace " + hours + " h";
        long days = hours / 24;
        return days == 1 ? "ayer" : "hace " + days + " días";
    }

    static String transport(String transport) {
        return "wifi".equals(transport) ? "Wi-Fi" : "Bluetooth";
    }
}
