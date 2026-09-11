#include <stddef.h>
#include <stdlib.h>
#include <string.h>

enum {
    BaselineCount = 20,
    BaselineSize = 64 * 1024,
    SpikeSize = 4 * 1024 * 1024,
    TailSize = 256 * 1024
};

static volatile unsigned long observed;

int main(void)
{
    void *baseline[BaselineCount];

    for (size_t i = 0; i < BaselineCount; i++) {
        baseline[i] = malloc(BaselineSize);
        if (baseline[i] == NULL)
            return 1;
        memset(baseline[i], 1, BaselineSize);
    }

    unsigned char *temporary = malloc(SpikeSize);
    if (temporary == NULL)
        return 1;
    memset(temporary, 2, SpikeSize);
    observed += temporary[SpikeSize - 1];
    free(temporary);

    for (size_t i = 0; i < 40; i++) {
        unsigned char *tail = malloc(TailSize);
        if (tail == NULL)
            return 1;
        memset(tail, (int)i, TailSize);
        observed += tail[TailSize - 1];
        free(tail);
    }

    for (size_t i = 0; i < BaselineCount; i++)
        free(baseline[i]);

    return observed == 0;
}
