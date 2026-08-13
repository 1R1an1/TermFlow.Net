using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using TermFlow.Core;

namespace TermFlow.Demo;

public static class TermCanvasDemo
{
    public static async Task Run()
    {
        Engine.AlternateBuffer(true);
        Console.CursorVisible = false;

        // ---- Cambia estos valores para ajustar la demo ---- //
        int maxFrames = 1000;
        int interval = 100;
        bool setCursorPosition = true;
        bool showCursor = true;
        // --------------------------------------------------- //

        using var canvas = new TermCanvas(automaticResize: true, resizeIntervalms: interval);
        Random rnd = new Random();
        var history = new Queue<int>();
        var logs = new Queue<string>();

        string[] fakeLogs = {
            "Renderizado diferencial activo.",
            "Celdas sucias: 0%",
            "Buffer ANSI sincronizado.",
            "Esperando input del usuario...",
            "Optimización de memoria OK.",
            "Thread de resize durmiendo.",
            "ParseAnsi procesando secuencias.",
            "GC en gen 0 limpiado."
        };

        // Variables para medir el CPU real del proceso
        var process = Process.GetCurrentProcess();
        DateTime prevTime = DateTime.UtcNow;
        TimeSpan prevCpuTime = Environment.CpuUsage.TotalTime;

        for (int frame = 0; frame < maxFrames; frame++)
        {
            if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape)
                break;

            int width = canvas.Width;
            int height = canvas.Height;

            // 1. Marco estático 
            DrawBox(canvas, 0, 0, width - 1, height - 1, AnsiColor.Dim);
            canvas.WriteAt(2, 0, " TERM CANVAS DASHBOARD ", AnsiColor.Cyan + AnsiColor.Bold);
            var text = $" Width: {width}, Height: {height} ";
            canvas.WriteAt(width / 2 - text.GetVisualLength() / 2, 0, text, AnsiColor.Red);
            text = $" Frame: {frame,3}/{maxFrames,3} ";
            canvas.WriteAt(width - text.GetVisualLength() - 2, 0, text, AnsiColor.Yellow);

            // 2. Cálculo de CPU REAL DEL PROCESO
            DateTime curTime = DateTime.UtcNow;
            TimeSpan curCpuTime = Environment.CpuUsage.TotalTime;

            double timePassed = (curTime - prevTime).TotalMilliseconds;
            double cpuUsed = (curCpuTime - prevCpuTime).TotalMilliseconds;

            // % CPU = (Tiempo de CPU usado / Tiempo total pasado) * Núcleos lógicos * 100
            double cpuUsage = cpuUsed / timePassed * (Environment.ProcessorCount / 2) * 100.0;
            int cpuPercent = Math.Clamp((int)cpuUsage, 0, 100);

            prevTime = curTime;
            prevCpuTime = curCpuTime;

            // 3. Gráfico de barras con el CPU REAL
            int graphX = 2;
            int graphY = 3;
            int graphW = width / 2 - 4;
            int graphH = height - 8;

            history.Enqueue(cpuPercent);
            while (history.Count > graphW) history.Dequeue();

            canvas.WriteAt(2, 2, " CPU Usage (Real Process) ", AnsiColor.Green);
            canvas.ClearArea(graphX, graphY, graphX + graphW - 1, graphY + graphH - 1);

            int[] histArray = history.ToArray();
            for (int i = 0; i < histArray.Length; i++)
            {
                int val = histArray[i];
                int barH = val * graphH / 100;

                for (int y = 0; y < barH; y++)
                {
                    char c = y == barH - 1 ? '▀' : '█';
                    AnsiColor color = val > 80 ? AnsiColor.Red : (val > 50 ? AnsiColor.Yellow : AnsiColor.Green);
                    canvas.WriteAtAndClear(graphX + i, graphY + graphH - 1 - y, c.ToString(), color, 1);
                }
            }
            canvas.WriteAtAndClear(graphX, graphY + graphH + 1, $"Fake usage: {cpuPercent:D2}%, Real usage: {(cpuUsed / timePassed / Environment.ProcessorCount * 100.0):F2} (Cores: {Environment.ProcessorCount})", AnsiColor.White, -1);

            // 4. Panel de Logs 
            int logX = width / 2 + 2;
            int logW = width - logX - 3;
            canvas.WriteAt(logX, 2, " Event Log (fake logs)", AnsiColor.Magenta);
            DrawBox(canvas, logX - 1, 3, logX + logW, height - 5, AnsiColor.Dim);

            if (frame % 6 == 0)
            {
                string newLog = fakeLogs[rnd.Next(fakeLogs.Length)];
                logs.Enqueue($"[{DateTime.Now:HH:mm:ss}] {newLog}");
            }
            while (logs.Count > height - 9) logs.Dequeue();

            int logY = 4;
            foreach (var log in logs)
            {
                AnsiColor logColor = log.Contains("ERROR") ? AnsiColor.Red :
                                     log.Contains("GC") ? AnsiColor.Yellow : AnsiColor.White;

                canvas.WriteAtAndClear(logX, logY, log, logColor, -3);
                logY++;
            }

            // 5. Barra de input simulada 
            int inputY = height - 2;
            canvas.WriteAt(2, inputY, " > Escribí algo (ESC para salir)...", AnsiColor.Dim);

            canvas.CursorVisible = showCursor;
            if (setCursorPosition)
            {
                canvas.CursorY = inputY;
                canvas.CursorX = 30 + (frame % 10);
            }

            canvas.Flush();
            await Task.Delay(interval);
        }

        // Pantalla final
        canvas.Clear();
        canvas.CursorX = null;
        canvas.CursorY = null;
        var finalText = " DEMO FINALIZADA CORRECTAMENTE ";
        int centerX = canvas.Width / 2 - finalText.Length / 2;
        int centerY = canvas.Height / 2;
        canvas.WriteAt(centerX, centerY, finalText, AnsiColor.Green + AnsiColor.Bold);
        canvas.Flush();
        await Task.Delay(2500);

        Console.CursorVisible = true;
        Engine.AlternateBuffer(false);
    }

    private static void DrawBox(TermCanvas canvas, int x1, int y1, int x2, int y2, AnsiColor color)
    {
        canvas.WriteAt(x1 + 1, y1, new string('─', x2 - x1 - 1), color);
        canvas.WriteAt(x1 + 1, y2, new string('─', x2 - x1 - 1), color);

        for (int y = y1 + 1; y < y2; y++)
        {
            canvas.WriteAt(x1, y, "│", color);
            canvas.WriteAt(x2, y, "│", color);
        }

        canvas.WriteAt(x1, y1, "┌", color);
        canvas.WriteAt(x2, y1, "┐", color);
        canvas.WriteAt(x1, y2, "└", color);
        canvas.WriteAt(x2, y2, "┘", color);
    }
}
