#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef struct { uint16_t *data; int32_t length; } AeroString;

// Strings are heap allocated and never freed until reference counting exists
static void from_ascii(AeroString *out, const char *text, int32_t length)
{
    uint16_t *data = malloc((size_t)(length > 0 ? length : 1) * sizeof(uint16_t));
    for (int32_t i = 0; i < length; i++) data[i] = (uint16_t)(unsigned char)text[i];
    out->data = data;
    out->length = length;
}

void aero_str_concat(AeroString *out, const AeroString *a, const AeroString *b)
{
    int32_t length = a->length + b->length;
    uint16_t *data = malloc((size_t)(length > 0 ? length : 1) * sizeof(uint16_t));
    if (a->length > 0) memcpy(data, a->data, (size_t)a->length * sizeof(uint16_t));
    if (b->length > 0) memcpy(data + a->length, b->data, (size_t)b->length * sizeof(uint16_t));
    out->data = data;
    out->length = length;
}

uint8_t aero_str_eq(const AeroString *a, const AeroString *b)
{
    if (a->length != b->length) return 0;
    return a->length == 0 || memcmp(a->data, b->data, (size_t)a->length * sizeof(uint16_t)) == 0;
}

void aero_str_from_int(AeroString *out, int32_t value)
{
    char text[16];
    int n = snprintf(text, sizeof text, "%d", value);
    from_ascii(out, text, n);
}

void aero_str_from_float(AeroString *out, float value)
{
    char text[48];
    int n = 0;

    // Shortest text that reads back as the same Float
    for (int precision = 1; precision <= 9; precision++)
    {
        n = snprintf(text, sizeof text, "%.*g", precision, (double)value);
        if (strtof(text, NULL) == value || value != value) break;
    }

    // 3 prints as 3.0, so a Float never looks like an Int
    if (strpbrk(text, ".eEnN") == NULL && n < (int)sizeof text - 3)
    {
        text[n++] = '.';
        text[n++] = '0';
        text[n] = '\0';
    }
    from_ascii(out, text, n);
}

void aero_str_from_bool(AeroString *out, uint8_t value)
{
    if (value) from_ascii(out, "true", 4);
    else from_ascii(out, "false", 5);
}

void aero_str_from_char(AeroString *out, uint16_t value)
{
    uint16_t *data = malloc(sizeof(uint16_t));
    data[0] = value;
    out->data = data;
    out->length = 1;
}

typedef struct { void *data; int32_t length; } AeroArray;

// Zeroed, so a fresh buffer never holds garbage
void *aero_alloc(int64_t bytes)
{
    void *memory = calloc(1, (size_t)(bytes > 0 ? bytes : 1));
    if (memory == NULL)
    {
        fputs("Out of memory\n", stderr);
        abort();
    }
    return memory;
}

void aero_array_oob(int32_t index, int32_t length)
{
    fprintf(stderr, "Index %d is out of range for an array of length %d\n", index, length);
    abort();
}

// UTF-16 never needs more units than UTF-8 has bytes
static void from_utf8(AeroString *out, const char *text)
{
    size_t bytes = strlen(text);
    uint16_t *data = malloc((bytes > 0 ? bytes : 1) * sizeof(uint16_t));
    int32_t n = 0;

    for (size_t i = 0; i < bytes;)
    {
        unsigned char c = (unsigned char)text[i];
        uint32_t cp;
        size_t len;

        if (c < 0x80) { cp = c; len = 1; }
        else if ((c >> 5) == 6) { cp = c & 0x1Fu; len = 2; }
        else if ((c >> 4) == 14) { cp = c & 0x0Fu; len = 3; }
        else if ((c >> 3) == 30) { cp = c & 0x07u; len = 4; }
        else { cp = 0xFFFD; len = 1; }

        if (len > 1)
        {
            if (i + len > bytes) { cp = 0xFFFD; len = 1; }
            else for (size_t k = 1; k < len; k++) cp = (cp << 6) | ((unsigned char)text[i + k] & 0x3Fu);
        }
        i += len;

        if (cp >= 0x10000)
        {
            cp -= 0x10000;
            data[n++] = (uint16_t)(0xD800 + (cp >> 10));
            data[n++] = (uint16_t)(0xDC00 + (cp & 0x3FF));
        }
        else data[n++] = (uint16_t)cp;
    }

    out->data = data;
    out->length = n;
}

void aero_args(AeroArray *out, int32_t argc, char **argv)
{
    int32_t count = argc > 1 ? argc - 1 : 0;
    AeroString *items = aero_alloc((int64_t)count * (int64_t)sizeof(AeroString));
    for (int32_t i = 0; i < count; i++) from_utf8(&items[i], argv[i + 1]);

    out->data = items;
    out->length = count;
}