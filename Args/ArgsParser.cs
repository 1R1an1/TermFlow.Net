/* SPDX-License-Identifier: MPL-2.0
 * Copyright (c) 2026 1R1an1 */
using System;
using System.Collections.Generic;

namespace TermFlow.Args;

/// <summary>
/// Resultado tipado de parsear argumentos.
/// </summary>
public record class Args
{
    public readonly string[] Positionals;
    private readonly Dictionary<string, string> _singleOptions;
    private readonly Dictionary<string, string[]> _multiOptions;
    private readonly HashSet<string> _flags;

    internal Args(Dictionary<string, string> singleOptions, Dictionary<string, string[]> multiOptions, HashSet<string> flags, string[] positionals)
    {
        _singleOptions = singleOptions;
        _multiOptions = multiOptions;
        _flags = flags;
        Positionals = positionals;
    }

    /// <summary>Devuelve el primer valor encontrado entre los alias provistos, o null.</summary>
    public string GetSingle(params string[] options)
    {
        foreach (var o in options)
            if (_singleOptions.TryGetValue(o, out var v))
                return v;
        return null;
    }

    /// <summary>Devuelve los valores de la opción múltiple cuyo alias exista, o null.</summary>
    public string[] GetMany(params string[] options)
    {
        foreach (var o in options)
            if (_multiOptions.TryGetValue(o, out var v))
                return v;
        return null;
    }

    /// <summary>True si alguna de las flags provistas fue pasada.</summary>
    public bool HasFlag(params string[] flags)
    {
        foreach (var f in flags)
            if (_flags.Contains(f))
                return true;
        return false;
    }
}

/// <summary>
/// Parser simple de argumentos CLI.
/// </summary>
public static class ArgsParser
{
    /// <summary>Parsea <paramref name="args"/>. Devuelve null si <paramref name="args"/> es vacío o null.</summary>
    /// <param name="args">Argumentos a parsear.</param>
    /// <returns>Un <see cref="Args"/> con opciones, flags y positionals, o null si no hay args.</returns>
    public static Args Parse(string[] args)
    {
        if (args is null || args.Length == 0)
            return null;

        var singleOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var multiOptions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var positionals = new List<string>();
        bool positionalOnly = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (arg == "--") { positionalOnly = true; continue; }
            if (positionalOnly) { positionals.Add(arg); continue; }

            // --flag=value → Single
            if ((arg.StartsWith("--") || arg.StartsWith('-')) && arg.Contains('='))
            {
                int eq = arg.IndexOf('=');
                singleOptions[arg[..eq]] = arg[(eq + 1)..];
                continue;
            }

            // --flag o -x, con o sin valores siguientes
            if (arg.StartsWith("--") || (arg.StartsWith('-') && arg.Length > 1))
            {
                var values = new List<string>();
                while (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                    values.Add(args[++i]);

                if (values.Count == 0)
                    flags.Add(arg);
                else if (values.Count == 1)
                    singleOptions[arg] = values[0];
                else
                    multiOptions[arg] = values.ToArray();
                continue;
            }

            // Posicional
            positionals.Add(arg);
        }

        return new Args(singleOptions, multiOptions, flags, positionals.ToArray());
    }
}
