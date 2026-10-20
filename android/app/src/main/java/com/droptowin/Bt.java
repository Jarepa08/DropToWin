package com.droptowin;

import android.Manifest;
import android.bluetooth.BluetoothAdapter;
import android.bluetooth.BluetoothClass;
import android.bluetooth.BluetoothDevice;
import android.bluetooth.BluetoothManager;
import android.bluetooth.BluetoothSocket;
import android.content.Context;
import android.content.pm.PackageManager;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.os.Build;

import com.droptowin.core.Duplex;
import com.droptowin.core.DtwException;
import com.droptowin.core.Transfer;

import java.io.BufferedInputStream;
import java.io.BufferedOutputStream;
import java.io.IOException;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.UUID;

import javax.net.SocketFactory;

/** Bluetooth y red: conexión RFCOMM con el PC y sockets ligados a la red Wi-Fi. */
final class Bt {
    /** El mismo UUID que anuncia la app de Windows por SDP. */
    static final UUID SERVICE = UUID.fromString("4f9a6c1e-2b7d-4c3a-9e58-d17b2a6f0c93");

    private Bt() {}

    static BluetoothAdapter adapter(Context c) {
        BluetoothManager manager = (BluetoothManager) c.getSystemService(Context.BLUETOOTH_SERVICE);
        return manager != null ? manager.getAdapter() : null;
    }

    static boolean hasPermission(Context c) {
        return Build.VERSION.SDK_INT < 31
                || c.checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) == PackageManager.PERMISSION_GRANTED;
    }

    static boolean isEnabled(Context c) {
        BluetoothAdapter adapter = adapter(c);
        return adapter != null && adapter.isEnabled();
    }

    /** Dispositivos emparejados, primero los que se anuncian como ordenadores. */
    static List<BluetoothDevice> bondedDevices(Context c) {
        BluetoothAdapter adapter = adapter(c);
        if (adapter == null) return Collections.emptyList();
        List<BluetoothDevice> list = new ArrayList<>(adapter.getBondedDevices());
        Collections.sort(list, (a, b) -> {
            boolean ca = isComputer(a), cb = isComputer(b);
            if (ca != cb) return ca ? -1 : 1;
            return name(a).compareToIgnoreCase(name(b));
        });
        return list;
    }

    static boolean isComputer(BluetoothDevice device) {
        BluetoothClass bc = device.getBluetoothClass();
        return bc != null && bc.getMajorDeviceClass() == BluetoothClass.Device.Major.COMPUTER;
    }

    static String name(BluetoothDevice device) {
        String name = device.getName();
        return name != null && !name.isEmpty() ? name : device.getAddress();
    }

    /** Abre el canal RFCOMM "seguro" (cifrado y autenticado por el emparejamiento del sistema). */
    static Transfer.Connector connector(final Context context, final String address) {
        final Context c = context.getApplicationContext();
        return () -> {
            BluetoothAdapter adapter = adapter(c);
            if (adapter == null) throw new DtwException("bluetooth", "Este teléfono no tiene Bluetooth.");
            if (!hasPermission(c)) {
                throw new DtwException("permission", "Falta el permiso de Bluetooth. Abre DropToWin para concederlo.");
            }
            if (!adapter.isEnabled()) throw new DtwException("bluetooth-off", "El Bluetooth del teléfono está desactivado.");
            BluetoothSocket socket = null;
            try {
                try {
                    adapter.cancelDiscovery();
                } catch (SecurityException ignored) {
                    // Sin permiso de búsqueda no hay búsqueda que cancelar.
                }
                BluetoothDevice device = adapter.getRemoteDevice(address);
                socket = device.createRfcommSocketToServiceRecord(SERVICE);
                socket.connect();
                return new Duplex(
                        new BufferedInputStream(socket.getInputStream(), 16 * 1024),
                        new BufferedOutputStream(socket.getOutputStream(), 32 * 1024),
                        socket, "bluetooth");
            } catch (SecurityException e) {
                closeQuietly(socket);
                throw new DtwException("permission", "Falta el permiso de Bluetooth. Abre DropToWin para concederlo.");
            } catch (IOException e) {
                closeQuietly(socket);
                throw new IOException(Errors.BT_CONNECT_FAILED, e);
            }
        };
    }

    /**
     * Sockets por la red Wi-Fi aunque no sea la red por defecto (p. ej. una Wi-Fi sin Internet,
     * como la zona con cobertura del PC). Si el teléfono no es cliente Wi-Fi (por ejemplo, es él
     * quien comparte Internet), se usa el enrutamiento normal.
     */
    @SuppressWarnings("deprecation")
    static SocketFactory wifiSocketFactory(Context c) {
        ConnectivityManager cm = (ConnectivityManager) c.getSystemService(Context.CONNECTIVITY_SERVICE);
        if (cm != null) {
            for (Network network : cm.getAllNetworks()) {
                NetworkCapabilities caps = cm.getNetworkCapabilities(network);
                if (caps != null && (caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)
                        || caps.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET))) {
                    return network.getSocketFactory();
                }
            }
        }
        return SocketFactory.getDefault();
    }

    private static void closeQuietly(BluetoothSocket socket) {
        if (socket == null) return;
        try {
            socket.close();
        } catch (IOException ignored) {
            // La conexión ya falló.
        }
    }
}
