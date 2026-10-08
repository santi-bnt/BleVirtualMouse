# Siete pruebas fisicas del puntero

## Preparacion

Desde la raiz, sin administrador:

```powershell
powershell -ExecutionPolicy Bypass -File .\run.ps1
```

Conecta USB en la app, activa AssistiveTouch y espera BLE emparejado/suscrito.
Inicia mirroring y selecciona iPhone Controller en Duplicar pantalla del iPhone.
La calibracion existente se conserva. Usa una pantalla segura, sin compras,
mensajes ni acciones destructivas. Los botones de prueba estan arriba de las
metricas; la barra lateral puede desplazarse para acceder a todos los controles.
Nada se ejecuta automaticamente al iniciar. Las siete pruebas requieren observacion
fisica: los tests de transporte simulado no sustituyen ver el resultado en iPhone.

| Test | Boton | Resultado que debes observar | Log en logs/latest.log |
| --- | --- | --- | --- |
| A - Homing | HOME POINTER | Movimiento por pasos arriba-izquierda; termina en la esquina. Homed=True y estimacion=(0,0). | [POINTER] homing started / homing complete / estimated position=(0,0) |
| B - 9 posiciones | POSITION TEST | Homing y nueve puntos, por filas: x=.1,.5,.9; y=.1,.5,.9. Pausa 650 ms por punto, sin clic. | target normalized, estimated position, position test 1/9..9/9 y commands sent/failed |
| C - Centro | TAP CENTER | En Precise hace homing, se coloca al centro y pulsa/suelta una vez. | [GESTURE] tap normalized=(0.500,0.500), homing, BUTTON LEFT DOWN y UP |
| D - Drag | DRAG TEST | Arrastra desde (.5,.75) hasta (.5,.25), manteniendo pulsado aproximadamente 1 s. | [GESTURE] swipe ... duration=1000, DOWN, movimientos B:01 y UP |
| E - Rueda | SCROLL TEST | En una lista desplazable, rueda +3, pausa 650 ms y rueda -3. No reposiciona el puntero. | [GESTURE] scroll up / scroll down; SCROLL 3 y -3 |
| F - Parada | EMERGENCY TEST, despues ESC o EMERGENCY STOP | Drag de 5 s. Cuando ya este arrastrando, pulsa ESC con foco en la app; se cancela, solicita UP y bloquea entradas. RESUME desbloquea, pero requiere nuevo HOME. | [EMERGENCY] stop requested / current gesture cancelled / mouse released / input locked; no MOVE posterior hasta RESUME |
| G - Deriva | DRIFT TEST | Homing, 50 visitas por cinco puntos repetidos y termina al centro, sin clic. Compara centro real antes/despues. | drift movements=1/50..50/50; drift complete estimated final, commands sent y failed |

## Configuracion y limites reales

config.json agrega pointerStep=24, pointerDelayMs=20, homingIterations=96,
tapDelayMs=40, dragUpdateHz=50 y pointerMode=Precise. Mantiene WidthUnits,
HeightUnits y VideoAspect de tu calibracion. Edita las opciones con la app cerrada.
Fast omite homing antes del tap; Precise lo realiza. No hay correccion visual.
Homing admite 1..1000 pasos de 1..127 unidades, pausa 20..1000 ms. Si no llega a
la esquina, ajusta homingIterations y vuelve a probar; no falsifiques la calibracion.
Drag acepta 30..60 Hz, pero limita el envio efectivo a 50 Hz por el firmware actual.
La interpolacion amplifica la duracion si la distancia requiere mas paquetes que
los permitidos con pointerStep. Usa los valores por defecto para estas pruebas.

Homed es una referencia ESTIMADA tras completar los comandos, no una deteccion
de esquina. No hay feedback de cursor; aceleracion y saturacion pueden causar
desfase. USB commands sent cuenta escrituras satisfactorias, no entrega HID/iOS.
Commands failed cuenta excepciones de transporte; los failed de NimBLE siguen
visibles en las lineas BLE STATUS. PING, STATUS y gestion de vinculos no cuentan
como comandos de puntero. Estimated X/Y son unidades HID, no pixeles del iPhone.

Emergency Stop cancela delays y movimientos pendientes de la app y pide UP en
try/finally. No puede retirar un reporte ya escrito por USB o aceptado por NimBLE;
el protocolo actual no tiene flush/cancel en firmware. Tampoco puede garantizar
liberacion fisica con USB/BLE desconectado. Si falla UP, conserva el bloqueo y
RESUME reintenta la liberacion antes de permitir entrada. ESC no es global.

## Tests locales

```powershell
powershell -ExecutionPolicy Bypass -File .\test.ps1 -AirPlay
```

Las salidas quedan aisladas en artifacts/test-build, para no sobrescribir la app
abierta. Se ejecutan los tests anteriores y los nuevos de configuracion,
normalizacion, homing, drag, emergencia/resume, fallos y recorridos sin clic.
Los PNG de prueba son sinteticos, no capturas de una conexion fisica de esta fase.
