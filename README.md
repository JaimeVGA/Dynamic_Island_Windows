# Dynamic Island para Windows 11

Una "Dynamic Island" ligera para el escritorio de Windows 11. Es una píldora flotante en la parte superior de la pantalla que muestra la hora y lo que estás escuchando en Spotify. Se expande al pasar el mouse con una animación de resorte.

> Lee los datos de reproducción con la API nativa de Windows (GSMTC). No usa la Web API de Spotify, así que no hay OAuth ni tokens.

## Características

- **Tres modos de isla**
  - **Clásico:** píldora compacta con la hora y un indicador de reproducción.
  - **Barra delgada:** una línea fina arriba que crece al pasar el mouse.
  - **Barra ancha:** carátula, título y hora siempre visibles.
- **Vista expandida (hover):** carátula, título, artista, barra de progreso, controles (anterior, play/pausa, siguiente) y un botón de ajustes.
- **Modo automático:** cambia solo de modo según la situación:
  - cuando hay una **app maximizada** o a pantalla completa en el monitor principal,
  - cuando **suena música**,
  - cuando estás en el **escritorio**.

  Prioridad: app maximizada, luego música, luego escritorio.
- **15 temas de color**, con cambio animado.
- **Reloj** en formato de 12 h o 24 h.
- **Iniciar con Windows** (opcional).
- **Transiciones animadas** al expandir, al cambiar de modo, de tema y de canción.
- **Comportamiento nativo de Windows:**
  - ventana sin bordes, transparente y siempre visible,
  - no aparece en Alt+Tab ni en la barra de tareas,
  - no roba el foco,
  - los clics fuera de la isla pasan a las ventanas de debajo.
- **Ligera en reposo:** la ventana no cambia de tamaño y los temporizadores son mínimos.

## Requisitos

- Windows 10 (1903) o superior. Pensada para Windows 11.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) para compilar, o el .NET 8 Desktop Runtime para ejecutar.
- Spotify (u otro reproductor compatible con los controles multimedia de Windows).

## Compilar y ejecutar

```powershell
git clone https://github.com/JaimeVGA/Dynamic_Island_Windows.git
cd "Dynamic Island"
dotnet build -c Release
```

El ejecutable queda en:

```
bin\Release\net8.0-windows10.0.19041.0\DynamicIsland.exe
```

También puedes ejecutarla directamente con `dotnet run -c Release`.

### Acceso directo

Para abrirla rápido, crea un acceso directo en el Escritorio (y en el Menú Inicio, si quieres):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Crear-AccesoDirecto.ps1 -StartMenu
```

Quita `-StartMenu` si solo quieres el del Escritorio. Después de crearlo puedes anclarlo a la barra de tareas con clic derecho → *Anclar a la barra de tareas*.

### Versión portátil (opcional)

Para generar un ejecutable que no necesite tener .NET instalado:

```powershell
dotnet publish -c Release -r win-x64 --self-contained -o publish
```

El resultado queda en la carpeta `publish\`.

## Uso

- Pasa el mouse sobre la isla para expandirla; al salir se contrae.
- Abre la **configuración** con el botón de engranaje de la isla expandida, con clic derecho en el icono de la bandeja → *Configuración…*, o con doble clic en ese icono.
- Para cerrar la app: clic derecho en el icono de la bandeja → *Salir*.

### Configuración

| Opción | Descripción |
|---|---|
| Modo automático | Activa el cambio de modo según la situación y elige un modo para cada una. |
| Modo | Modo fijo, cuando el automático está desactivado. |
| Tema | Elige entre 15 temas de color. |
| Reloj | Formato de 24 horas. |
| Sistema | Iniciar con Windows. |

Los ajustes se guardan en `%APPDATA%\DynamicIsland\settings.json`.

## Estructura del proyecto

```
Animation/    Animador de resorte (SpringAnimator)
Assets/       Icono de la app
Interop/      P/Invoke de Win32 (estilos de ventana, detección de ventana maximizada)
Services/     MediaService (GSMTC), Settings, Themes, TrayService
scripts/      Utilidades (acceso directo)
MainWindow    Isla: animación, hover, click-through
SettingsWindow  Ventana de configuración
```

## Detalles técnicos

- **WPF sobre .NET 8**, con el TFM `net8.0-windows10.0.19041.0` para usar `Windows.Media.Control` sin paquetes adicionales.
- **Sin parpadeo al redimensionar:** la ventana tiene tamaño fijo y solo se anima la píldora interna.
- **Click-through:** `WM_NCHITTEST` devuelve `HTTRANSPARENT` fuera de la isla.
- **Sin robar foco:** `WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW` y `WM_MOUSEACTIVATE` → `MA_NOACTIVATE`.
- **Eventos en lugar de sondeo:** los datos de Spotify llegan por eventos de GSMTC y el progreso se interpola localmente.

## Limitaciones conocidas

- No se muestra sobre juegos en pantalla completa exclusiva.
- Solo se coloca en el monitor principal.
- La detección de app maximizada considera únicamente la ventana en primer plano.

## Licencia

Distribuido bajo la licencia [MIT](LICENSE).
