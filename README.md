# LsifDotnet [![.NET 6.0](https://github.com/tcz717/LsifDotnet/actions/workflows/dotnet.yml/badge.svg)](https://github.com/tcz717/LsifDotnet/actions/workflows/dotnet.yml)

Visit https://lsif.dev/ to learn about LSIF.

Only tested in win platform and don't support `VisualBasic` yet.

Implmented LSIF data:

- `textDocument/hover`
- `textDocument/references`
- `textDocument/definition`
- `moniker`

### Requirement

Installed [.net 6.0](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)

### Usage

```
PS C:\Downloads\lsif-dotnet-win-0.0.1-beta6\win> .\lsif-dotnet.exe -h
Description:
  An indexer that dumps lsif information from dotnet solution.

Usage:
  lsif-dotnet [<SolutionFile>] [options]

Arguments:
  <SolutionFile>  The solution to be indexed. Default is the only solution file in the current folder.

Options:
  --output <output>    The lsif dump output file. [default: dump.lsif]
  --dot                Dump graphviz dot file.
  --svg                Dump graph svg file.
  --culture <culture>  The culture used to show hover info. [default: en-US]
  --version            Show version information
  -?, -h, --help       Show help and usage information

```

#### Dump a solution's lsif file

Goto the folder of the solution file and run:
```powershell
 .\lsif-dotnet.exe
```

And a `dump.lsif` file will be created in the current folder.

### Download:

Github Release: https://github.com/tcz717/LsifDotnet/releases

### Next

- Formated logging
- Linux/OSX verfication
- Unit tests
- VB support
- nuget information extraction
- More lsif features
