using System.IO;

namespace PdfSharpCore.Drawing
{
    public interface IImageSource
    {
        int Width { get; }
        int Height { get; }
        string Name { get; }
        bool Transparent { get; }
        void SaveAsJpeg(MemoryStream ms);
        void SaveAsPdfBitmap(MemoryStream ms);
    }
}