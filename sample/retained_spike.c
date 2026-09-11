#include <stddef.h>
#include <stdlib.h>
#include <string.h>

enum { BaselineCount = 20, BaselineSize = 64 * 1024, SpikeSize = 4 * 1024 * 1024 };

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

    unsigned char *retained = malloc(SpikeSize);
    if (retained == NULL)
        return 1;
    memset(retained, 2, SpikeSize);
    observed += retained[SpikeSize - 1];

    for (size_t i = 0; i < BaselineCount; i++)
        free(baseline[i]);

    /* The large allocation intentionally remains resident. */
    return observed == 0;
}
