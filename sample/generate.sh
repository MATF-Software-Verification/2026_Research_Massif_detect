#!/bin/sh
set -eu

sample_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
build_dir=$(mktemp -d)
trap 'rm -rf "$build_dir"' EXIT

for name in normal leak at_exit transient_spike retained_spike overhead; do
    gcc -g -O0 -Wall -Wextra "$sample_dir/$name.c" -o "$build_dir/$name"
    valgrind \
        --tool=massif \
        --time-unit=i \
        --detailed-freq=1 \
        --max-snapshots=200 \
        --threshold=0.1 \
        --massif-out-file="$sample_dir/massif.out.$name" \
        "$build_dir/$name"
done
