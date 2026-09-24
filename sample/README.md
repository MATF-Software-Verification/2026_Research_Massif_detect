# Massif detection examples

These programs demonstrate different memory-use patterns. Generated executables are
placed in a temporary directory; only the C source and `massif.out.*` profiles remain here.

| Program | Expected findings |
|---|---|
| `normal.c` | No findings |
| `leak.c` | `LEAK` Warning and `ATEXIT` Info |
| `at_exit.c` | `ATEXIT` Info |
| `transient_spike.c` | Transient `SPIKE` Info |
| `retained_spike.c` | Retained `SPIKE` Warning and `ATEXIT` Info |
| `overhead.c` | `FRAG` Warning |

The spike examples continue recording smaller allocations after the large increase.
This gives the detector enough later snapshots to classify recovery when the time
axis uses executed instructions.

Regenerate all profiles from the repository root:

```sh
./sample/generate.sh
```

The script uses `gcc` and Valgrind Massif. It profiles with `--time-unit=i`, so the
time axis counts executed instructions, not seconds. It uses `--detailed-freq=1`
to record allocation trees frequently.
