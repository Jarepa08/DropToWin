package com.droptowin;

import android.content.Context;
import android.content.Intent;
import android.content.pm.ShortcutInfo;
import android.content.pm.ShortcutManager;
import android.graphics.drawable.Icon;
import android.os.Build;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

/**
 * Accesos de "Compartir directo": cada PC conocido aparece en la fila superior del menú
 * Compartir de Android, y al tocarlo el envío empieza sin más preguntas.
 */
final class Shortcuts {
    static final String SHARE_CATEGORY = "com.droptowin.category.PC_SHARE_TARGET";
    static final String ID_PREFIX = "pc:";

    private Shortcuts() {}

    static void update(Context c) {
        ShortcutManager manager = c.getSystemService(ShortcutManager.class);
        if (manager == null) return;
        List<Store.Pc> pcs = Store.pcs(c);
        int max = Math.min(manager.getMaxShortcutCountPerActivity(), 4);
        List<ShortcutInfo> shortcuts = new ArrayList<>();
        for (int i = 0; i < pcs.size() && i < max; i++) {
            Store.Pc pc = pcs.get(i);
            ShortcutInfo.Builder builder = new ShortcutInfo.Builder(c, ID_PREFIX + pc.address)
                    .setShortLabel(pc.label())
                    .setLongLabel("Enviar a " + pc.label())
                    .setIcon(Icon.createWithResource(c, R.mipmap.ic_shortcut_pc))
                    .setIntent(new Intent(Intent.ACTION_MAIN, null, c, MainActivity.class))
                    .setCategories(Collections.singleton(SHARE_CATEGORY))
                    .setRank(i);
            if (Build.VERSION.SDK_INT >= 29) builder.setLongLived(true);
            shortcuts.add(builder.build());
        }
        try {
            manager.setDynamicShortcuts(shortcuts);
        } catch (RuntimeException ignored) {
            // Límite de frecuencia del sistema: se actualizarán en la próxima ocasión.
        }
    }

    /** Dirección Bluetooth del PC elegido como "Compartir directo", o null. */
    static String addressFromShortcutId(String shortcutId) {
        return shortcutId != null && shortcutId.startsWith(ID_PREFIX) ? shortcutId.substring(ID_PREFIX.length()) : null;
    }
}
