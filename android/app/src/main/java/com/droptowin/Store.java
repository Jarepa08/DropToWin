package com.droptowin;

import android.content.Context;
import android.content.SharedPreferences;
import android.os.Build;
import android.provider.Settings;

import com.droptowin.core.Json;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Map;
import java.util.UUID;

/** Datos persistentes de la app: identidad del teléfono y PCs conocidos. */
final class Store {
    static final String APP_VERSION = "1.0.0";

    static final class Pc {
        final String address;
        String btName;
        String pcId;
        String pcName;
        long lastUsed;

        Pc(String address) {
            this.address = address;
        }

        String label() {
            if (pcName != null && !pcName.isEmpty()) return pcName;
            return btName != null && !btName.isEmpty() ? btName : address;
        }
    }

    private Store() {}

    private static SharedPreferences prefs(Context c) {
        return c.getSharedPreferences("droptowin", Context.MODE_PRIVATE);
    }

    static synchronized String deviceId(Context c) {
        String id = prefs(c).getString("deviceId", null);
        if (id == null) {
            id = UUID.randomUUID().toString();
            prefs(c).edit().putString("deviceId", id).apply();
        }
        return id;
    }

    static String deviceName(Context c) {
        String name = Settings.Global.getString(c.getContentResolver(), Settings.Global.DEVICE_NAME);
        if (name != null && !name.trim().isEmpty()) return name.trim();
        String maker = Build.MANUFACTURER == null ? "" : Build.MANUFACTURER;
        String model = Build.MODEL == null ? "Android" : Build.MODEL;
        if (maker.isEmpty() || model.toLowerCase().startsWith(maker.toLowerCase())) return model;
        return Character.toUpperCase(maker.charAt(0)) + maker.substring(1) + " " + model;
    }

    /** Campos del saludo del protocolo que identifican a este teléfono. */
    static Map<String, Object> hello(Context c) {
        return Json.obj("deviceId", deviceId(c), "deviceName", deviceName(c), "app", "android", "appVersion", APP_VERSION);
    }

    static synchronized List<Pc> pcs(Context c) {
        List<Pc> list = new ArrayList<>();
        String raw = prefs(c).getString("pcs", "[]");
        try {
            Object parsed = Json.parse(raw);
            if (parsed instanceof List) {
                for (Object o : (List<?>) parsed) {
                    if (!(o instanceof Map)) continue;
                    @SuppressWarnings("unchecked") Map<String, Object> m = (Map<String, Object>) o;
                    String address = Json.str(m, "address");
                    if (address == null) continue;
                    Pc pc = new Pc(address);
                    pc.btName = Json.str(m, "btName");
                    pc.pcId = Json.str(m, "pcId");
                    pc.pcName = Json.str(m, "pcName");
                    pc.lastUsed = Json.lng(m, "lastUsed", 0);
                    list.add(pc);
                }
            }
        } catch (IllegalArgumentException ignored) {
            // Datos dañados: se empieza de cero.
        }
        Collections.sort(list, (a, b) -> Long.compare(b.lastUsed, a.lastUsed));
        return list;
    }

    private static void save(Context c, List<Pc> pcs) {
        List<Object> out = new ArrayList<>();
        for (Pc pc : pcs) {
            out.add(Json.obj("address", pc.address, "btName", pc.btName, "pcId", pc.pcId,
                    "pcName", pc.pcName, "lastUsed", pc.lastUsed));
        }
        prefs(c).edit().putString("pcs", Json.stringify(out)).apply();
    }

    static synchronized void savePc(Context c, Pc pc) {
        List<Pc> list = pcs(c);
        for (int i = 0; i < list.size(); i++) {
            if (list.get(i).address.equals(pc.address)) {
                list.remove(i);
                break;
            }
        }
        list.add(0, pc);
        save(c, list);
    }

    static synchronized Pc find(Context c, String address) {
        for (Pc pc : pcs(c)) if (pc.address.equals(address)) return pc;
        return null;
    }

    /** El último PC usado (el que se muestra en la pantalla principal). */
    static Pc defaultPc(Context c) {
        List<Pc> list = pcs(c);
        return list.isEmpty() ? null : list.get(0);
    }

    static synchronized void touch(Context c, String address, String pcName) {
        Pc pc = find(c, address);
        if (pc == null) return;
        if (pcName != null) pc.pcName = pcName;
        pc.lastUsed = System.currentTimeMillis();
        savePc(c, pc);
    }

    static synchronized void remove(Context c, String address) {
        List<Pc> list = pcs(c);
        for (int i = 0; i < list.size(); i++) {
            if (list.get(i).address.equals(address)) {
                list.remove(i);
                break;
            }
        }
        save(c, list);
    }

    static boolean askedPermissions(Context c) {
        return prefs(c).getBoolean("askedPermissions", false);
    }

    static void setAskedPermissions(Context c) {
        prefs(c).edit().putBoolean("askedPermissions", true).apply();
    }
}
