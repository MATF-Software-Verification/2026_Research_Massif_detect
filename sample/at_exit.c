#include <stddef.h>
#include <stdlib.h>
#include <string.h>

enum { HeldBlockCount = 12, HeldBlockSize = 64 * 1024, WorkBlockSize = 4 * 1024 };

static volatile unsigned long observed;

int main(void)
{
    void *held[HeldBlockCount];

    for (size_t i = 0; i < HeldBlockCount; i++) {
        held[i] = malloc(HeldBlockSize);
        if (held[i] == NULL)
            return 1;
        memset(held[i], 1, HeldBlockSize);
        observed += ((unsigned char *)held[i])[HeldBlockSize - 1];
    }

    for (size_t i = 0; i < 30; i++) {
        unsigned char *work = malloc(WorkBlockSize);
        if (work == NULL)
            return 1;
        memset(work, (int)i, WorkBlockSize);
        observed += work[WorkBlockSize - 1];
        free(work);
    }

    /* This example models memory intentionally held for the process lifetime. */
    return observed == 0;
}
