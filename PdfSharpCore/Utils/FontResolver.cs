using System.Linq;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using PdfSharpCore.Internal;
using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using SixLabors.Fonts;


namespace PdfSharpCore.Utils
{
    public class FontResolver
        : IFontResolver
    {
        public string DefaultFontName => "Arial";

        private static readonly Dictionary<string, FontFamilyModel> InstalledFonts = new Dictionary<string, FontFamilyModel>();

        private static readonly string[] SSupportedFonts;

        public FontResolver()
        {
        }

        static FontResolver()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                SSupportedFonts = LinuxSystemFontResolver.Resolve();
                SetupFontsFiles(SSupportedFonts);
                return;
            }

            string[] fontDirs;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                fontDirs = new[]
                {
                    "/Library/Fonts/",
                    "/System/Library/Fonts/"
                };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                fontDirs = new[]
                {
                    System.Environment.ExpandEnvironmentVariables(@"%SystemRoot%\Fonts"),
                    System.Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\Windows\Fonts")
                };
            }
            else
            {
                throw new System.NotImplementedException(
                    "FontResolver not implemented for this platform (PdfSharpCore.Utils.FontResolver.cs).");
            }

            var fontPaths = new List<string>();
            foreach (var fontDir in fontDirs)
            {
                if (Directory.Exists(fontDir))
                {
                    fontPaths.AddRange(Directory.GetFiles(fontDir, "*.ttf", SearchOption.AllDirectories));
                }
            }
            SSupportedFonts = fontPaths.ToArray();
            SetupFontsFiles(SSupportedFonts);
        }


        private readonly struct FontFileInfo
        {
            private FontFileInfo(string path, FontDescription fontDescription)
            {
                this.Path = path;
                this.FontDescription = fontDescription;
            }

            public string Path { get; }

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
                return new FontFileInfo(path, fontDescription);
            }
        }


        public static void SetupFontsFiles(string[] sSupportedFonts)
        {
            List<FontFileInfo> tempFontInfoList = new List<FontFileInfo>();
            foreach (string fontPathFile in sSupportedFonts)
            {
                try
                {
                    FontFileInfo fontInfo = FontFileInfo.Load(fontPathFile);
                    Debug.WriteLine(fontPathFile);
                    tempFontInfoList.Add(fontInfo);
                }
                catch (System.Exception e)
                {
                    System.Console.Error.WriteLine(e);
                }
            }

            // Deserialize all font families
            foreach (IGrouping<string, FontFileInfo> familyGroup in tempFontInfoList.GroupBy(info => info.FamilyName))
                try
                {
                    string familyName = familyGroup.Key;
                    FontFamilyModel family = DeserializeFontFamily(familyName, familyGroup);
                    InstalledFonts.Add(familyName.ToLower(), family);
                }
                catch (System.Exception e)
                {
                    System.Console.Error.WriteLine(e);
                }
        }


        [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
        private static FontFamilyModel DeserializeFontFamily(string fontFamilyName, IEnumerable<FontFileInfo> fontList)
        {
            FontFamilyModel font = new FontFamilyModel { Name = fontFamilyName };

            // there is only one font
            if (fontList.Count() == 1)
                font.FontFiles.Add(XFontStyle.Regular, fontList.First().Path);
            else
            {
                foreach (FontFileInfo info in fontList)
                {
                    XFontStyle style = info.GuessFontStyle();
                    if (!font.FontFiles.ContainsKey(style))
                        font.FontFiles.Add(style, info.Path);
                }
            }

            return font;
        }

        public virtual byte[] GetFont(string faceFileName)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                string ttfPathFile = "";
                try
                {
                    ttfPathFile = SSupportedFonts.ToList().First(x => x.ToLower().Contains(
                        Path.GetFileName(faceFileName).ToLower())
                    );

                    using (Stream ttf = File.OpenRead(ttfPathFile))
                    {
                        ttf.CopyTo(ms);
                        ms.Position = 0;
                        return ms.ToArray();
                    }
                }
                catch (System.Exception e)
                {
                    System.Console.WriteLine(e);
                    throw new System.Exception("No Font File Found - " + faceFileName + " - " + ttfPathFile);
                }
            }
        }

        public bool NullIfFontNotFound { get; set; } = false;

        public virtual FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            if (InstalledFonts.Count == 0)
                throw new FileNotFoundException("No Fonts installed on this device!");

            if (InstalledFonts.TryGetValue(familyName.ToLower(), out FontFamilyModel family))
            {
                if (isBold && isItalic)
                {
                    if (family.FontFiles.TryGetValue(XFontStyle.BoldItalic, out string boldItalicFile))
                        return new FontResolverInfo(Path.GetFileName(boldItalicFile));
                }
                else if (isBold)
                {
                    if (family.FontFiles.TryGetValue(XFontStyle.Bold, out string boldFile))
                        return new FontResolverInfo(Path.GetFileName(boldFile));
                }
                else if (isItalic)
                {
                    if (family.FontFiles.TryGetValue(XFontStyle.Italic, out string italicFile))
                        return new FontResolverInfo(Path.GetFileName(italicFile));
                }

                if (family.FontFiles.TryGetValue(XFontStyle.Regular, out string regularFile))
                    return new FontResolverInfo(Path.GetFileName(regularFile));

                return new FontResolverInfo(Path.GetFileName(family.FontFiles.First().Value));
            }

            if (NullIfFontNotFound)
                return null;

            string ttfFile = InstalledFonts.First().Value.FontFiles.First().Value;
            return new FontResolverInfo(Path.GetFileName(ttfFile));
        }
    }
}