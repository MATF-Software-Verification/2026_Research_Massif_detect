# Massif detection examples

Each program demonstrates one simple, explainable analysis outcome. Generated executables are
placed in a temporary directory; only the C source and `massif.out.*` profiles remain here.

| Program | Expected findings |
|---|---|
| `normal.c` | No findings |
| `leak.c` | `LEAK` Warning and `ATEXIT` Info |
| `at_exit.c` | `ATEXIT` Info |
| `transient_spike.c` | Transient `SPIKE` Info |
| `retained_spike.c` | Retained `SPIKE` Warning and `ATEXIT` Info |
| `overhead.c` | `FRAG` Warning |

Regenerate all profiles from the repository root:

```sh
./sample/generate.sh
```

The script uses `gcc` and Valgrind Massif. It profiles with `--time-unit=B` for reproducible
allocation-based timing and `--detailed-freq=1` so every retained snapshot has an allocation
tree available for attribution.
