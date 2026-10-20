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
 * Convierte el APK en formato proto que genera "aapt2 link --proto-format" y classes.dex en el módulo
 * base que espera "bundletool build-bundle": manifest/, dex/, res/, assets/, resources.pb y root/ para
 * lo demás.
 *
 * Uso: java BundleModule.java proto.apk classes.dex base.zip
 */
public final class BundleModule {
    public static void main(String[] args) throws IOException {
        File proto = new File(args[0]);
        File dex = new File(args[1]);
        File out = new File(args[2]);
        try (ZipFile zip = new ZipFile(proto);
             ZipOutputStream zos = new ZipOutputStream(new BufferedOutputStream(new FileOutputStream(out)))) {
            Enumeration<? extends ZipEntry> entries = zip.entries();
            while (entries.hasMoreElements()) {
                ZipEntry entry = entries.nextElement();
                if (entry.isDirectory()) continue;
                zos.putNextEntry(new ZipEntry(modulePath(entry.getName())));
                try (InputStream in = zip.getInputStream(entry)) {
                    in.transferTo(zos);
                }
                zos.closeEntry();
            }
            zos.putNextEntry(new ZipEntry("dex/classes.dex"));
            Files.copy(dex.toPath(), zos);
            zos.closeEntry();
        }
    }

    static String modulePath(String name) {
        if (name.equals("AndroidManifest.xml")) return "manifest/AndroidManifest.xml";
        if (name.equals("resources.pb") || name.startsWith("res/") || name.startsWith("assets/")) return name;
        return "root/" + name;
    }
}
