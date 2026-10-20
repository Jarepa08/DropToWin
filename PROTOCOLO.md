# Protocolo DropToWin v1

Documento de referencia para quien quiera modificar o portar alguna de las apps. Las secciones de transportes, tramas, sesión, mensajes y reanudación describen el protocolo de Android; la parte de iPhone tiene su propia sección más abajo.

## Transportes

| Canal | Uso | Detalles |
|---|---|---|
| **Bluetooth RFCOMM** | Canal principal: autorización, texto, archivos | UUID de servicio `4f9a6c1e-2b7d-4c3a-9e58-d17b2a6f0c93`, nombre SDP `DropToWin`. El cifrado y la autenticación los da el emparejamiento Bluetooth del sistema. |
| **Wi-Fi turbo (TCP + TLS)** | Solo datos pesados, opcional | Puerto `47811`. Certificado ECDSA P-256 autofirmado por el PC; el teléfono lo **fija por huella SHA-256**, que recibe por Bluetooth junto con una llave de sesión de un solo dispositivo. Nunca se negocia confianza por Wi-Fi. |
| Transporte de prueba | Solo desarrollo (`--test-transport`) | TCP sin cifrar en `127.0.0.1:47819`, se comporta como si fuera Bluetooth. |

El protocolo de mensajes es **idéntico** en todos los canales: el teléfono abre un flujo de bytes y habla igual por Bluetooth o por Wi-Fi.

## Tramas

```
+----------------+--------+-------------------+
| longitud (u32) | tipo   | carga (longitud)  |
| big-endian     | 1 byte | máx. 1 MiB        |
+----------------+--------+-------------------+
```

* `J` (0x4A): objeto JSON en UTF-8. Campo `t` = tipo de mensaje.
* `D` (0x44): bytes del archivo en curso.

## Sesión

```
Teléfono                                  PC
   | J hello {v, deviceId, deviceName, token?}  |
   |------------------------------------------->|
   |            J pending {reason:"approval"}   |  (solo la primera vez: el PC pide permiso al usuario)
   |<-------------------------------------------|
   |   J welcome {pcId, pcName, transport,      |
   |              wifi?:{port, fp, token, addrs}}|
   |<-------------------------------------------|
```

* `deviceId` es un identificador aleatorio que la app genera una vez.
* Por Bluetooth, un dispositivo desconocido (o un `deviceId` conocido desde otra dirección Bluetooth) dispara un diálogo de aprobación en el PC (60 s). Si se rechaza: `J denied {reason: "rejected" | "busy" | "timeout" | "not-paired"}` y se cierra.
* `wifi` solo se ofrece por Bluetooth y si el Wi-Fi turbo está activado. `addrs` son direcciones IPv4 con prefijo (`192.168.1.20/24`); el teléfono solo prueba las que comparten subred con alguna de sus interfaces.
* Por Wi-Fi el `hello` debe incluir el `token` recibido por Bluetooth (válido 30 min desde el último uso, ligado al `deviceId`). Si no es válido: `denied {reason:"token"}`.

## Mensajes

| Teléfono → PC | Respuesta del PC |
|---|---|
| `ping` | `pong` |
| `text {text}` | `ok` (el PC lo copia al portapapeles) |
| `batch {files:[{name, size, mime?, modified?}]}` | `batch-ok {batchId, files:[{id, done}]}` o `error {code:"no-space"\|"invalid", message}` |
| `file {batchId, fileId}` | `file-ready {offset}` o `file-done {savedAs}` si ya estaba completo |
| *(tras `file-ready`)* tramas `D` hasta completar `size - offset` bytes | `file-done {savedAs}` o `error {code:"disk"}` |
| `cancel {batchId}` | `ok` |
| `bye` | (cierra) |

`modified` es la fecha de modificación en milisegundos Unix; el PC la aplica al archivo guardado.

## Reanudación

El PC guarda cada archivo en `<carpeta>\.droptowin-partial\<batch>-<id>.part` mientras llega. Si la conexión se corta, el teléfono vuelve a conectar (por Bluetooth y, si se puede, salta a Wi-Fi), envía `file` con el mismo `batchId`/`fileId` y el PC responde `file-ready {offset}` con los bytes que ya tiene. Los lotes viven en memoria del PC 12 h; si el PC se reinició, responde `error {code:"unknown-batch"}` y el teléfono crea un lote nuevo con los archivos pendientes.

Si una sesión nueva pide un archivo que otra sesión (ya muerta) seguía escribiendo, el PC cancela la vieja y continúa con la nueva.

## iPhone (HTTP en la red local, puerto 47812)

iOS no permite transferir archivos por Bluetooth a un PC, así que el iPhone usa HTTP sobre Wi-Fi. No hay TLS porque la app Atajos no acepta certificados propios; la autorización es una llave por iPhone en `Authorization: Bearer <llave>` (también se acepta la llave sin el prefijo `Bearer`, o en `X-DTW-Token`). El PC solo guarda el SHA-256 de cada llave.

| Método y ruta | Uso |
|---|---|
| `GET /iphone` | Página para Safari (emparejamiento, subida, guía del atajo). |
| `GET /api/info` | `{app, pcName}`, sin autorización. |
| `POST /api/pair {code, name}` | Canjea el código de un solo uso del QR (10 min). Devuelve `{token, deviceId, deviceName, pcName, uploadUrl}`; si el código no vale, 403. |
| `GET /api/ping` | Comprueba la llave: `{pcName, deviceName}` o 401. |
| `POST /api/upload` | Lo que usa **Atajos**: `multipart/form-data`. Cada campo de tipo archivo se guarda (el nombre sale de `filename`/`filename*`); un campo de texto llamado `texto` va al portapapeles. También acepta un cuerpo crudo con `?name=` o `X-DTW-Filename`. Devuelve `{saved: [...]}`. |
| `POST /api/text {text}` | Texto al portapapeles. |
| `POST /api/batch {files:[{name,size,mime?,modified?}]}` | Lo que usa **Safari**: crea un lote (igual que el mensaje `batch` del protocolo de Android). |
| `PUT /api/batch/{b}/{f}?offset=N` | Trozo de archivo a partir del byte N. Devuelve `{received, done, savedAs}`; si N no coincide con lo recibido, 409 con `{received}`. |
| `GET /api/batch/{b}/{f}` | Estado para reanudar: `{received, done, savedAs}`. |

El QR codifica `http://<IP>:47812/iphone#p=<código>`. El código va en el fragmento, así que nunca viaja al servidor salvo en el `POST /api/pair`. Tras emparejar, la página guarda la llave en `localStorage` y también en el fragmento de la URL, para que *Añadir a pantalla de inicio* la conserve.

## Seguridad

* Bluetooth: requiere emparejamiento del sistema (el socket RFCOMM del teléfono es "seguro"), más la aprobación del dispositivo en la app del PC la primera vez. Los dispositivos autorizados se pueden revocar desde la pestaña *Dispositivos*.
* Wi-Fi: TLS 1.2/1.3 con certificado fijado por huella; la huella y la llave viajan por el canal Bluetooth ya autenticado, así que un atacante en la red Wi-Fi no puede suplantar al PC ni al teléfono.
* Nombres de archivo saneados en el PC (sin rutas, sin caracteres inválidos, sin nombres reservados de Windows); nunca se sobrescribe un archivo existente.
