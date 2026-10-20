import com.droptowin.core.CameraNames;

import java.io.PrintStream;
import java.time.LocalDateTime;
import java.time.ZoneId;

/**
 * Pruebas de los nombres de cámara para fotos y videos con nombre numérico (Google Fotos).
 *
 * Uso: javac -encoding UTF-8 -d tests/build/unit android/app/src/main/java/com/droptowin/core/CameraNames.java tests/CameraNamesTest.java
 *      java -cp tests/build/unit CameraNamesTest
 */
public final class CameraNamesTest {
    static int failures;
    static PrintStream out;

    public static void main(String[] args) throws Exception {
        out = new PrintStream(System.out, true, "UTF-8");
        ZoneId mexico = ZoneId.of("America/Mexico_City");
        LocalDateTime when = LocalDateTime.of(2026, 10, 20, 15, 43, 12);

        check(CameraNames.isNumeric("1000012345.jpg"), "«1000012345.jpg» es numérico");
        check(CameraNames.isNumeric("1000012345"), "«1000012345» es numérico");
        check(!CameraNames.isNumeric("IMG_20261020_154312.jpg"), "un nombre de cámara no es numérico");
        check(!CameraNames.isNumeric("20261020_154312.jpg"), "fecha con guion bajo no es numérica");
        check(!CameraNames.isNumeric(".jpg") && !CameraNames.isNumeric(""), "sin nombre no es numérico");
        check(!CameraNames.isNumeric("12.5.jpg"), "con puntos en el nombre no es numérico");

        same(CameraNames.fromDate("1000012345.jpg", false, when), "IMG_20261020_154312.jpg", "foto");
        same(CameraNames.fromDate("1000012346.mp4", true, when), "VID_20261020_154312.mp4", "video");
        same(CameraNames.fromDate("1000012347.HEIC", false, when), "IMG_20261020_154312.HEIC", "conserva la extensión");
        same(CameraNames.fromDate("1000012348", false, when), "IMG_20261020_154312", "sin extensión");

        same(CameraNames.parseExif("2026:10:20 15:43:12"), when, "EXIF normal");
        same(CameraNames.parseExif("2026-10-20 15:43:12"), when, "EXIF con guiones");
        same(CameraNames.parseExif(" 2026:10:20 15:43:12\u0000"), when, "EXIF con espacios y nulo final");
        same(CameraNames.parseExif("0000:00:00 00:00:00"), null, "EXIF vacío (ceros)");
        same(CameraNames.parseExif("    :  :     :  :  "), null, "EXIF vacío (espacios)");
        same(CameraNames.parseExif("2026:10:20"), null, "EXIF incompleto");
        same(CameraNames.parseExif(null), null, "sin EXIF");

        same(CameraNames.parseVideo("20261020T214312.000Z", mexico), when, "video en UTC pasado a la hora local");
        same(CameraNames.parseVideo("20261020T214312Z", mexico), when, "video sin milisegundos");
        same(CameraNames.parseVideo("19040101T000000.000Z", mexico), null, "video sin fecha (1904)");
        same(CameraNames.parseVideo("basura", mexico), null, "fecha de video inválida");

        same(CameraNames.fromMillis(when.atZone(mexico).toInstant().toEpochMilli(), mexico), when, "milisegundos a hora local");
        same(CameraNames.fromMillis(0, mexico), null, "sin fecha del proveedor");

        out.println(failures == 0 ? "OK" : failures + " FALLOS");
        System.exit(failures == 0 ? 0 : 1);
    }

    static void same(Object actual, Object expected, String what) {
        boolean ok = expected == null ? actual == null : expected.equals(actual);
        check(ok, what + (ok ? "" : ": se esperaba «" + expected + "», salió «" + actual + "»"));
    }

    static void check(boolean ok, String what) {
        if (!ok) failures++;
        out.println((ok ? "  ✓ " : "  ✗ ") + what);
    }
}
