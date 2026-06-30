using System;
using System.IO;

namespace CompMacro11
{
    // Headless CLI harness for the Mini-C -> Macro-11 compiler core.
    // Mirrors the Compile() pipeline from Form1.cs (minus sprite injection / GUI).
    internal static class CliMain
    {
        static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("usage: minic <input.c> [output.mac]");
                return 2;
            }

            string inPath = args[0];
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
                var cg = new CodeGen();
                cg.OptimizeRuntime = true;
                string asm = cg.Generate(ast);

                if (args.Length >= 2)
                    File.WriteAllText(args[1], asm);

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
