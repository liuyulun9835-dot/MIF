using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

class ProbingLoadContext : AssemblyLoadContext
{
    private readonly string[] _probeDirs;
    public ProbingLoadContext(params string[] probeDirs) => _probeDirs = probeDirs;

    protected override Assembly? Load(AssemblyName name)
    {
        foreach (var dir in _probeDirs)
        {
            var path = Path.Combine(dir, name.Name + ".dll");
            if (File.Exists(path)) return LoadFromAssemblyPath(path);
        }
        return null; // 回到默认解析
    }
}

class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0) { Console.WriteLine("usage: typedump <path-to-dll>"); return; }

        var target = Path.GetFullPath(args[0]);
        var indDir = Path.GetDirectoryName(target)!;
        var probes = new[] {
            indDir,
            @"C:\MIF\externals\ATAS",                                       // 你的 ATAS 依赖目录
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "packs", "Microsoft.NETCore.App.Ref", "8.0.20", "ref", "net8.0")
        };

        var alc = new ProbingLoadContext(probes);
        var asm = alc.LoadFromAssemblyPath(target);

        foreach (var t in asm.ExportedTypes.OrderBy(t => t.FullName))
            Console.WriteLine($"{t.FullName}  |  base: {t.BaseType?.FullName}");
    }
}
