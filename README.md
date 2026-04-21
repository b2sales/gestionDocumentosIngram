# GestionDocumentos

Servicio Windows que unifica el procesamiento de documentos `GRE` e `IDOC`.

## Operación

- Nombre del servicio Windows: `GestionDocumentos`
- Archivo de configuración: `Parametros.json` (no se versiona por contener secretos; copiar `Parametros.example.json` y renombrar o ajustar)
- Modo servicio: se activa cuando corre en Windows sin `--console`
- Modo consola: usar `--console` para pruebas locales

## Logging en producción

En producción, cuando el proceso corre como servicio Windows:

- `Microsoft` / `System` quedan filtrados a `Warning` o superior
- el código propio `GestionDocumentos*` queda visible desde `Information`
- el proveedor de Windows Event Log queda configurado explícitamente
- `SourceName`: `GestionDocumentos`
- `LogName`: `Application`
- opcionalmente se puede registrar además un archivo local de errores mediante `ErrorFileLog`

Esto significa que en `Event Viewer` se registran principalmente:

- `Warning`
- `Error`
- `Critical`

El ruido del framework queda reducido, pero los logs propios pueden seguir saliendo en `Information` a los providers que lo acepten.

## Dónde revisar errores

En Windows, abrir:

1. `Event Viewer`
2. `Windows Logs`
3. `Application`

Buscar eventos con origen `GestionDocumentos`.

## Archivo de errores

Además del `Windows Event Log`, el servicio puede escribir errores en archivos rotados por día.

- Se configura en la sección `ErrorFileLog` de `Parametros.json`
- Solo persiste eventos `Error` y `Critical`
- El archivo diario queda como `{FileNamePrefix}-yyyy-MM-dd.log`
- `RetentionDays` elimina automáticamente logs más antiguos que la retención configurada

Ejemplo:

```json
"ErrorFileLog": {
  "Enabled": true,
  "FolderPath": "C:\\Servicios\\GestionDocumentos\\logs",
  "FileNamePrefix": "errors",
  "RetentionDays": 30
}
```

## Conciliación

La conciliación diaria sirve como red de seguridad para reprocesar archivos no capturados por `FileSystemWatcher`.

Configuración relevante en `Parametros.json`:

- `Reconciliation.Enabled`
- `Reconciliation.DailyTimeLocal`
- `Reconciliation.OnlyTodaysFiles`
- `Reconciliation.MaxFilesPerSource`
- `Reconciliation.SkipAlreadyInDatabase`

## Cuarentena de archivos (DLQ)

Cuando un archivo falla `maxProcessAttempts` veces (default: `3`), se mueve a la carpeta configurada en `greFailed` / `greFailedTest` o `idocFailed` / `idocFailedTest`, bajo una subcarpeta `yyyy-MM-dd/`. Junto al archivo se escribe un `.log` con el stacktrace. Si la clave de carpeta está vacía, la cuarentena queda desactivada y el archivo se deja en origen.

Parámetros relevantes en `Parametros.json`:

- `greFailed`, `greFailedTest`, `idocFailed`, `idocFailedTest`
- `maxProcessAttempts` (global; min `1`)

## Índices únicos recomendados (DBA, fuera de la app)

Por regla de solución la app no ejecuta DDL. Los siguientes índices son necesarios para que los reintentos tras carrera sean idempotentes (el servicio detecta SQL errors `2601`/`2627` y responde con "ya existe"):

```sql
-- IDOC: previene doble inserción del mismo archivo Tibco.
CREATE UNIQUE INDEX UX_Documentos_NameFile ON Documentos(NameFile);

-- GRE: previene doble inserción de la misma guía activa
-- (se filtra por Auditoria_Deleted = 0 para permitir anular + reinsertar).
CREATE UNIQUE INDEX UX_GreInfos_greName ON GreInfos(greName)
    WHERE Auditoria_Deleted = 0;
```

## Notificaciones por correo

El envío por SMTP es opcional y complementario.

- El transporte SMTP usa `MailKit` (reemplaza a `System.Net.Mail.SmtpClient`, en mantenimiento por MS)
- Los correos dependen de la sección `Smtp` en `Parametros.json`
- El canal obligatorio de observabilidad en producción debe ser el `Windows Event Log`
- Los errores se agregan en una ventana (`Smtp.AggregationWindowSeconds`, default 60s) hasta un máximo (`Smtp.MaxBatchSize`, default 50) y se envían como un único correo resumen con conteos por categoría y detalle — además del throttle mínimo entre correos (`Smtp.ThrottleSeconds`)
- La cola interna es acotada y con política `DropOldest`, por lo que un SMTP caído no provoca fuga de memoria

## Instalación del servicio en Windows

### 1. Publicar la aplicación

Ejecutar desde la raíz del proyecto:

```powershell
dotnet publish "GestionDocumentos.Host/GestionDocumentos.Host.csproj" -c Release -r win-x64 --self-contained false
```

### 2. Copiar los archivos al servidor

Copiar el contenido publicado a una carpeta fija, por ejemplo:

```text
C:\Servicios\GestionDocumentos\
```

En esa carpeta debe quedar el ejecutable del host junto con `Parametros.json`.

### 3. Ajustar configuración

Antes de instalar el servicio, revisar `Parametros.json` y confirmar:

- rutas de entrada y salida
- cadenas de conexión
- parámetros de conciliación
- permisos sobre carpetas de red o rutas locales

### 4. Crear el servicio

Abrir una consola de Windows como administrador y ejecutar:

```powershell
sc create GestionDocumentos binPath= "C:\Servicios\GestionDocumentos\GestionDocumentos.Host.exe" start= auto
```

### 5. Iniciar el servicio

```powershell
sc start GestionDocumentos
```

### 6. Verificar que quedó instalado

```powershell
sc query GestionDocumentos
```

También se puede validar en `services.msc`.

### Actualización o reinstalación

Si necesitas actualizar la versión publicada:

1. Detener el servicio:
   ```powershell
   sc stop GestionDocumentos
   ```
2. Reemplazar los archivos de la carpeta publicada.
3. Iniciar nuevamente:
   ```powershell
   sc start GestionDocumentos
   ```

Si necesitas reinstalar desde cero:

1. Detener el servicio:
   ```powershell
   sc stop GestionDocumentos
   ```
2. Eliminarlo:
   ```powershell
   sc delete GestionDocumentos
   ```
3. Volver a crearlo con el comando de instalación.

### Permisos recomendados

La cuenta que ejecute el servicio debe tener permisos para:

- leer carpetas de entrada `GRE` e `IDOC`
- leer el `TXT` asociado de `GRE`
- escribir en las carpetas destino de `GRE`
- conectarse a las bases de datos configuradas
- escribir en `Windows Event Log`

## Validación post despliegue

Checklist sugerida después de instalar o actualizar:

1. Confirmar que el servicio aparece como `Running` en `services.msc`.
2. Revisar `Event Viewer > Windows Logs > Application` y verificar eventos con origen `GestionDocumentos`.
3. Confirmar que no aparecen errores de arranque por rutas inexistentes, conexión SQL o lectura de parámetros.
4. Validar que las carpetas configuradas en `Parametros.json` existen y son accesibles para la cuenta que ejecuta el servicio.
5. Dejar un archivo de prueba `GRE` o `IDOC` en la carpeta de entrada y confirmar que:
   - el archivo es detectado,
   - se procesa,
   - se refleja en la base de datos,
   - y, para `GRE`, el PDF se copia al destino esperado.
6. Confirmar que la conciliación quedó habilitada y con la hora esperada.
7. Forzar una condición de warning o error controlado y verificar que aparece en `Event Viewer`.

## Validación rápida recomendada

Para una prueba mínima en producción o preproducción:

1. Reiniciar el servicio.
2. Revisar el `Event Viewer` durante el arranque.
3. Procesar un `GRE` real.
4. Procesar un `IDOC` real.
5. Confirmar inserciones en BD y ausencia de errores persistentes en el log de Windows.
