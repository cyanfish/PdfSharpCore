using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Fonts.OpenType;

namespace PdfSharpCore.Utils
{
    public class FontSubsetBuilder
    {
        private readonly XFontSource _fontSource;
        private readonly CMapInfo _cMapInfo;

        public FontSubsetBuilder(string familyName)
        {
            var fontResolverInfo =
                FontFactory.ResolveTypeface(familyName, new FontResolvingOptions(XFontStyle.Regular), null);
            _fontSource = FontFactory.GetFontSourceByFontName(fontResolverInfo.FaceName);
            _cMapInfo = new CMapInfo(new OpenTypeDescriptor("", "", XFontStyle.Regular, _fontSource.Fontface,
                XPdfFontOptions.UnicodeDefault));
        }

        public void AddGlyphs(string text)
        {
            _cMapInfo.AddChars(text);
        }

        public byte[] Build()
        {
            var subset = _fontSource.Fontface.CreateFontSubSet(_cMapInfo.GlyphIndices, false);
            return subset.FontSource.Bytes;
        }
    }
}