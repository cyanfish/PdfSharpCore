using System.IO;

namespace PdfSharpCore.Drawing
{
    public interface IImageSource
    {
        int Width { get; }
        int Height { get; }
        string Name { get; }
        void SaveAsJpeg(MemoryStream ms);
        XImageFormat ImageFormat { get; }
        void SaveAsPdfBitmap(MemoryStream ms);
        void SaveAsPdfIndexedBitmap(MemoryStream ms);
    }
}