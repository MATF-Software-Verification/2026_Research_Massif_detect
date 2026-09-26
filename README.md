# MassifDetect

MassifDetect is a desktop application for viewing and analyzing Valgrind Massif
heap profiles. It shows memory usage over time and highlights suspicious patterns
such as steady growth, retained memory, sudden spikes, and high allocator overhead.

![MassifDetect showing an instruction-based memory chart and findings](docs/massifdetect.png)

## Authors

- Vladeta Vujacic 1017/2024
- Marko Nikitovic 1007/2024

## Requirements

- .NET 10 SDK
- GCC and Valgrind for compiling and profiling C source files
- Avalonia and ScottPlot packages (downloaded automatically by
`dotnet restore`)
- GCC and Valgrind (not required if you only want to open an
existing `massif.out.*` profile)

## Build and run

Run these commands from the repository root:

```sh
dotnet restore
dotnet build -c Release
dotnet run --project MassifVisualizer/MassifVisualizer.csproj
```

After the Release build, you can also run the built application directly:

```sh
dotnet MassifVisualizer/bin/Release/net10.0/MassifVisualizer.dll
```

To open a profile immediately when the application starts, pass its path as an
argument:

```sh
dotnet run --project MassifVisualizer/MassifVisualizer.csproj -- sample/massif.out.leak
```

## Usage example

To inspect an existing profile:

1. Start the application or use the command above with `sample/massif.out.leak`.
2. If no profile was passed on startup, select **File > Open massif file** and
   choose `sample/massif.out.leak`.
3. Select snapshots on the left to inspect their allocation tree and details.
4. Open the **Hot Functions** tab to see the largest allocation sites.
5. Open the **Findings** tab. This example should show a `LEAK` warning and an
   `ATEXIT` information finding. Select a finding to jump to its evidence
   snapshot.

To analyze a C source file directly:

1. Start the application and select **File > Open source file**.
2. Choose a file such as `sample/leak.c`.
3. The application uses GCC to compile the file and Valgrind Massif to profile
   it, then loads the generated profile automatically.

The **Settings** window can change Massif options and detection thresholds for
the current session.

## Included examples

The `sample/` directory contains focused C programs and matching Massif profiles:

| Input | Expected result |
| --- | --- |
| `normal.c` | No findings |
| `leak.c` | `LEAK` warning and `ATEXIT` information |
| `at_exit.c` | `ATEXIT` information |
| `transient_spike.c` | Transient `SPIKE` information |
| `retained_spike.c` | Retained `SPIKE` warning and `ATEXIT` information |
| `overhead.c` | `FRAG` warning |

The matching `massif.out.*` files can be opened without compiling the C files.
To regenerate all profiles, run:

```sh
./sample/generate.sh
```

This command requires GCC and Valgrind. The generated profiles use executed
instructions as the time unit.
