# AGENTS.md

Project-wide guidance for AI agents lives in `CLAUDE.md` (in Russian) — read it
for language/compiler details, UKNC hardware facts, and the human workflow.

## Cursor Cloud specific instructions

### What this project is
`CompMacro11` is a **Windows-only WinForms GUI app targeting .NET Framework 4.8**
(`CompMacro11/CompMacro11.csproj`, `OutputType=WinExe`). It is an IDE + compiler
that translates Mini-C into Macro-11 assembly for the Soviet UKNC (PDP-11)
computer. The full GUI app **cannot be built or run on Linux** (no .NET
Framework; Mono cannot build the C# 7 features it uses — see `CLAUDE.md`).

### How to build/run on Linux (the dev-testable surface)
The compiler core (`Lexer.cs`, `Parser.cs`, `AST.cs`, `CodeGen.cs`,
`CodeGen.Runtime.cs`, `Peephole.cs`, `Stdlib.cs`) has **no GUI dependencies**.
A headless `dotnet` (net8.0) harness in `clitest/` links those files and exposes
the exact compile pipeline used by `Form1.Compile()` (minus the GUI-only sprite
injection step):

- Build: `dotnet build clitest/minic.csproj -c Debug`
- Run (compile a Mini-C file): `dotnet clitest/bin/Debug/net8.0/minic.dll <input.c> [out.mac]`
  - prints `OK: <n> chars, <n> lines` and exit 0 on success
  - prints `COMPILE ERROR: ...` and exit 1 on a compile error
- Example program: `clitest/hello.c`. Repo samples are `Samples/*/main.mc`
  (same Mini-C language; rename/copy to `.c` or just pass the `.mc` path).

The `clitest/` harness has **no external NuGet packages** (a `NuGet.config`
with `<clear/>` is included because public NuGet may be blocked).

### Lint / test
There is **no separate lint config and no automated test suite** in this repo.
Verification = building the `clitest/` harness (the C# compiler is the linter)
and compiling sample Mini-C programs through it.

### Toolchain notes
- The .NET SDK 8 is provided by the VM snapshot. If it is ever missing, install
  with `sudo apt-get update && sudo apt-get install -y dotnet-sdk-8.0`.
- Only `dotnet` (net8.0) works for the core; Mono does **not** compile it.
