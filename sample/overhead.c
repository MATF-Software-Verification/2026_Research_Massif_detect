#include <stddef.h>
#include <stdlib.h>

enum { AllocationCount = 2000 };

static volatile unsigned long observed;

int main(void)
{
    unsigned char *blocks[AllocationCount];

    for (size_t i = 0; i < AllocationCount; i++) {
        blocks[i] = malloc(1);
        if (blocks[i] == NULL)
            return 1;
        blocks[i][0] = (unsigned char)i;
        observed += blocks[i][0];
    }

    for (size_t i = 0; i < AllocationCount; i++)
        free(blocks[i]);

    return observed == 0;
}
