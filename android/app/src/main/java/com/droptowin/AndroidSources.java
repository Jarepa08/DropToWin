package com.droptowin;

import android.content.ContentResolver;
import android.content.Context;
import android.database.Cursor;
import android.media.ExifInterface;
import android.media.MediaMetadataRetriever;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import android.provider.DocumentsContract;
import android.provider.MediaStore;
import android.provider.OpenableColumns;
import android.webkit.MimeTypeMap;

import com.droptowin.core.CameraNames;
import com.droptowin.core.DtwException;
import com.droptowin.core.FileSource;

import java.io.BufferedInputStream;
import java.io.EOFException;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileNotFoundException;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.time.LocalDateTime;
import java.time.ZoneId;
import java.util.ArrayList;
import java.util.List;
import java.util.UUID;

/** Convierte los content:// que llegan de otras apps en {@link FileSource} con nombre, tamaño y fecha. */
final class AndroidSources {
    private AndroidSources() {}

    static List<FileSource> resolve(Context c, List<Uri> uris, List<File> tempFiles) throws DtwException {
        ContentResolver cr = c.getContentResolver();
        List<FileSource> list = new ArrayList<>();
        for (Uri uri : uris) list.add(fromUri(c, cr, uri, tempFiles));
        return list;
    }

    private static FileSource fromUri(Context c, ContentResolver cr, Uri uri, List<File> tempFiles) throws DtwException {
        String mime = null;
        try {
            mime = cr.getType(uri);
        } catch (RuntimeException ignored) {
            // Algunos proveedores fallan al preguntar el tipo; no es imprescindible.
        }

        if ("file".equals(uri.getScheme()) && uri.getPath() != null) {
            File file = new File(uri.getPath());
            if (!file.canRead()) throw new DtwException("source", "No se puede leer «" + file.getName() + "».");
            return new LocalFileSource(file, file.getName(), guessMime(file.getName(), mime), file.lastModified());
        }

        String name = null;
        long size = -1;
        long taken = 0;
        long modified = 0;
        try (Cursor cursor = cr.query(uri, null, null, null, null)) {
            if (cursor != null && cursor.moveToFirst()) {
                name = string(cursor, OpenableColumns.DISPLAY_NAME);
                size = number(cursor, OpenableColumns.SIZE, -1);
                taken = number(cursor, "datetaken", 0);                                            // ms (fotos y videos)
                long docModified = number(cursor, DocumentsContract.Document.COLUMN_LAST_MODIFIED, 0); // ms
                long mediaModified = number(cursor, MediaStore.MediaColumns.DATE_MODIFIED, 0);         // s
                modified = taken > 0 ? taken : docModified > 0 ? docModified : mediaModified * 1000;
            }
        } catch (RuntimeException ignored) {
            // Proveedor sin columnas consultables: se usan valores por defecto.
        }

        try (ParcelFileDescriptor pfd = cr.openFileDescriptor(uri, "r")) {
            if (pfd != null && pfd.getStatSize() >= 0) size = pfd.getStatSize();
        } catch (FileNotFoundException | SecurityException e) {
            throw new DtwException("source", "No se puede leer «" + (name != null ? name : uri.getLastPathSegment()) + "».");
        } catch (IOException ignored) {
            // Error al cerrar: el tamaño ya se leyó.
        }

        if (name == null || name.trim().isEmpty()) name = fallbackName(uri);
        name = withExtension(name, mime);

        // Google Fotos y el selector de fotos usan el id interno como nombre («1000012345.jpg»):
        // se nombra como la cámara, con la fecha de captura del EXIF o de los metadatos del video.
        String type = guessMime(name, mime);
        boolean video = type != null && type.startsWith("video/");
        if (CameraNames.isNumeric(name) && (video || (type != null && type.startsWith("image/")))) {
            ZoneId zone = ZoneId.systemDefault();
            LocalDateTime when = video ? videoDate(c, uri, zone) : exifDate(cr, uri);
            if (when != null && taken <= 0) modified = when.atZone(zone).toInstant().toEpochMilli();
            if (when == null) when = CameraNames.fromMillis(modified, zone);
            if (when != null) name = CameraNames.fromDate(name, video, when);
        }

        if (size < 0) return copyToCache(c, cr, uri, name, mime, modified, tempFiles);
        return new UriSource(cr, uri, name, size, mime, modified);
    }

    /** Fecha de captura del EXIF de la foto (hora local de la cámara), o null. */
    private static LocalDateTime exifDate(ContentResolver cr, Uri uri) {
        try (InputStream in = cr.openInputStream(uri)) {
            if (in == null) return null;
            ExifInterface exif = new ExifInterface(in);
            for (String tag : new String[] {ExifInterface.TAG_DATETIME_ORIGINAL, ExifInterface.TAG_DATETIME_DIGITIZED,
                    ExifInterface.TAG_DATETIME}) {
                LocalDateTime date = CameraNames.parseExif(exif.getAttribute(tag));
                if (date != null) return date;
            }
        } catch (IOException | RuntimeException e) {
            // EXIF ilegible o ausente: se usa la fecha que da el proveedor.
        }
        return null;
    }

    /** Fecha de creación del video en la zona del teléfono, o null. */
    private static LocalDateTime videoDate(Context c, Uri uri, ZoneId zone) {
        MediaMetadataRetriever retriever = new MediaMetadataRetriever();
        try {
            retriever.setDataSource(c, uri);
            return CameraNames.parseVideo(retriever.extractMetadata(MediaMetadataRetriever.METADATA_KEY_DATE), zone);
        } catch (RuntimeException e) {
            return null; // Formato sin metadatos legibles: se usa la fecha que da el proveedor.
        } finally {
            try {
                retriever.release();
            } catch (IOException | RuntimeException ignored) {
                // Ya se leyó lo necesario.
            }
        }
    }

    /** Proveedores que no informan del tamaño (flujos): se copia antes a la caché. */
    private static FileSource copyToCache(Context c, ContentResolver cr, Uri uri, String name, String mime,
                                          long modified, List<File> tempFiles) throws DtwException {
        File dir = new File(c.getCacheDir(), "envio-" + UUID.randomUUID());
        File file = new File(dir, "contenido");
        tempFiles.add(dir);
        if (!dir.mkdirs()) throw new DtwException("source", "No se pudo preparar «" + name + "».");
        try (InputStream in = cr.openInputStream(uri); OutputStream out = new FileOutputStream(file)) {
            if (in == null) throw new FileNotFoundException();
            byte[] buffer = new byte[64 * 1024];
            int n;
            while ((n = in.read(buffer)) > 0) out.write(buffer, 0, n);
        } catch (IOException | SecurityException e) {
            throw new DtwException("source", "No se pudo leer «" + name + "».");
        }
        return new LocalFileSource(file, name, mime, modified);
    }

    private static String fallbackName(Uri uri) {
        String last = uri.getLastPathSegment();
        if (last != null) {
            int slash = last.lastIndexOf('/');
            if (slash >= 0) last = last.substring(slash + 1);
        }
        return last != null && !last.trim().isEmpty() ? last : "archivo";
    }

    private static String withExtension(String name, String mime) {
        if (name.lastIndexOf('.') > 0 || mime == null) return name;
        String ext = MimeTypeMap.getSingleton().getExtensionFromMimeType(mime);
        return ext != null ? name + "." + ext : name;
    }

    private static String guessMime(String name, String fallback) {
        int dot = name.lastIndexOf('.');
        if (dot < 0) return fallback;
        String mime = MimeTypeMap.getSingleton().getMimeTypeFromExtension(name.substring(dot + 1).toLowerCase());
        return mime != null ? mime : fallback;
    }

    private static String string(Cursor c, String column) {
        int i = c.getColumnIndex(column);
        return i >= 0 && !c.isNull(i) ? c.getString(i) : null;
    }

    private static long number(Cursor c, String column, long fallback) {
        int i = c.getColumnIndex(column);
        if (i < 0 || c.isNull(i)) return fallback;
        try {
            return c.getLong(i);
        } catch (RuntimeException e) {
            return fallback;
        }
    }

    private static void skipFully(InputStream in, long count) throws IOException {
        long remaining = count;
        while (remaining > 0) {
            long skipped = in.skip(remaining);
            if (skipped <= 0) {
                if (in.read() < 0) throw new EOFException();
                skipped = 1;
            }
            remaining -= skipped;
        }
    }

    static final class UriSource implements FileSource {
        private final ContentResolver cr;
        private final Uri uri;
        private final String name;
        private final long size;
        private final String mime;
        private final long modified;

        UriSource(ContentResolver cr, Uri uri, String name, long size, String mime, long modified) {
            this.cr = cr;
            this.uri = uri;
            this.name = name;
            this.size = size;
            this.mime = mime;
            this.modified = modified;
        }

        @Override public String name() { return name; }
        @Override public long size() { return size; }
        @Override public String mime() { return mime; }
        @Override public long modified() { return modified; }

        @Override
        public InputStream open(long offset) throws IOException {
            ParcelFileDescriptor pfd;
            try {
                pfd = cr.openFileDescriptor(uri, "r");
            } catch (SecurityException e) {
                throw new IOException("permiso denegado", e);
            }
            if (pfd == null) throw new FileNotFoundException(name);
            FileInputStream in = new ParcelFileDescriptor.AutoCloseInputStream(pfd);
            if (offset > 0) {
                try {
                    in.getChannel().position(offset);
                } catch (IOException e) {
                    skipFully(in, offset); // Descriptor no posicionable (tubería): se lee y descarta.
                }
            }
            return new BufferedInputStream(in, 64 * 1024);
        }
    }

    static final class LocalFileSource implements FileSource {
        private final File file;
        private final String name;
        private final String mime;
        private final long modified;

        LocalFileSource(File file, String name, String mime, long modified) {
            this.file = file;
            this.name = name;
            this.mime = mime;
            this.modified = modified;
        }

        @Override public String name() { return name; }
        @Override public long size() { return file.length(); }
        @Override public String mime() { return mime; }
        @Override public long modified() { return modified; }

        @Override
        public InputStream open(long offset) throws IOException {
            FileInputStream in = new FileInputStream(file);
            if (offset > 0) in.getChannel().position(offset);
            return new BufferedInputStream(in, 64 * 1024);
        }
    }
}
