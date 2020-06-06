﻿#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ICSharpCode.SharpZipLib.Zip;
using JetBrains.Annotations;
using SevenZip;
using SevenZip.Compression.LZMA;

namespace OMODFramework
{
    [PublicAPI]
    public enum CompressionType : byte
    {
        SevenZip,
        Zip
    }

    [PublicAPI]
    public enum CompressionLevel
    {
        VeryHigh = 9, 
        High = 7, 
        Medium = 5,
        Low = 3,
        VeryLow = 1,
        None = 0
    }

    internal static class CompressionHandler
    {
        internal static Stream DecompressStream(IEnumerable<OMODCompressedEntry> entryList, Stream compressedStream, CompressionType compressionType)
        {
            var outSize = entryList.Select(x => x.Length).Aggregate((x, y) => x + y);
            return compressionType switch
            {
                CompressionType.SevenZip => SevenZipDecompress(compressedStream, outSize),
                CompressionType.Zip => ZipDecompress(compressedStream, outSize),
                _ => throw new ArgumentOutOfRangeException(nameof(compressionType), compressionType, null)
            };
        }

        internal static void CompressFiles(IEnumerable<CreationOptions.CreationOptionFile> files, CompressionType type,
            CompressionLevel level, out Stream compressedStream, out Stream crcStream)
        {
            IEnumerable<CreationOptions.CreationOptionFile> creationOptionFiles = files.ToList();
            crcStream = GenerateCRCStream(creationOptionFiles);
            compressedStream = type switch
            {
                CompressionType.SevenZip => SevenZipCompress(creationOptionFiles, level),
                CompressionType.Zip => ZipCompress(creationOptionFiles, level),
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
            };
        }

        private static Stream GenerateCRCStream(IEnumerable<CreationOptions.CreationOptionFile> files)
        {
            var stream = new MemoryStream();
            //can't use "using" here because binary writer will close the
            //underlying stream when disposed
            var bw = new BinaryWriter(stream);

            foreach (var file in files)
            {
                bw.Write(file.To);
                bw.Write(Utils.CRC32(file.From));
                bw.Write(file.From.Length);
            }
            bw.Flush();

            stream.Seek(0, SeekOrigin.Begin);
            return stream;
        }

        private static Stream CreateDecompressedStream(IEnumerable<CreationOptions.CreationOptionFile> files)
        {
            var list = files.ToList();
            var length = list.Select(x => x.From.Length).Aggregate((x, y) => x + y);

            var decompressedStream = new MemoryStream((int)length);
            foreach (var file in list.Select(x => x.From))
            {
                using var fs = file.OpenRead();
                fs.CopyTo(decompressedStream);
            }

            decompressedStream.Position = 0;

            return decompressedStream;
        }

        private static Stream SevenZipCompress(IEnumerable<CreationOptions.CreationOptionFile> files,
            CompressionLevel level)
        {
            var encoder = new Encoder();
            var dictionarySize = level switch
            {
                CompressionLevel.VeryHigh => 1 << 26,
                CompressionLevel.High => 1 << 25,
                CompressionLevel.Medium => 1 << 23,
                CompressionLevel.Low => 1 << 21,
                CompressionLevel.VeryLow => 1 << 19,
                CompressionLevel.None => 0,
                _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
            };

            encoder.SetCoderProperties(new[] {CoderPropID.DictionarySize}, new object[] {dictionarySize});

            var compressedStream = new MemoryStream();
            encoder.WriteCoderProperties(compressedStream);

            using var decompressedStream = CreateDecompressedStream(files);

            encoder.Code(decompressedStream, compressedStream, decompressedStream.Length, -1, null);

            compressedStream.Position = 0;

            return compressedStream;
        }

        private static Stream ZipCompress(IEnumerable<CreationOptions.CreationOptionFile> files,
            CompressionLevel level)
        {
            throw new NotImplementedException();
        }

        private static Stream SevenZipDecompress(Stream compressedStream, long outSize)
        {
            var buffer = new byte[5];
            var decoder = new Decoder();
            compressedStream.Read(buffer, 0, 5);
            decoder.SetDecoderProperties(buffer);

            var inSize = compressedStream.Length - compressedStream.Position;
            var stream = new MemoryStream((int)outSize);

            decoder.Code(compressedStream, stream, inSize, outSize, null);

            stream.Seek(0, SeekOrigin.Begin);
            return stream;
        }

        private static Stream ZipDecompress(Stream compressedStream, long outSize)
        {
            var zip = new ZipFile(compressedStream);
            using var inputStream = zip.GetInputStream(0);
            var stream = new MemoryStream((int)outSize);

            inputStream.CopyTo(stream);

            stream.Seek(0, SeekOrigin.Begin);
            if(stream.Length != outSize)
                throw new Exception($"Expected stream length to be {outSize} but is {stream.Length}!");

            return stream;
        }
    }
}
