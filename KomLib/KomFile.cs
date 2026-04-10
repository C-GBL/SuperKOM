using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace KOM_DUMP_MARCH.KomLib
{
    internal static class KomFile
    {
        private const string MAGIC_V3 = "KOG GC TEAM MASSFILE V.0.3.";
        private const string MAGIC_V2 = "KOG GC TEAM MASSFILE V.0.2.";
        private const string MAGIC_V1 = "KOG GC TEAM MASSFILE V.0.1.";
        private const string ENCRYPT_KEY = "gpfrpdlxm";
        private const int HEADER_OFFSET = 72;

        //  Public API 

        public static void Extract(string komPath, string outputDir)
        {
            using (var fs = new FileStream(komPath, FileMode.Open, FileAccess.Read))
            using (var br = new BinaryReader(fs, Encoding.ASCII))
            {
                string magic = ReadMagic(br);

                if (magic == MAGIC_V3)
                    ExtractV3(br, outputDir);
                else if (magic == MAGIC_V2 || magic == MAGIC_V1)
                    ExtractLegacy(br, outputDir);
                else
                    throw new InvalidDataException("Not a valid KOM file.");
            }
        }

        public static void Pack(string folderPath, string komPath)
        {
            string[] files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);
            if (files.Length == 0) throw new InvalidOperationException("Folder is empty.");

            // Build file entries
            var entries = new List<KomEntry>();
            foreach (string file in files)
            {
                string relPath = MakeRelative(folderPath, file).Replace('\\', '/');
                byte[] rawData = File.ReadAllBytes(file);
                byte[] compressed = ZlibCompress(rawData);
                int algorithm = compressed.Length >= 8 ? 1 : 0;
                if (algorithm == 1)
                {
                    byte[] enc = (byte[])compressed.Clone();
                    BlowfishDecryptOrEncrypt(enc, enc.Length, encrypt: true);
                    compressed = enc;
                }
                uint filetime = ToUnixTime(File.GetLastWriteTimeUtc(file));
                entries.Add(new KomEntry
                {
                    Name = relPath,
                    Data = compressed,
                    UncompressedSize = (uint)rawData.Length,
                    CompressedSize = (uint)compressed.Length,
                    Adler32 = Adler32.Compute(compressed),
                    FileTime = filetime,
                    Algorithm = algorithm
                });
            }

            // Build XML header
            string xml = BuildXml(entries);
            byte[] xmlBytes = Encoding.UTF8.GetBytes(xml);

            // Pad to 8-byte boundary
            int paddedLen = xmlBytes.Length;
            if (paddedLen % 8 != 0) paddedLen += 8 - (paddedLen % 8);
            byte[] header = new byte[paddedLen];
            Array.Copy(xmlBytes, header, xmlBytes.Length);

            // Encrypt header
            BlowfishDecryptOrEncrypt(header, header.Length, encrypt: true);

            // Compute header checksum (over encrypted header)
            uint headerAdler = Adler32.Compute(header);

            // Compute filetime sum
            uint totalFileTime = 0;
            foreach (var e in entries) totalFileTime += e.FileTime;

            using (var fs = new FileStream(komPath, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs, Encoding.ASCII))
            {
                // Magic (52 bytes)
                byte[] magicBytes = new byte[52];
                byte[] magicStr = Encoding.ASCII.GetBytes(MAGIC_V3);
                Array.Copy(magicStr, magicBytes, magicStr.Length);
                bw.Write(magicBytes);

                bw.Write((uint)entries.Count);   // file count
                bw.Write((uint)1);               // compressed flag
                bw.Write(totalFileTime);
                bw.Write(headerAdler);
                bw.Write((uint)header.Length);
                bw.Write(header);

                foreach (var e in entries)
                    bw.Write(e.Data);
            }
        }

        //  Extraction helpers 

        private static void ExtractV3(BinaryReader br, string outputDir)
        {
            br.ReadUInt32(); // file count (parsed from XML instead)
            br.ReadUInt32(); // compressed flag
            br.ReadUInt32(); // filetime sum
            br.ReadUInt32(); // header adler32
            uint headerSize = br.ReadUInt32();

            byte[] headerBytes = br.ReadBytes((int)headerSize);
            DecryptHeader(headerBytes);

            // Null-terminate the xml string properly
            int xmlLen = Array.IndexOf(headerBytes, (byte)0);
            if (xmlLen < 0) xmlLen = headerBytes.Length;
            string xmlText = Encoding.UTF8.GetString(headerBytes, 0, xmlLen);

            var doc = new XmlDocument();
            doc.LoadXml(xmlText);

            long dataOffset = HEADER_OFFSET + headerSize;
            foreach (XmlNode node in doc.DocumentElement.ChildNodes)
            {
                if (node.NodeType != XmlNodeType.Element || node.Name != "File") continue;

                string name = node.Attributes["Name"]?.Value ?? "";
                uint size = ParseUInt(node.Attributes["Size"]?.Value);
                uint compSize = ParseUInt(node.Attributes["CompressedSize"]?.Value);
                int algo = (int)ParseUInt(node.Attributes["Algorithm"]?.Value);

                // Seek to file data
                br.BaseStream.Seek(dataOffset, SeekOrigin.Begin);
                byte[] compData = br.ReadBytes((int)compSize);
                dataOffset += compSize;

                // Decrypt if needed
                if (algo == 1)
                    BlowfishDecryptOrEncrypt(compData, (int)compSize, encrypt: false);

                // Decompress
                byte[] rawData = ZlibDecompress(compData, (int)size);

                // Write output
                string outPath = Path.Combine(outputDir, name.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                File.WriteAllBytes(outPath, rawData);
            }
        }

        private static void ExtractLegacy(BinaryReader br, string outputDir)
        {
            uint count = br.ReadUInt32();
            br.ReadUInt32(); // compressed flag

            // Read all filename entries (60 bytes each)
            var names = new string[count];
            for (int i = 0; i < count; i++)
            {
                byte[] nb = br.ReadBytes(60);
                int nlen = Array.IndexOf(nb, (byte)0);
                names[i] = Encoding.ASCII.GetString(nb, 0, nlen < 0 ? 60 : nlen);
            }

            // Read per-file metadata (size, compressedsize, offset - 12 bytes each)
            var metas = new (uint size, uint compSize, uint offset)[count];
            int headerBase = 52 + 4 + 4 + (int)(count * 60);
            for (int i = 0; i < count; i++)
            {
                uint sz = br.ReadUInt32();
                uint csz = br.ReadUInt32();
                uint off = br.ReadUInt32();
                metas[i] = (sz, csz, off + (uint)headerBase);
            }

            for (int i = 0; i < count; i++)
            {
                br.BaseStream.Seek(metas[i].offset, SeekOrigin.Begin);
                byte[] compData = br.ReadBytes((int)metas[i].compSize);
                byte[] rawData = ZlibDecompress(compData, (int)metas[i].size);

                string outPath = Path.Combine(outputDir, names[i].Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                File.WriteAllBytes(outPath, rawData);
            }
        }

        //  Crypto helpers 

        private static void DecryptHeader(byte[] header)
        {
            if (header.Length >= 5 &&
                header[0] == '<' && header[1] == '?' &&
                header[2] == 'x' && header[3] == 'm' && header[4] == 'l')
                return; // already plaintext

            BlowfishDecryptOrEncrypt(header, header.Length, encrypt: false);
        }

        private static void BlowfishDecryptOrEncrypt(byte[] data, int length, bool encrypt)
        {
            int aligned = (length / 8) * 8;
            if (aligned < 8) return;
            var bf = new BlowfishCBC(Encoding.ASCII.GetBytes(ENCRYPT_KEY));
            if (encrypt)
                bf.Encrypt(data, aligned);
            else
                bf.Decrypt(data, aligned);
        }

        //  zlib helpers 

        private static byte[] ZlibDecompress(byte[] data, int expectedSize)
        {
            // Skip 2-byte zlib header; DeflateStream stops at DEFLATE EOS marker
            if (data.Length < 3) return data;
            using (var ms = new MemoryStream(data, 2, data.Length - 2))
            using (var deflate = new DeflateStream(ms, CompressionMode.Decompress))
            using (var result = new MemoryStream(expectedSize))
            {
                deflate.CopyTo(result);
                return result.ToArray();
            }
        }

        private static byte[] ZlibCompress(byte[] data)
        {
            using (var ms = new MemoryStream())
            {
                // zlib header: CMF=0x78 (deflate, 32K window), FLG=0x9C (default level)
                ms.WriteByte(0x78);
                ms.WriteByte(0x9C);
                using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                    deflate.Write(data, 0, data.Length);
                // Append Adler-32 of original data (big-endian)
                uint a = Adler32.Compute(data);
                ms.WriteByte((byte)(a >> 24));
                ms.WriteByte((byte)(a >> 16));
                ms.WriteByte((byte)(a >> 8));
                ms.WriteByte((byte)a);
                return ms.ToArray();
            }
        }

        //  XML builder 

        private static string BuildXml(List<KomEntry> entries)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\"?>\n<Files>\n");
            foreach (var e in entries)
            {
                sb.Append($"  <File Name=\"{EscapeXml(e.Name)}\"");
                sb.Append($" Size=\"{e.UncompressedSize}\"");
                sb.Append($" CompressedSize=\"{e.CompressedSize}\"");
                sb.Append($" Checksum=\"{e.Adler32:x}\"");
                sb.Append($" FileTime=\"{e.FileTime:x}\"");
                sb.Append($" Algorithm=\"{e.Algorithm}\"");
                sb.Append(" />\n");
            }
            sb.Append("</Files>");
            return sb.ToString();
        }

        private static string EscapeXml(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

        //  Utility 

        private static string ReadMagic(BinaryReader br)
        {
            byte[] b = br.ReadBytes(52);
            int len = Array.IndexOf(b, (byte)0);
            return Encoding.ASCII.GetString(b, 0, len < 0 ? 52 : len);
        }

        private static uint ParseUInt(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToUInt32(s, 16);
            if (uint.TryParse(s, out uint v)) return v;
            return Convert.ToUInt32(s, 16);
        }

        private static uint ToUnixTime(DateTime dt) =>
            (uint)(dt - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

        private static string MakeRelative(string basePath, string fullPath)
        {
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                basePath += Path.DirectorySeparatorChar;
            return fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)
                ? fullPath.Substring(basePath.Length)
                : Path.GetFileName(fullPath);
        }

        //  Inner types 

        private class KomEntry
        {
            public string Name;
            public byte[] Data;
            public uint UncompressedSize;
            public uint CompressedSize;
            public uint Adler32;
            public uint FileTime;
            public int Algorithm;
        }
    }
}
