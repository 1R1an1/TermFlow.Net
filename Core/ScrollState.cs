/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
using System;

namespace TermFlow.Core
{
    /// <summary>
    /// Pequeña estructura de estado que centraliza la matemática del scroll y cursor
    /// para componentes con listas scrollables (Menu, SearchList, TreeExplorer, etc.).
    /// </summary>
    internal struct ScrollState
    {
        /// <summary>Posición lógica del cursor dentro de la lista completa.</summary>
        public int Cursor { get; private set; }
        /// <summary>Desplazamiento actual de la ventana visible (índice del primer item mostrado).</summary>
        public int Scroll { get; private set; }
        /// <summary>Cantidad de filas visibles calculadas según el alto de la consola.</summary>
        public int VisibleRows { get; private set; }

        private int _lastHeight;
        private int _lastWidth;

        /// <summary>
        /// Actualiza la matemática del scroll y detecta si la pantalla cambió de tamaño.
        /// </summary>
        /// <param name="targetCursor">Nueva posición deseada del cursor.</param>
        /// <param name="totalItems">Cantidad total de ítems de la lista.</param>
        /// <param name="reservedRows">Filas reservadas (cabecera, footer, etc.) que no se usan para datos.</param>
        /// <returns><c>true</c> si la consola fue redimensionada desde la última llamada.</returns>
        public bool Update(int targetCursor, int totalItems, int reservedRows)
        {
            bool sizeChanged = false;

            // 1. Detectar redimensionado automáticamente
            if (Console.WindowHeight != _lastHeight || Console.WindowWidth != _lastWidth)
            {
                _lastHeight = Console.WindowHeight;
                _lastWidth = Console.WindowWidth;
                sizeChanged = true;
            }

            // 2. Calcular filas disponibles para los datos
            VisibleRows = Math.Max(1, Console.WindowHeight - reservedRows);

            // 3. Ajustar límites del cursor por seguridad
            Cursor = targetCursor;
            if (totalItems == 0) Cursor = 0;
            else if (Cursor >= totalItems) Cursor = totalItems - 1;
            if (Cursor < 0) Cursor = 0;

            // 4. Mover la ventana de scroll según la posición del cursor
            if (Cursor < Scroll) Scroll = Cursor;
            if (Cursor >= Scroll + VisibleRows) Scroll = Cursor - VisibleRows + 1;

            return sizeChanged;
        }
    }
}
