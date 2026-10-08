# iPhone Controller con BleVirtualMouse / ESP32

## Pruebas del puntero

Se agregaron HOME POINTER, POSITION TEST, DRAG TEST, SCROLL TEST, EMERGENCY TEST,
DRIFT TEST y el nuevo controlador IPhonePointerController. TAP CENTER reutiliza
TapNormalizedAsync y hace homing previo en el modo Precise configurado por defecto.
Guia de las siete pruebas, logs, opciones y limites: [PHYSICAL_TESTS.md](PHYSICAL_TESTS.md).

## Ejecutar

Desde esta carpeta, en PowerShell normal (sin administrador):

```powershell
powershell -ExecutionPolicy Bypass -File .\run.ps1
```

`run.ps1` cierra normalmente la instancia anterior, restaura, compila Debug x64
y abre la app. No usa Visual Studio ni una solucion .sln. `build.ps1` solo compila;
cierra la app antes de usarlo. Tambien: `dotnet run --project .\BleVirtualMouse`.
Para pruebas: `powershell -ExecutionPolicy Bypass -File .\test.ps1`.

## Ver el iPhone

Prepara AirPlay una vez:

```powershell
powershell -ExecutionPolicy Bypass -File .\setup-airplay.ps1
```

Necesita MSYS2; si falta, el script muestra `winget install MSYS2.MSYS2 --source winget`.
Se compila UxPlay oficial con GCC, no con herramientas de Visual Studio.
No instala Bonjour ni modifica automaticamente el firewall.

1. Conecta la PC y el iPhone a la misma red privada confiable, sin aislamiento de clientes.
2. Abre la app y pulsa **Iniciar mirroring**.
3. En el Centro de control del iPhone, abre **Duplicar pantalla / Screen Mirroring**.
4. Selecciona **iPhone Controller** y escribe la clave que muestra la app.
5. El estado cambia a **recibiendo video** solo cuando llega un fotograma real.

Si el receptor no aparece, revisa red privada y permisos de UxPlay en firewall
solo para redes privadas. No desactives firewall/antivirus ni permitas redes publicas.
Detalles de integracion, fuente, dependencias y limitaciones: [AIRPLAY.md](AIRPLAY.md).
AirPlay no queda probado con iPhone solo por iniciar UxPlay.

## Calibrar y controlar

1. Conecta el puerto USB ESP32 y espera BLE emparejado y suscrito; conserva AssistiveTouch activo.
2. Pulsa **Calibrar**. Usa las flechas para colocar el cursor fisico arriba-izquierda.
3. Pulsa **Guardar arriba-izquierda / sincronizar**.
4. Usando solo las flechas de esta app, coloca el cursor abajo-derecha; evita
   seguir moviendolo contra el borde, porque esos desplazamientos tambien se cuentan.
5. Pulsa **Guardar abajo-derecha**. Se guarda `config.json` en esta carpeta.

Un clic en el video mueve relativamente y pulsa/suelta; mantener presionado y
arrastrar produce drag. La rueda envia scroll. Los botones TAP/SWIPE usan la
misma API IPhonePointerController, que reutiliza GestureController. La automatizacion sigue deshabilitada.
El espacio negro exterior de la imagen no acepta coordenadas.

**La calibracion es aproximada, no posicionamiento absoluto fiable.** El firmware
solo envia HID relativo y no conoce el cursor real. La aceleracion de iOS, perdidas
de paquetes y topes pueden provocar deriva. Mantiene una estimacion de unidades HID,
no coordenadas confirmadas por iPhone. Tras cerrar, reconectar, cambiar orientacion
o pulsar Emergency Stop, coloca el cursor arriba-izquierda y marca la referencia
de nuevo. Si cambias orientacion/velocidad del cursor, recalibra ambas esquinas.
Para reutilizar el perfil, despues de marcar arriba-izquierda pulsa
**Usar calibracion guardada**; no necesitas recorrer ambas esquinas otra vez.

Conectar/Desconectar abre/cierra el control USB, no apaga la radio BLE de la ESP32.
La desconexion solicita soltar botones antes de cerrar USB. Para apagar el
dispositivo Bluetooth por completo desconecta la alimentacion de la placa.

**Emergency Stop o ESC** cancela gestos, solicita UP para ambos botones y bloquea
nuevos comandos hasta **RESUME**. ESC funciona cuando la app tiene foco, no es
un atajo global. No puede garantizar liberacion fisica si USB/BLE se desconecto.
Al perder la suscripcion se bloquea el control. Detener mirroring tambien activa
la parada. Los gestos se envian cada 20 ms, con paquetes de hasta 24 unidades;
si la distancia es grande, un swipe puede durar mas que el tiempo solicitado.

Logs: `logs/latest.log` (BLE, AIRPLAY, INPUT, POINTER, GESTURE, EMERGENCY, ERROR). Mostrar debug incluye
coordenadas, destino estimado y FPS recibido. No se guarda video en disco.

## Firmware existente

Lista de archivos y pruebas reales: [VALIDACION.md](VALIDACION.md).

La ESP32 implementa el mouse Bluetooth LE HID. La aplicacion Windows solo envia movimientos y clics por el cable USB; el adaptador Bluetooth de la PC no se usa.

El proyecto no usa Visual Studio, MSIX, certificados locales ni Windows SDK. El firmware se compila y carga con PlatformIO desde PowerShell.

## Preparar una vez

```powershell
powershell -ExecutionPolicy Bypass -File .\setup.ps1
```

Esto prepara PlatformIO 6.1.19 dentro de `.venv` y `.platformio` en esta misma carpeta.

La configuracion principal esta en `firmware\platformio.ini`. Usa `esp32dev` por defecto, guarda sus herramientas en `..\.platformio` y configura el puerto serial a 115200 baud. No hace falta abrir un IDE. [Referencia oficial de core_dir](https://docs.platformio.org/en/latest/projectconf/sections/platformio/options/directory/core_dir.html).

## Compilar el firmware

```powershell
powershell -ExecutionPolicy Bypass -File .\firmware-build.ps1
```

Perfiles disponibles:

- `esp32dev`: ESP32 DevKit clasica.
- `esp32-c3`: ESP32-C3.
- `esp32-s3`: ESP32-S3.

El comando anterior solo compila `esp32dev`: no carga ni reinicia la placa. Para comprobar todas las variantes, agrega `-Board all`.

## Encontrar el puerto USB

```powershell
powershell -ExecutionPolicy Bypass -File .\firmware-devices.ps1
```

Esto solo enumera dispositivos: no abre el puerto. En la prueba anterior se detecto una ESP32 clasica con CP210x en `COM3`, pero el puerto puede cambiar. Confirma tambien el modelo impreso en tu placa antes de cargarla.

Cuando decidas cargarlo:

```powershell
powershell -ExecutionPolicy Bypass -File .\firmware-flash.ps1 -Board esp32dev -Port COM3
```

Usa `COM3` solo si aparece en la lista actual; si hay un unico puerto puedes omitir `-Port`. El script pide confirmacion antes de reemplazar el firmware. Para simular sin escribir nada, agrega `-WhatIf`.

Si no entra automaticamente en modo de carga, manten pulsado `BOOT` mientras empieza la conexion y sueltalo cuando comience la escritura. Desconecta primero la app del puerto USB y cierra cualquier monitor serial.

## Abrir la aplicacion

```powershell
powershell -ExecutionPolicy Bypass -File .\run.ps1
```

No requiere PowerShell como administrador. El script restaura, compila Debug x64 y abre la aplicacion directamente.

## Probar con iPhone

1. Carga primero el firmware correcto en la ESP32.
2. Ejecuta `run.ps1`.
3. En la app pulsa `Buscar ESP32`, selecciona el puerto de tu placa y pulsa `Conectar`.
4. Activa AssistiveTouch en el iPhone.
5. Abre `Configuracion > Accesibilidad > Tocar > AssistiveTouch > Dispositivos > Dispositivos Bluetooth`.
6. Selecciona `BleVirtualMouse ESP32`.
7. Espera a que la app indique `cliente emparejado y suscrito a HID` y prueba los botones.

Los botones se activan solo despues de verificar el firmware y recibir los estados de emparejamiento y suscripcion HID. El usuario confirmo que el mouse existente ya mueve el puntero; el nuevo control por video requiere su propia prueba fisica.

El estado serial incluye `subscribed`, `sent` y `failed`. `OK MOVE queued=1` significa comando encolado, mientras que `HID report accepted` significa que NimBLE acepto el reporte, no una confirmacion de iOS. `rc=6` es `BLE_HS_ENOMEM` (memoria/buffers insuficientes), no el codigo de desconexion. La biblioteca local espera la suscripcion y reintenta con pausa si falla. Consulta `firmware\lib\HijelHID_BLEMouse\LOCAL_CHANGES.md` para los cambios al codigo de terceros.

Para viajar no hace falta Wi-Fi ni Internet durante el uso, pero esta version necesita la PC conectada por USB para mandar comandos. Para usar solamente ESP32 e iPhone harian falta controles fisicos, como botones o un joystick, que aun no estan implementados.

## Seguridad

BLE usa Secure Connections con cifrado y un vinculo persistente en modo Just Works, el modo normal para un mouse sin pantalla ni teclado. El firmware rechaza movimientos hasta que el enlace esta emparejado. Haz el primer emparejamiento cerca del iPhone y usa `Borrar vinculo iPhone` antes de prestarlo o conectarlo a otro telefono.

Just Works no ofrece verificacion de identidad contra ataques de intermediario durante el primer emparejamiento. No significa que cualquier dispositivo conectado sea tu iPhone; emparejalo en un lugar controlado y apaga la placa cuando no la uses.

El mouse no necesita Bluetooth de Windows ni certificados. El firmware solo acepta comandos ASCII por el puerto USB local. El nuevo mirroring si abre servicios AirPlay de red mientras esta iniciado y un receptor JPEG en localhost; consulta AIRPLAY.md. No usar en una red desconocida.
