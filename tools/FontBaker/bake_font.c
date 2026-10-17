/*
 * Bakes the balloon font (Tahoma 13 px regular and bold, monochrome with full TrueType
 * hinting) into BalloonFont.g.cs for OfficeAssistant.Core's BitmapFontRenderer.
 *
 *   clang -O2 bake_font.c $(pkg-config --cflags --libs freetype2) -o bake_font
 *   ./bake_font Tahoma.ttf "Tahoma Bold.ttf" > ../../src/OfficeAssistant.Core/Rendering/BalloonFont.g.cs
 *
 * FreeType's TrueType interpreter in monochrome mode runs the font's hinting instructions
 * much like GDI does for NONANTIALIASED_QUALITY text. Where the two disagree on a pixel that
 * the Office XP screenshots show, the pixel is corrected below (see `corrections`). On
 * Windows, `AcsTool bake-font` produces the same file straight from GDI instead.
 */
#include <ft2build.h>
#include FT_FREETYPE_H
#include FT_MODULE_H
#include FT_DRIVER_H
#include FT_TRUETYPE_TABLES_H
#include <stdio.h>
#include <stdlib.h>

#define PIXEL_HEIGHT 13

/*
 * Scripts that need shaping or reordering (Hebrew, Arabic, Thai, ...) are left out: those
 * runs go to the platform's own text renderer, as do CJK characters that Tahoma lacks.
 */
static const unsigned int ranges[][2] = {
    {0x0020, 0x007E}, /* ASCII */
    {0x00A0, 0x00FF}, /* Latin-1 Supplement */
    {0x0100, 0x024F}, /* Latin Extended-A and B */
    {0x02B0, 0x02FF}, /* Spacing modifier letters */
    {0x0370, 0x03FF}, /* Greek */
    {0x0400, 0x052F}, /* Cyrillic */
    {0x1E00, 0x1EFF}, /* Latin Extended Additional */
    {0x2000, 0x206F}, /* General punctuation */
    {0x20A0, 0x20CF}, /* Currency */
    {0x2100, 0x214F}, /* Letterlike symbols */
    {0x2190, 0x21FF}, /* Arrows */
    {0x2200, 0x22FF}, /* Mathematical operators */
    {0x2500, 0x25FF}, /* Box drawing, block elements, geometric shapes */
    {0xFB00, 0xFB06}, /* Ligatures */
};

/* Rows where FreeType and GDI disagree, as measured from the screenshots in Screenshots/. */
static const struct { int bold; unsigned int code; unsigned int row; unsigned int mask; } corrections[] = {
    {1, 's', 3, 0x78}, /* "Microsoft Word" in Screenshot 2026-10-15 204312.png */
};

static unsigned int correct(int bold, unsigned int code, unsigned int row, unsigned int mask)
{
    for (size_t i = 0; i < sizeof(corrections) / sizeof(corrections[0]); i++)
    {
        if (corrections[i].bold == bold && corrections[i].code == code && corrections[i].row == row)
        {
            return corrections[i].mask;
        }
    }

    return mask;
}

static void fail(const char *what, FT_Error error)
{
    fprintf(stderr, "%s failed (FreeType error %d)\n", what, error);
    exit(1);
}

static void bake(FT_Library library, const char *path, const char *name, int bold)
{
    FT_Face face;
    FT_Error error = FT_New_Face(library, path, 0, &face);
    if (error) fail("FT_New_Face", error);
    error = FT_Set_Pixel_Sizes(face, 0, PIXEL_HEIGHT);
    if (error) fail("FT_Set_Pixel_Sizes", error);

    /* GDI's tmAscent: usWinAscent scaled to the pixel size, rounded. */
    TT_OS2 *os2 = (TT_OS2 *)FT_Get_Sfnt_Table(face, FT_SFNT_OS2);
    int ascent = (int)((os2->usWinAscent * PIXEL_HEIGHT + face->units_per_EM / 2) / face->units_per_EM);

    printf("    internal static readonly BitmapFont %s = BitmapFont.Decode(\n        ascent: %d,\n        glyphs:\n        [\n", name, ascent);
    for (size_t r = 0; r < sizeof(ranges) / sizeof(ranges[0]); r++)
    {
        for (unsigned int c = ranges[r][0]; c <= ranges[r][1]; c++)
        {
            FT_UInt index = FT_Get_Char_Index(face, c);
            if (index == 0) continue;
            error = FT_Load_Glyph(face, index, FT_LOAD_RENDER | FT_LOAD_TARGET_MONO | FT_LOAD_MONOCHROME);
            if (error) fail("FT_Load_Glyph", error);

            FT_GlyphSlot slot = face->glyph;
            FT_Bitmap *bitmap = &slot->bitmap;
            if (bitmap->width > 32) { fprintf(stderr, "U+%04X is wider than 32 px\n", c); exit(1); }

            /*
             * (code, advance, left, top below the cell top, width, rows). Each row is
             * ceil(width / 4) hex digits, the leftmost pixel in the highest bit.
             */
            unsigned int digits = (bitmap->width + 3) / 4;
            printf("            new(0x%04X, %ld, %d, %d, %u, \"", c, slot->advance.x >> 6, slot->bitmap_left, ascent - slot->bitmap_top, bitmap->width);
            for (unsigned int y = 0; y < bitmap->rows && digits > 0; y++)
            {
                unsigned int mask = 0;
                for (unsigned int x = 0; x < bitmap->width; x++)
                {
                    unsigned char byte = bitmap->buffer[y * bitmap->pitch + (x >> 3)];
                    if (byte & (0x80 >> (x & 7))) mask |= 1u << (digits * 4 - 1 - x);
                }
                printf("%0*X", (int)digits, correct(bold, c, y, mask));
            }
            printf("\"),\n");
        }
    }
    printf("        ]);\n");
    FT_Done_Face(face);
}

int main(int argc, char **argv)
{
    if (argc != 3)
    {
        fprintf(stderr, "usage: bake_font <Tahoma.ttf> <Tahoma Bold.ttf>\n");
        return 2;
    }

    FT_Library library;
    FT_Error error = FT_Init_FreeType(&library);
    if (error) fail("FT_Init_FreeType", error);
    FT_UInt interpreter = TT_INTERPRETER_VERSION_35;
    error = FT_Property_Set(library, "truetype", "interpreter-version", &interpreter);
    if (error) fail("FT_Property_Set", error);

    printf("// <auto-generated>\n// Generated by tools/FontBaker/bake_font.c from Tahoma at %d px. Do not edit.\n// </auto-generated>\n\n", PIXEL_HEIGHT);
    printf("namespace OfficeAssistant.Core.Rendering;\n\n");
    printf("/// <summary>Tahoma at %d px without anti-aliasing, as GDI draws it, for <see cref=\"BitmapFontRenderer\"/>.</summary>\n", PIXEL_HEIGHT);
    printf("internal static class BalloonFont\n{\n");
    bake(library, argv[1], "Regular", 0);
    printf("\n");
    bake(library, argv[2], "Bold", 1);
    printf("}\n");
    FT_Done_FreeType(library);
    return 0;
}
