# Luft

The compiler toolchain for **Aero** — a compiled, statically-typed language inspired by Kotlin and C#, aiming for the performance profile of C++/Rust.

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Language](https://img.shields.io/badge/language-C%23-239120)
![Status](https://img.shields.io/badge/status-in%20development-yellow)
![Stage](https://img.shields.io/badge/stage-type%20checking-orange)
![License](https://img.shields.io/badge/license-unlicensed-lightgrey)

---

## About Aero

Aero draws its syntax and ergonomics from Kotlin and C#, but targets systems-level performance:

- **No garbage collector** — reference counting, with `weak` references for cycles
- **No raw pointers** — `unsafe` blocks opt in to low-level control when needed
- **Fiber-based concurrency** via `@Pure`-annotated functions, instead of async/await
- Kotlin-style niceties: extension functions, `enum class`, nullable types

Aero source (`.aero`) compiles down through Luft's pipeline toward native/LLVM output.

## Pipeline

```
 .aero source
      │
      ▼
 ┌─────────┐     ┌───────────────┐     ┌───────────┐
 │  Lexer  │ ──▶│  AstBuilder   │ ──▶│    AST    │
 └─────────┘     └───────────────┘     └───────────┘
                                            │
                             ┌──────────────┼──────────────┐
                             ▼                             ▼
                     ┌───────────────┐            ┌─────────────────┐
                     │ Pretty/Tree   │            │  TypeChecker    │ ◀── we are here
                     │  Printers     │            │ (in progress)   │
                     └───────────────┘            └─────────────────┘
                                                          │
                                                          ▼
                                                 ┌──────────────────┐
                                                 │  Builder (LLVM)  │ ── planned
                                                 └──────────────────┘
```

| Component | Status |
|---|---|
| `Lexer` | ✅ |
| `AstBuilder` (parser) | ✅ |
| `Printers` (Pretty/Tree) | ✅ |
| `TypeChecker` | 🚧 |
| `Builder` (LLVM codegen) | ⏳ |
| `LSP` | ⏳ |

## What's next: LLVM

Once the type checker is stable, the `Builder` project takes over: the type-checked AST will be lowered/transpiled into **LLVM IR**, which LLVM's own backend then compiles to native machine code. 
This is what will eventually make `luft --build`/`luft --run` in the CLI produce real executables instead of just tokenizing and building an AST.

After that, editor tooling (`LSP`, starting with syntax highlighting) is the next milestone.

## Project layout

```
src/
├── Lexer/         # Tokenizer
├── Ast/            # AST nodes, AstBuilder (parser), AeroType system
├── Utility/        # Shared primitives: SourceSpan, ValueList, Operator, SafeIterator
├── Printers/       # PrettyPrinter & TreePrinter (AstVisitor-based)
├── TypeChecker/    # TypeLookup, TypeResolver, BodyResolver, symbol tables
├── Builder/        # LLVM codegen (LLVMSharp) — early stage
├── Cli/            # Command-line entry point
└── LSP/            # Language server — not started
```

## Building

```bash
dotnet build src/Cli/Cli.csproj
```

Requires the **.NET 10** SDK.

## Repositories

- [`Luft`](https://github.com/Aero-Language/Luft) — this compiler toolchain
- [`Acli`](https://github.com/Aero-Language/Acli) — shared CLI framework
- [`Aero-Docs`](https://github.com/Aero-Language/Aero-Docs) — language documentation (not actively kept in sync during rapid development)

## License

The Luft compiler is Open-Source under the [MIT License](LICENSE)
