using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0) { Console.WriteLine("usage: typedump <path-to-dll>"); return; }
        var p = System.IO.Path.GetFullPath(args[0]);
        var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(p);
        foreach (var t in asm.ExportedTypes.OrderBy(t => t.FullName))
            Console.WriteLine($"{t.FullName}  |  base: {t.BaseType?.FullName}");
    }
}
