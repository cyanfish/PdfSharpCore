using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PdfSharpCore.Fonts.OpenType
{
    internal class CompactFontFormatTable : OpenTypeFontTable
    {
        public const string Tag = TableTagNames.Cff;

        private const int OpCharset = 15;
        private const int OpEncoding = 16;
        private const int OpCharStrings = 17;
        private const int OpPrivate = 18;
        private const int OpSubrs = 19;
        private const int OpFdArray = 1236;
        private const int OpFdSelect = 1237;
        
        private byte[] _encodedTable;
        
        private byte _majorVersion;
        private byte _minorVersion;
        private byte _hdrSize;
        private byte _offSize;
        
        private byte[][] _nameIndex;
        private byte[][] _stringIndex;
        private byte[][] _globalSubrIndex;
        private Dictionary<int, object> _topDict;
        private byte[][] _charStringsIndex;
        private Dictionary<int,object>[] _fontDictIndex;
        private Dictionary<int,object>[] _privateDicts;
        private byte[][][] _localSubrIndexes;
        private ushort[] _charsets; // maps glyph id to string id in _charStringsIndex
        private byte[] _fdSelect; // maps glyph id to entry in fontDictIndex + privateDicts + localSubrIndexes

        public CompactFontFormatTable()
            : base(null, Tag)
        {
            this.DirectoryEntry.Tag = Tag;
        }

        public CompactFontFormatTable(OpenTypeFontface fontData)
            : base(fontData, Tag)
        {
            this.DirectoryEntry.Tag = Tag;
            Read();
        }

        public CompactFontFormatTable CreateSubset(Dictionary<int, object> glyphs)
        {
            // Calculate references

            HashSet<int> referencedGlyphs = new HashSet<int>(glyphs.Keys);
            // Always reference glyph 0 (.notdef)
            referencedGlyphs.Add(0);
            
            HashSet<int> referencedCharStrings = new HashSet<int>();
            HashSet<int> referencedFds = new HashSet<int>();
            HashSet<int>[] referencedLocalSubrs = new HashSet<int>[_localSubrIndexes.Length];
            for (int i = 0; i < _localSubrIndexes.Length; i++)
            {
                referencedLocalSubrs[i] = new HashSet<int>();
            }
            HashSet<int> referencedGlobalSubrs = new HashSet<int>();
            foreach (var glyphId in referencedGlyphs)
            {
                int charString = _charsets[glyphId];
                int fd = _fdSelect[glyphId];
                referencedCharStrings.Add(charString);
                referencedFds.Add(fd);
                UpdateSubrReferences(_charStringsIndex[charString], fd, referencedLocalSubrs[fd], referencedGlobalSubrs);
            }

            // Map to new ids
            
            Dictionary<int, int> fdMap = new Dictionary<int, int>();
            foreach (var fd in referencedFds.OrderBy(fd => fd))
            {
                fdMap.Add(fd, fdMap.Count);
            }

            Dictionary<int, int>[] localSubrMap = new Dictionary<int, int>[_localSubrIndexes.Length];
            for (int i = 0; i < _localSubrIndexes.Length; i++)
            {
                localSubrMap[i] = new Dictionary<int, int>();
                foreach (var subr in referencedLocalSubrs[i].OrderBy(subr => subr))
                {
                    localSubrMap[i].Add(subr, localSubrMap[i].Count);
                }
            }
            
            Dictionary<int, int> globalSubrMap = new Dictionary<int, int>();
            foreach (var subr in referencedGlobalSubrs.OrderBy(subr => subr))
            {
                globalSubrMap.Add(subr, globalSubrMap.Count);
            }

            // Create subsets
            
            byte[][] charStringsSubset = new byte[_charStringsIndex.Length][];
            for (int i = 0; i < _charStringsIndex.Length; i++)
            {
                if (referencedCharStrings.Contains(i))
                {
                    int fd = _fdSelect[i];
                    charStringsSubset[i] = RemapSubrs(_charStringsIndex[i], fd, localSubrMap[fd], globalSubrMap);
                }
                else
                {
                    charStringsSubset[i] = Array.Empty<byte>();
                }
            }

            byte[] fdSelectSubset = new byte[_fdSelect.Length];
            int lastUsedFd = 0;
            for (int i = 0; i < _fdSelect.Length; i++)
            {
                if (referencedFds.Contains(_fdSelect[i]))
                {
                    lastUsedFd = fdMap[_fdSelect[i]];
                }
                fdSelectSubset[i] = (byte) lastUsedFd;
            }

            Dictionary<int, object>[] fontDictIndexSubset = new Dictionary<int, object>[fdMap.Count];
            for (int i = 0, j = 0; i < _fontDictIndex.Length; i++)
            {
                if (referencedFds.Contains(i))
                {
                    fontDictIndexSubset[j++] = _fontDictIndex[i];
                }
            }

            Dictionary<int, object>[] privateDictsSubset = new Dictionary<int, object>[fdMap.Count];
            for (int i = 0, j = 0; i < _privateDicts.Length; i++)
            {
                if (referencedFds.Contains(i))
                {
                    privateDictsSubset[j++] = _privateDicts[i];
                }
            }

            byte[][][] localSubrsSubset = new byte[fdMap.Count][][];
            for (int i = 0, j = 0; i < _localSubrIndexes.Length; i++)
            {
                if (referencedFds.Contains(i))
                {
                    byte[][] originalIndex = _localSubrIndexes[i];
                    byte[][] newIndex = new byte[referencedLocalSubrs[i].Count][];
                    foreach (var kvp in localSubrMap[i])
                    {
                        newIndex[kvp.Value] = RemapSubrs(originalIndex[kvp.Key], i, localSubrMap[i], globalSubrMap);
                    }
                    localSubrsSubset[j++] = newIndex;
                }
            }

            byte[][] globalSubrsSubset = new byte[referencedGlobalSubrs.Count][];
            foreach (var kvp in globalSubrMap)
            {
                globalSubrsSubset[kvp.Value] = RemapSubrs(_globalSubrIndex[kvp.Key], 0, null, globalSubrMap);
            }

            // Copy to new table
            
            CompactFontFormatTable subset = new CompactFontFormatTable
            {
                _majorVersion = _majorVersion,
                _minorVersion = _minorVersion,
                _hdrSize = _hdrSize,
                _offSize = _offSize,
                _nameIndex = _nameIndex,
                _stringIndex = _stringIndex,
                _globalSubrIndex = globalSubrsSubset,
                _topDict = _topDict,
                _charStringsIndex = charStringsSubset,
                _fontDictIndex = fontDictIndexSubset,
                _privateDicts = privateDictsSubset,
                _localSubrIndexes = localSubrsSubset,
                _charsets = _charsets,
                _fdSelect = fdSelectSubset
            };
            return subset;
        }

        private byte[] RemapSubrs(byte[] charString, int fd, Dictionary<int,int> localMap, Dictionary<int,int> globalMap)
        {
            int localBias = GetBias(_localSubrIndexes[fd]?.Length ?? 0);
            int globalBias = GetBias(_globalSubrIndex.Length);
            int newLocalBias = GetBias(localMap?.Count ?? 0);
            int newGlobalBias = GetBias(globalMap.Count);
            Type2CharString cs = Type2CharString.Parse(charString);
            for (int i = 1; i < cs.Tokens.Count; i++)
            {
                if (cs.Tokens[i].IsOperator(Type2CharString.OpLocalSubr))
                {
                    int subr = cs.Tokens[i - 1].Value + localBias;
                    cs.Tokens[i - 1].Value = localMap[subr] - newLocalBias;
                }
                if (cs.Tokens[i].IsOperator(Type2CharString.OpGlobalSubr))
                {
                    int subr = cs.Tokens[i - 1].Value + globalBias;
                    cs.Tokens[i - 1].Value = globalMap[subr] - newGlobalBias;
                }
            }
            return cs.Serialize();
        }

        private void UpdateSubrReferences(byte[] charString, int fd, HashSet<int> local, HashSet<int> global)
        {
            int localBias = GetBias(_localSubrIndexes[fd]?.Length ?? 0);
            int globalBias = GetBias(_globalSubrIndex.Length);
            Type2CharString cs = Type2CharString.Parse(charString);
            for (int i = 1; i < cs.Tokens.Count; i++)
            {
                if (cs.Tokens[i].IsOperator(Type2CharString.OpLocalSubr))
                {
                    int subr = cs.Tokens[i - 1].Value + localBias;
                    if (!local.Contains(subr))
                    {
                        local.Add(subr);
                        UpdateSubrReferences(_localSubrIndexes[fd][subr], fd, local, global);
                    }
                }
                if (cs.Tokens[i].IsOperator(Type2CharString.OpGlobalSubr))
                {
                    int subr = cs.Tokens[i - 1].Value + globalBias;
                    if (!global.Contains(subr))
                    {
                        global.Add(subr);
                        UpdateSubrReferences(_globalSubrIndex[subr], fd, local, global);
                    }
                }
            }
        }

        private int GetBias(int nSubrs)
        {
            return nSubrs < 1240 ? 107 : nSubrs < 33900 ? 1131 : 32768;
        }

        public void Read()
        {
            try
            {
                int basePos = _fontData.Position;
                _majorVersion = _fontData.ReadByte();
                _minorVersion = _fontData.ReadByte();
                _hdrSize = _fontData.ReadByte();
                _offSize = _fontData.ReadByte();
                _fontData.Position = basePos + _hdrSize;
                
                _nameIndex = ReadIndex();
                byte[][] topDictIndex = ReadIndex();
                _stringIndex = ReadIndex();
                _globalSubrIndex = ReadIndex();

                if (_nameIndex.Length != 1 || topDictIndex.Length != 1)
                {
                    throw new InvalidOperationException("CFF table should have exactly one font");
                }

                _topDict = ParseDict(topDictIndex[0], 0, topDictIndex[0].Length);
                
                if (_topDict.ContainsKey(OpEncoding))
                {
                    _fontData.Position = basePos + (int) _topDict[OpEncoding];
                    ReadEncodings();
                }
                if (_topDict.ContainsKey(OpCharStrings))
                {
                    _fontData.Position = basePos + (int) _topDict[OpCharStrings];
                    _charStringsIndex = ReadIndex();
                }
                if (_topDict.ContainsKey(OpCharset))
                {
                    int operand = (int) _topDict[OpCharset];
                    if (operand == 0 || operand == 1 || operand == 2)
                    {
                        // Predefined charsets, not to be used with CID fonts
                        throw new NotImplementedException();
                    }
                    _fontData.Position = basePos + operand;
                    ReadCharsets();
                }
                if (_topDict.ContainsKey(OpFdSelect))
                {
                    _fontData.Position = basePos + (int) _topDict[OpFdSelect];
                    ReadFdSelect();
                }
                if (_topDict.ContainsKey(OpFdArray))
                {
                    _fontData.Position = basePos + (int) _topDict[OpFdArray];
                    _fontDictIndex = ReadIndex().Select(index => ParseDict(index, 0, index.Length)).ToArray();
                    _privateDicts = new Dictionary<int, object>[_fontDictIndex.Length];
                    _localSubrIndexes = new byte[_fontDictIndex.Length][][];
                    for (int i = 0; i < _fontDictIndex.Length; i++)
                    {
                        if (_fontDictIndex[i].ContainsKey(OpPrivate))
                        {
                            object[] operands = (object[]) _fontDictIndex[i][OpPrivate];
                            int privateDictBasePos = basePos + (int) operands[1];
                            _fontData.Position = privateDictBasePos;
                            _privateDicts[i] = ParseDict(_fontData.FontSource.Bytes, _fontData.Position, (int) operands[0]);
                            _fontData.Position += (int) operands[0];
                            if (_privateDicts[i].ContainsKey(OpSubrs))
                            {
                                _fontData.Position = privateDictBasePos + (int) _privateDicts[i][OpSubrs];
                                _localSubrIndexes[i] = ReadIndex();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(PSSR.ErrorReadingFontData, ex);
            }
        }

        private void ReadFdSelect()
        {
            int format = _fontData.ReadByte();
            if (format == 0)
            {
                throw new NotImplementedException();
            }
            else if (format == 3)
            {
                int rangeCount = _fontData.ReadUShort();
                _fdSelect = new byte[65536];
                int rangeStart = _fontData.ReadUShort();
                while (rangeStart < _charStringsIndex.Length)
                {
                    byte fd = _fontData.ReadByte();
                    int nextRangeStart = _fontData.ReadUShort();
                    for (int i = rangeStart; i < nextRangeStart; i++)
                    {
                        _fdSelect[i] = fd;
                    }
                    rangeStart = nextRangeStart;
                }
            }
            else
            {
                throw new InvalidOperationException("Unexpected format for fdselect");
            }
        }
        
        private byte[] SerializeFdSelect()
        {
            MemoryStream stream = new MemoryStream();
            
            stream.WriteByte(3);
            
            // Allocate space for range count
            ushort rangeCount = 0;
            stream.WriteByte(0); 
            stream.WriteByte(0);
            
            // First range start (always 0)
            stream.WriteByte(0);
            stream.WriteByte(0);
            for (int i = 0; i < _charsets.Length; i++)
            {
                while (i + 1 < _charsets.Length && _fdSelect[i + 1] == _fdSelect[i])
                {
                    i++;
                }
                stream.WriteByte(_fdSelect[i]);
                int nextRangeStart = i + 1;
                stream.WriteByte((byte) ((nextRangeStart >> 8) & 0xFF));
                stream.WriteByte((byte) (nextRangeStart & 0xFF));
                rangeCount++;
            }
            
            // Write range count
            stream.Position = 1;
            stream.WriteByte((byte) ((rangeCount >> 8) & 0xFF));
            stream.WriteByte((byte) (rangeCount & 0xFF));
            
            return stream.ToArray();
        }

        private void ReadCharsets()
        {
            int format = _fontData.ReadByte();
            if (format == 0)
            {
                throw new NotImplementedException();
            }
            else if (format == 1)
            {
                throw new NotImplementedException();
            }
            else if (format == 2)
            {
                int glyphsCount = _charStringsIndex.Length;
                _charsets = new ushort[glyphsCount];
                ushort glyphsFilled = 1;
                while (glyphsFilled < glyphsCount)
                {
                    ushort first = _fontData.ReadUShort();
                    ushort leftInRange = _fontData.ReadUShort();
                    for (int i = first; i <= first + leftInRange; i++)
                    {
                        _charsets[glyphsFilled++] = (ushort) i;
                    }
                }
            }
            else
            {
                throw new InvalidOperationException("Unexpected format for charsets");
            }
        }

        private byte[] SerializeCharsets()
        {
            MemoryStream stream = new MemoryStream();

            stream.WriteByte(2);
            for (int i = 1; i < _charsets.Length; i++)
            {
                int rangeStart = i;
                int first = _charsets[i];
                while (i + 1 < _charsets.Length && _charsets[i + 1] == _charsets[i] + 1)
                {
                    i++;
                }
                int leftInRange = i - rangeStart;
                stream.WriteByte((byte) ((first >> 8) & 0xFF));
                stream.WriteByte((byte) (first & 0xFF));
                stream.WriteByte((byte) ((leftInRange >> 8) & 0xFF));
                stream.WriteByte((byte) (leftInRange & 0xFF));
            }

            return stream.ToArray();
        }

        private void ReadEncodings()
        {
            throw new NotImplementedException();
            // int format = _fontData.ReadByte();
            // if (format == 0)
            // {
            //     int codeCount = _fontData.ReadByte();
            //     byte[] codes = new byte[codeCount];
            //     for (int i = 0; i < codeCount; i++)
            //     {
            //         codes[i] = _fontData.ReadByte();
            //     }
            // }
            // else if (format == 1)
            // {
            //     int rangeCount = _fontData.ReadByte();
            //     
            // }
            // else
            // {
            //     throw new InvalidOperationException("Unexpected format for encodings");
            // }
        }

        private Dictionary<int,object> ParseDict(byte[] bytes, int start, int count)
        {
            Dictionary<int, object> dict = new Dictionary<int, object>();
            List<object> operands = new List<object>();
            for (int i = start; i < start + count; i++)
            {
                if (bytes[i] <= 21)
                {
                    int op = CffPrimitives.ParseOperator(bytes, ref i);
                    if (operands.Count == 0)
                    {
                        dict[op] = null;
                    }
                    else if (operands.Count == 1)
                    {
                        dict[op] = operands[0];
                    }
                    else
                    {
                        dict[op] = operands.ToArray();
                    }
                    operands.Clear();
                }
                else if ((bytes[i] >= 32 && bytes[i] <= 254) || bytes[i] == 28 || bytes[i] == 29)
                {
                    operands.Add(CffPrimitives.ParseVarInt(bytes, ref i));
                }
                else if (bytes[i] == 30)
                {
                    operands.Add(CffPrimitives.ParseRealNumber(bytes, ref i));
                }
            }
            return dict;
        }

        private byte[][] ReadIndex()
        {
            int count = _fontData.ReadUShort();
            if (count == 0)
            {
                return new byte[0][];
            }
            byte offSize = _fontData.ReadByte();
            int[] offsets = new int[count + 1];
            for (int i = 0; i < count + 1; i++)
            {
                offsets[i] = ReadOffset(offSize);
            }
            int startPos = _fontData.Position - 1;
            byte[][] data = new byte[count][];
            for (int i = 0; i < count; i++)
            {
                _fontData.Position = startPos + offsets[i];
                data[i] = _fontData.ReadBytes(offsets[i + 1] - offsets[i]);
            }
            return data;
        }

        private int ReadOffset(byte offSize)
        {
            int offset = 0;
            for (int i = 0; i < offSize; i++)
            {
                offset = (offset << 8) | _fontData.ReadByte();
            }
            return offset;
        }

        public override void PrepareForCompilation()
        {
            base.PrepareForCompilation();

            _encodedTable = Encode();
            DirectoryEntry.Length = _encodedTable.Length;
            DirectoryEntry.CheckSum = CalcChecksum(_encodedTable);
        }

        private byte[] Encode()
        {
            var stream = new MemoryStream();
            var writer = new BinaryWriter(stream);
            
            writer.Write(_majorVersion);
            writer.Write(_minorVersion);
            writer.Write((byte) 4);
            writer.Write(_offSize);
            
            byte[] nameIndexBytes = SerializeIndex(_nameIndex);
            byte[] stringIndexBytes = SerializeIndex(_stringIndex);
            byte[] globalSubrBytes = SerializeIndex(_globalSubrIndex);

            byte[] charStringBytes = _charStringsIndex != null ? SerializeIndex(_charStringsIndex) : null;
            byte[] charsetsBytes = _charsets != null ? SerializeCharsets() : null;
            byte[] fdSelectBytes = _fdSelect != null ? SerializeFdSelect() : null;

            int topDictSize = SerializeIndex(new [] { SerializeDict(_topDict) }).Length;
            int startOffset = 4 + nameIndexBytes.Length + stringIndexBytes.Length + globalSubrBytes.Length + topDictSize;
            int offset = startOffset;

            var newTopDict = new Dictionary<int, object>(_topDict);
            if (charsetsBytes != null)
            {
                newTopDict[OpCharset] = offset;
                offset += charsetsBytes.Length;
            }
            if (fdSelectBytes != null)
            {
                newTopDict[OpFdSelect] = offset;
                offset += fdSelectBytes.Length;
            }
            if (charStringBytes != null)
            {
                newTopDict[OpCharStrings] = offset;
                offset += charStringBytes.Length;
            }
            if (_fontDictIndex != null)
            {
                newTopDict[OpFdArray] = offset;
            }
            
            int fdLen = _fontDictIndex?.Length ?? 0;
            Dictionary<int, object>[] newFontDictIndex = _fontDictIndex?.Select(dict => new Dictionary<int, object>(dict)).ToArray();
            if (newFontDictIndex != null)
            {
                offset += SerializeIndex(newFontDictIndex.Select(SerializeDict).ToArray()).Length;
            }
            int subrsOffset = offset + _privateDicts.Select(dict => SerializeDict(dict).Length).Sum();
            
            byte[][] localSubrsBytes = new byte[fdLen][];
            byte[][] privateDictBytes = new byte[fdLen][];
            for (int i = 0; i < fdLen; i++)
            {
                if (_privateDicts[i] == null) continue;
                if (_localSubrIndexes[i] == null)
                {
                    privateDictBytes[i] = SerializeDict(_privateDicts[i]);
                }
                else
                {
                    localSubrsBytes[i] = SerializeIndex(_localSubrIndexes[i]);
                    Dictionary<int, object> newPrivateDict = new Dictionary<int, object>(_privateDicts[i]); 
                    newPrivateDict[OpSubrs] = subrsOffset - offset;
                    privateDictBytes[i] = SerializeDict(newPrivateDict);
                    subrsOffset += localSubrsBytes[i].Length;
                }
                if (newFontDictIndex != null)
                {
                    newFontDictIndex[i][OpPrivate] = new object[] { privateDictBytes[i].Length, offset };
                }
                offset += privateDictBytes[i].Length;
            }
            
            byte[] fdArrayBytes = newFontDictIndex == null ? null : SerializeIndex(newFontDictIndex.Select(SerializeDict).ToArray());

            var topDictIndexBytes = SerializeIndex(new [] { SerializeDict(newTopDict) });
            
            writer.Write(nameIndexBytes);
            writer.Write(topDictIndexBytes);
            writer.Write(stringIndexBytes);
            writer.Write(globalSubrBytes);
            
            if (charsetsBytes != null) writer.Write(charsetsBytes);
            if (fdSelectBytes != null) writer.Write(fdSelectBytes);
            if (charStringBytes != null) writer.Write(charStringBytes);
            if (fdArrayBytes != null)
            {
                writer.Write(fdArrayBytes);
            }
            for (int i = 0; i < fdLen; i++)
            {
                if (privateDictBytes[i] != null)
                {
                    writer.Write(privateDictBytes[i]);
                }
            }
            for (int i = 0; i < fdLen; i++)
            {
                if (localSubrsBytes[i] != null)
                {
                    writer.Write(localSubrsBytes[i]);
                }
            }
            
            writer.Flush();
            
            // Pad out to a multiple of 4 bytes
            int paddedLength = ((int) stream.Length + 3) & ~3;
            stream.Write(new byte[4], 0, paddedLength - (int) stream.Length);
            
            return stream.ToArray();
        }

        /// <summary>
        /// Converts the font into its binary representation.
        /// </summary>
        public override void Write(OpenTypeFontWriter writer)
        {
            writer.Write(_encodedTable);
        }

        private byte[] SerializeDict(Dictionary<int,object> dict)
        {
            MemoryStream stream = new MemoryStream();

            foreach (var kvp in dict)
            {
                WriteDictValue(stream, kvp.Value);
                if (kvp.Key >= 1200)
                {
                    stream.WriteByte(12);
                    stream.WriteByte((byte) (kvp.Key - 1200));
                }
                else
                {
                    stream.WriteByte((byte) kvp.Key);
                }
            }
            
            return stream.ToArray();
        }

        private void WriteDictValue(MemoryStream stream, object value)
        {
            if (value is int)
            {
                // TODO: WriteVarInt could save some space (~1kb), but we should avoid using it for offsets as that
                // makes the dictionary size non-deterministic which can invalidate the offset.
                CffPrimitives.WriteFullInt(stream, (int) value);
            }
            else if (value is double)
            {
                CffPrimitives.WriteReal(stream, (double) value);
            }
            else if (value is object[])
            {
                foreach (var element in (object[]) value)
                {
                    WriteDictValue(stream, element);
                }
            }
            else
            {
                throw new InvalidOperationException("Unexpected dict type");
            }
        }

        private byte[] SerializeIndex(byte[][] index)
        {
            if (index.Length == 0)
            {
                return new byte[2];
            }
            
            int totalBytes = index.Sum(entry => entry.Length);
            byte offSize = (byte) (totalBytes > 0xFFFFFF ? 4 : totalBytes > 0xFFFF ? 3 : totalBytes > 0xFF ? 2 : 1);

            int valuesStart = 3 + (index.Length + 1) * offSize;
            byte[] data = new byte[valuesStart + totalBytes];
            data[0] = (byte) ((index.Length >> 8) & 0xFF);
            data[1] = (byte) (index.Length & 0xFF);
            data[2] = offSize;
            int offset = 0;
            for (int i = 0; i < index.Length; i++)
            {
                WriteOffset(data, 3 + offSize * i, offSize, offset + 1);
                Array.Copy(index[i], 0, data, valuesStart + offset, index[i].Length);
                offset += index[i].Length;
            }
            WriteOffset(data, 3 + offSize * index.Length, offSize, offset + 1);
            return data;
        }

        private void WriteOffset(byte[] data, int start, byte offSize, int offset)
        {
            for (int i = 0; i < offSize; i++)
            {
                byte b = (byte) (offset >> ((offSize - i - 1) * 8) & 0xFF);
                data[start + i] = b;
            }
        }
    }
}
