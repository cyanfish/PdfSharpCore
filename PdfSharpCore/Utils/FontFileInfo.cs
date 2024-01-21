using System.Linq;
using PdfSharpCore.Drawing;
using SixLabors.Fonts;

namespace PdfSharpCore.Utils
{
    public readonly struct FontFileInfo
    {
        private FontFileInfo(string path, int collectionNumber, FontDescription fontDescription)
        {
            this.Path = path;
            this.CollectionNumber = collectionNumber;
            this.FontDescription = fontDescription;
        }

        public string Path { get; }

        public int CollectionNumber { get; }

        public FontDescription FontDescription { get; }

        public string FamilyName => this.FontDescription.FontFamilyInvariantCulture;


        public XFontStyle GuessFontStyle()
        {
            switch (this.FontDescription.Style)
            {
                case FontStyle.Bold:
                    return XFontStyle.Bold;
                case FontStyle.Italic:
                    return XFontStyle.Italic;
                case FontStyle.BoldItalic:
                    return XFontStyle.BoldItalic;
                default:
                    return XFontStyle.Regular;
            }
        }

        public static FontFileInfo Load(string path)
        {
            FontDescription fontDescription = FontDescription.LoadDescription(path);
            return new FontFileInfo(path, 0, fontDescription);
        }

        public static FontFileInfo[] LoadCollection(string path)
        {
            FontDescription[] fontDescriptions = FontDescription.LoadFontCollectionDescriptions(path);
            int i = 0;
            return fontDescriptions.Select(desc => new FontFileInfo(path, i++, desc)).ToArray();
        }
    }
}