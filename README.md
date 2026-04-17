# GestionDocumentos

Servicio Windows que unifica el procesamiento de documentos `GRE` e `IDOC`.

## Operación

- Nombre del servicio Windows: `GestionDocumentos`
- Archivo de configuración: `Parametros.json` (no se versiona por contener secretos; copiar `Parametros.example.json` y renombrar o ajustar)
- Modo servicio: se activa cuando corre en Windows sin `--console`
- Modo consola: usar `--console` para pruebas locales

## Logging en producción

En producción, cuando el proceso corre como servicio Windows:

- el logging global queda filtrado a `Warning` o superior
- el proveedor de Windows Event Log queda configurado explícitamente
- `SourceName`: `GestionDocumentos`
- `LogName`: `Application`

Esto significa que en `Event Viewer` se registran:

- `Warning`
- `Error`
- `Critical`

No se registran eventos `Information` ni `Debug` en producción.

## Dónde revisar errores

En Windows, abrir:

1. `Event Viewer`
2. `Windows Logs`
3. `Application`

Buscar eventos con origen `GestionDocumentos`.

## Conciliación

La conciliación diaria sirve como red de seguridad para reprocesar archivos no capturados por `FileSystemWatcher`.

Configuración relevante en `Parametros.json`:

- `Reconciliation.Enabled`
- `Reconciliation.DailyTimeLocal`
- `Reconciliation.OnlyTodaysFiles`
- `Reconciliation.MaxFilesPerSource`
- `Reconciliation.SkipAlreadyInDatabase`

## Notificaciones por correo

El envío por SMTP es opcional y complementario.

- Los correos dependen de la sección `Smtp` en `Parametros.json`
- El canal obligatorio de observabilidad en producción debe ser el `Windows Event Log`

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
