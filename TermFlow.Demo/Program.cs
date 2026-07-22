using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TermFlow.Components.FullScreen;
using TermFlow.Components.FullScreen.TreeExplorer;
using TermFlow.Components.InLine;
using TermFlow.Core;

namespace TermFlow.Demo;

internal class Program
{
    private static readonly string[] MainMenuItems =
    {
        "TextViewer — Info / Success / Warn / Error",
        "TextInput — ReadString + Ask (y/n)",
        "SpinnerDisplay — tarea de fondo",
        "ProgressBarDisplay — barra con ETA y velocidad",
        "Menu.SelectOneAsync — selección única",
        "Menu.SelectMultiAsync — selección múltiple",
        "SearchList.FilterOneAsync — buscador único",
        "SearchList.FilterMultiAsync — buscador múltiple",
        "TableView.Show — tabla con bordes",
        "TreeExplorer.ExploreOneAsync — archivos físicos",
        "TreeExplorer.ExploreMultiAsync — multi físicos",
        "TreeExplorer.ExploreMultiAsync — virtual",
        "LivePanel — panel de logs dinámico",
        "LiveConsole — Consola en estilo chat",
        "Salir"
    };

    private static async Task Main(string[] args)
    {
        Engine.Setup();
        ThemeColors.Primary = AnsiColor.Green + AnsiColor.Bold;
        ThemeColors.Selector = ThemeColors.Bright;
        while (true)
        {
            int choice = await Menu.SelectOneAsync($"{ThemeColors.Primary}TermFlow.Net{ThemeColors.Reset} — {AnsiColor.Cyan}{AnsiColor.Bold}Interactive Demo{ThemeColors.Reset} — {AnsiColor.Magenta}{AnsiColor.Bold}¿Qué querés testear?{ThemeColors.Reset}", MainMenuItems);
            if (choice == -1 || choice == MainMenuItems.Length - 1) break;

            try
            {
                TextViewer.WritePlain("");
                await RunTestAsync(choice);
            }
            catch (Exception ex)
            {
                TextViewer.Error($"Excepción durante el test: {ex.Message}");
            }

            TextViewer.Info("Volviendo al menú principal...");
            TextInput.PressToContinue();
        }

        TextViewer.Success("¡Chau!");
    }

    private static async Task RunTestAsync(int choice)
    {
        switch (choice)
        {
            case 0: await TestTextViewer(); break;
            case 1: await TestTextInput(); break;
            case 2: await TestSpinner(); break;
            case 3: await TestProgressBar(); break;
            case 4: await TestMenuOne(); break;
            case 5: await TestMenuMulti(); break;
            case 6: await TestSearchOne(); break;
            case 7: await TestSearchMulti(); break;
            case 8: TestTableView(); break;
            case 9: await TestTreeOnePhysical(); break;
            case 10: await TestTreeMultiPhysical(); break;
            case 11: await TestTreeMultiVirtual(); break;
            case 12: await TestLivePanel(); break;
            case 13: await TestLiveConsole(); break;
        }
    }

    // ==================== Demos InLine ====================

    private static async Task TestTextViewer()
    {
        TextViewer.WriteHeader($"{ThemeColors.Primary}TextViewer{ThemeColors.Reset}");
        TextViewer.Info("Mensaje informativo (InfoBullet)");
        TextViewer.Success("Operación exitosa (Checked)");
        TextViewer.Warn("Advertencia menor (Warning)");
        TextViewer.Error("Algo falló — esto es solo una demo (Error)");
        TextViewer.WritePlain("Mensaje plano sin formato ni ícono.");
        await Task.CompletedTask;
    }

    private static async Task TestTextInput()
    {
        TextViewer.WriteHeader($"{ThemeColors.Primary}TextInput{ThemeColors.Reset}");
        string nombre = await TextInput.ReadStringAsync("¿Cómo te llamás? ");
        TextViewer.Info($"Ingresaste: \"{nombre}\"");
        bool ok = await TextInput.AskAsync("¿Confirmás los datos?");
        TextViewer.Info($"Respondiste: {(ok ? "sí" : "no")}");
    }

    private static async Task TestSpinner()
    {
        TextViewer.WriteHeader($"{ThemeColors.Primary}SpinnerDisplay{ThemeColors.Reset}");
        await SpinnerDisplay.RunAsync("Simulando trabajo...", async (token) =>
        {
            await Task.Delay(3000, token);
        });
        TextViewer.Success("Spinner terminado");
    }

    private static async Task TestProgressBar()
    {
        TextViewer.WriteHeader($"{ThemeColors.Primary}ProgressBarDisplay{ThemeColors.Reset}");
        await ProgressBarDisplay.RunAsync(
            description: "Descargando archivo",
            maxValue: 500L,
            workerTask: async (p) =>
            {
                for (long i = 0; i <= 500; i += 10)
                {
                    p.Value = i;
                    await Task.Delay(40);
                }
            },
            showSpeed: true);
        TextViewer.Success("Barra completada");
    }

    // ==================== Demos FullScreen ====================

    private static async Task TestMenuOne()
    {
        string[] items = ["Opción Alpha", "Opción Beta", "Opción Gamma", "Opción Delta"];
        int idx = await Menu.SelectOneAsync($"{ThemeColors.Primary}Selección única{ThemeColors.Reset}", items);
        if (idx == -1) TextViewer.Warn("Cancelaste la selección");
        else TextViewer.Success($"Elegiste el índice {idx}: {items[idx]}");
    }

    private static async Task TestMenuMulti()
    {
        string[] items = ["Lectura", "Escritura", "Ejecución", "Acceso de red", "Acceso admin"];
        int[] sel = await Menu.SelectMultiAsync($"{ThemeColors.Primary}Selección múltiple (permisos){ThemeColors.Reset}", items);
        if (sel.Length == 0) TextViewer.Warn("No marcaste nada");
        else
        {
            TextViewer.Success($"Marcaste {sel.Length} ítems:");
            foreach (int i in sel)
                TextViewer.Info($"  → [{i}] {items[i]}");
        }
    }

    private static async Task TestSearchOne()
    {
        string[] items = ["Ana Pérez", "Bruno Díaz", "Carla Soto", "Diego Luna", "Eva Marín", "Franco Ríos"];
        int idx = await SearchList.FilterOneAsync($"{ThemeColors.Primary}Buscar cliente{ThemeColors.Reset}", items);
        if (idx == -1) TextViewer.Warn("Búsqueda cancelada");
        else TextViewer.Success($"Elegiste: {items[idx]}");
    }

    private static async Task TestSearchMulti()
    {
        string[] items = ["bug", "documentation", "enhancement", "duplicate", "wontfix", "help wanted", "good first issue"];
        int[] sel = await SearchList.FilterMultiAsync($"{ThemeColors.Primary}Etiquetas (buscá y marcá){ThemeColors.Reset}", items);
        if (sel.Length == 0) TextViewer.Warn("Sin selección");
        else
        {
            TextViewer.Success($"Marcaste {sel.Length}:");
            foreach (int i in sel)
                TextViewer.Info($"  → {items[i]}");
        }
    }

    private static void TestTableView()
    {
        TextViewer.WriteHeader($"{ThemeColors.Primary}TableView{ThemeColors.Reset}");
        string[] headers = ["ID", "Servidor", "IP:Puerto", "Estado"];
        var rows = new List<string[]>
        {
            new[] {$"{AnsiColor.Cyan}001{ThemeColors.Reset}", "ControlHub", "127.0.0.1:8080", $"{ThemeColors.Success}ONLINE{ThemeColors.Reset}"},
            new[] {$"{AnsiColor.Cyan}002{ThemeColors.Reset}", "Backup", "127.0.0.1:8081", $"{ThemeColors.Warning}STANDBY{ThemeColors.Reset}"},
            new[] {$"{AnsiColor.Cyan}003{ThemeColors.Reset}", "Failover", "192.168.1.50:9000", $"{ThemeColors.Error}OFFLINE{ThemeColors.Reset}"}
        };
        TableView.Show(headers, rows, ThemeColors.Primary + AnsiColor.Bold);
    }

    // ==================== Demos TreeExplorer ====================

    private static async Task TestTreeOnePhysical()
    {
        string inicio = Directory.GetCurrentDirectory();
        ThemeColors.Selector = ThemeColors.Primary;
        string elegido = await TreeExplorer.ExploreOneAsync($"{ThemeColors.Primary}Abrir archivo (físico){ThemeColors.Reset}", inicio);
        ThemeColors.Selector = ThemeColors.Bright;
        if (string.IsNullOrEmpty(elegido)) TextViewer.Warn("Cancelaste");
        else TextViewer.Success($"Ruta elegida: {elegido}");
    }

    private static async Task TestTreeMultiPhysical()
    {
        string inicio = Directory.GetCurrentDirectory();
        ThemeColors.Selector = ThemeColors.Primary;
        string[] rutas = await TreeExplorer.ExploreMultiAsync($"{ThemeColors.Primary}Seleccionar múltiples (físico){ThemeColors.Reset}", inicio);
        ThemeColors.Selector = ThemeColors.Bright;
        if (rutas.Length == 0) TextViewer.Warn("Sin selección");
        else
        {
            TextViewer.Success($"Elegiste {rutas.Length} rutas:");
            foreach (string r in rutas)
                TextViewer.Info($"  → {r}");
        }
    }

    private static async Task TestTreeMultiVirtual()
    {
        var rutas = new[]
        {
            "s3/bucket-logs",
            "s3/bucket-backups",
            "ec2/i-abc12345",
            "ec2/i-def67890",
            "rds/prod-db",
            "rds/staging-db"
        };
        ThemeColors.Selector = ThemeColors.Primary;
        string[] sel = await TreeExplorer.ExploreMultiAsync($"{ThemeColors.Primary}Recursos virtuales{ThemeColors.Reset}", rutas, virtualRoot: "aws");
        ThemeColors.Selector = ThemeColors.Bright;
        if (sel.Length == 0) TextViewer.Warn("Sin selección");
        else
        {
            TextViewer.Success($"Elegiste {sel.Length}:");
            foreach (string r in sel)
                TextViewer.Info($"  → {r}");
        }
    }

    // ==================== Demos LivePanel / LiveConsole ====================

    private static async Task TestLivePanel()
    {
        LivePanel.Start(maxLogs: 200);
        try
        {
            TextViewer.WriteHeader($"{ThemeColors.Primary}LivePanel — logs dentro del panel{ThemeColors.Reset}");
            TextViewer.Info("Este mensaje va al panel (no a la consola inline).");
            long dyn = LivePanel.AddDynamic("Iniciando trabajo...");

            await Task.Delay(800);
            LivePanel.UpdateLine(dyn, "Trabajando... 50%");

            // Spinner dentro del panel
            await SpinnerDisplay.RunAsync("Conectando al servidor", async (token) =>
            {
                await Task.Delay(2000, token);
            });

            // Barra de progreso dentro del panel
            await ProgressBarDisplay.RunAsync("Descargando payload", 150L, async (p) =>
            {
                for (long i = 0; i <= 150; i += 10)
                {
                    p.Value = i;
                    TextViewer.Info($"Progreso: {i}/150");
                    await Task.Delay(75);
                }
            });

            LivePanel.UpdateLine(dyn, TextViewer.SuccessF($"{ThemeColors.Bright}Trabajo completado."));
            LivePanel.UpdateDecorations(dyn, suffix: ThemeColors.Reset);
            TextViewer.Success("Panel terminado — todo OK.");
            TextInput.PressToContinue("Presioná Enter para cerrar el panel");
        }
        finally
        {
            LivePanel.Stop();
        }
    }

    private static async Task TestLiveConsole()
    {
        var console = new LiveConsole();
        AnsiColor prevPrimary = ThemeColors.Primary;
        ThemeColors.Primary = AnsiColor.Cyan + AnsiColor.Bold;
        try
        {
            await console.RunAsync(">>> ", async (input) =>
            {
                string trimmed = input.Trim();

                if (trimmed.Equals("/help", StringComparison.OrdinalIgnoreCase))
                {
                    console.WriteLog($"{ThemeColors.Bright}Comandos disponibles{ThemeColors.Reset}:");
                    console.WriteLog("  /help     — mostrar esta ayuda");
                    console.WriteLog("  /echo X   — repetir X");
                    console.WriteLog("  /exit     — salir");
                }
                else if (trimmed.StartsWith("/echo ", StringComparison.OrdinalIgnoreCase))
                {
                    string rest = input.Substring(6);
                    console.WriteLog($"{ThemeColors.Primary}Echo{ThemeColors.Reset}: {rest}");
                }
                else
                {
                    console.WriteLog($"{ThemeColors.Dim}Escribiste{ThemeColors.Reset}: {input}");
                    console.WriteLog($"{ThemeColors.Dim}(probá /help){ThemeColors.Reset}");
                }
            });
        }
        finally
        {
            ThemeColors.Primary = prevPrimary;
        }
        TextViewer.Success("LiveConsole cerrada.");
    }
}
