using System.Linq;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using PdfSharpCore.Internal;
using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;


namespace PdfSharpCore.Utils
{
    public class FontResolver
        : IFontResolver
    {
        public string DefaultFontName => "Arial";

        public static readonly Dictionary<string, FontFamilyModel> InstalledFonts = new Dictionary<string, FontFamilyModel>();

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
                    fontPaths.AddRange(Directory.GetFiles(fontDir, "*.ttc", SearchOption.AllDirectories));
                    fontPaths.AddRange(Directory.GetFiles(fontDir, "*.ttf", SearchOption.AllDirectories));
                }
            }
            SSupportedFonts = fontPaths.ToArray();
            SetupFontsFiles(SSupportedFonts);
        }


        public static void SetupFontsFiles(string[] sSupportedFonts)
        {
            List<FontFileInfo> tempFontInfoList = new List<FontFileInfo>();
            foreach (string fontPathFile in sSupportedFonts)
            {
                try
                {
                    Debug.WriteLine(fontPathFile);
                    if (fontPathFile.EndsWith(".ttc"))
                    {
                        tempFontInfoList.AddRange(FontFileInfo.LoadCollection(fontPathFile));
                    }
                    else
                    {
                        tempFontInfoList.Add(FontFileInfo.Load(fontPathFile));
                    }
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
                font.FontFiles.Add(XFontStyle.Regular, fontList.First());
            else
            {
                foreach (FontFileInfo info in fontList)
                {
                    XFontStyle style = info.GuessFontStyle();
                    if (!font.FontFiles.ContainsKey(style))
                        font.FontFiles.Add(style, info);
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
                    if (family.FontFiles.TryGetValue(XFontStyle.BoldItalic, out FontFileInfo info))
                        return new FontResolverInfo(Path.GetFileName(info.Path), info.CollectionNumber);
                }
                else if (isBold)
                {
                    if (family.FontFiles.TryGetValue(XFontStyle.Bold, out FontFileInfo info))
                        return new FontResolverInfo(Path.GetFileName(info.Path), info.CollectionNumber);
                }
                else if (isItalic)
                {
                    if (family.FontFiles.TryGetValue(XFontStyle.Italic, out FontFileInfo info))
                        return new FontResolverInfo(Path.GetFileName(info.Path), info.CollectionNumber);
                }
                else
                {
                    if (family.FontFiles.TryGetValue(XFontStyle.Regular, out FontFileInfo info))
                        return new FontResolverInfo(Path.GetFileName(info.Path), info.CollectionNumber);
                }

                FontFileInfo firstInfo = family.FontFiles.First().Value;
                return new FontResolverInfo(Path.GetFileName(firstInfo.Path), firstInfo.CollectionNumber);
            }

            if (NullIfFontNotFound)
                return null;

            FontFileInfo firstInstalledInfo = InstalledFonts.First().Value.FontFiles.First().Value;
            return new FontResolverInfo(Path.GetFileName(firstInstalledInfo.Path), firstInstalledInfo.CollectionNumber);
        }
    }
}