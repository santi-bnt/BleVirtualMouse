# AirPlay: integracion y seguridad

Se investigo UxPlay oficial: https://github.com/FDH2/UxPlay
El README documenta Windows con MSYS2/MinGW y pipelines GStreamer configurables.
Se compilo el commit 2c7b63ee9c36edfb121186db928397c582852133 (1.74 experimental).
Incluye mDNS propio; no se instalo Bonjour ni se uso Visual Studio.

La integracion usa un proceso externo UxPlay, no una reimplementacion de AirPlay.
Su salida GStreamer se convierte en JPEG multipart y llega a un puerto TCP
aleatorio enlazado exclusivamente a 127.0.0.1. La app muestra estos fotogramas.
No se captura el escritorio, otras ventanas ni se graba video en disco.
La vista conserva solo el fotograma mas reciente; limita JPEG a 8 MB y 16 millones
de pixeles antes de cargar la imagen completa y reduce el ancho visible a 1080.
Se eligio esta integracion directa de salida en lugar de capturar una ventana:
evita problemas de captura de superficies Direct3D y conserva el tamano del video.

`setup-airplay.ps1` instala paquetes oficiales firmados de MSYS2 con pacman,
descarga el codigo oficial fijado al commit y compila con GCC/CMake/Ninja.
El ejecutable local esta en tools/uxplay/uxplay.exe; runtime.txt apunta al runtime
MSYS2 UCRT64. El codigo fuente/licencia GPLv3 permanecen en tools/sources.
Se instalaron aproximadamente 169 MB de descargas / 1.2 GB de dependencias.
No se agregaron servicios, excepciones de antivirus ni reglas de firewall.

Al iniciar mirroring se exige una clave aleatoria nueva, visible en la app,
no guardada deliberadamente en logs/config. La clave se pasa al proceso;
otros procesos del mismo usuario pueden inspeccionar su linea de comandos.
No usar en redes publicas/desconocidas. AirPlay expone un servicio de red
mientras este iniciado; un PIN/password no elimina todo riesgo de software.
Para cerrarlo usa Detener mirroring o cierra la app.
Emergency Stop bloquea entradas HID, no detiene la visualizacion.

Si Windows pregunta por acceso de red, permite solamente redes privadas
confiables. No desactives el firewall ni permitas redes publicas. Si el receptor
no aparece, verifica la misma LAN, red privada, firewall y aislamiento del router.
No se cambian estos ajustes automaticamente. Se usan puertos AirPlay desde 7000
y mDNS UDP 5353; el enlace interno JPEG solo usa localhost.

Estado conectado/video: solo tras decodificar un fotograma recibido. Si no llega
video durante cinco segundos se elimina la imagen y se bloquea control sobre ella.
El FPS mostrado mide fotogramas JPEG recibidos, no la tasa de pantalla del iPhone.
Una imagen recibida por localhost no autentica por si sola la identidad del telefono.

Limitaciones: mDNS interno 1.74 es experimental, DRM puede impedir video,
latencia depende de Wi-Fi/CPU y la codificacion JPEG tiene coste adicional.
La precision del cursor necesita prueba fisica: HID relativo sin feedback,
aceleracion de iOS y paquetes perdidos pueden causar deriva. La calibracion
no convierte el dispositivo en HID absoluto. No se declara AirPlay probado
con iPhone hasta recibir una conexion real.
