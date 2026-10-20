# Publicar DropToWin en Google Play

Guía y textos listos para la app Android. El código ya cumple lo que exige Play: apunta a Android 16 (API 36), la pantalla principal funciona en modo edge-to-edge y `build.ps1 -Bundle` genera el App Bundle (`.aab`). Lo que queda son trámites de la cuenta y de Play Console, que se hacen a mano.

## 1. Compilar y probar

```bash
powershell -ExecutionPolicy Bypass -File build.ps1 -SkipWindows -Bundle
```

- Deja `dist\DropToWin.apk` (para probar) y `dist\DropToWin.aab` (para subir), firmados con la clave de `android\keystore\`. Necesita `android\tools\bundletool.jar` (ver el README).
- Antes de subir, instala el APK y revisa en un teléfono con Android 15 o 16 que la barra de título y la parte de abajo de la pantalla principal no queden tapadas por las barras del sistema.
- Cada subida a Play necesita un `versionCode` mayor que el anterior. Para las siguientes versiones: `powershell -ExecutionPolicy Bypass -File android\build-apk.ps1 -Out dist\DropToWin.apk -VersionCode 2 -VersionName 1.0.1 -Bundle`.

## 2. Cuenta de desarrollador

- Pago único de 25 USD y verificación de identidad en [Play Console](https://play.google.com/console).
- **Cuenta personal**: antes de publicar hay que hacer una **prueba cerrada con al menos 12 testers durante 14 días seguidos** y luego solicitar el acceso a producción.
- **Cuenta de organización**: no tiene ese requisito, pero pide un número D-U-N-S, que puede tardar hasta 30 días.

## 3. Firma de la app

Play firma lo que distribuye con su propia clave (Play App Signing). El `.aab` se firma con la clave de `android\keystore\`, que hace de **clave de subida**. Al crear la app, Play Console pregunta qué clave usar para distribuir:

- **Que Google genere la clave** (lo más simple). La versión de Play y los APK que compiles tú tendrán firmas distintas: quien tenga instalado un APK tuyo deberá desinstalarlo una vez antes de instalar desde Play, y volver a elegir el PC.
- **Subir tu propia clave** (`android\keystore\droptowin.jks`, con la herramienta que ofrece Play Console). Las dos versiones serán intercambiables. Conviene si vas a seguir repartiendo el APK fuera de Play.

## 4. Ficha de Play Store

**Nombre** (máx. 30 caracteres):

> DropToWin: envía al PC

**Descripción breve** (máx. 80):

> Envía fotos, videos, archivos y texto a tu PC con Windows por Bluetooth o Wi-Fi.

**Descripción completa** (máx. 4000):

> Pasa fotos, videos, documentos, enlaces y texto de tu teléfono a tu PC con Windows en segundos, sin cables, sin cuentas y sin subir nada a la nube.
>
> DropToWin aparece en el menú Compartir de cualquier app como «Enviar al PC». Elige lo que quieras enviar en la Galería, Google Fotos, Archivos o el navegador, toca Compartir y listo: el archivo llega a la carpeta Descargas\DropToWin de tu PC y recibes un aviso.
>
> CÓMO FUNCIONA
> • Bluetooth: funciona aunque no haya Wi-Fi. Solo hace falta emparejar el teléfono con el PC una vez.
> • Wi-Fi turbo: si el teléfono y el PC están en la misma red, los archivos grandes viajan por Wi-Fi, mucho más rápido, y cifrados.
> • Si la conexión se corta, el envío continúa donde se quedó.
>
> QUÉ PUEDES ENVIAR
> • Fotos y videos, con su fecha original.
> • Cualquier archivo: PDF, documentos, música, APK…
> • Texto y enlaces: se copian al portapapeles del PC.
>
> PRIVADO
> • Todo va directo de tu teléfono a tu PC. No hay servidores, cuentas, anuncios ni analítica.
> • El PC pide permiso la primera vez que se conecta un teléfono, y puedes quitárselo cuando quieras.
> • La conexión por Wi-Fi va cifrada (TLS) con un certificado propio de tu PC.
>
> NECESITAS
> • Un PC con Windows 10 u 11 con la app DropToWin para escritorio (gratis): [enlace de descarga de la app para Windows].
> • Bluetooth en el PC.

**Categoría**: Herramientas. **Correo de contacto**: obligatorio y público. **Política de privacidad**: la URL de la sección 7.

**Gráficos**:

- Icono: PNG de 512 × 512.
- Gráfico destacado: 1024 × 500.
- Capturas del teléfono: al menos 2. Sugeridas: la pantalla principal con el PC elegido, el menú Compartir con «Enviar al PC», la notificación de progreso y la ventana de DropToWin en el PC con lo recibido.

## 5. Contenido de la app (Play Console › Supervisar y mejorar › Contenido de la app)

| Sección | Respuesta |
|---|---|
| Política de privacidad | La URL donde publiques el texto de la sección 7. |
| Anuncios | No contiene anuncios. |
| ID de publicidad | No lo usa. |
| Acceso a la app | No hay inicio de sesión, pero la app necesita el PC. Elige que parte de la funcionalidad está restringida y añade las instrucciones de abajo. |
| Clasificación de contenido | Categoría «Utilidad, productividad, comunicación u otra». Todas las respuestas son «No»: los archivos solo van al PC del propio usuario, no a otras personas. |
| Público objetivo | 13 años o más (13–15, 16–17 y 18+). Incluir menores de 13 activaría la política de familias. |
| Seguridad de los datos | Ver abajo. |
| Servicios en primer plano | Ver abajo. |
| Apps de noticias, salud, financieras, gobierno | No aplica. |

**Instrucciones para el acceso a la app** (para los revisores):

> DropToWin envía archivos del teléfono a un PC con Windows, así que necesita la app gratuita de escritorio: [enlace de descarga de la app para Windows]. Pasos: 1) Instala la app en el PC y empareja el teléfono con el PC por Bluetooth desde los ajustes del sistema. 2) Abre DropToWin en el teléfono, toca «Elegir PC» y selecciona el PC; en el PC pulsa «Permitir». 3) En la Galería, comparte una foto y elige «Enviar al PC». Video de demostración: [enlace al video].

### Seguridad de los datos

- **¿La app recopila o comparte alguno de los tipos de datos de usuario requeridos?** → **No**.
- Por qué: Google considera «recopilados» los datos que el desarrollador obtiene del dispositivo. DropToWin no tiene servidores ni SDK de terceros, y el desarrollador nunca recibe nada. Los archivos solo viajan del teléfono al PC del propio usuario, cifrados: por Bluetooth, con el cifrado del emparejamiento; por Wi-Fi, con TLS y una clave que solo tiene ese PC.

### Servicio en primer plano (`connectedDevice`)

- **Tipo**: Dispositivo conectado (`FOREGROUND_SERVICE_CONNECTED_DEVICE`). Entre los casos de uso que ofrezca la lista, elige el de transferencia de datos o comunicación con un dispositivo externo por Bluetooth.
- **Descripción de la función**:
  > Cuando el usuario elige «Enviar al PC» en el menú Compartir, o «Fotos y videos», «Archivos» o «Texto del portapapeles» en la app, DropToWin se conecta por Bluetooth (y, si es posible, por Wi-Fi local) al PC con Windows que el usuario emparejó y le envía esos archivos. Mientras dura el envío muestra una notificación con el progreso y la opción de cancelar. El servicio termina en cuanto acaba el envío.
- **Impacto si se interrumpe o se aplaza**:
  > El envío empieza porque el usuario lo pidió y debe hacerse en ese momento. Por Bluetooth, un video puede tardar varios minutos: si el sistema detuviera la transferencia al salir de la app, el usuario tendría que dejar la pantalla abierta todo ese tiempo o el archivo llegaría incompleto.
- **Video** (enlace de YouTube u otro, sin necesidad de que sea público en listas). Que muestre: abrir DropToWin y elegir el PC; ir a la Galería, compartir un video y tocar «Enviar al PC»; bajar la cortina y ver la notificación de progreso; el archivo recibido en el PC.

## 6. Prueba cerrada y producción

1. Crea la app en Play Console (idioma predeterminado: español), completa la ficha y el contenido de la app.
2. En **Pruebas › Prueba cerrada**, crea un canal, añade a los testers (por correo o con un Grupo de Google) y sube `dist\DropToWin.aab`.
3. Envía el enlace de inscripción a los testers. Cuenta personal: espera a que al menos 12 lleven 14 días seguidos inscritos.
4. Solicita el acceso a producción y publica.

## 7. Política de privacidad (texto para publicar)

Publícala en una URL pública que no sea un PDF, por ejemplo con Google Sites o con GitHub Pages en un repositorio público. Sustituye lo que está entre corchetes.

---

**Política de privacidad de DropToWin**

Última actualización: [fecha]

DropToWin es una app para enviar archivos, fotos, videos, enlaces y texto desde un teléfono Android a un PC con Windows que tiene instalada la app DropToWin para escritorio. Esta política explica qué datos usa y qué hace con ellos.

**Resumen**: DropToWin no envía ningún dato al desarrollador ni a terceros. No tiene servidores, cuentas, anuncios ni analítica. Lo que envías va directamente de tu teléfono a tu PC.

**Qué datos usa la app Android**

- **Lo que eliges enviar**: los archivos, fotos, videos, enlaces o texto que compartes con «Enviar al PC» o eliges dentro de la app. El portapapeles solo se lee cuando tocas «Texto del portapapeles». Se envían únicamente al PC que elegiste.
- **Bluetooth**: la app muestra los dispositivos emparejados con el teléfono para que elijas tu PC, y guarda en el teléfono la dirección Bluetooth y el nombre de ese PC.
- **Identificación del teléfono ante tu PC**: la app genera un identificador aleatorio y lo envía a tu PC junto con el nombre del teléfono, para que el PC pueda pedirte permiso la primera vez y reconocerlo después.
- **Red local**: si el teléfono y el PC están en la misma red Wi-Fi, la app se conecta al PC por esa red. La conexión va cifrada con TLS y la app comprueba que es tu PC. La app no se conecta a internet.

**Qué guarda la app de Windows en tu PC**

Los archivos recibidos (en la carpeta que elijas), la lista de lo recibido, la lista de teléfonos autorizados (nombre, identificador y dirección Bluetooth) y un registro de diagnóstico. Todo queda en tu PC.

**Con quién se comparten los datos**

Con nadie. Los datos no salen de tu teléfono salvo hacia tu propio PC.

**Cómo borrar los datos**

- En el teléfono: desinstala la app o borra sus datos en *Ajustes › Apps › DropToWin*.
- En el PC: en *DropToWin › Dispositivos* puedes quitar el permiso a un teléfono. Al desinstalar la app de escritorio, los archivos recibidos se conservan; bórralos desde su carpeta si quieres.

**Menores**

DropToWin no está dirigida a menores de 13 años.

**Cambios**

Si esta política cambia, se publicará aquí la nueva versión con su fecha.

**Contacto**

[tu correo de contacto]

---

## 8. Más adelante

- **Android 17**: cuando Play exija apuntar a API 37, el Wi-Fi turbo necesitará el permiso `ACCESS_LOCAL_NETWORK`. Está en el grupo «Dispositivos cercanos», que la app ya pide, así que no aparecerá un aviso nuevo.
- **Verificación de desarrolladores**: desde 2027, en todo el mundo, los APK instalados fuera de Play también deberán venir de un desarrollador verificado. Con la cuenta de Play Console se puede registrar el paquete `com.droptowin`.
- **App de Windows**: tiene que poder descargarse públicamente (un repositorio o release públicos, una web o la Microsoft Store) para poner el enlace en la ficha y en las instrucciones para los revisores.
