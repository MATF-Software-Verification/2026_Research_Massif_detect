#include <stddef.h>
#include <stdlib.h>
#include <string.h>

enum { BlockCount = 24, BlockSize = 64 * 1024 };

static volatile unsigned long observed;

int main(void)
{
    void *blocks[BlockCount];

    for (size_t i = 0; i < BlockCount; i++) {
        blocks[i] = malloc(BlockSize);
        if (blocks[i] == NULL)
            return 1;
        memset(blocks[i], (int)i, BlockSize);
        observed += ((unsigned char *)blocks[i])[BlockSize - 1];
    }

    for (size_t i = 0; i < BlockCount; i++)
        free(blocks[i]);

    return observed == 0;
}
