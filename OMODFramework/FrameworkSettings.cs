﻿using System;
using JetBrains.Annotations;

namespace OMODFramework
{
    /// <summary>
    /// Settings for the entire Framework
    /// </summary>
    [PublicAPI]
    public class FrameworkSettings
    {
        /// <summary>
        /// Default Framework Settings used when they are not provided
        /// </summary>
        public static FrameworkSettings DefaultFrameworkSettings => new FrameworkSettings();

        /// <summary>
        /// Current OMOD version, Default is 4
        /// </summary>
        public byte CurrentOMODVersion { get; set; } = 4;

        /// <summary>
        /// Current version of the Oblivion Mod Manager, Default is 1.1.12.0
        /// </summary>
        public Version CurrentOBMMVersion { get; set; } = new Version(1, 1, 12, 0);

        /// <summary>
        /// Progress reporter for compression and decompression of SevenZip archives.
        /// </summary>
        public ICodeProgress? CodeProgress { get; set; }
    }
}
