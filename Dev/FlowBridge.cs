using System;
using System.Threading;
using System.Threading.Tasks;
using TermFlow.Components.FullScreen;
using TermFlow.Core;

namespace TermFlow.Dev;

/// <summary>
/// Puente de salida unificado que decide automáticamente entre <see cref="LivePanel"/> y la consola común,
/// manteniendo el estado de la línea dinámica asociada si la hubiera.
/// </summary>
public class FlowBridge
{
    /// <summary>
    /// Evento estático opcional. Si está suscripto, toda la salida de <see cref="Write"/> y
    /// <see cref="WriteIndependient"/> se redirige a los handlers en vez de ir a la consola o al panel.
    /// Útil para testing, redirección a logs externos, componente custom, etc.
    /// </summary>
    public static event Action<long, string> onContentReceived = null;

    /// <summary>
    /// Evento estático opcional. Si está suscripto, toda la entrada solucitada de <see cref="Read"/>
    /// y <see cref="ReadAsync"/> se redirige a los handlers en vez de ir a la consola o al panel.
    /// Útil para testing, redirección a logs externos, componente custom, etc.
    /// </summary>
    public static event Func<long, ConsoleKeyInfo> onKeySolicited = null;

    /// <summary>
    /// ID de la línea dinámica en <see cref="LivePanel"/> si la instancia fue creada con el panel activo.
    /// Es <c>null</c> si la salida va a la consola común.
    /// </summary>
    internal long? PanelId { get; } = null;


    private static long _nextId = 0;
    public static long NextId { get { return Interlocked.Increment(ref _nextId); } set { Interlocked.Exchange(ref _nextId, value); } }

    internal long? LogId { get; } = null;

    /// <summary>
    /// Crea una nueva instancia. Si <see cref="LivePanel"/> está activo, asocia o crea una línea dinámica.
    /// </summary>
    /// <param name="panelId">ID opcional de una línea dinámica existente a reutilizar. Si es <c>null</c>, se crea una nueva.</param>
    public FlowBridge(string initialContent = "", long? panelId = null)
    {
        if (onContentReceived is not null)
        {
            LogId = NextId;
            return;
        }

        if (LivePanel.IsActive)
        {
            if (panelId is not null)
                PanelId = panelId.Value;
            else
                PanelId = LivePanel.AddDynamic(initialContent);
        }
    }

    /// <summary>
    /// Escribe contenido usando el destino actual (LivePanel o consola común).
    /// </summary>
    /// <param name="content">Texto a escribir (puede contener ANSI).</param>
    /// <param name="newLine">Si es <c>true</c>, agrega un salto de línea después del contenido.</param>
    /// <param name="formatConsole">Si es <c>true</c>, agrega \r al incio y \x1b[K al final del contenido en la salida de la consola.</param>
    /// <exception cref="InvalidOperationException">Si el estado de <see cref="LivePanel"/> cambió desde la creación de la instancia.</exception>
    internal void Write(string content, bool newLine = false, bool formatConsole = true)
    {
        if (onContentReceived is not null)
        {
            onContentReceived.Invoke(LogId.Value, content + (newLine ? Environment.NewLine : ""));
            return;
        }

        if (PanelId != null && !LivePanel.IsActive)
            throw new InvalidOperationException("No se puede actualizar la línea, el LivePanel se desactivó mientras la instancia estaba activa.");
        if (PanelId == null && LivePanel.IsActive)
            throw new InvalidOperationException("No se puede escribir, el LivePanel se activó después de crear la instancia.");

        if (LivePanel.IsActive)
            LivePanel.UpdateLine(PanelId.Value, content);
        else
            Console.Write($"{(formatConsole ? "\r" : "")}{content}{(formatConsole ? "\x1b[K" : "")}{(newLine ? Environment.NewLine : "")}");
    }

    /// <summary>
    /// Escribe contenido sin necesidad de mantener una instancia de <see cref="FlowBridge"/>.
    /// Decide el destino en el momento según si <see cref="LivePanel"/> está activo o no.
    /// </summary>
    /// <param name="content">Texto a escribir.</param>
    /// <param name="newLine">Si es <c>true</c>, agrega un salto de línea.</param>
    /// <param name="panelId">ID opcional de línea dinámica a reutilizar. Si es <c>null</c>, se crea un nuevo log.</param>
    internal static void WriteIndependient(string content, bool newLine = true, long? panelId = null)
    {
        if (onContentReceived is not null)
        {
            onContentReceived.Invoke(panelId ?? NextId, content + (newLine ? Environment.NewLine : ""));
            return;
        }

        if (LivePanel.IsActive)
        {
            if (panelId is null)
                LivePanel.AddLog(content);
            else
                LivePanel.UpdateLine(panelId.Value, content);
        }
        else
            Console.Write($"\r{content}\x1b[K{(newLine ? Environment.NewLine : "")}");
    }

    /// <summary>
    /// Lee una tecla del origen actual (LivePanel si está activo, consola común en caso contrario).
    /// Si <see cref="onKeySolicited"/> está suscripto, la tecla la provee el handler.
    /// </summary>
    /// <returns><see cref="ConsoleKeyInfo"/> de la tecla presionada.</returns>
    public ConsoleKeyInfo Read()
    {
        if (onKeySolicited is not null)
            return onKeySolicited.Invoke(LogId.Value);
        return LivePanel.IsActive && !LivePanel.IsKeepingLogs ? LivePanel.WaitForKey() : InputReader.ReadInput().KeyInfo;
    }

    /// <summary>
    /// Lee una tecla de forma asíncrona del origen actual (LivePanel si está activo, consola común en caso contrario).
    /// Si <see cref="onKeySolicited"/> está suscripto, la tecla la provee el handler.
    /// </summary>
    /// <remarks>
    /// Esta operación es verdaderamente asíncrona solo cuando <see cref="LivePanel"/> está activo.
    /// Si el panel no está activo, la lectura es síncrona (se envuelve en <see cref="Task"/> por compatibilidad de firma).
    /// </remarks>
    /// <returns><see cref="ConsoleKeyInfo"/> de la tecla presionada.</returns>
    public async Task<ConsoleKeyInfo> ReadAsync()
    {
        if (onKeySolicited is not null)
            return onKeySolicited.Invoke(LogId.Value);
        return LivePanel.IsActive && !LivePanel.IsKeepingLogs ? await LivePanel.WaitForKeyAsync() : InputReader.ReadInput().KeyInfo;
    }
}
