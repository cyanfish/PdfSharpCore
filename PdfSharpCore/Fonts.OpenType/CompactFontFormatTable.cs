namespace PdfSharpCore.Fonts.OpenType
{
    internal class CompactFontFormatTable : OpenTypeFontTable
    {
        public const string Tag = TableTagNames.Cff;

        internal byte[] GlyphTable;

        public CompactFontFormatTable()
            : base(null, Tag)
        {
        }

        public CompactFontFormatTable(OpenTypeFontface fontData)
            : base(fontData, Tag)
        {
        }
    }
}
