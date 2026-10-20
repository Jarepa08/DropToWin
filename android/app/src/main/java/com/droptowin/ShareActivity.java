package com.droptowin;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.ClipData;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;
import android.widget.Toast;

import java.util.ArrayList;
import java.util.List;

/**
 * Destino del menú Compartir ("Enviar al PC"). Si hay un solo PC conocido (o se eligió uno con
 * "Compartir directo") el envío empieza al instante y esta pantalla, transparente, se cierra.
 */
public class ShareActivity extends Activity {
    private static final String EXTRA_SHORTCUT_ID = "android.intent.extra.shortcut.ID";

    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        Intent intent = getIntent();
        final ArrayList<Uri> uris = extractUris(intent);
        final String text = uris.isEmpty() ? extractText(intent) : null;
        if (uris.isEmpty() && text == null) {
            toast("No hay nada que enviar.");
            finish();
            return;
        }

        final List<Store.Pc> pcs = Store.pcs(this);
        if (pcs.isEmpty() || !Bt.hasPermission(this) || !Bt.isEnabled(this)) {
            startActivity(new Intent(this, MainActivity.class)
                    .putExtra(MainActivity.EXTRA_SETUP, true)
                    .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
            toast(pcs.isEmpty() ? "Primero elige tu PC en DropToWin." : "Activa el Bluetooth y vuelve a compartir.");
            finish();
            return;
        }

        Store.Pc target = null;
        String shortcutAddress = Shortcuts.addressFromShortcutId(intent.getStringExtra(EXTRA_SHORTCUT_ID));
        if (shortcutAddress != null) target = Store.find(this, shortcutAddress);
        if (target == null && pcs.size() == 1) target = pcs.get(0);
        if (target != null) {
            send(target, uris, text);
            return;
        }

        String[] labels = new String[pcs.size()];
        for (int i = 0; i < pcs.size(); i++) labels[i] = pcs.get(i).label();
        new AlertDialog.Builder(this)
                .setTitle("Enviar a…")
                .setItems(labels, (d, which) -> send(pcs.get(which), uris, text))
                .setOnCancelListener(d -> finish())
                .show();
    }

    private void send(Store.Pc pc, ArrayList<Uri> uris, String text) {
        if (!uris.isEmpty()) {
            TransferService.sendUris(this, pc.address, uris);
            toast(uris.size() == 1 ? "Enviando a " + pc.label() + "…" : "Enviando " + uris.size() + " archivos a " + pc.label() + "…");
        } else {
            TransferService.sendText(this, pc.address, text);
            toast("Enviando texto a " + pc.label() + "…");
        }
        finish();
    }

    private void toast(String message) {
        Toast.makeText(this, message, Toast.LENGTH_SHORT).show();
    }

    @SuppressWarnings("deprecation")
    static ArrayList<Uri> extractUris(Intent intent) {
        ArrayList<Uri> list = new ArrayList<>();
        if (Intent.ACTION_SEND_MULTIPLE.equals(intent.getAction())) {
            ArrayList<Uri> extra = intent.getParcelableArrayListExtra(Intent.EXTRA_STREAM);
            if (extra != null) for (Uri u : extra) if (u != null) list.add(u);
        } else if (Intent.ACTION_SEND.equals(intent.getAction())) {
            Uri u = intent.getParcelableExtra(Intent.EXTRA_STREAM);
            if (u != null) list.add(u);
        }
        ClipData clip = intent.getClipData();
        if (list.isEmpty() && clip != null) {
            for (int i = 0; i < clip.getItemCount(); i++) {
                Uri u = clip.getItemAt(i).getUri();
                if (u != null) list.add(u);
            }
        }
        return list;
    }

    static String extractText(Intent intent) {
        CharSequence text = intent.getCharSequenceExtra(Intent.EXTRA_TEXT);
        if (text != null && !text.toString().trim().isEmpty()) return text.toString();
        String subject = intent.getStringExtra(Intent.EXTRA_SUBJECT);
        return subject != null && !subject.trim().isEmpty() ? subject : null;
    }
}
