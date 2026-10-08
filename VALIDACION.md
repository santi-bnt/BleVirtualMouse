# Resultado real de esta fase

## Fase de robustez del puntero

- Nuevo IPhonePointerController como frontera de comandos HID de la app.
- Nuevo PointerOptions, seis ajustes persistentes y modo Precise.
- GestureController extendido con homing, 9 posiciones, drift y scroll de prueba;
  mantiene interpolacion y liberacion en finally.
- MainWindow.xaml/.cs: siete acciones, RESUME y metricas de puntero/USB/video.
- IPhoneCoordinateMapper.cs: migracion de configuracion y preservacion de ajustes.
- config.json: se mantuvo la calibracion real y se agregaron seis opciones.
- tests/PointerControllerTests.cs y tests/Program.cs: tests nuevos sin quitar los anteriores.
- tests/AirPlaySmokeTests.cs y tests/UiSmokeTests.cs: conteo solo de comprobaciones realmente ejecutadas.
- test.ps1: salidas aisladas en artifacts/test-build, sin bloquear el ejecutable abierto.
- README.md, VALIDACION.md y PHYSICAL_TESTS.md: instrucciones y limitaciones.
- 71 comprobaciones locales correctas con test.ps1 -AirPlay.
- Compilacion Debug x64: cero errores/advertencias. Firmware esp32dev: SUCCESS.
- run.ps1 sigue sin cambios y abre la app. No se modifico ni flasheo firmware.
- Cliente serial, HID/pairing/protocolo y receptor UxPlay no se cambiaron.

Estas siete pruebas fisicas no fueron ejecutadas por el agente en el telefono:
los recorridos se verificaron con transporte simulado. El usuario ya habia
confirmado AirPlay, suscripcion HID y movimiento del puntero. La validacion de
centro exacto, drift, scroll real y parada fisica requiere observar el iPhone.
Guia: [PHYSICAL_TESTS.md](PHYSICAL_TESTS.md).

Lo que sigue describe la fase inicial de mirroring y sus pruebas.

## Cambios

Modificados:
- BleVirtualMouse/MainWindow.xaml
- BleVirtualMouse/MainWindow.xaml.cs
- run.ps1 (cierre normal y raiz de logs/config)
- setup.ps1 (instruccion para dependencia AirPlay)
- clean.ps1 (tests y validacion de limite de rutas)
- README.md

Nuevos:
- BleVirtualMouse/Services/BluetoothMouseService.cs
- BleVirtualMouse/Services/AirPlayReceiverService.cs
- BleVirtualMouse/Services/ControllerLog.cs
- BleVirtualMouse/Controllers/IPhoneCoordinateMapper.cs
- BleVirtualMouse/Controllers/GestureController.cs
- BleVirtualMouse/Controllers/AutomationController.cs (deshabilitado)
- BleVirtualMouse/Controls/ScreenView.cs
- setup-airplay.ps1
- test.ps1
- tests/ControllerTests.csproj
- tests/Program.cs
- tests/AirPlaySmokeTests.cs
- tests/UiSmokeTests.cs
- AIRPLAY.md y VALIDACION.md

build.ps1 se conserva. El firmware y Esp32MouseClient.cs no se editaron.
SHA256 del cliente antes y despues:
611446ED8E2386B57448597961108C0AF9D467EA543225C67BE2D93E52381CBA
UxPlay externo, fuentes y build quedan en tools/. config.json se crea al guardar
una calibracion real; no se invento una calibracion inicial.

## Pruebas ejecutadas

- dotnet restore mediante build.ps1/run.ps1: correcto.
- dotnet build Debug x64: correcto, cero errores y cero advertencias.
- run.ps1: abre el ejecutable y verifica que permanece vivo.
- firmware-build.ps1, esp32dev: SUCCESS. No se flasheo ni abrio COM3.
- setup-airplay.ps1: UxPlay compilado con GCC/CMake/Ninja, sin Visual Studio.
- test.ps1 -AirPlay: ALL TESTS PASSED.
- Mapper: centro y esquinas, bandas laterales, tamano cero, persistencia sin
  restaurar una posicion fisica desconocida.
- Swipe: destino final, direccion monotona, paquetes pequenos, tiempo minimo.
- Parada: cancelacion durante swipe y durante pulsacion, UP de ambos botones,
  bloqueo de nuevas entradas, invalidacion de estimacion del cursor.
- Multipart: dos fotogramas consecutivos, rechazo de tamano excesivo.
- UxPlay nativo: inicializacion y espera sin cliente; no informa video falso.
- Paradas concurrentes de UxPlay serializadas sin dejar el receptor activo.
- GStreamer real: cinco imagenes sinteticas JPEG, TCP local, parseo y decodificacion.
- WPF: smoke test interno del evento Click de Emergency Stop y ruta PreviewKeyDown
  de ESC. No es una prueba de inyeccion de teclado desde el sistema operativo.
- ScreenView: comprobacion de pixeles no vacios y bandas con imagen sintetica.
- Renderizado de ventana en 1100x820 y 820x660; PNG en artifacts/ui-smoke.

El ayudante de automatizacion visual de Windows no estaba disponible
(native pipe no encontrado). Por ello se usaron pruebas WPF dentro del proceso,
y se inspeccionaron sus imagenes renderizadas; no se simulo una prueba externa.

## Pendiente fisicamente

Recibir una conexion Screen Mirroring real del iPhone, verificar password y
descubrimiento/firewall en esta red, medir latencia, validar orientacion y
calibracion, probar tap/drag/rueda sobre el telefono y parada durante un drag real.
El usuario habia confirmado que el mouse BLE previo funciona. Esta fase no
pretende convertir HID relativo en absoluto ni garantizar precision sin feedback.
No se implemento vision artificial ni automatizacion de videojuegos.
