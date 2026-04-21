# Runbook de fallas externas

Este documento resume qué pasa cuando una dependencia externa falla, qué logs esperar y cómo recupera el sistema.

## Objetivo

El servicio está diseñado para **degradarse sin caerse** cuando fallan carpetas de red, SMTP, SQL Server u otras dependencias externas. La idea es que:

- el proceso siga vivo;
- los pipelines sanos continúen;
- el error quede registrado;
- el sistema reintente o aísle solo la parte afectada.

## Tabla rápida

| Falla externa | Ejemplo | Qué hace el sistema | Log esperado | Recuperación |
| --- | --- | --- | --- | --- |
| Carpeta GRE caída | `\\\\servidor\\gre\\pdf` no responde o no existe | El watcher GRE no se inicia; el host sigue vivo y reintenta cada 30s | `GRE pipeline: directorio grePDF no existe` | Automática cuando la ruta vuelve a existir |
| Carpeta IDOC caída | `I2CARPETA` apunta a una ruta caída | El watcher IDOC queda pendiente; GRE puede seguir funcionando | `IDOC pipeline pendiente: carpeta no existe` | Automática cuando la ruta vuelve |
| backOfficeDB caído | No se puede leer `IDOC/I2CARPETA` | IDOC no resuelve carpeta y reintenta cada 30s | `no se pudo resolver IDOC/I2CARPETA` | Automática cuando vuelve la DB |
| Error interno del watcher | Buffer overflow, error del SO o SMB | El engine loggea error, intenta reconstruir watcher y reescanea archivos pendientes | `FileSystemWatcher error` y luego `Watcher recuperado` | Automática, con reintentos cada 30s |
| Archivo bloqueado o incompleto | Archivo aún copiándose por red | Se reintenta apertura varias veces; si no queda listo, se deja para próximos eventos | `Archivo no disponible tras ... reintentos` | Automática ante un `Changed` posterior o conciliación |
| Archivo corrupto | XML inválido, TXT ambiguo | Solo falla ese archivo; tras N intentos va a cuarentena | `Archivo falló X/Y` y luego `Moviendo a cuarentena` | Manual sobre el archivo fallado |
| SQL Server inestable | timeout, red, deadlock, throttling | Se aplican reintentos a errores transitorios; si no alcanza, falla la operación puntual | `SQL transitorio en ... intento X/Y` | Automática en transitorios; manual si la falla persiste |
| SMTP caído | credenciales inválidas, host offline | No se cae el servicio; el correo falla y se descarta el batch | `Console.Error` del sender/processor | Automática cuando SMTP vuelva |
| Carpeta de logs caída | share o disco destino de `ErrorFileLog` no disponible | Falla solo la escritura del log a archivo; el host sigue | `Console.Error` del `ErrorFileLoggerProvider` | Automática cuando la carpeta vuelva |
| Ambas carpetas documentales caídas | GRE e IDOC inaccesibles al mismo tiempo | El host sigue corriendo pero ambos pipelines quedan esperando/reintentando | Warnings periódicos de ambos watchers | Automática cuando vuelvan las rutas |

## Casos detallados

### 1. Servidor documental GRE caído

Ejemplos:

- el share `\\\\servidor\\carpeta\\pdf` no responde;
- el NAS está apagado;
- la cuenta del servicio perdió permisos;
- la VPN o enlace a sede remota cayó.

Comportamiento:

- `GreWatcherHostedService` verifica la carpeta antes de iniciar el watcher;
- si no existe o no está accesible, registra `Warning`;
- espera `30s`;
- vuelve a intentar;
- el proceso principal sigue vivo.

Impacto:

- no entran nuevos GRE por watcher mientras la carpeta esté caída;
- IDOC puede seguir procesando normalmente;
- la conciliación diaria GRE también omitirá esa carpeta si sigue inaccesible.

Operación:

1. Validar acceso a la ruta con la misma cuenta del servicio.
2. Confirmar conectividad al servidor/share.
3. Revisar permisos NTFS/share.
4. Una vez restaurado el acceso, el watcher debería retomar sin reiniciar el host.

### 2. Ambas locaciones documentales caídas

Ejemplo:

- caen simultáneamente las rutas GRE e IDOC;
- o el servidor de archivos común deja de responder.

Comportamiento:

- ambos hosted services quedan en modo reintento;
- el host sigue vivo;
- heartbeat, logging y otros componentes siguen corriendo;
- no se procesan archivos nuevos hasta que reaparezcan las carpetas.

Impacto:

- servicio vivo pero sin throughput documental;
- no hay caída total del proceso;
- se acumulan archivos en origen fuera del proceso hasta que vuelva la disponibilidad.

Operación:

1. Revisar warnings de ambos watchers.
2. Confirmar si el problema es de red, permisos o disponibilidad del file server.
3. Cuando vuelvan las rutas, los watchers retoman solos.
4. La conciliación diaria ayuda a recapturar archivos que no hayan disparado eventos.

### 3. Watcher roto por error del SO / red / buffer

Ejemplos:

- desborde del buffer interno de `FileSystemWatcher`;
- desconexión/reconexión del share SMB;
- error inesperado del watcher del sistema operativo.

Comportamiento:

- `FolderWatcherEngine` recibe el evento `Error`;
- registra `LogError`;
- evita loops de recuperación paralelos;
- intenta recrear el watcher;
- al recuperarse, hace reescaneo inicial de archivos existentes;
- ese reescaneo está acotado por `RescanMaxFiles` y `RescanMaxAge`.

Impacto:

- puede haber una pequeña ventana sin eventos en vivo;
- el reescaneo reduce la posibilidad de pérdida de archivos.

Operación:

1. Buscar `FileSystemWatcher error`.
2. Confirmar luego un `Watcher recuperado`.
3. Si el error persiste en loop, revisar carga, tamaño de buffer y estabilidad del share.

### 4. Archivo bloqueado, incompleto o en copia

Ejemplos:

- un tercero todavía está copiando un PDF/XML grande;
- antivirus o indexador mantiene lock;
- SMB entrega archivo visible antes de finalizar la escritura.

Comportamiento:

- el engine intenta abrir el archivo varias veces;
- usa `FileReadyRetries` y `FileReadyDelayMs`;
- si no queda listo, loggea `Warning`;
- no se cae el pipeline;
- si llega otro evento `Changed`, se vuelve a intentar.

Impacto:

- retraso puntual sobre ese archivo;
- el resto puede seguir avanzando.

Operación:

1. Revisar si el origen escribe archivos de forma no atómica.
2. Ajustar reintentos si el entorno de red es lento.
3. Confirmar luego si el archivo entró por evento posterior o conciliación.

### 5. Archivo corrupto

Ejemplos:

- XML con nodos faltantes;
- número inválido;
- TXT GRE con formato ambiguo;
- archivo mal generado por sistema upstream.

Comportamiento:

- el error se maneja por archivo;
- el pipeline sigue con los demás;
- el engine cuenta fallos por path;
- tras `MaxProcessAttempts`, mueve el archivo a cuarentena (`FailedFolder/yyyy-MM-dd/`);
- genera `.log` adyacente con stacktrace.

Impacto:

- un archivo malo no bloquea el lote completo ni mata el host.

Operación:

1. Ir a la carpeta de cuarentena.
2. Revisar el `.log`.
3. Corregir/regenerar el archivo si aplica.
4. Reinyectarlo manualmente si corresponde.

### 6. SQL Server caído o inestable

Ejemplos:

- timeout;
- caída de red;
- reinicio del motor SQL;
- deadlock;
- throttling.

Comportamiento:

- las operaciones IDOC usan `SqlTransientRetry`;
- los errores transitorios conocidos se reintentan hasta 5 veces con backoff;
- si no se recupera, falla la operación puntual;
- el host sigue vivo;
- la conciliación continúa con otros archivos aunque uno falle.

Impacto:

- pueden fallar inserciones individuales;
- puede crecer la cola natural de archivos en origen;
- archivos persistentemente fallidos pueden terminar en cuarentena según el flujo.

Operación:

1. Verificar disponibilidad y latencia hacia SQL.
2. Revisar timeouts/deadlocks en servidor.
3. Si fue un evento transitorio, el propio sistema debería recuperarse solo.

### 7. SMTP caído

Ejemplos:

- host SMTP fuera de línea;
- credenciales inválidas;
- TLS/STARTTLS incompatible;
- firewall bloqueando puerto.

Comportamiento:

- el envío por correo falla sin derribar el servicio;
- no hay feedback loop infinito;
- la cola es bounded;
- los batches fallidos se descartan;
- el problema se escribe en `Console.Error`.

Impacto:

- se pierde esa alerta por correo puntual;
- pero los logs siguen quedando en Event Log y, si aplica, en archivo.

Operación:

1. Revisar conectividad SMTP, credenciales y TLS.
2. No depender del correo como único canal de observabilidad.

### 8. Carpeta de logs de error no disponible

Ejemplos:

- `ErrorFileLog.FolderPath` apunta a un disco/share caído;
- permisos insuficientes sobre la carpeta.

Comportamiento:

- el provider de archivo intenta crear carpeta/escribir;
- si falla, informa por `Console.Error`;
- no rompe el proceso ni el resto del logging.

Impacto:

- se pierde temporalmente la persistencia local del log de error;
- Event Log y resto del sistema siguen operativos.

Operación:

1. Confirmar que la carpeta exista.
2. Confirmar permisos de la cuenta del servicio.
3. Preferir una ruta local estable si el objetivo es resiliencia.

## Qué mirar en operación

### Logs esperados

Buscar estos patrones:

- `GRE pipeline: directorio grePDF no existe`
- `IDOC pipeline pendiente: carpeta no existe`
- `no se pudo resolver IDOC/I2CARPETA`
- `FileSystemWatcher error`
- `Watcher recuperado`
- `Archivo no disponible tras`
- `Moviendo a cuarentena`
- `SQL transitorio en`

### Señales de salud

El heartbeat reporta:

- si el watcher está activo;
- cuántos archivos pendientes tiene;
- cuál fue el último archivo procesado;
- qué ruta está observando.

Si un watcher no aparece activo durante mucho tiempo o el `lastProcessed` queda viejo, hay una dependencia externa degradada o ausencia real de carga.

## Resumen operativo

Ante fallas externas, el diseño actual prioriza:

- **continuidad del host**;
- **aislamiento del fallo a un pipeline o archivo**;
- **reintentos automáticos donde tiene sentido**;
- **cuarentena para datos corruptos**;
- **observabilidad por logs y heartbeat**.

La limitación deliberada es que, si una dependencia sigue caída, el sistema no “adivina” una solución: simplemente queda reintentando hasta que el entorno vuelva a estar sano.
