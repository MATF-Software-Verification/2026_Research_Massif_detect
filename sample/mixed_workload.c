/* A small batch-processing workload with several allocation sites.
   The input and index intentionally remain allocated at exit.
   cache_result deliberately loses its allocation, simulating a missing cleanup.
   Massif shows the pattern, but cannot distinguish these two kinds of retention. */
#include <stddef.h>
#include <stdlib.h>
#include <string.h>

enum {
    InputCount = 16,
    InputSize = 32 * 1024,
    IndexSize = 2 * 1024 * 1024,
    BatchCount = 48,
    ResultSize = 64 * 1024,
    ScratchSize = 8 * 1024
};

static unsigned char *inputs[InputCount];
static unsigned char *index_data;
static volatile unsigned long observed;

static void load_inputs(void)
{
    for (size_t i = 0; i < InputCount; i++) {
        inputs[i] = malloc(InputSize);
        if (inputs[i] == NULL)
            exit(EXIT_FAILURE);
        memset(inputs[i], (int)i, InputSize);
        observed += inputs[i][InputSize - 1];
    }
}

static void build_index(void)
{
    index_data = malloc(IndexSize);
    if (index_data == NULL)
        exit(EXIT_FAILURE);
    memset(index_data, 1, IndexSize);
    observed += index_data[IndexSize - 1];
}

static void cache_result(size_t batch)
{
    unsigned char *result = malloc(ResultSize);
    if (result == NULL)
        exit(EXIT_FAILURE);
    /* Several processing passes leave enough instruction time after the index
       allocation for SPIKE to observe its recovery window. */
    for (int pass = 0; pass < 4; pass++) {
        memset(result, (int)batch + pass, ResultSize);
        observed += result[ResultSize - 1];
    }
    /* Deliberate bug: no free(result), and no saved pointer for later cleanup. */
}

static void process_batch(size_t batch)
{
    unsigned char *scratch = malloc(ScratchSize);
    if (scratch == NULL)
        exit(EXIT_FAILURE);
    memset(scratch, inputs[batch % InputCount][0], ScratchSize);
    observed += scratch[ScratchSize - 1] + index_data[batch];
    cache_result(batch);
    free(scratch);
}

int main(void)
{
    load_inputs();
    build_index();
    for (size_t batch = 0; batch < BatchCount; batch++)
        process_batch(batch);

    /* Keep the input and index until exit to illustrate ATEXIT as well. */
    return observed == 0;
}
