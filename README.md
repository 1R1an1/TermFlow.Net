
# TermFlow.Net

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-14-239120?logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux-lightgrey)
[![License](https://img.shields.io/badge/license-MPL--2.0-blue)](https://www.mozilla.org/en-US/MPL/2.0/)
[![Last Commit](https://img.shields.io/github/last-commit/1R1an1/TermFlow.Net)](https://github.com/1R1an1/TermFlow.Net/commits/master/)
[![Repo Stars](https://img.shields.io/github/stars/1R1an1/TermFlow.Net?style=social)](https://github.com/1R1an1/TermFlow.Net)


> Biblioteca de clases para C# / .NET 10 para construir **TUIs** (Terminal User Interfaces) interactivas, estéticas y fluidas.

TermFlow.Net te permite construir desde simples barras de progreso hasta aplicaciones full-screen con menús, exploradores de archivos, paneles de logs en vivo y consolas estilo chat — todo con soporte nativo para secuencias ANSI, teclado, rueda del mouse y redimensionamiento automático.

---

## Tabla de contenidos

1. [Requisitos](#requisitos)
2. [Instalación](#instalación)
3. [Inicio rápido](#inicio-rápido)
4. [Demo](#demo)
5. [Arquitectura](#arquitectura)
6. [Componentes InLine](#componentes-inline)
7. [Componentes FullScreen](#componentes-fullscreen)
8. [Motor Core](#motor-core)
9. [Sistema de temas y glifos](#sistema-de-temas-y-glifos)
10. [Atajos de teclado](#atajos-de-teclado)
11. [Estructura del proyecto](#estructura-del-proyecto)
12. [Compatibilidad](#compatibilidad)
13. [Licencia](#licencia)

---

## Requisitos



| Requisito | Detalle |
|-----------|---------|
| .NET SDK | **10.0+** |
| Sistema operativo | Windows 10/11 o Linux |
| Terminal | Cualquiera con soporte ANSI/VT100 |
| Fuente (recomendada) | Una fuente monoespaciada con buena cobertura Unicode |

> ℹ️ En Windows, `Engine.Setup()` activa automáticamente `ENABLE_VIRTUAL_TERMINAL_PROCESSING` vía P/Invoke para garantizar el render ANSI, incluso en terminales que no lo habilitan por defecto.

> ⚠️ **Fuente**: los glifos usados (`▶ ✔ ✖ ⚠ ● ┌ ┐ └ ┘`) son Unicode estándar, no Nerd Font. `consolas` (por defecto en cmd/PowerShell de Windows 10) no los renderiza. Usá **Cascadia Code**, **JetBrains Mono**, **Fira Code** o cualquier monoespaciada con buena cobertura Unicode. La app funciona igual aunque algunos caracteres se vean mal.
---

## Instalación

Actualmente TermFlow.Net se distribuye como código fuente. Para usarla en tu proyecto:

```bash
# 1. Cloná el repositorio
git clone https://github.com/1R1an1/TermFlow.Net.git

# 2. Compilá en Release
cd TermFlow.Net
dotnet build -c Release

# 3. Referenciala desde tu proyecto
dotnet add reference ../TermFlow.Net/TermFlow.Net.csproj
```

Alternativamente, podés copiar las carpetas `Core/` y `Components/` directamente a tu solución.

---

## Inicio rápido

```csharp
using TermFlow.Components.InLine;
using TermFlow.Components.FullScreen;
using TermFlow.Core;

// 1. Inicializá el motor (UTF-8 + ANSI + hooks de cierre)
Engine.Setup();

// 2. Encabezado y mensaje informativo
TextViewer.WriteHeader("Mi Aplicación");
TextViewer.Info("Iniciando proceso...");

// 3. Barra de progreso con ETA y velocidad automáticos
await ProgressBarDisplay.RunAsync("Descargando", maxValue: 100, async (p) =>
{
    for (int i = 0; i <= 100; i += 5)
    {
        p.Value = i;
        await Task.Delay(50);
    }
});

// 4. Menú de selección única full-screen
string[] items = { "Opción A", "Opción B", "Opción C" };
int elegido = await Menu.SelectOneAsync("Elegí una opción", items);

TextViewer.Success($"Elegiste: {items[elegido]}");
```

---

## Demo

El repositorio incluye un proyecto de demostración interactivo (`TermFlow.Demo/`) con 14 demos que cubren todos los componentes de la biblioteca. Útil para ver TermFlow.Net en acción antes de integrarlo a tu proyecto.

```bash
# Desde la raíz del repo
dotnet run --project TermFlow.Demo/TermFlow.Demo.csproj
```

Aparece un menú full-screen desde donde podés elegir qué componente probar (TextViewer, Menu, SearchList, TreeExplorer, LivePanel, LiveConsole, etc.). Cada demo muestra su resultado inline y vuelve al menú al presionar Enter.

---

## Arquitectura

TermFlow.Net se organiza en tres capas claramente separadas:

```
┌─────────────────────────────────────────────────┐
│              Components (UI)                    │
│  ┌──────────────┐  ┌─────────────────────────┐  │
│  │   InLine     │  │      FullScreen         │  │
│  │  (fluido)    │  │ (alternate screen buf)  │  │
│  └──────────────┘  └─────────────────────────┘  │
├─────────────────────────────────────────────────┤
│                Core (Motor)                     │
│  Engine · InputReader · InputRouter             │
│  ScrollState · AnsiColor · AnsiStringHelper     │
│  ThemeColors · ConsoleGlyphs                    │
└─────────────────────────────────────────────────┘
```

- **Core**: primitivas de bajo nivel (ANSI, lectura de input, matemática de scroll, helpers de strings con ANSI, tema central).
- **Components/InLine**: componentes que se imprimen en el flujo normal de la consola, sin tomar el control total.
- **Components/FullScreen**: componentes que entran al alternate buffer y toman control de toda la pantalla.

> 💡 Todos los componentes InLine detectan automáticamente si `LivePanel` está activo y redirigen su salida al panel en lugar de imprimir directamente.

---

## Componentes InLine

Componentes que se integran al flujo normal de la consola. Todos son compatibles con `LivePanel`: si el panel está activo, redirigen su salida automáticamente.

### `TextViewer`
Mensajes con estilo semántico, encabezados simples y figlet ASCII.

```csharp
TextViewer.Info("Procesando...");            // ● Procesando...
TextViewer.Success("OK");                    // ✔ OK
TextViewer.Warn("Atención");                 // ⚠ Atención
TextViewer.Error("Falló");                   // ✖ Falló
TextViewer.WriteHeader("Sección 1");         // Subrayado con ───
```

### `TextInput`
Entrada de texto, preguntas sí/no y "presionar para continuar".

```csharp
string nombre    = await TextInput.ReadStringAsync("Nombre: ");
bool   confirmar = await TextInput.AskAsync("¿Continuar?");
TextInput.PressToContinue();
```

### `SpinnerDisplay`
Spinner animado Braille mientras corre una tarea de fondo.

```csharp
await SpinnerDisplay.RunAsync("Instalando dependencias...", async (token) =>
{
    await Task.Delay(3000, token);
});
```

### `ProgressBarDisplay`
Barra de progreso con cálculo automático de velocidad (B/s, KB/s, MB/s) y ETA.

```csharp
await ProgressBarDisplay.RunAsync(
    description: "Descargando ISO",
    maxValue: 1024L * 1024 * 500,
    workerTask: async (p) =>
    {
        for (long i = 0; i <= 1024L * 1024 * 500; i += 1024 * 100)
        {
            p.Value = i;
            await Task.Delay(10);
        }
    },
    showSpeed: true
);
```

---

## Componentes FullScreen

Toman el control total de la pantalla usando el alternate buffer (no rompen el historial previo de la consola). Al salir, restauran el estado original.

### `Menu`
Menú de selección única o múltiple con scroll y navegación estilo Vim.

```csharp
string[] items  = ["Ana", "Bruno", "Carla"];

// Selección única
int idx = await Menu.SelectOneAsync("Clientes", items);

// Selección múltiple con pre-selección
int[] elegidos = await Menu.SelectMultiAsync("Permisos", items,
      preselected: [true, false, true]);
```

### `SearchList`
Buscador en vivo con selección única o múltiple.

```csharp
int   idx  = await SearchList.FilterOneAsync("Buscar cliente", clientes);
int[] idxs = await SearchList.FilterMultiAsync("Etiquetas", tags);
```

### `TableView`
Tablas auto-ajustables con bordes Unicode. El ancho de cada columna se calcula a partir del contenido.

```csharp
TableView.Show(
    headers: new[] { "ID", "Servidor", "IP", "Estado" },
    rows: new List<string[]>
    {
        new[] { "001", "Hub",    "127.0.0.1:8080",     "ONLINE"  },
        new[] { "002", "Backup", "192.168.1.50:9000",  "OFFLINE" }
    }
);
```

### `TreeExplorer`
Explorador jerárquico con soporte para directorios físicos o estructuras virtuales. Soporta filtros (todo / solo carpetas / solo archivos) y selección múltiple con herencia de marcas (marcar una carpeta marca todos sus hijos).

```csharp
// Exploración física
string   archivo  = await TreeExplorer.ExploreOneAsync("Abrir archivo", @"/home/user/docs");
string[] archivos = await TreeExplorer.ExploreMultiAsync("Seleccionar logs",
    @"/var/log", ExplorerFilter.OnlyFiles);

// Exploración virtual (rutas en memoria)
string[] virtuales = await TreeExplorer.ExploreMultiAsync(
    "Seleccionar nodos",
    new[] { "s3/bucket1", "s3/bucket2", "ec2/i-123" },
    virtualRoot: "aws"
);

// Origen de datos personalizado (implementando IExplorerDataSource)
string[] custom = await TreeExplorer.ExploreMultiAsync(
    "Mi origen", miDataSource, ExplorerFilter.All);
```

### `LivePanel`
Panel de logs dinámico a pantalla completa. Combina logs estáticos con líneas dinámicas actualizables (spinners, barras de progreso, etc.). Usa caché de wrapping y un loop de render reactivo accionado por semáforo para minimizar CPU.

```csharp
LivePanel.Start(maxLogs: 500);

long id = LivePanel.AddDynamic("Procesando...");
// ... más tarde, actualizar la línea dinámica:
LivePanel.UpdateLine(id, "Procesando... 50%");
LivePanel.UpdateDecorations(id, prefix: AnsiColor.Dim, suffix: ThemeColors.Reset);

TextViewer.Success("Tarea completada");

LivePanel.Stop();
```

### `LiveConsole`
Consola interactiva estilo chat con historial scrollable, barra divisoria inteligente que avisa cuando hay mensajes nuevos abajo, y soporte para input multilínea con `Shift+Enter`.

```csharp
var console = new LiveConsole();
ThemeColors.Primary  =  AnsiColor.Cyan  +  AnsiColor.Bold;
await console.RunAsync(">>> ", async (input) =>
{
    if (input == "/help")
        console.WriteLog($"{ThemeColors.Bright}Comandos{ThemeColors.Reset}: /help, /exit");
    else
        console.WriteLog($"{ThemeColors.Primary}Echo{ThemeColors.Reset}: {input}");
});
// Salir con /exit o Escape
```

---

## Motor Core

| Clase | Responsabilidad |
|-------|-----------------|
| `Engine` | Inicializa UTF-8, ANSI nativo en Windows, y gestiona la entrada/salida del alternate buffer. |
| `InputReader` | Lector de bajo nivel: decodifica teclas comunes y secuencias ANSI SGR del mouse (scroll up/down), filtrando clicks fantasma. |
| `InputRouter` | Enrutador fluent de teclas a acciones, con agrupación automática del footer contextual. |
| `ScrollState` | Matemática de cursor + ventana de scroll, con detección automática de resize. |
| `AnsiColor` | Wrapper tipado para secuencias ANSI. Soporta composición con `+` y conversión implícita a `string`. |
| `AnsiStringHelper` | Extensiones para envolver, truncar y medir strings respetando códigos ANSI. |
| `ThemeColors` | Paleta semántica central (`Success`, `Warning`, `Error`, `Info`, etc.). Modificable en runtime. |
| `ConsoleGlyphs` | Catálogo de glifos Unicode (`┌ ┐ └ ┘ ─ │ ✔ ⚠ ● ▶`). Modificable en runtime. |

---

## Sistema de temas y glifos

Toda la estética se controla desde dos clases estáticas centralizadas, modificables en runtime:

```csharp
// Cambiar la paleta semántica
ThemeColors.Primary  = AnsiColor.BrightCyan + AnsiColor.Bold;
ThemeColors.Success  = AnsiColor.BrightGreen;
ThemeColors.Selector = AnsiColor.BrightYellow;
ThemeColors.Warning  = AnsiColor.BrightRed;

// Cambiar los glifos usados por todos los componentes
ConsoleGlyphs.Indicator  = "→";
ConsoleGlyphs.Checked    = "✓";
ConsoleGlyphs.Unchecked  = "□";
ConsoleGlyphs.Horizontal = '━';
ConsoleGlyphs.TopLeft    = '┏';
```

Componer estilos ANSI es seguro y simple gracias al operador `+`:

```csharp
AnsiColor miEstilo = AnsiColor.BgBlue + AnsiColor.BrightWhite + AnsiColor.Bold;
Console.Write($"{miEstilo}Texto{ThemeColors.Reset}");
```

> 💡 Las extensiones de `AnsiStringHelper` (`GetVisualLength`, `WrapText`, `Truncate`, `StripAnsi`) permiten medir y recortar texto con ANSI sin romper los códigos de color.

---

## Atajos de teclado

Los componentes FullScreen comparten un esquema común de atajos (configurables vía `InputRouter`):

| Tecla | Acción | Componentes |
|-------|--------|-------------|
| `↑` / `↓` | Navegar | Todos |
| `j` / `k` | Navegar (estilo Vim) | Menu, TreeExplorer |
| `g` / `G` | Ir al inicio / final | Menu |
| `Space` | Marcar / desmarcar | Menu (multi), SearchList (multi), TreeExplorer (multi) |
| `Enter` | Confirmar / entrar a carpeta | Todos |
| `Esc` / `q` | Cancelar / salir | Todos |
| `h` / `l` | Volver / entrar (estilo Vim) | TreeExplorer |
| `←` / `→` | Volver / entrar | TreeExplorer |
| `c` | Confirmar selección múltiple | TreeExplorer |
| `Backspace` | Borrar carácter | SearchList |
| `Rueda del mouse` | Scroll vertical | Todos |
| `PageUp` / `PageDown` | Scroll al inicio / final | LivePanel |
| `Shift+Enter` | Salto de línea en el input | LiveConsole |
| `End` | Ir al presente (scroll 0) | LiveConsole |
| `/exit` | Comando para salir | LiveConsole |

---


## Estructura del proyecto

```
TermFlow.Net/
├── TermFlow.Net.csproj
├── Core/
│   ├── Engine.cs                       # Setup ANSI/UTF-8 + alternate buffer
│   ├── InputReader.cs                  # Decoder de teclas + mouse SGR
│   ├── InputRouter.cs                  # Binds fluent + footer contextual
│   ├── ScrollState.cs                  # Matemática de scroll
│   ├── AnsiColor.cs                    # Wrapper de secuencias ANSI
│   ├── AnsiStringHelper.cs             # Wrap/truncate/medida con ANSI
│   ├── ThemeColors.cs                  # Paleta semántica
│   └── ConsoleGlyphs.cs                # Glifos Unicode
├── Components/
│   ├── InLine/
│   │   ├── TextViewer.cs               # Info/Success/Warn/Error/Figlet
│   │   ├── TextInput.cs                # ReadString/Ask/PressToContinue
│   │   ├── SpinnerDisplay.cs           # Spinner Braille async
│   │   └── ProgressBarDisplay.cs       # Barra con ETA + velocidad
│   └── FullScreen/
│       ├── Menu.cs                     # SelectOne / SelectMulti
│       ├── SearchList.cs               # FilterOne / FilterMulti
│       ├── TableView.cs                # Tablas con bordes
│       ├── LivePanel.cs                # Panel de logs dinámico
│       ├── LiveConsole.cs              # Consola estilo chat
│       └── TreeExplorer/
│           ├── TreeExplorer.cs                     # Motor central
│           ├── TreeExplorerModel.cs                # Interfaces + ExplorerEntry
│           ├── TreeExplorer.PhysicalDataSource.cs  # Implementación sobre FS
│           └── TreeExplorer.VirtualDataSource.cs   # Implementación in-memory
└── TermFlow.Demo/                      # Proyecto de demo interactivo (no es parte de la biblioteca)
    ├── TermFlow.Demo.csproj            # Referencia al csproj principal
    └── Program.cs                      # Menú de tests de todos los componentes
```

---



## Compatibilidad

TermFlow.Net usa secuencias ANSI estándar. Probado en:

| Terminal | Estado |
|----------|--------|
| Windows Terminal (Win 10/11) | ✅ Recomendado en Windows |
| cmd.exe y powershell (Windows 10+) | ⚠️ Funciona, pero con la fuente `consolas` por defecto algunos glifos se ven como `?` |
| Kitty | ✅ |
| GNOME Terminal | ✅ |
| Alacritty | ✅ |
| cmd.exe (Windows 7-) | ⚠️ No testeado |

> ⚠️ **Fuente**: los glifos usados (`▶ ✔ ✖ ⚠ ● ┌ ┐ └ ┘ ─ │`) son Unicode estándar, **no** Nerd Font. Cambiá la fuente de la terminal a **Cascadia Code**, **JetBrains Mono**, **Fira Code** o cualquier monoespaciada con buena cobertura Unicode. Ver [Requisitos](#requisitos) para más detalle.

> ⚠️ La captura del mouse usa el modo SGR (1006). Algunos terminales muy antiguos pueden no soportarlo correctamente; en ese caso los clicks podrían no registrarse pero el teclado seguirá funcionando.

---

<!--
## Reporte de errores

Al ser una biblioteca enfocada en la manipulación directa del buffer de la consola, el comportamiento puede variar según el emulador de terminal.

Si encontrás un bug o querés proponer una mejora, abrí un [Issue](https://github.com/1R1an1/TermFlow.Net/issues) incluyendo:

- Sistema operativo y versión
- Emulador de terminal y versión
- Pasos para reproducir
- Comportamiento esperado vs. real
- Captura o GIF si aplica

---
-->

## Licencia

Este proyecto está bajo la licencia **Mozilla Public License 2.0 (MPL-2.0)**.

```
SPDX-License-Identifier: MPL-2.0
Copyright (c) 2026 1R1an1
```

Ver [LICENSE](https://github.com/1R1an1/TermFlow.Net/blob/master/LICENSE) para el texto completo.
