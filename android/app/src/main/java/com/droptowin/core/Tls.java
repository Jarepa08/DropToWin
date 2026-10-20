package com.droptowin.core;

import java.io.BufferedInputStream;
import java.io.BufferedOutputStream;
import java.io.IOException;
import java.net.InetSocketAddress;
import java.net.Socket;
import java.security.GeneralSecurityException;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.security.SecureRandom;
import java.security.cert.CertificateException;
import java.security.cert.X509Certificate;
import java.util.Locale;

import javax.net.SocketFactory;
import javax.net.ssl.SSLContext;
import javax.net.ssl.SSLSocket;
import javax.net.ssl.TrustManager;
import javax.net.ssl.X509TrustManager;

/**
 * Conexión TLS al PC fijando el certificado por su huella SHA-256 (recibida por Bluetooth).
 * No se usa ninguna autoridad de certificación ni el nombre de host: solo vale ese certificado.
 */
public final class Tls {
    private Tls() {}

    public static Duplex connect(SocketFactory plain, String host, int port, String fingerprint, int timeoutMs)
            throws IOException {
        Socket raw = plain.createSocket();
        try {
            raw.connect(new InetSocketAddress(host, port), timeoutMs);
            raw.setTcpNoDelay(true);
            raw.setSoTimeout(60_000);
            SSLContext context = SSLContext.getInstance("TLS");
            context.init(null, new TrustManager[] {new PinnedTrustManager(fingerprint)}, new SecureRandom());
            SSLSocket ssl = (SSLSocket) context.getSocketFactory().createSocket(raw, host, port, true);
            ssl.startHandshake();
            return new Duplex(
                    new BufferedInputStream(ssl.getInputStream(), 64 * 1024),
                    new BufferedOutputStream(ssl.getOutputStream(), 64 * 1024),
                    ssl, "wifi");
        } catch (GeneralSecurityException e) {
            closeQuietly(raw);
            throw new IOException("TLS no disponible: " + e.getMessage(), e);
        } catch (IOException | RuntimeException e) {
            closeQuietly(raw);
            throw e;
        }
    }

    public static String sha256Hex(byte[] data) {
        try {
            byte[] hash = MessageDigest.getInstance("SHA-256").digest(data);
            StringBuilder sb = new StringBuilder(hash.length * 2);
            for (byte b : hash) sb.append(String.format(Locale.ROOT, "%02x", b & 0xFF));
            return sb.toString();
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException(e);
        }
    }

    private static void closeQuietly(Socket socket) {
        try {
            socket.close();
        } catch (IOException ignored) {
            // Ya falló la conexión.
        }
    }

    static final class PinnedTrustManager implements X509TrustManager {
        private final String expected;

        PinnedTrustManager(String expected) {
            this.expected = expected.toLowerCase(Locale.ROOT);
        }

        @Override
        public void checkClientTrusted(X509Certificate[] chain, String authType) throws CertificateException {
            throw new CertificateException("No se aceptan certificados de cliente");
        }

        @Override
        public void checkServerTrusted(X509Certificate[] chain, String authType) throws CertificateException {
            if (chain == null || chain.length == 0) throw new CertificateException("El PC no presentó certificado");
            if (!sha256Hex(chain[0].getEncoded()).equals(expected)) {
                throw new CertificateException("La huella del certificado del PC no coincide");
            }
        }

        @Override
        public X509Certificate[] getAcceptedIssuers() {
            return new X509Certificate[0];
        }
    }
}
