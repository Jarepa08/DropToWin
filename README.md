# DropToWin

Envía archivos, fotos, videos, enlaces y texto desde tu teléfono a tu PC con Windows:

- **Android → Windows por Bluetooth**, con salto automático a Wi-Fi cifrado cuando ambos comparten red.
- **iPhone → Windows por Wi-Fi** (iOS no permite enviar archivos por Bluetooth a un PC), desde el menú Compartir con la app Atajos o desde Safari.

Qué incluye:

- **`dist/DropToWin.exe`**: app de escritorio para Windows 10/11. Vive en la bandeja del sistema y recibe en segundo plano.
- **`dist/DropToWin.apk`**: app para Android 8.0 o superior. Aparece en el menú **Compartir** de cualquier app como «Enviar al PC».
- **iPhone**: no necesita instalar nada. El PC muestra un código QR, y la página que se abre explica cómo crear el atajo «Enviar al PC».

## Cómo funciona

```
 Android                                               PC con Windows
┌──────────────────────────┐  Bluetooth RFCOMM       ┌──────────────────────────────┐
│ Compartir › Enviar al PC │ ──────────────────────▶ │ DropToWin (bandeja)          │
│ App DropToWin            │  autorización, texto,   │  · pide permiso la 1.ª vez   │
│                          │  archivos y la llave    │  · guarda en Descargas\      │
│                          │  del Wi-Fi turbo        │    DropToWin                 │
│                          │ ══════════════════════▶ │  · el texto va al            │
└──────────────────────────┘  Wi-Fi turbo (TLS)      │    portapapeles              │
 iPhone                                              │  · avisa con una notificación│
┌──────────────────────────┐  Wi-Fi (HTTP, red local)│                              │
│ Compartir › Enviar al PC │ ──────────────────────▶ │                              │
│ (Atajos) o Safari        │  llave propia por iPhone│                              │
└──────────────────────────┘                         └──────────────────────────────┘
```

**Android**: Bluetooth es el canal principal. Encuentra el PC, autoriza al teléfono y transfiere aunque no haya Wi-Fi. Como el Bluetooth clásico es lento, si los dos equipos están en la misma red el PC le pasa al teléfono por Bluetooth una llave de un solo uso y la huella de su certificado, y el contenido viaja cifrado por Wi-Fi. Si la conexión se corta, el envío continúa donde se quedó.

**iPhone**: iOS no implementa la transferencia de archivos por Bluetooth (OBEX), y una app nativa necesitaría un Mac con Xcode y una cuenta de desarrollador de Apple. Por eso el iPhone envía por la red Wi-Fi usando lo que ya trae: la app **Atajos**, que aparece en el menú Compartir, y **Safari**.

| Canal | Velocidad típica | Una foto de 4 MB | Un video de 100 MB |
|---|---|---|---|
| Bluetooth (Android) | 0,15–0,25 MB/s | ~20 s | ~8 min |
| Wi-Fi (Android turbo o iPhone) | 10–60 MB/s, según el router | instantáneo | 2–10 s |

## Instalación en el PC

1. Requisitos: Windows 10 (versión 2004 o posterior) u 11, un adaptador **Bluetooth** (solo para Android) y los runtimes de .NET 10 (*Desktop Runtime* y *ASP.NET Core Runtime*, desde [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0)). Si compilas con `-SelfContained`, los runtimes no hacen falta.
2. Instala para tu usuario, sin permisos de administrador:
   ```bash
   powershell -ExecutionPolicy Bypass -File install.ps1
   ```
   Copia la app a `%LOCALAPPDATA%\Programs\DropToWin`, crea el acceso **DropToWin** en el menú Inicio, la arranca con Windows (oculta en la bandeja) y la registra en *Configuración › Aplicaciones* para poder desinstalarla. Si lo vuelves a ejecutar, actualiza la app y conserva tus ajustes y archivos.
3. La primera vez que se abre, el Firewall de Windows pregunta si permite la app: acepta. Es lo que habilita el Wi-Fi turbo y la recepción desde iPhone.
4. Si tu red Wi-Fi está marcada como **Pública** y en el aviso solo permitiste redes privadas, el firewall la bloqueará. Márcala como privada en *Configuración › Red e Internet › Wi-Fi › (tu red) › Privada*. La pestaña **Ajustes** de DropToWin avisa de ello, y el botón *Permitir en el Firewall de Windows…* repara las reglas.

## Android

1. **Empareja el teléfono con el PC por Bluetooth** (solo una vez): en el PC abre *Configuración › Bluetooth y dispositivos › Agregar dispositivo*; en el teléfono, *Ajustes › Dispositivos conectados › Vincular nuevo dispositivo*. Confirma que el código coincide en ambos.
2. **Instala el APK**: conecta el teléfono por cable en modo *Transferencia de archivos* y copia `DropToWin.apk` a *Descargas* (o usa `tools\copy-apk-to-phone.ps1`, que lo hace solo). Ábrelo con *Files* y permite instalar apps de esa fuente.
   > Android no acepta archivos `.apk` recibidos por Bluetooth: para este primer paso usa cable o la nube.
3. Abre **DropToWin**, permite *Dispositivos cercanos* y las notificaciones, toca **Elegir PC** y selecciona tu equipo. En el PC pulsa **Permitir** en «Nueva conexión». Solo se pregunta la primera vez.
4. Desde cualquier app: **Compartir › Enviar al PC**. Tu PC aparece además en la fila de *Compartir directo*. El envío sigue en segundo plano con una notificación de progreso.

## iPhone

1. El iPhone y el PC deben estar en la **misma red Wi-Fi**. Sin router, también sirve conectar el PC a la *Compartición de Internet* del iPhone.
2. En el PC: **DropToWin › Dispositivos › Conectar un iPhone…**. Aparece un código QR de un solo uso, válido 10 minutos.
3. En el iPhone: abre la **Cámara**, apunta al código y toca el enlace. En la página escribe un nombre (por ejemplo, «iPhone de Ana») y toca **Conectar**.
4. Desde esa página ya se pueden enviar fotos, videos, archivos y texto. Truco: *Compartir › Añadir a pantalla de inicio* la deja como una app.
5. **Menú Compartir (recomendado)**: la misma página tiene una guía paso a paso para crear en la app **Atajos** el atajo «Enviar al PC», con la URL y la llave listas para copiar. Resumen del atajo:
   - *Mostrar en hoja para compartir* activado.
   - **Repetir con cada** elemento de *Entrada del atajo*, y dentro **Obtener contenido de URL**: método **POST** a `http://<IP del PC>:47812/api/upload`, encabezado `Authorization` con la llave, y cuerpo **Formulario** con un campo **Archivo** llamado `archivo` cuyo valor es *Elemento de repetición*.
   - La primera vez, iOS pide permiso para acceder a la **red local**: acéptalo.

Notas sobre el iPhone:

- El atajo usa la IP del PC. Si un día deja de funcionar porque el PC cambió de dirección, vuelve a abrir la página desde el QR y actualiza la URL del atajo. Para evitarlo, puedes reservar la IP del PC en el router.
- Para videos muy grandes es más fiable el botón de la página, que sube por trozos y se reanuda si se corta.
- Para que lo use otra persona con su propio PC con Windows: instala DropToWin en ese PC (`dist\` más `install.ps1`) y conecta su iPhone a ese PC.

## En el PC

- Los archivos se guardan en **`Descargas\DropToWin`** (se puede cambiar en *Ajustes*, y también organizar en subcarpetas *Imágenes, Videos, Audio, Documentos, Otros*).
- Nunca se sobrescribe nada: un segundo `foto.jpg` se guarda como `foto (1).jpg`. Desde Android y desde Safari se conserva además la fecha original del archivo.
- Las fotos y videos que Google Fotos (o el selector de fotos de Android) comparte con un nombre solo numérico, como `1000012345.jpg`, llegan con nombre de cámara según su fecha de captura (EXIF o metadatos del video): `IMG_20261020_154312.jpg`, `VID_20261020_154312.mp4`.
- Al terminar aparece una notificación, una sola por tanda de archivos; al hacer clic se abre la carpeta.
- El **texto** se copia al portapapeles. Los **enlaces** pueden abrirse solos en el navegador (opción en *Ajustes*).
- **Recibidos**: doble clic abre el archivo; clic derecho ofrece *Mostrar en la carpeta*, *Copiar* y *Quitar de la lista*; también se pueden arrastrar a otras apps.
- **Dispositivos**: teléfonos autorizados (Android e iPhone), con la opción de quitarles el permiso.
- Cerrar la ventana la oculta en la bandeja. Para salir: clic derecho en el icono › *Salir*. Para desinstalar: *Configuración › Aplicaciones › DropToWin › Desinstalar* (tus archivos recibidos y ajustes se conservan).

## Seguridad

- **Android por Bluetooth**: requiere el emparejamiento del sistema (enlace cifrado y autenticado), y además la aprobación de cada teléfono en el PC.
- **Wi-Fi turbo de Android**: TLS 1.2/1.3 con un certificado propio del PC, que el teléfono **fija por su huella SHA-256**. La huella y la llave llegan por el canal Bluetooth ya autenticado, así que nadie en la red puede suplantar al PC.
- **iPhone**: cada iPhone se empareja con un código QR de un solo uso y recibe una **llave propia**; el PC solo guarda su hash. La conexión es **HTTP en la red local, sin cifrar**: Atajos no admite certificados propios. Es razonable en la Wi-Fi de casa, pero en redes públicas o de terceros es mejor no enviar nada privado desde el iPhone.
- Todas las autorizaciones se pueden revocar en **Dispositivos**. Los nombres de archivo se sanean (sin `..\`, caracteres inválidos ni nombres reservados de Windows) y se comprueba el espacio libre antes de recibir.
- La app no hace visible el PC por Bluetooth. Puertos que usa: TCP 47811 (Wi-Fi turbo, TLS) y TCP 47812 (iPhone, HTTP).

Los protocolos están documentados en [PROTOCOLO.md](PROTOCOLO.md).

## Solución de problemas

| Problema | Qué revisar |
|---|---|
| Android: «No se pudo conectar por Bluetooth con el PC» | DropToWin abierto (icono en la bandeja), Bluetooth activado en ambos y el teléfono emparejado. Si sigue fallando, quita el emparejamiento en los dos y vuelve a emparejar. |
| Android: siempre «solo Bluetooth» (lento) | Misma Wi-Fi en ambos, firewall permitido para tu tipo de red (*Ajustes*). Las redes de invitados suelen aislar a los dispositivos entre sí. |
| iPhone: la página o el atajo no conectan | Misma Wi-Fi; DropToWin abierto; firewall permitido; en el iPhone, *Ajustes › Privacidad › Red local › Atajos* activado. Comprueba que la IP del atajo sea la que muestra «Conectar un iPhone…». |
| iPhone: el atajo responde «no autorizado» | El valor del encabezado `Authorization` debe ser exactamente el de la página («Bearer …»). Si quitaste el permiso al iPhone en el PC, vuelve a conectarlo con el QR. |
| No salen notificaciones en el PC | *Configuración › Sistema › Notificaciones* y el modo *No molestar*. Lo recibido aparece igualmente en **Recibidos**. |
| Diagnóstico | `%APPDATA%\DropToWin\registro.txt` (en *Ajustes*, *Abrir carpeta de datos*). |

## Compilar

Requisitos: .NET 10 SDK, JDK 17 o posterior, y el Android SDK con *build-tools 36.0.0* y *platform android-36* (en `%LOCALAPPDATA%\Android\Sdk` o donde indique `ANDROID_HOME`).

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
```

- Deja `DropToWin.exe` y `DropToWin.apk` en `dist\`. Con `-SelfContained`, el `.exe` incluye .NET y funciona en cualquier Windows 10/11 x64 (ocupa más). Con `-SkipAndroid` o `-SkipWindows` compila solo una de las dos partes.
- Después, `install.ps1` instala o actualiza la app en el PC.
- Con `-Bundle` deja además `dist\DropToWin.aab`, el formato que exige Google Play, firmado con la misma clave. Necesita `bundletool-all-<versión>.jar`, de [github.com/google/bundletool/releases](https://github.com/google/bundletool/releases), guardado como `android\tools\bundletool.jar`. Lo que falta para publicar está en [GOOGLE-PLAY.md](GOOGLE-PLAY.md).
- `android\build-apk.ps1` compila el APK **sin Gradle**: aapt2, javac, d8, zipalign y apksigner. La clave de firma está en `android\keystore\`. **Guárdala**: Android solo instala actualizaciones firmadas con la misma clave.
- Para abrir el proyecto Android en Android Studio, abre la carpeta `android\`. Los archivos Gradle están incluidos, pero solo se verificó la compilación con `build-apk.ps1`.

## Pruebas

- `tests\run-protocol-tests.ps1 -Data <carpeta> -Work <carpeta>`: ejecuta el **mismo código Java** de la app Android contra la app de Windows. Un transporte local hace de Bluetooth, y los diálogos de aprobación se responden por UI Automation. Cubre la aprobación y el rechazo, la integridad SHA-256, los nombres conflictivos, las fechas, los cortes con reanudación (Wi-Fi y Bluetooth) y la seguridad del Wi-Fi turbo.
- `tests\iphone-api-test.ps1 -Token <llave> -Received <carpeta>`: simula exactamente lo que envía el atajo de iOS. Cubre nombres con acentos, un video de 25 MB, varios archivos con texto, archivos sin extensión, llaves falsas o ausentes y `..\`.
- `tests\CameraNamesTest.java`: los nombres de cámara para fotos y videos con nombre numérico (no necesita el PC ni el teléfono; el modo de uso está al principio del archivo).
- La página del iPhone se probó en un navegador con tamaño de móvil: emparejamiento, subida por trozos con el SHA-256 verificado, texto y modo oscuro.

## Qué está verificado y qué no

- **Android con un teléfono real (motorola edge 60 fusion)**: emparejamiento, aprobación en el PC y envío de una foto por Bluetooth con salto a Wi-Fi turbo. Funciona.
- **Nombres de cámara para fotos de Google Fotos**: la parte en Java puro está probada en el PC y el código compila contra la API de Android, pero **falta probarlo en el teléfono** compartiendo desde Google Fotos.
- **Android 16 y Google Play**: el objetivo API 36, la pantalla principal en modo edge-to-edge y la generación del `.aab` (`-Bundle`) compilan contra la API de Android 16 y se revisaron contra el código del sistema, pero **falta compilarlos con el Android SDK real y probarlos en el teléfono**. Los archivos Gradle se actualizaron a AGP 8.13 y Gradle 8.13 sin probarlos.
- **iPhone**: el lado del PC y la página están probados, y lo que envía el atajo se simuló con peticiones idénticas. **No se ha probado con un iPhone real**, porque aquí no hay ninguno. Los nombres de las acciones de Atajos pueden variar un poco según la versión de iOS.
- Solo funciona del teléfono al PC, no al revés.

## Estructura

```
windows/                 App de escritorio (C# / .NET 10, WinForms)
  src/Server/            BluetoothServer (RFCOMM vía WinRT), WifiServer (TLS), IphoneServer (HTTP), SessionHandler
  src/Transfers/         Lotes, archivos parciales, nombres seguros, historial
  src/Ui/                Ventana principal, aprobación, QR para iPhone, barra de secciones
  web/                   Página para el iPhone (se incrusta en el .exe)
android/app/src/main/    App Android (Java, sin dependencias externas)
  java/.../core/         Protocolo, TLS con huella fijada, reanudación y nombres de cámara (Java puro, probado en el PC)
tests/                   Pruebas de extremo a extremo y utilidades
tools/                   Icono, copia del APK al teléfono por USB
install.ps1              Instalación por usuario (menú Inicio, inicio con Windows, desinstalador)
PROTOCOLO.md             Especificación de los protocolos
```
