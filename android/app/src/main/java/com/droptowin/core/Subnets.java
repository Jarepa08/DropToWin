package com.droptowin.core;

import java.net.Inet4Address;
import java.net.InterfaceAddress;
import java.net.NetworkInterface;
import java.net.SocketException;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

/** Decide qué direcciones del PC merece la pena probar: las que comparten subred con este equipo. */
final class Subnets {
    private Subnets() {}

    /** @param offered direcciones del PC en formato "192.168.1.20/24" */
    static List<String> reachable(List<String> offered) {
        List<int[]> local = localIPv4();
        List<String> result = new ArrayList<>();
        for (String entry : offered) {
            int slash = entry.indexOf('/');
            if (slash <= 0) continue;
            Integer pc = parseIPv4(entry.substring(0, slash));
            int prefix;
            try {
                prefix = Integer.parseInt(entry.substring(slash + 1));
            } catch (NumberFormatException e) {
                continue;
            }
            if (pc == null || prefix < 1 || prefix > 32) continue;
            int mask = prefix == 32 ? -1 : ~((1 << (32 - prefix)) - 1);
            for (int[] addr : local) {
                if ((addr[0] & mask) == (pc & mask)) {
                    result.add(entry.substring(0, slash));
                    break;
                }
            }
        }
        return result;
    }

    private static List<int[]> localIPv4() {
        List<int[]> list = new ArrayList<>();
        try {
            for (NetworkInterface ni : Collections.list(NetworkInterface.getNetworkInterfaces())) {
                if (!ni.isUp()) continue;
                for (InterfaceAddress ia : ni.getInterfaceAddresses()) {
                    if (!(ia.getAddress() instanceof Inet4Address)) continue;
                    byte[] b = ia.getAddress().getAddress();
                    int value = ((b[0] & 0xFF) << 24) | ((b[1] & 0xFF) << 16) | ((b[2] & 0xFF) << 8) | (b[3] & 0xFF);
                    list.add(new int[] {value, ia.getNetworkPrefixLength()});
                }
            }
        } catch (SocketException | NullPointerException e) {
            // Sin interfaces de red: no habrá Wi-Fi turbo.
        }
        return list;
    }

    private static Integer parseIPv4(String text) {
        String[] parts = text.split("\\.");
        if (parts.length != 4) return null;
        int value = 0;
        for (String part : parts) {
            int n;
            try {
                n = Integer.parseInt(part);
            } catch (NumberFormatException e) {
                return null;
            }
            if (n < 0 || n > 255) return null;
            value = (value << 8) | n;
        }
        return value;
    }
}
