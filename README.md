# TermFlow.Net

TermFlow.Net es una biblioteca de clases para C# diseñada para construir interfaces de usuario de terminal (TUI) interactivas, estéticas y fluidas. Permite crear desde aplicaciones simples hasta aplicaciones complejas a pantalla completa con soporte para teclado, scroll con mouse y redimensionamiento automático.

---

## 🚀 Requisitos

Asegurate de tener instalado en tu sistema:
* **.NET 10 SDK**   
* Sistema operativo compatible con secuencias ANSI (Windows 10/11, Linux)

---

## ✨ Características

La biblioteca se divide en componentes **InLine** (se integran en el flujo normal de la consola) y **FullScreen** (toman el control total de la pantalla):

### 📌 Componentes InLine
* **Spinners y Barras de Progreso:** Animaciones asíncronas (`SpinnerDisplay`, `ProgressBarDisplay`) con cálculo automático de velocidad y tiempo restante (ETA).
* **Inputs Interactivos:** Lectura de texto (`TextInput`) con intercepción de teclado para una experiencia limpia y libre de parpadeos.
* **Text Viewer:** Formateo rápido de mensajes (Info, Success, Warn, Error) y encabezados estéticos.

### 🖥️ Componentes FullScreen
* **Menús y Buscadores (`Menu`, `SearchList`):** Selección única o múltiple con soporte para navegación estilo Vim (`h`, `j`, `k`, `l`), scroll con la rueda del mouse y filtros de búsqueda en tiempo real.
* **Explorador de Árboles (`TreeExplorer`):** Navegación interactiva de directorios físicos o estructuras virtuales.
* **Live Console (`LiveConsole`):** Interfaz inmersiva estilo chat con historial de registros, modo de lectura con scroll y alertas de mensajes nuevos.
* **Tablas (`TableView`):** Renderizado automático de tablas auto-ajustables según el contenido.

### ⚙️ Motor Interno (Core)
* Soporte nativo para secuencias ANSI (SGR).
* Captura avanzada de eventos (ratón, scroll, teclas modificadoras).
* Manejo seguro de redimensionamiento de ventana y cancelación asíncrona (`CancellationToken`).

---

## 📥 Instalación

Actualmente TermFlow.Net se compila desde el código fuente.

1. Cloná el repositorio:

````bash
git clone https://github.com/1R1an1/TermFlow.Net.git
````


2. Compilá la biblioteca en tu proyecto:

````bash
cd TermFlow.Net
dotnet build -c Release
````

3. Agregá la referencia a tu proyecto principal.

---

## 🐛 Reporte de Errores e Issues
⚠️ **Importante**: Al ser una biblioteca enfocada en la manipulación directa del buffer de la consola, el comportamiento puede variar según el emulador de terminal que uses (Windows Terminal, Alacritty, GNOME Terminal, etc.).

Si el error persiste, encontrás un bug o querés proponer una mejora, abrí un [Issue](https://github.com/1R1an1/TermFlow.Net/issues) indicando tu sistema operativo y emulador de terminal.

---

## 📄 Licencia
Este proyecto está bajo la licencia **Mozilla Public License 2.0 (MPL-2.0)**.
