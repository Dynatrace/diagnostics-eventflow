﻿// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;
using System.IO;
using Microsoft.Diagnostics.EventFlow.HealthReporters;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Diagnostics.EventFlow.TestHelpers
{
    public class SharedFolderTestHealthReporter : CsvHealthReporter
    {
        public string CurrentLogFilePath { get; private set; }

        public SharedFolderTestHealthReporter(IConfiguration configuration)
            : base(configuration.ToCsvHealthReporterConfiguration())
        {
        }

        internal override FileStream CreateFileStream(string logFilePath)
        {
            CurrentLogFilePath = logFilePath;
            return null;
        }

        internal override void SetNewStreamWriter(Func<string, bool> directoryExists, Func<string, DirectoryInfo> createDirectory)
        {
            base.SetNewStreamWriter(path => true, newDirPath => null);
        }
    }
}
