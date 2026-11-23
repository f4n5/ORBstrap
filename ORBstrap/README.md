# ORBstrap

Roblox PC bootstrapper inspired by **Fishstrap / Bloxstrap**, connected to the **ORB official bot**
(`https://orb-official-bot.onrender.com`).

**Powered by RoValra** → https://www.rovalra.com
(Please keep this attribution — required by RoValra API Terms of Use.)

---

## Cómo funciona

1. **Instalación** – al abrir `ORBstrap.exe` por primera vez aparece un panel para configurar e instalar
   (carpeta, accesos directos, ajustes iniciales). También hay "Run without installing". Se instala por usuario
   (sin administrador) y aparece en *Configuración → Aplicaciones*. **Se desinstala desde la propia app**
   (About → *Uninstall ORBstrap…*).
2. **Acceso** – para usar la app hay que estar en el servidor de Discord, verificarse con el bot e iniciar sesión.
   Cuando el bot verifica a la persona, la app dice **«¡Bienvenido, usuario!»** y aparece todo.
3. **Funciones** – todas las de antes + **Show in Discord** (con el bot) + **Custom textures**.

## Funciones

| Función | Estado |
|---------|--------|
| Login con Discord + verificación por el bot ORB | Implementado |
| Instalador / desinstalador integrado | Implementado |
| Show in Discord (el bot publica «X está jugando Roblox») | Implementado; con «Show the game I'm in» también envía el juego (el bot debe aceptar `action: "update"`) |
| **Discord profile show** (tarjeta con avatar, nombre e ID; vista previa de cómo apareces) | Implementado |
| **Show games** (juego actual detectado por el log de Roblox + últimos 10 juegos) | Implementado |
| **Join best ping / player server** (mejor ping, más jugadores o equilibrado) | Implementado |
| **Custom textures** (packs .zip → `content\...`, con restauración de originales) | Implementado |
| Multi-instance | Implementado |
| FastFlags (guardado y escritura en `ClientAppSettings.json`) | Implementado |
| Server region & uptime vía RoValra | Servicio listo (falta el panel de actividad) |
| Cleaner, Activity tracking, temas | Solo interfaz (pendiente) |

## Custom textures

Importa un `.zip` (o copia archivos a mano en `%LocalAppData%\ORBstrap\Mods`) con la misma estructura que Roblox:

```
content\textures\...
content\sounds\ouch.ogg
content\fonts\...
```

Se aplican al lanzar Roblox desde ORBstrap. Solo se tocan archivos dentro de `content` y se ignoran los ejecutables.
*Restore originals* devuelve los archivos originales de Roblox. Úsalo bajo tu responsabilidad: Roblox puede cambiar
sus reglas de integridad.

## Backend (el bot)

La app habla con la API del bot (`/api/app/*`). Ver el README del bot para variables y endpoints.

## Build (Windows, .NET 8 SDK)

```
dotnet publish ORBstrap.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

(el ejecutable resultante es `publish\ORBstrap.exe`)

Argumentos: `--portable` (omite el instalador), `--uninstall` (modo desinstalación).
