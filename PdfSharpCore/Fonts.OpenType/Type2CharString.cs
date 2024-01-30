using System;
using System.Collections.Generic;
using System.IO;

namespace PdfSharpCore.Fonts.OpenType
{
    internal class Type2CharString
    {
        public const int OpLocalSubr = 10;
        public const int OpGlobalSubr = 29;

        public static Type2CharString Parse(byte[] bytes)
        {
            List<Token> tokens = new List<Token>();
            int stemHintCount = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] <= 31 && bytes[i] != 28) // Operator
                {
                    int op = bytes[i];
                    if (op == 12)
                    {
                        i++;
                        op = op * 100 + bytes[i];
                    }
                    Token token = new Token { Type = Token.Operator, Value = op };
                    if (op == 1 || op == 3 || op == 18 || op == 23)
                    {
                        stemHintCount++;
                    }
                    if (op == 20 || op == 19)
                    {
                        // TODO: Shouldn't be max. Is parsing of subroutines contextual based on the number of stems in the caller?
                        int maskBytes = Math.Max(1, (stemHintCount + 7) / 8);
                        token.Mask = new byte[maskBytes];
                        Array.Copy(bytes, i + 1, token.Mask, 0, maskBytes);
                        i += maskBytes;
                    }
                    tokens.Add(token);
                }
                else if (bytes[i] == 255)
                {
                    tokens.Add(new Token { Type = Token.Fixed, Value = CffPrimitives.ParseFixed(bytes, ref i) });
                }
                else if (bytes[i] == 28 || bytes[i] >= 32)
                {
                    tokens.Add(new Token { Type = Token.Integer, Value = CffPrimitives.ParseVarInt(bytes, ref i) });
                }
            }
            return new Type2CharString(tokens);
        }
        
        private Type2CharString(List<Token> tokens)
        {
            Tokens = tokens;
        }

        public List<Token> Tokens { get; }

        public byte[] Serialize()
        {
            MemoryStream stream = new MemoryStream();
            foreach (var token in Tokens)
            {
                int value = token.Value;
                if (token.Type == Token.Operator)
                {
                    CffPrimitives.WriteOperator(stream, value);
                }
                else if (token.Type == Token.Integer)
                {
                    CffPrimitives.WriteVarInt(stream, value);
                }
                else if (token.Type == Token.Fixed)
                {
                    CffPrimitives.WriteFixed(stream, value);
                }
                if (token.Mask != null)
                {
                    stream.Write(token.Mask, 0, token.Mask.Length);
                }
            }
            return stream.ToArray();
        }

        public class Token
        {
            public const int Operator = 0;
            public const int Integer = 1;
            public const int Fixed = 2;
            
            public int Type;
            public int Value;
            public byte[] Mask;

            public bool IsOperator(int op)
            {
                return Type == Operator && Value == op;
            }

            public override string ToString()
            {
                return $"{Type} {Value} {Mask.Length}";
            }
        }
    }
}