﻿#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using ICSharpCode.SharpZipLib.Zip;
using JetBrains.Annotations;
using OMODFramework.Exceptions;

namespace OMODFramework
{
    /// <summary>
    /// Enum for all possible files in an omod file
    /// </summary>
    [PublicAPI]
    public enum OMODFile : byte
    {
        DataCRC,
        Data,
        PluginsCRC,
        Plugins,
        Config,
        Readme,
        Script,
        Image
    }

    /// <summary>
    /// Files inside .crc entries
    /// </summary>
    [PublicAPI]
    public struct OMODCompressedEntry
    {
        /// <summary>
        /// Path and Name of the file
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// CRC32 of the file
        /// </summary>
        public readonly uint CRC;

        /// <summary>
        /// Length (in bytes) of the file
        /// </summary>
        public readonly long Length;

        public OMODCompressedEntry(string name, uint crc, long length)
        {
            Name = name;
            CRC = crc;
            Length = length;
        }

        internal string GetFullPath(DirectoryInfo directory)
        {
            return Path.Combine(directory.FullName, Name);
        }
    }

    internal static partial class Utils
    {
        internal static string ToFileString(this OMODFile file)
        {
            return file switch
            {
                OMODFile.DataCRC => "data.crc",
                OMODFile.PluginsCRC => "plugins.crc",
                OMODFile.Config => "config",
                OMODFile.Readme => "readme",
                OMODFile.Script => "script",
                OMODFile.Image => "image",
                OMODFile.Data => "data",
                OMODFile.Plugins => "plugins",
                _ => throw new ArgumentOutOfRangeException(nameof(file), file, "Should not be possible!")
            };
        }
    }

    [PublicAPI]
    public class Config
    {
        public string Name { get; set; } = string.Empty;
        private int _majorVersion, _minorVersion, _buildVersion;
        public Version Version => new Version(_majorVersion, _minorVersion, _buildVersion);
        public string Description { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Website { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;

        public DateTime CreationTime { get; set; }
        public byte FileVersion { get; set; }
        public CompressionType CompressionType { get; set; }

        internal static Config ParseConfig(Stream stream)
        {
            var config = new Config();
            using var br = new BinaryReader(stream);

            config.FileVersion = br.ReadByte();
            config.Name = br.ReadString();
            config._majorVersion = br.ReadInt32();
            config._minorVersion = br.ReadInt32();
            config.Author = br.ReadString();
            config.Email = br.ReadString();
            config.Website = br.ReadString();
            config.Description = br.ReadString();

            if (config.FileVersion >= 2)
                config.CreationTime = DateTime.FromBinary(br.ReadInt64());
            else
            {
                var sCreationTime = br.ReadString();
                config.CreationTime = !DateTime.TryParseExact(sCreationTime, "dd/MM/yyyy HH:mm", null, DateTimeStyles.None,
                    out var creationTime) ? new DateTime(2006, 1, 1) : creationTime;
            }

            config.CompressionType = (CompressionType) br.ReadByte();
            if (config.FileVersion >= 1)
                config._buildVersion = br.ReadInt32();

            if (config._majorVersion < 0)
                config._majorVersion = 0;
            if (config._minorVersion < 0)
                config._minorVersion = 0;
            if (config._buildVersion < 0)
                config._buildVersion = 0;

            return config;
        }
    }

    [PublicAPI]
    public class OMOD : IDisposable
    {
        private readonly FrameworkSettings _frameworkSettings = null!;
        private readonly ZipFile _zipFile;

        public readonly Config Config = null!;

        public OMOD(FileInfo path, FrameworkSettings? settings = null, bool checkIntegrity = true)
        {
            if (!path.Exists)
                throw new ArgumentException($"File at {path} does not exists!", nameof(path));

            if (settings == null)
                _frameworkSettings = FrameworkSettings.DefaultFrameworkSettings;

            _zipFile = new ZipFile(path.OpenRead());

            if (checkIntegrity)
            {
                if (!_zipFile.CheckIntegrity())
                    return;
            }

            Config = Config.ParseConfig(ExtractFile(OMODFile.Config));

            if(Config.FileVersion > _frameworkSettings.CurrentOMODVersion)
                throw new OMODInvalidConfigException(Config, $"The file version in the config: {Config.FileVersion} is greater than the set OMOD version in the Framework Settings: {_frameworkSettings.CurrentOMODVersion}!");
        }

        /// <summary>
        /// Checks if the OMOD contains the given file
        /// </summary>
        /// <param name="file">The file</param>
        /// <returns></returns>
        public bool HasFile(OMODFile file) => _zipFile.HasFile(file.ToFileString());

        /// <summary>
        /// Extract the given file from the OMOD and returns a <see cref="Stream"/> with the data
        /// instead of writing to file.
        /// </summary>
        /// <param name="file">The file to extract</param>
        /// <returns></returns>
        /// <exception cref="ZipFileEntryNotFoundException"></exception>
        public Stream ExtractFile(OMODFile file)
        {
            return _zipFile.ExtractFile(file.ToFileString());
        }

        private string ExtractStringFile(OMODFile file)
        {
            using var stream = ExtractFile(OMODFile.Script);
            using var br = new BinaryReader(stream);
            return br.ReadString();
        }

        /// <summary>
        /// Returns the readme of the OMOD
        /// </summary>
        /// <returns></returns>
        /// <exception cref="ZipFileEntryNotFoundException"></exception>
        public string ExtractReadme()
        {
            return ExtractStringFile(OMODFile.Readme);
        }

        /// <summary>
        /// Returns the script of the OMOD
        /// </summary>
        /// <returns></returns>
        /// <exception cref="ZipFileEntryNotFoundException"></exception>
        public string ExtractScript()
        {
            return ExtractStringFile(OMODFile.Script);
        }

        /// <summary>
        /// Extracts the Image in the OMOD
        /// </summary>
        /// <returns></returns>
        /// <exception cref="ZipFileEntryNotFoundException"></exception>
        public Bitmap ExtractImage()
        {
            using var stream = ExtractFile(OMODFile.Image);
            var image = Image.FromStream(stream);
            return (Bitmap) image;
        }

        /// <summary>
        /// Returns an enumerable of all data files.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<OMODCompressedEntry> GetDataFileList()
        {
            using var stream = ExtractFile(OMODFile.DataCRC);
            using var br = new BinaryReader(stream);

            var list = new List<OMODCompressedEntry>();
            while (br.PeekChar() != -1)
            {
                var name = br.ReadString();
                var crc = br.ReadUInt32();
                var length = br.ReadInt64();
                list.Add(new OMODCompressedEntry(name, crc, length));
            }

            return list;
        }

        /// <summary>
        /// Returns an enumerable for all plugins. Use <see cref="HasFile"/> beforehand
        /// so you don't get a <see cref="ZipFileEntryNotFoundException"/>!
        /// </summary>
        /// <returns></returns>
        /// <exception cref="ZipFileEntryNotFoundException"></exception>
        public IEnumerable<OMODCompressedEntry> GetPlugins()
        {
            using var stream = ExtractFile(OMODFile.PluginsCRC);
            using var br = new BinaryReader(stream);

            var list = new List<OMODCompressedEntry>();
            while (br.PeekChar() != -1)
            {
                var name = br.ReadString();
                var crc = br.ReadUInt32();
                var length = br.ReadInt64();
                list.Add(new OMODCompressedEntry(name, crc, length));
            }

            return list;
        }

        public void ExtractPluginFiles(DirectoryInfo outputDirectory, IEnumerable<OMODCompressedEntry>? entryList = null)
        {
            entryList ??= GetPlugins();
            ExtractCompressedData(OMODFile.Data, entryList, outputDirectory);
        }

        public void ExtractDataFiles(DirectoryInfo outputDirectory, IEnumerable<OMODCompressedEntry>? entryList = null)
        {
            entryList ??= GetDataFileList();
            ExtractCompressedData(OMODFile.Data, entryList, outputDirectory);
        }

        private void ExtractCompressedData(OMODFile file, IEnumerable<OMODCompressedEntry> entryList, DirectoryInfo outputDirectory)
        {
            if(file != OMODFile.Data && file != OMODFile.Plugins)
                throw new ArgumentException($"Provided OMODFile can only be Data or Plugins but is {file}!", nameof(file));

            if(!outputDirectory.Exists)
                outputDirectory.Create();

            using var compressedStream = ExtractFile(file);
            using var decompressedStream = CompressionHandler.DecompressStream(entryList, compressedStream, Config.CompressionType);
            
            CompressionHandler.WriteDecompressedStream(entryList, decompressedStream, outputDirectory);
        }

        public void Dispose()
        {
            _zipFile.Close();
        }
    }
}
