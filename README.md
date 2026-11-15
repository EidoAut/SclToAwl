<!-- EIDO repository presentation. Detailed technical documentation is preserved below. -->
<p align="center"><img src="docs/assets/eido-project-banner.svg" alt="SclToAwl — EIDO Automation" width="100%"></p>

# SclToAwl

**Siemens SCL to readable AWL / STL**

<p align="justify">A command-line transpiler focused on converting supported SCL logic into ladder-friendly AWL.</p>

[![License: MIT](https://img.shields.io/badge/License-MIT-5bbc85.svg)](LICENSE)

**Maintained by [EIDO Automation](https://github.com/EidoAut).** See the [MIT license](LICENSE) for reuse conditions. The capabilities and limitations described below are those of this repository, not a guarantee of production suitability.

## Explore

- [Documentation and usage](#project-documentation)
- [Source code](./)
- [License](LICENSE)

## Project documentation

**SclToAwl** is a command-line tool that converts a ladder-friendly subset of **Siemens SCL (Structured Control Language)** into **AWL/STL (Statement List)**, with a strong focus on producing **clean, readable, ladder-like code**.

It is designed for automation engineers who want to:
- Transpile existing SCL logic into AWL
- Write textual logic and paste it directly as networks into **TIA Portal / STEP7**
- Work *per network*, not only with full programs

> *SCL → AWL like it's 1998 again. Ladders never die.*

---

## Philosophy

- Prefer **ladder-style AWL** whenever possible
- Avoid unnecessary temporaries (`#t`, `#COND`, `#N`)
- Emit `U / O( ) / = / S / R` instead of jumps
- Use permissive conversion only when required
- Make **network-level workflows** first-class

---

## Internal pipeline

1. **Lexing / Parsing** → SCL AST  
2. **Lowering** → ladder-oriented IR  
3. **Emission** → readable AWL (inline or NETWORK-based)

---

## Build (development)

From the repository root:

```bash
dotnet build
```

Run without publishing:

```bash
dotnet run -- --help
```

> Note: `--` separates `dotnet` arguments from application arguments.

---

## Publish (generate executable)

### Windows (self-contained)

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

### Linux

```bash
dotnet publish -c Release -r linux-x64 --self-contained true
```

### macOS

```bash
dotnet publish -c Release -r osx-x64 --self-contained true
```

Output directory:

```text
SclToAwl/bin/Release/<TFM>/<RID>/publish/
```

---

## Installation (PATH)

### Windows

```powershell
copy SclToAwl.exe C:\tools\
```

Add `C:\tools` to your `PATH` and test:

```powershell
scltoawl --help
```

### Linux / macOS

```bash
sudo cp SclToAwl /usr/local/bin/scltoawl
sudo chmod +x /usr/local/bin/scltoawl
```

---

## Main commands

### `network`  (recommended)

Converts **only the body of a single SCL network** and emits **inline AWL**, without `NETWORK` or `TITLE`.

Perfect for:
- Pasting directly into a TIA Portal network
- Splitting large programs into multiple `.scl` files
- Network-by-network refactoring

```bash
scltoawl network <input.scl> [more.scl ...] [options]
scltoawl network <dir>
scltoawl network <glob>
```

#### Example SCL (network body only)

```scl
Motor_On := Start_Button AND NOT Stop_Button;

IF Overload THEN
 Alarm := TRUE;
END_IF;

Ready := Motor_On AND (NOT Alarm OR Override);
```

#### Generated AWL (inline)

```awl
U START_BUTTON
UN STOP_BUTTON
= MOTOR_ON

U OVERLOAD
S ALARM

U MOTOR_ON
U(
UN ALARM
O OVERRIDE
)
= READY
```

---

### `convert`

Converts SCL to classic AWL with `NETWORK` / `TITLE` blocks.

```bash
scltoawl convert Demo.scl
```

---

## Conversion modes

- `--strict` (default)  
 Only emits **100% ladder-clean** logic. Fails if temporaries are required.

- `--convert`  
 Allows:
  - complex parentheses
  - nested ORs
  - arithmetic
  - FOR / WHILE / CASE  
 while still **preferring ladder-style emission** whenever possible.

```bash
scltoawl network Demo.scl --convert
```

---

## Batch processing

```bash
# multiple files
scltoawl network A.scl B.scl C.scl

# whole directory
scltoawl network ./Networks

# glob
scltoawl network Networks/*.scl
```

### Concatenated output

```bash
scltoawl network Networks/*.scl --concat --output all.awl
```

---

## Prefixes (important)

<p align="justify">To avoid collisions when pasting multiple networks into the same block:</p>

```bash
scltoawl network NW12.scl --prefix NW12_
```

Automatic prefix based on file name:

```bash
scltoawl network Networks/*.scl --prefix-from-name
```

---

## Common options

| Option | Description |
|------|-------------|
| `--strict` | Ladder-only mode |
| `--convert` | Permissive mode (ladder-first) |
| `--prefix <txt>` | Manual prefix for temps/labels |
| `--prefix-from-name` | Automatic per-file prefix |
| `--outdir <dir>` | Output directory |
| `--suffix <txt>` | Filename suffix |
| `--concat` | Concatenate outputs |
| `--overwrite` | Overwrite existing files |

---

## Tests

```bash
scltoawl test
scltoawl test ./Tests
```

Conventions:
- `*.scl` → input
- `*.expected.awl` → expected output
- missing expected file → error expected

---

## Notes

- The project prioritizes **readability and ladder style**, not aggressive optimization.
- `--convert` is **not a raw translator**: it still tries to emit ladder whenever feasible.
- Ideal for refactoring legacy logic or documenting complex networks.

---

## Credits

- **Samuel Seijo** — Programmer at **EIDO Automation**

---

## License

MIT License. See `LICENSE`.
