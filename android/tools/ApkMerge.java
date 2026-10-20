import java.io.BufferedOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.nio.file.Files;
import java.util.Enumeration;
import java.util.zip.ZipEntry;
import java.util.zip.ZipFile;
import java.util.zip.ZipOutputStream;

/**
 * Une el APK de recursos que genera aapt2 con classes.dex. Conserva sin comprimir las entradas que
 * aapt2 dejó así (resources.arsc debe ir sin comprimir desde Android 11).
 *
 * Uso: java ApkMerge.java base.apk classes.dex salida.apk
 */
public final class ApkMerge {
    public static void main(String[] args) throws IOException {
        File base = new File(args[0]);
        File dex = new File(args[1]);
        File out = new File(args[2]);
        try (ZipFile zip = new ZipFile(base);
             ZipOutputStream zos = new ZipOutputStream(new BufferedOutputStream(new FileOutputStream(out)))) {
            Enumeration<? extends ZipEntry> entries = zip.entries();
            while (entries.hasMoreElements()) {
                ZipEntry entry = entries.nextElement();
                ZipEntry copy = new ZipEntry(entry.getName());
                copy.setTime(entry.getTime());
                if (entry.getMethod() == ZipEntry.STORED) {
                    copy.setMethod(ZipEntry.STORED);
                    copy.setSize(entry.getSize());
                    copy.setCompressedSize(entry.getSize());
                    copy.setCrc(entry.getCrc());
                } else {
                    copy.setMethod(ZipEntry.DEFLATED);
                }
                zos.putNextEntry(copy);
                try (InputStream in = zip.getInputStream(entry)) {
                    in.transferTo(zos);
                }
                zos.closeEntry();
            }
            ZipEntry classes = new ZipEntry("classes.dex");
            classes.setMethod(ZipEntry.DEFLATED);
            zos.putNextEntry(classes);
            Files.copy(dex.toPath(), zos);
            zos.closeEntry();
        }
    }
}
