using System.Collections.Generic;
using PdfSharpCore.Drawing;
using PdfSharpCore.Utils;

namespace PdfSharpCore.Internal
{
    public class FontFamilyModel
    {
        public string Name { get; set; }

        public Dictionary<XFontStyle, FontFileInfo> FontFiles = new Dictionary<XFontStyle, FontFileInfo>();

        public bool IsStyleAvailable(XFontStyle fontStyle)
        {
            return FontFiles.ContainsKey(fontStyle);
        }
    }
}
