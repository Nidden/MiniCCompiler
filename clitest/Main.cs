using System;
using System.IO;

namespace CompMacro11
{
    // Headless CLI front-end for the Mini-C -> Macro-11 compiler core.
    // Mirrors the compile pipeline in Form1.Compile() (minus the GUI-only
    // sprite injection step), so the actual compiler can be exercised on Linux.
    internal static class CliMain
    {
        private static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("usage: minic <input.c> [output.mac]");
                return 2;
            }

            string inputPath = args[0];
            string outputPath = args.Length >= 2 ? args[1] : Path.ChangeExtension(inputPath, ".mac");

            string src;
            try
            {
                src = File.ReadAllText(inputPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("READ ERROR: " + ex.Message);
                return 2;
            }

            try
            {
                string fullSrc = StdLib.Inject(src);               // built-in Mini-C libraries
                var tokens = new Lexer(fullSrc).Tokenize();
                var ast = new Parser(tokens).ParseProgram();
                var cg = new CodeGen { OptimizeRuntime = true };
                string asm = cg.Generate(ast);

                File.WriteAllText(outputPath, asm);
                int lines = asm.Split('\n').Length;
                Console.WriteLine($"OK: {asm.Length} chars, {lines} lines -> {outputPath}");
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
