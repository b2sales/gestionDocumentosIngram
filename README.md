# GestionDocumentos

Servicio Windows que automatiza el procesamiento e integración de dos tipos de documentos electrónicos:

- **GRE (Guía de Remisión Electrónica):** archivos PDF que se parsean, se insertan en base de datos SQL Server y se distribuyen a carpetas de destino según el cliente.
- **IDOC:** archivos XML generados por la plataforma Tibco que se parsean, normalizan e insertan en base de datos SQL Server.

El servicio monitorea carpetas de red en tiempo real, encola los archivos detectados y los procesa de forma concurrente. Una reconciliación diaria programada actúa como red de seguridad para reprocesar archivos que el monitor haya podido perder.

---

## Tabla de contenidos

1. [Arquitectura y estructura del proyecto](#arquitectura-y-estructura-del-proyecto)
2. [Inicio rápido para desarrolladores](#inicio-rápido-para-desarrolladores)
3. [Referencia completa de Parametros.json](#referencia-completa-de-parametrosjson)
4. [Instalación del servicio en Windows](#instalación-del-servicio-en-windows)
5. [Validación post despliegue](#validación-post-despliegue)
6. [Operación y monitoreo](#operación-y-monitoreo)
7. [Índices únicos recomendados (DBA)](#índices-únicos-recomendados-dba)
8. [Troubleshooting](#troubleshooting)

---

## Arquitectura y estructura del proyecto

El sistema está organizado en cuatro proyectos dentro de la solución:

```
GestionDocumentos.sln
├── GestionDocumentos.Core/      # Infraestructura compartida
│   ├── Services/                #   Motor de cola y monitoreo de carpetas (FolderWatcherEngine)
│   ├── Email/                   #   Infraestructura de alertas por SMTP
│   └── Logging/                 #   Logger de archivo con rotación diaria
│
├── GestionDocumentos.Gre/       # Pipeline GRE (PDF → BD + distribución)
├── GestionDocumentos.Idoc/      # Pipeline IDOC (XML → BD)
│
├── GestionDocumentos.Host/      # Punto de entrada: orquesta todos los servicios
│   ├── Program.cs               #   Bootstrap del host Windows / consola
│   ├── *WatcherHostedService.cs #   Servicios de monitoreo GRE e IDOC
│   └── DailyReconciliation*     #   Reconciliación diaria programada
│
└── GestionDocumentos.Tests/     # Suite xUnit (65 tests)
    └── examples/                #   Archivos de ejemplo reales para tests de integración
```

### Flujo general

```
Carpeta de red (PDF / XML)
        │
        ▼
FolderWatcherEngine          ← FileSystemWatcher + cola interna (Channel<T>)
        │
        ▼
N workers concurrentes       ← processingConcurrency (default: 6)
        │
        ├─► GreFileProcessor     → extrae texto del PDF → parsea campos → inserta en BD → copia PDF
        └─► IdocFileProcessor    → parsea XML → normaliza campos → inserta en BD
                │
                ▼
        SQL Server (GreInfos / Documentos)

Diariamente a las 20:00 (configurable):
        DailyReconciliationHostedService  → reescanea carpetas → reprocesa archivos faltantes
```

### Modos de ejecución

| Modo | Cómo activarlo | Uso |
|------|----------------|-----|
| Servicio Windows | Sin argumentos (instalado con `sc`) | Producción |
| Consola | Pasar `--console` al ejecutable | Desarrollo y testing local |

---

## Inicio rápido para desarrolladores

### Requisitos previos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- Windows (el servicio usa APIs nativas de Windows; el modo consola también corre en Windows)
- Acceso a SQL Server para pruebas de integración (opcional; la mayoría de los tests no lo requieren)

### 1. Clonar y restaurar

```powershell
git clone <url-del-repo>
cd gestionDocumentos
dotnet restore
```

### 2. Configurar el ambiente local

Copiar el archivo de ejemplo y ajustarlo con rutas y conexiones locales:

```powershell
copy Parametros.example.json Parametros.json
```

Editar `Parametros.json` y configurar al menos:

- Rutas `*Test` (las rutas sin sufijo `Test` son para producción)
- `"environmentTest": "True"` para que el servicio use las rutas y conexiones de test
- Cadenas de conexión `greContext`, `idocContext` y `backOfficeContext` apuntando a instancias locales o de desarrollo

> **Nota:** `Parametros.json` está en `.gitignore` porque contiene secretos. Nunca commitear este archivo.

### 3. Correr los tests

```powershell
dotnet test
```

Para ver los tests con detalle:

```powershell
dotnet test --logger "console;verbosity=detailed"
```

Para generar reporte de cobertura con Coverlet:

```powershell
dotnet test --collect:"XPlat Code Coverage"
```

### 4. Ejecutar en modo consola

```powershell
dotnet run --project GestionDocumentos.Host -- --console
```

El servicio arranca, muestra logs en consola y monitorea las carpetas configuradas en `Parametros.json`. Presionar `Ctrl+C` para detenerlo.

### 5. Publicar para despliegue

```powershell
dotnet publish "GestionDocumentos.Host/GestionDocumentos.Host.csproj" -c Release -r win-x64 --self-contained false
```

Los archivos publicados quedan en `GestionDocumentos.Host/bin/Release/net8.0/win-x64/publish/`.

---

## Referencia completa de Parametros.json

Todas las claves viven en `Parametros.json`, en la raíz del directorio publicado. Copiar `Parametros.example.json` como punto de partida.

El servicio elige entre rutas de producción y rutas de test según el valor de `environmentTest`:

- `"environmentTest": "False"` → usa claves sin sufijo (`grePDF`, `greFailed`, etc.) — **producción**
- `"environmentTest": "True"` → usa claves con sufijo `Test` (`grePDFTest`, `greFailedTest`, etc.) — **desarrollo**

### Rutas GRE

| Clave | Descripción |
|-------|-------------|
| `grePDF` / `grePDFTest` | Carpeta de entrada donde llegan los PDF de GRE. El watcher la monitorea en tiempo real. |
| `greTXT` / `greTXTTest` | Carpeta de triggers `.txt` asociados a las guías GRE. |
| `dirPDFs` / `dirPDFsTest` | Carpeta de destino principal para los PDF procesados. |
| `dirEcommerce` / `dirEcommerceTest` | Carpeta de destino para clientes de e-commerce. |
| `dirHpmps` / `dirHpmpsTest` | Carpeta de destino para clientes HPMPS. |
| `greFailed` / `greFailedTest` | Carpeta de cuarentena para PDF que fallaron todos los reintentos. Dejar vacío para desactivar. |

### Rutas IDOC

| Clave | Descripción |
|-------|-------------|
| `idocFolder` / `idocFolderTest` | Carpeta de entrada de archivos XML. Si está vacía, el sistema intenta resolverla consultando la base de datos back office. |
| `idocFailed` / `idocFailedTest` | Carpeta de cuarentena para XML que fallaron todos los reintentos. Dejar vacío para desactivar. |

### Conexiones a bases de datos

| Clave | Descripción |
|-------|-------------|
| `greContext` | Connection string para la base de datos GRE (tabla `GreInfos`). Soporta Integrated Security o SQL Auth. |
| `idocContext` | Connection string para la base de datos IDOC (tabla `Documentos`). |
| `backOfficeContext` / `backOfficeContextTest` | Connection string de la base de datos back office, usada para resolver la carpeta IDOC y leer parámetros. |

### Parámetros de concurrencia y resiliencia

| Clave | Default | Descripción |
|-------|---------|-------------|
| `processingConcurrency` | `6` | Cantidad de workers que procesan archivos en paralelo. |
| `queueCapacity` | `2000` | Capacidad máxima de la cola interna. Si se llena, los nuevos archivos esperan (no se descartan). |
| `fileReadyRetries` | `30` | Intentos de verificar que un archivo esté listo para leer antes de procesarlo. |
| `fileReadyDelayMs` | `1000` | Milisegundos entre cada intento de verificación de archivo listo. |
| `watcherInternalBufferSize` | `65536` | Tamaño del buffer interno del FileSystemWatcher (bytes). Aumentar si hay picos de muchos archivos simultáneos. |
| `maxProcessAttempts` | `3` | Intentos máximos antes de enviar un archivo a cuarentena (DLQ). Mínimo: `1`. |
| `environmentTest` | `"False"` | `"True"` para usar rutas y conexiones con sufijo `Test`. Siempre `"False"` en producción. |

### Cuarentena (DLQ)

Cuando un archivo falla `maxProcessAttempts` veces, se mueve a la carpeta de cuarentena bajo una subcarpeta `yyyy-MM-dd/`. Junto al archivo se crea un `.log` con el stacktrace completo.

Si la clave de carpeta está vacía (`"greFailed": ""`), la cuarentena queda desactivada y el archivo permanece en la carpeta de origen.

### Heartbeat

```json
"Heartbeat": {
  "Enabled": true,
  "IntervalMinutes": 15
}
```

| Campo | Descripción |
|-------|-------------|
| `Enabled` | Activa o desactiva la señal de vida periódica. |
| `IntervalMinutes` | Cada cuántos minutos el servicio emite una entrada de log confirmando que está operativo. |

### Reconciliación diaria

```json
"Reconciliation": {
  "Enabled": true,
  "DailyTimeLocal": "20:00",
  "OnlyTodaysFiles": false,
  "GreEnabled": true,
  "IdocEnabled": true,
  "MaxConcurrent": 2,
  "MaxFilesPerSource": 10000,
  "SkipAlreadyInDatabase": true
}
```

| Campo | Descripción |
|-------|-------------|
| `Enabled` | Activa o desactiva la reconciliación diaria. |
| `DailyTimeLocal` | Hora local de ejecución en formato `HH:mm` (ej: `"20:00"`). |
| `OnlyTodaysFiles` | `true`: solo reprocesa archivos del día. `false`: reprocesa todos los archivos de la carpeta. |
| `GreEnabled` | Incluye el pipeline GRE en la reconciliación. |
| `IdocEnabled` | Incluye el pipeline IDOC en la reconciliación. |
| `MaxConcurrent` | Archivos que se reenvían al procesador en simultáneo durante la reconciliación. |
| `MaxFilesPerSource` | Límite de archivos a considerar por pipeline. Protege contra carpetas con miles de archivos históricos. |
| `SkipAlreadyInDatabase` | `true`: omite archivos cuyo nombre ya existe en BD (consulta batch antes de reenviar). `false`: reenvía todos y deja que la idempotencia SQL lo resuelva. |

### Notificaciones SMTP

```json
"Smtp": {
  "Enabled": false,
  "Host": "mail.empresa.com",
  "Port": 587,
  "UserName": "usuario",
  "Password": "contraseña",
  "From": "GestionDocumentos <gestiondocumentos@empresa.com>",
  "To": "Soporte TI <soporte@empresa.com>; Operaciones <operaciones@empresa.com>",
  "UseTls": true,
  "SubjectPrefix": "[GestionDocumentos] Error",
  "ThrottleSeconds": 120,
  "AggregationWindowSeconds": 60,
  "MaxBatchSize": 50
}
```

| Campo | Descripción |
|-------|-------------|
| `Enabled` | `false` por defecto. El canal principal de producción debe ser el Event Log, no el email. |
| `Host` / `Port` | Servidor SMTP y puerto. |
| `UserName` / `Password` | Credenciales de autenticación. |
| `From` / `To` | Remitente y destinatario(s). Múltiples destinatarios separados por `;`. |
| `UseTls` | Activa STARTTLS en la conexión SMTP. |
| `SubjectPrefix` | Prefijo del asunto de cada email. |
| `ThrottleSeconds` | Intervalo mínimo entre emails (segundos). Evita spam ante errores en cascada. |
| `AggregationWindowSeconds` | Ventana de tiempo para agrupar errores en un único email. |
| `MaxBatchSize` | Máximo de errores incluidos en un email agrupado. |

> La cola interna de email es acotada con política `DropOldest`, por lo que un servidor SMTP caído no genera fuga de memoria.

### Log de archivo

```json
"ErrorFileLog": {
  "Enabled": true,
  "FolderPath": "C:\\Servicios\\GestionDocumentos\\logs",
  "FileNamePrefix": "errors",
  "RetentionDays": 30
}
```

| Campo | Descripción |
|-------|-------------|
| `Enabled` | Activa o desactiva el log en archivo. |
| `FolderPath` | Carpeta donde se escriben los archivos de log. Debe existir y ser escribible por la cuenta del servicio. |
| `FileNamePrefix` | Prefijo del nombre de archivo. El archivo diario queda como `{FileNamePrefix}-yyyy-MM-dd.log`. |
| `RetentionDays` | Días de retención. Archivos más antiguos se eliminan automáticamente al iniciar el servicio. |

---

## Instalación del servicio en Windows

### 1. Publicar la aplicación

```powershell
dotnet publish "GestionDocumentos.Host/GestionDocumentos.Host.csproj" -c Release -r win-x64 --self-contained false
```

### 2. Copiar al servidor

Copiar el contenido publicado a una carpeta fija en el servidor:

```
C:\Servicios\GestionDocumentos\
```

En esa carpeta debe quedar el ejecutable junto con `Parametros.json` (configurado para producción, con `"environmentTest": "False"`).

### 3. Ajustar Parametros.json

Antes de instalar, revisar y confirmar:

- Rutas de entrada y salida (sin sufijo `Test`)
- Cadenas de conexión a las tres bases de datos
- Hora de reconciliación y parámetros de concurrencia
- Rutas de cuarentena (o vacías para desactivar)
- Permisos de la cuenta de servicio sobre carpetas de red y locales

### 4. Crear el servicio

Abrir una consola como administrador:

```powershell
sc create GestionDocumentos binPath= "C:\Servicios\GestionDocumentos\GestionDocumentos.Host.exe" start= auto
```

### 5. Iniciar y verificar

```powershell
sc start GestionDocumentos
sc query GestionDocumentos
```

También se puede verificar en `services.msc`.

### Permisos recomendados

La cuenta que ejecute el servicio necesita:

- **Lectura** en carpetas de entrada GRE e IDOC
- **Lectura** en la carpeta de triggers `.txt` de GRE
- **Escritura** en carpetas de destino de GRE, cuarentena y logs
- **Conexión** a los servidores SQL Server configurados
- **Escritura** en Windows Event Log (fuente `GestionDocumentos`)

### Actualización del servicio

```powershell
sc stop GestionDocumentos
# Reemplazar archivos en C:\Servicios\GestionDocumentos\
sc start GestionDocumentos
```

### Reinstalación desde cero

```powershell
sc stop GestionDocumentos
sc delete GestionDocumentos
# Volver a crear con el comando de instalación
```

---

## Validación post despliegue

Checklist después de instalar o actualizar:

1. El servicio aparece como `Running` en `services.msc`.
2. `Event Viewer → Windows Logs → Application` muestra eventos con origen `GestionDocumentos` sin errores de arranque.
3. No hay errores de conexión SQL, rutas inexistentes o parámetros faltantes en el log.
4. Las carpetas configuradas existen y son accesibles para la cuenta del servicio.
5. Dejar un archivo GRE de prueba en la carpeta de entrada y confirmar que:
   - es detectado por el watcher,
   - se procesa sin errores,
   - aparece insertado en la base de datos,
   - el PDF se copia a las carpetas de destino esperadas.
6. Repetir el punto anterior con un IDOC de prueba.
7. Confirmar que la reconciliación está habilitada y con la hora esperada.
8. Verificar que el log de archivo (si está habilitado) se está escribiendo en la ruta configurada.

---

## Operación y monitoreo

### Windows Event Log

Canal principal de observabilidad en producción:

```
Event Viewer → Windows Logs → Application
```

Filtrar por origen: `GestionDocumentos`

El servicio registra `Warning`, `Error` y `Critical`. El ruido del framework (.NET / Microsoft) está filtrado a `Warning` o superior. Los logs propios del sistema salen desde `Information` en modo consola.

### Log de archivo

Si `ErrorFileLog.Enabled` es `true`, se escriben archivos diarios en la ruta configurada:

```
{FolderPath}\{FileNamePrefix}-yyyy-MM-dd.log
```

Persiste eventos `Warning`, `Error` y `Critical`. Los archivos más antiguos que `RetentionDays` se eliminan automáticamente.

### Cuarentena (DLQ)

Los archivos que superan `maxProcessAttempts` reintentos se mueven a:

```
{greFailed}\yyyy-MM-dd\{archivo-original}
{greFailed}\yyyy-MM-dd\{archivo-original}.log   ← stacktrace completo
```

Revisar periódicamente esta carpeta para detectar documentos con problemas recurrentes.

### Heartbeat

Cada `IntervalMinutes` minutos el servicio emite una entrada de log a nivel `Information` confirmando que está operativo. Si el heartbeat deja de aparecer en Event Viewer, el servicio puede haberse detenido.

---

## Índices únicos recomendados (DBA)

La aplicación no ejecuta DDL. Los siguientes índices son necesarios para que el procesamiento sea idempotente (el servicio detecta los errores SQL `2601`/`2627` y los trata como "ya procesado", no como fallo):

```sql
-- IDOC: previene doble inserción del mismo archivo Tibco.
CREATE UNIQUE INDEX UX_Documentos_NameFile ON Documentos(NameFile);

-- GRE: previene doble inserción de la misma guía activa.
-- El filtro WHERE permite anular una guía y volver a insertarla.
CREATE UNIQUE INDEX UX_GreInfos_greName ON GreInfos(greName)
    WHERE Auditoria_Deleted = 0;
```

Crear estos índices **antes del primer despliegue productivo**.

---

## Troubleshooting

### El servicio no arranca

**Síntoma:** `sc start GestionDocumentos` falla o el servicio queda en estado `Stopped` inmediatamente.

**Verificar:**
1. Abrir Event Viewer y buscar el error específico en `Application` con origen `GestionDocumentos`.
2. Correr el ejecutable en modo consola para ver el error directamente:
   ```powershell
   cd C:\Servicios\GestionDocumentos
   .\GestionDocumentos.Host.exe --console
   ```
3. Causas frecuentes:
   - `Parametros.json` no existe o tiene JSON inválido.
   - Una cadena de conexión SQL es incorrecta o el servidor no es accesible.
   - Una ruta de carpeta configurada no existe.
   - La cuenta del servicio no tiene permisos para escribir en el Event Log.

### Los archivos no se detectan al llegar a la carpeta

**Síntoma:** Se copian archivos a la carpeta de entrada pero el servicio no los procesa.

**Verificar:**
1. Confirmar que la carpeta configurada en `grePDF` / `idocFolder` es exactamente la misma donde se están copiando los archivos (mayúsculas, barras, UNC path).
2. Si hay muchos archivos llegando en ráfaga simultánea, el buffer interno del FileSystemWatcher puede saturarse. Aumentar `watcherInternalBufferSize` (máximo recomendado: `131072`).
3. La reconciliación nocturna reprocesará cualquier archivo que el watcher haya perdido.

### Archivos que aparecen en la cuarentena

**Síntoma:** Hay archivos en la carpeta `greFailed` o `idocFailed`.

**Diagnóstico:**
1. Abrir el archivo `.log` junto al documento fallido — contiene el stacktrace completo.
2. Causas frecuentes:
   - El PDF de GRE tiene un formato distinto al esperado (campos faltantes o con nombres distintos).
   - El XML del IDOC tiene estructura diferente a la esperada.
   - Error de conexión a la base de datos durante el procesamiento.
   - La carpeta de destino de GRE no existe o no tiene permisos de escritura.
3. Una vez corregido el problema, copiar el archivo de vuelta a la carpeta de entrada para que sea reprocesado.

### Errores de conexión SQL transitorios

**Síntoma:** Errores esporádicos de timeout o conexión en el Event Log, pero el servicio continúa funcionando.

**Comportamiento esperado:** El pipeline IDOC tiene reintentos automáticos con backoff exponencial (hasta 5 intentos). Errores transitorios aislados son normales y el sistema se recupera solo.

**Si los errores son persistentes:** verificar la disponibilidad del servidor SQL, el pool de conexiones y que la cadena de conexión incluya `TrustServerCertificate=true` si el certificado del servidor no es de confianza.

### Los emails de alerta no llegan

**Síntoma:** `Smtp.Enabled` es `true` pero no se reciben emails.

**Verificar:**
1. En el Event Log, buscar errores con categoría `SmtpErrorEmailSender` que indiquen fallos de conexión SMTP.
2. Confirmar que `Host`, `Port`, `UserName` y `Password` son correctos.
3. El throttle de `ThrottleSeconds` (default: 120s) puede retrasar el primer email si se dispararon varios en poco tiempo.
4. Verificar que la dirección en `To` no tiene espacios adicionales ni caracteres inválidos.
5. Correr en modo consola para ver los errores SMTP en tiempo real.

### La reconciliación no se ejecuta a la hora esperada

**Verificar:**
1. `Reconciliation.Enabled` es `true`.
2. `Reconciliation.DailyTimeLocal` tiene el formato `HH:mm` (ej: `"20:00"`, no `"8:00 PM"`).
3. La hora es la **hora local del servidor**, no UTC.
4. Buscar en el Event Log entradas de `DailyReconciliationHostedService` que confirmen la ejecución o indiquen el motivo de omisión.
