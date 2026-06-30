# AGENTS.md

Project-specific guidance for this repo lives in `CLAUDE.md` (working agreement,
Mini-C language reference, UKNC hardware facts). Read it first.

## Cursor Cloud specific instructions

### What this project is
Mini-C → Macro-11 compiler for the UKNC (Электроника МС-0511, PDP-11) computer.
The shipped app is a **Windows-only WinForms IDE** (`.NET Framework 4.8`,
`CompMacro11/CompMacro11.csproj`). It will NOT build or run on Linux because it
references `System.Windows.Forms`. Do not try to run the GUI here.

### How to build/run the core on Linux (headless)
The compiler core (`Lexer.cs`, `Parser.cs`, `AST.cs`, `CodeGen.cs`,
`CodeGen.Runtime.cs`, `Peephole.cs`, `Stdlib.cs`) has no WinForms dependency and
builds with the **.NET 8 SDK** (`dotnet-sdk-8.0`, installed via apt). A headless
CLI harness lives in `clitest/` (`minic.csproj` links the core files, `Main.cs`
mirrors the `Compile()` pipeline from `Form1.cs`).

- Build / lint: `dotnet build clitest/minic.csproj`
- Compile a Mini-C file: `dotnet run --project clitest -- <input.mc> <output.mac>`
  - Prints `OK: <n> chars, <n> lines .mac` on success, `COMPILE ERROR: ...`
    (exit 1) on failure.
- Sample programs are `Samples/*/main.mc` (Mini-C uses the `.mc` extension).

### Non-obvious caveats
- `mono` cannot compile this C# (uses C#7 pattern matching) — use `dotnet` only.
- NuGet is typically blocked (403). The harness needs no packages; `clitest/`
  ships a `NuGet.config` with `<clear/>` so restore stays offline.
- The CLI intentionally skips sprite injection (`InjectSprites`) and the GUI;
  samples still compile because sprite data is only needed by the editor.
- There are no automated unit tests in this repo; "testing" = compiling `.mc`
  sources through the CLI and (on Windows) running the `.mac` in the bundled
  UKNCBTL emulator under `_projectUKNC/`.
- Octal vs decimal matters everywhere in generated Macro-11 (a number without a
  trailing `.` is octal). See `UKNC_CODES.md`.
