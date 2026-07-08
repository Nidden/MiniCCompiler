using System;
using System.IO;

namespace CompMacro11
{
    internal static class CliMain
    {
        static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("usage: minic [--game|--cpu] <input.c> [output.mac]");
                return 2;
            }

            var cg = new CodeGen();
            int i = 0;
            while (i < args.Length && args[i].StartsWith("--"))
            {
                if (args[i] == "--game") cg.Mode = CodeGen.RtMode.Game;
                else if (args[i] == "--cpu") cg.Mode = CodeGen.RtMode.Cpu;
                else
                {
                    Console.Error.WriteLine($"unknown option: {args[i]}");
                    return 2;
                }
                i++;
            }

            if (i >= args.Length)
            {
                Console.Error.WriteLine("usage: minic [--game|--cpu] <input.c> [output.mac]");
                return 2;
            }

            string inPath = args[i++];
            if (!File.Exists(inPath))
            {
                Console.Error.WriteLine($"COMPILE ERROR: file not found: {inPath}");
                return 2;
            }

            try
            {
                string src = File.ReadAllText(inPath);
                string fullSrc = StdLib.Inject(src);

                var tokens = new Lexer(fullSrc).Tokenize();
                var ast = new Parser(tokens).ParseProgram();
                cg.OptimizeRuntime = true;
                string asm = cg.Generate(ast);

                if (i < args.Length)
                    File.WriteAllText(args[i], asm);

                Console.WriteLine($"OK: {asm.Length} chars, {asm.Split('\n').Length} lines .mac");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("COMPILE ERROR: " + ex.Message);
                return 1;
            }
        }
    }
}
