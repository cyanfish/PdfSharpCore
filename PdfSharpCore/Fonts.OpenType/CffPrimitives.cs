using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace PdfSharpCore.Fonts.OpenType
{
    internal static class CffPrimitives
    {
        public static int ParseVarInt(byte[] bytes, ref int i)
        {
            byte b0 = bytes[i];
            if (b0 >= 32 && b0 <= 246)
            {
                return b0 - 139;
            }
            if (b0 >= 247 && b0 <= 250)
            {
                byte b1 = bytes[++i];
                return (b0 - 247) * 256 + b1 + 108;
            }
            if (b0 >= 251 && b0 <= 254)
            {
                byte b1 = bytes[++i];
                return -(b0 - 251) * 256 - b1 - 108;
            }
            if (b0 == 28)
            {
                byte b1 = bytes[++i];
                byte b2 = bytes[++i];
                // Cast to short to make sure if the top bit is set it's interpreted as a negative sign
                return (short) ((b1 << 8) | b2);
            }
            if (b0 == 29)
            {
                byte b1 = bytes[++i];
                byte b2 = bytes[++i];
                byte b3 = bytes[++i];
                byte b4 = bytes[++i];
                return (b1 << 24) | (b2 << 16) | (b3 << 8) | b4;
            }

            throw new InvalidOperationException("Could not parse varint");
        }

        public static int ParseFixed(byte[] bytes, ref int i)
        {
            return (bytes[++i] << 24) | (bytes[++i] << 16) | (bytes[++i] << 8) | bytes[++i];
        }

        public static double ParseRealNumber(byte[] bytes, ref int i)
        {
            byte b0 = bytes[i];
            if (b0 == 30)
            {
                StringBuilder real = new StringBuilder();
                int n1, n2;
                do
                {
                    byte b = bytes[++i];
                    n1 = (b >> 4) & 0xF;
                    n2 = b & 0xF;
                    if (n1 <= 9) real.Append(n1);
                    if (n1 == 0xa) real.Append('.');
                    if (n1 == 0xb) real.Append('E');
                    if (n1 == 0xc) real.Append("E-");
                    if (n1 == 0xe) real.Append("-");
                    if (n2 <= 9) real.Append(n2);
                    if (n2 == 0xa) real.Append('.');
                    if (n2 == 0xb) real.Append('E');
                    if (n2 == 0xc) real.Append("E-");
                    if (n2 == 0xe) real.Append("-");
                } while (n1 != 0xF && n2 != 0xF);
                return double.Parse(real.ToString(), CultureInfo.InvariantCulture);
            }
            
            throw new InvalidOperationException("Could not parse real number");
        }

        public static int ParseOperator(byte[] bytes, ref int i)
        {
            int op = bytes[i];
            if (op == 12)
            {
                i++;
                op = op * 100 + bytes[i];
            }
            return op;
        }

        public static void WriteVarInt(MemoryStream stream, int value)
        {
            if (value >= -107 && value <= 107)
            {
                stream.WriteByte((byte)(value + 139));
            }
            else if (value >= 108 && value <= 1131)
            {
                stream.WriteByte((byte) ((value - 108) / 256 + 247));
                stream.WriteByte((byte) ((value - 108) % 256));
            }
            else if (value >= -1131 && value <= -108)
            {
                stream.WriteByte((byte) ((-108 - value) / 256 + 251));
                stream.WriteByte((byte) ((-108 - value) % 256));
            }
            else if (value >= -32768 && value <= 32767)
            {
                stream.WriteByte(28);
                stream.WriteByte((byte) (value >> 8));
                stream.WriteByte((byte) (value & 0xFF));
            }
            else
            {
                WriteFullInt(stream, value);
            }
        }

        public static void WriteFullInt(MemoryStream stream, int value)
        {
            stream.WriteByte(29);
            stream.WriteByte((byte) ((value >> 24) & 0xFF));
            stream.WriteByte((byte) ((value >> 16) & 0xFF));
            stream.WriteByte((byte) ((value >> 8) & 0xFF));
            stream.WriteByte((byte) (value & 0xFF));
        }

        public static void WriteReal(MemoryStream stream, double value)
        {
            string str = value.ToString(CultureInfo.InvariantCulture);
            stream.WriteByte(30);
            for (int i = 0; i < str.Length - 1; i += 2)
            {
                byte b = (byte) ((GetNibble(str[i]) << 4) | GetNibble(str[i + 1]));
                stream.WriteByte(b);
            }
            if (str.Length % 2 == 1)
            {
                stream.WriteByte((byte) ((GetNibble(str[str.Length - 1]) << 4) | 0xF));
            }
            else
            {
                stream.WriteByte(0xFF);
            }
        }

        private static byte GetNibble(char c)
        {
            // TODO: Handle "E-" = 0xc case?
            if (c >= '0' && c <= '9') return (byte) (c - '0');
            if (c == '.') return 0xa;
            if (c == 'e' || c == 'E') return 0xb;
            if (c == '-') return 0xe;
            throw new InvalidOperationException();
        }

        public static void WriteOperator(MemoryStream stream, int op)
        {
            if (op >= 1200)
            {
                stream.WriteByte(12);
                stream.WriteByte((byte) (op - 1200));
            }
            else
            {
                stream.WriteByte((byte) op);
            }
        }

        public static void WriteFixed(MemoryStream stream, int value)
        {
            stream.WriteByte(255);
            stream.WriteByte((byte) ((value >> 24) & 0xFF));
            stream.WriteByte((byte) ((value >> 16) & 0xFF));
            stream.WriteByte((byte) ((value >> 8) & 0xFF));
            stream.WriteByte((byte) (value & 0xFF));
        }
    }
}