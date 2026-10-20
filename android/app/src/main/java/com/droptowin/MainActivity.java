package com.droptowin;

import android.Manifest;
import android.app.Activity;
import android.app.AlertDialog;
import android.bluetooth.BluetoothAdapter;
import android.bluetooth.BluetoothDevice;
import android.content.ActivityNotFoundException;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.provider.MediaStore;
import android.provider.Settings;
import android.view.Gravity;
import android.view.View;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.TextView;
import android.widget.Toast;

import com.droptowin.core.Transfer;

import java.util.ArrayList;
import java.util.List;

/** Pantalla principal: elegir el PC, probar la conexión y enviar sin pasar por otra app. */
public class MainActivity extends Activity {
    static final String EXTRA_SETUP = "setup";
    private static final int REQ_PERMISSIONS = 1;
    private static final int REQ_MEDIA = 2;
    private static final int REQ_FILES = 3;

    private View warningCard;
    private TextView warningText;
    private Button warningButton;
    private TextView pcName;
    private TextView pcStatus;
    private String lastStatus;
    private boolean setupPending;

    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        setContentView(R.layout.activity_main);
        warningCard = findViewById(R.id.warning_card);
        warningText = findViewById(R.id.warning_text);
        warningButton = findViewById(R.id.warning_button);
        pcName = findViewById(R.id.pc_name);
        pcStatus = findViewById(R.id.pc_status);
        findViewById(R.id.choose_pc).setOnClickListener(v -> choosePc());
        findViewById(R.id.test_pc).setOnClickListener(v -> testDefaultPc());
        findViewById(R.id.send_media).setOnClickListener(v -> pickMedia());
        findViewById(R.id.send_files).setOnClickListener(v -> pickFiles());
        findViewById(R.id.send_clipboard).setOnClickListener(v -> sendClipboard());

        setupPending = getIntent().getBooleanExtra(EXTRA_SETUP, false);
        if (state == null && missingPermissions().length > 0) requestPermissionsNow();
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        if (intent.getBooleanExtra(EXTRA_SETUP, false)) setupPending = true;
    }

    @Override
    protected void onResume() {
        super.onResume();
        refresh();
        if (setupPending && Bt.hasPermission(this) && Bt.isEnabled(this) && Store.pcs(this).isEmpty()) {
            setupPending = false;
            choosePc();
        }
    }

    private void refresh() {
        BluetoothAdapter adapter = Bt.adapter(this);
        if (adapter == null) {
            showWarning("Este teléfono no tiene Bluetooth, así que DropToWin no puede funcionar.", null, null);
        } else if (!Bt.hasPermission(this)) {
            showWarning("DropToWin necesita permiso para usar el Bluetooth y conectarse con tu PC.", "Permitir",
                    this::requestPermissionsNow);
        } else if (!adapter.isEnabled()) {
            showWarning("El Bluetooth está desactivado.", "Activar", this::enableBluetooth);
        } else {
            warningCard.setVisibility(View.GONE);
        }

        Store.Pc pc = Store.defaultPc(this);
        if (pc == null) {
            pcName.setText(R.string.no_pc);
            pcStatus.setText(R.string.no_pc_hint);
        } else {
            pcName.setText(pc.label());
            pcStatus.setText(lastStatus != null ? lastStatus : "Último uso: " + Fmt.ago(pc.lastUsed));
        }
    }

    private void showWarning(String text, String action, Runnable onClick) {
        warningCard.setVisibility(View.VISIBLE);
        warningText.setText(text);
        warningButton.setVisibility(action != null ? View.VISIBLE : View.GONE);
        warningButton.setText(action);
        warningButton.setOnClickListener(onClick != null ? v -> onClick.run() : null);
    }

    // ───────────────────────── Permisos y Bluetooth ─────────────────────────

    private String[] missingPermissions() {
        List<String> missing = new ArrayList<>();
        if (Build.VERSION.SDK_INT >= 31
                && checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) != PackageManager.PERMISSION_GRANTED) {
            missing.add(Manifest.permission.BLUETOOTH_CONNECT);
        }
        if (Build.VERSION.SDK_INT >= 33
                && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            missing.add(Manifest.permission.POST_NOTIFICATIONS);
        }
        return missing.toArray(new String[0]);
    }

    private void requestPermissionsNow() {
        String[] missing = missingPermissions();
        if (missing.length == 0) return;
        boolean deniedForever = Store.askedPermissions(this) && Build.VERSION.SDK_INT >= 31
                && !Bt.hasPermission(this)
                && !shouldShowRequestPermissionRationale(Manifest.permission.BLUETOOTH_CONNECT);
        if (deniedForever) {
            // Android ya no vuelve a preguntar: hay que activarlo en los ajustes de la app.
            startActivity(new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.fromParts("package", getPackageName(), null)));
            Toast.makeText(this, "Activa el permiso «Dispositivos cercanos»", Toast.LENGTH_LONG).show();
            return;
        }
        Store.setAskedPermissions(this);
        requestPermissions(missing, REQ_PERMISSIONS);
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] results) {
        refresh();
        if (requestCode == REQ_PERMISSIONS && Bt.hasPermission(this) && Bt.isEnabled(this) && Store.pcs(this).isEmpty()) {
            choosePc();
        }
    }

    private void enableBluetooth() {
        try {
            startActivity(new Intent(BluetoothAdapter.ACTION_REQUEST_ENABLE));
        } catch (SecurityException | ActivityNotFoundException e) {
            openBluetoothSettings();
        }
    }

    private void openBluetoothSettings() {
        try {
            startActivity(new Intent(Settings.ACTION_BLUETOOTH_SETTINGS));
        } catch (ActivityNotFoundException e) {
            startActivity(new Intent(Settings.ACTION_SETTINGS));
        }
    }

    private boolean ensureBluetoothReady() {
        if (Bt.adapter(this) == null) {
            Toast.makeText(this, "Este teléfono no tiene Bluetooth.", Toast.LENGTH_LONG).show();
            return false;
        }
        if (!Bt.hasPermission(this)) {
            requestPermissionsNow();
            return false;
        }
        if (!Bt.isEnabled(this)) {
            enableBluetooth();
            return false;
        }
        return true;
    }

    // ───────────────────────── Elegir y probar el PC ─────────────────────────

    private void choosePc() {
        if (!ensureBluetoothReady()) return;
        final List<BluetoothDevice> devices;
        try {
            devices = Bt.bondedDevices(this);
        } catch (SecurityException e) {
            requestPermissionsNow();
            return;
        }
        if (devices.isEmpty()) {
            new AlertDialog.Builder(this)
                    .setTitle("Empareja primero tu PC")
                    .setMessage("El teléfono y el PC deben estar emparejados por Bluetooth (solo una vez):\n\n"
                            + "1. En el PC abre Configuración › Bluetooth y dispositivos.\n"
                            + "2. En el teléfono abre Ajustes › Bluetooth y toca el nombre del PC.\n"
                            + "3. Confirma que el código coincide en ambos.\n\n"
                            + "Después vuelve aquí y toca «Elegir PC».")
                    .setPositiveButton("Ajustes de Bluetooth", (d, w) -> openBluetoothSettings())
                    .setNegativeButton("Cancelar", null)
                    .show();
            return;
        }
        String[] labels = new String[devices.size()];
        for (int i = 0; i < devices.size(); i++) {
            BluetoothDevice d = devices.get(i);
            String label = Bt.name(d);
            if (Store.find(this, d.getAddress()) != null) label += "  ✓";
            else if (!Bt.isComputer(d)) label += "  (otro dispositivo)";
            labels[i] = label;
        }
        new AlertDialog.Builder(this)
                .setTitle("¿En qué PC está DropToWin?")
                .setItems(labels, (d, which) -> connect(devices.get(which).getAddress(), Bt.name(devices.get(which))))
                .setNeutralButton("Emparejar otro", (d, w) -> openBluetoothSettings())
                .setNegativeButton("Cancelar", null)
                .show();
    }

    private void testDefaultPc() {
        Store.Pc pc = Store.defaultPc(this);
        if (pc == null) {
            choosePc();
            return;
        }
        if (ensureBluetoothReady()) connect(pc.address, pc.btName);
    }

    /** Conecta, deja que el PC lo apruebe si es la primera vez y guarda el PC como predeterminado. */
    private void connect(final String address, final String btName) {
        final TextView message = new TextView(this);
        message.setText("Conectando por Bluetooth…");
        message.setTextSize(16);
        ProgressBar spinner = new ProgressBar(this);
        LinearLayout layout = new LinearLayout(this);
        layout.setOrientation(LinearLayout.HORIZONTAL);
        layout.setGravity(Gravity.CENTER_VERTICAL);
        int pad = (int) (24 * getResources().getDisplayMetrics().density);
        layout.setPadding(pad, pad, pad, pad / 2);
        layout.addView(spinner);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1);
        lp.setMarginStart(pad);
        layout.addView(message, lp);

        final Transfer transfer = new Transfer(Bt.connector(this, address), Bt.wifiSocketFactory(this), Store.hello(this),
                new Transfer.Listener() {
                    @Override public void onPhase(Transfer.Phase phase) {
                        final String text;
                        switch (phase) {
                            case WAITING_APPROVAL: text = "En el PC ha aparecido una solicitud: pulsa «Permitir»."; break;
                            case SWITCHING_TO_WIFI: text = "Comprobando el Wi-Fi turbo…"; break;
                            default: text = "Conectando por Bluetooth…"; break;
                        }
                        runOnUiThread(() -> message.setText(text));
                    }

                    @Override public void onProgress(Transfer.ProgressInfo info) { }
                });

        final AlertDialog dialog = new AlertDialog.Builder(this)
                .setTitle(btName != null ? btName : "PC")
                .setView(layout)
                .setNegativeButton("Cancelar", (d, w) -> transfer.cancel())
                .setOnCancelListener(d -> transfer.cancel())
                .show();

        new Thread(() -> {
            try {
                Transfer.TestResult r = transfer.test();
                Store.Pc pc = Store.find(this, address);
                if (pc == null) pc = new Store.Pc(address);
                pc.btName = btName;
                pc.pcId = r.pcId;
                pc.pcName = r.pcName;
                pc.lastUsed = System.currentTimeMillis();
                Store.savePc(this, pc);
                Shortcuts.update(this);
                lastStatus = r.wifiReachable
                        ? "Conectado ✓ · Wi-Fi turbo disponible"
                        : r.wifiOffered
                        ? "Conectado ✓ · solo Bluetooth (el PC no responde por Wi-Fi)"
                        : "Conectado ✓ · solo Bluetooth";
                runOnUiThread(() -> {
                    if (dialog.isShowing()) dialog.dismiss();
                    refresh();
                    Toast.makeText(this, "Listo: conectado con " + r.pcName, Toast.LENGTH_SHORT).show();
                });
            } catch (Exception e) {
                runOnUiThread(() -> {
                    if (dialog.isShowing()) dialog.dismiss();
                    if (transfer.isCancelled() || isFinishing()) return;
                    new AlertDialog.Builder(this)
                            .setTitle("No se pudo conectar")
                            .setMessage(Errors.message(e))
                            .setPositiveButton("Aceptar", null)
                            .show();
                });
            }
        }, "DropToWin-conectar").start();
    }

    // ───────────────────────── Enviar desde la app ─────────────────────────

    private Store.Pc readyPc() {
        if (!ensureBluetoothReady()) return null;
        Store.Pc pc = Store.defaultPc(this);
        if (pc == null) choosePc();
        return pc;
    }

    private void pickMedia() {
        if (readyPc() == null) return;
        Intent intent;
        if (Build.VERSION.SDK_INT >= 33) {
            intent = new Intent(MediaStore.ACTION_PICK_IMAGES);
            intent.putExtra(MediaStore.EXTRA_PICK_IMAGES_MAX, MediaStore.getPickImagesMaxLimit());
        } else {
            intent = new Intent(Intent.ACTION_GET_CONTENT);
            intent.setType("*/*");
            intent.putExtra(Intent.EXTRA_MIME_TYPES, new String[] {"image/*", "video/*"});
            intent.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true);
            intent.addCategory(Intent.CATEGORY_OPENABLE);
        }
        startPicker(intent, REQ_MEDIA);
    }

    private void pickFiles() {
        if (readyPc() == null) return;
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        intent.setType("*/*");
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        intent.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true);
        startPicker(intent, REQ_FILES);
    }

    private void startPicker(Intent intent, int request) {
        try {
            startActivityForResult(intent, request);
        } catch (ActivityNotFoundException e) {
            Toast.makeText(this, "No hay ninguna app para elegir archivos.", Toast.LENGTH_LONG).show();
        }
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if ((requestCode != REQ_MEDIA && requestCode != REQ_FILES) || resultCode != RESULT_OK || data == null) return;
        ArrayList<Uri> uris = new ArrayList<>();
        ClipData clip = data.getClipData();
        if (clip != null) {
            for (int i = 0; i < clip.getItemCount(); i++) {
                Uri u = clip.getItemAt(i).getUri();
                if (u != null) uris.add(u);
            }
        } else if (data.getData() != null) {
            uris.add(data.getData());
        }
        Store.Pc pc = Store.defaultPc(this);
        if (uris.isEmpty() || pc == null) return;
        TransferService.sendUris(this, pc.address, uris);
        Toast.makeText(this, uris.size() == 1 ? "Enviando a " + pc.label() + "…"
                : "Enviando " + uris.size() + " archivos a " + pc.label() + "…", Toast.LENGTH_SHORT).show();
    }

    private void sendClipboard() {
        Store.Pc pc = readyPc();
        if (pc == null) return;
        ClipboardManager cm = getSystemService(ClipboardManager.class);
        ClipData clip = cm != null ? cm.getPrimaryClip() : null;
        CharSequence text = clip != null && clip.getItemCount() > 0 ? clip.getItemAt(0).coerceToText(this) : null;
        if (text == null || text.toString().trim().isEmpty()) {
            Toast.makeText(this, "El portapapeles está vacío.", Toast.LENGTH_SHORT).show();
            return;
        }
        TransferService.sendText(this, pc.address, text.toString());
        Toast.makeText(this, "Enviando texto a " + pc.label() + "…", Toast.LENGTH_SHORT).show();
    }
}
