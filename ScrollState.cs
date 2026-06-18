using System;

namespace ConsoleUtils
{
    public struct ScrollState
    {
        public int Cursor { get; private set; }
        public int Scroll { get; private set; }
        public int VisibleRows { get; private set; }

        private int _lastHeight;
        private int _lastWidth;

        /// <summary>
        /// Actualiza la matemática del scroll y detecta si la pantalla cambió de tamaño.
        /// </summary>
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