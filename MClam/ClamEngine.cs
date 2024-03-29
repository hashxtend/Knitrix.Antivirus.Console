﻿// 
//     ClamEngine.cs  is part of MClam.
//     Copyright (C) 2017  Fahmi Noor Fiqri
// 
//     This program is free software; you can redistribute it and/or
//     modify it under the terms of the GNU General Public License
//     as published by the Free Software Foundation; either version 2
//     of the License, or (at your option) any later version.
// 
//     This program is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
// 
//     You should have received a copy of the GNU General Public License
//     along with this program; if not, write to the Free Software
//     Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.
using System;
using System.Security.Permissions;
using System.Runtime.InteropServices;
using MClam.Native;
using MClam.Shared;

namespace MClam
{
    /// <summary>
    /// ClamAV engine wrapper.
    /// </summary>
    [PermissionSet(SecurityAction.Demand)]
    public sealed class ClamEngine : IDisposable
    {
        private HandleRef _handle;
        private bool _compiled;
        private uint _signatureCount;

        #region Constructor
        /// <summary>
        /// Initialize new instance of <see cref="ClamEngine"/>.
        /// </summary>
        public ClamEngine()
        {
            _handle = new HandleRef(this, NativeMethods.cl_engine_new());
        }
        #endregion

        #region Properties
        /// <summary>
        /// File scan options.
        /// </summary>
        public ScanFlags Flags { get; set; } = ScanFlags.Standard;

        /// <summary>
        /// Gets loaded signature count.
        /// </summary>
        public uint LoadedSignatures => _signatureCount;

        /// <summary>
        /// Gets a value indicating current engine instance is loaded by database.
        /// </summary>
        public bool IsLoaded => _signatureCount > 0;

        /// <summary>
        ///  Gets a value indicating current engine instance is ready for scanning.
        /// </summary>
        public bool IsCompiled => _compiled;
        #endregion

        #region Methods
        /// <summary>
        /// Load a single database file or a directory with database inside to current instance of <see cref="ClamEngine"/>.
        /// </summary>
        /// <param name="path">Fullpath to a file or directory containing the database file(s).</param>
        /// <param name="flags">Database loading options (default <see cref="DatabaseFlags.Standard"/>.</param>
        public void Load(string path, DatabaseFlags flags = DatabaseFlags.Standard)
        {
            ArgValidate.NotEmptyString(path, nameof(path));
            ThrowIfCompiled();

            NativeMethods.cl_load(path, _handle.Handle, ref _signatureCount, (uint)flags).ThrowIfError();
        }

        /// <summary>
        /// Compiles current engine instance for scanning purposes.
        /// </summary>
        public void Compile()
        {
            ThrowIfNotLoaded();
            ThrowIfCompiled();

            NativeMethods.cl_engine_compile(_handle.Handle).ThrowIfError();
            _compiled = true;
        }

        /// <summary>
        /// Scans a file for viruses.
        /// </summary>
        /// <param name="path">Full path to file to be scanned.</param>
        /// <returns><see cref="ScanResult"/> object containing scan information.</returns>
        public ScanResult ScanFile(string path)
        {
            ArgValidate.NotEmptyString(path, nameof(path));
            ArgValidate.FileExist(path, nameof(path));

            return ScanFile(FileEntry.Open(path));
        }

        /// <summary>
        /// Scans a file for viruses.
        /// </summary>
        /// <param name="file">File to be scanned.</param>
        /// <returns><see cref="ScanResult"/> object containing scan information.</returns>
        public ScanResult ScanFile(FileEntry file)
        {
            ArgValidate.NotNull(file, nameof(file));
            ThrowIsNotCompiled();

            var fscaned = 0;
            var vname = IntPtr.Zero;
            int retv;
            ScanResult result;
            
            retv = NativeMethods.cl_scandesc(file.FileDescriptor, ref vname, ref fscaned, _handle.Handle, (uint)Flags);
            result = new ScanResult
            {
                FullPath = file.FilePath,
                Scanned = fscaned,
            };

            switch (retv)
            {
                case (int) cl_error_t.CL_VIRUS:
                    result.IsVirus = true;
                    result.MalwareName = Marshal.PtrToStringAnsi(vname);
                    break;

                case (int) cl_error_t.CL_CLEAN:
                    result.IsVirus = false;
                    break;

                default:
                    result.IsVirus = false;
                    break;
            }

            return result;
        }

        /// <summary>
        /// Sets engine field parameter value.
        /// </summary>
        /// <param name="field">Engine field.</param>
        /// <param name="value">Value to change.</param>
        public void SetField(EngineField field, object value)
        {
            ArgValidate.NotNull(value, nameof(value));
            ThrowIfCompiled();

            if (value is string || value is char)
            {
                var val = Marshal.StringToCoTaskMemAnsi(value.ToString());
                NativeMethods.cl_engine_set_str(_handle.Handle, field, val).ThrowIfError();
            }
            else
            {
                NativeMethods.cl_engine_set_num(_handle.Handle, field, Convert.ToInt16(value));
            }
        }

        /// <summary>
        /// Gets engine field parameter value.
        /// </summary>
        /// <param name="field">Engine field.</param>
        /// <returns>Engine parameter value.</returns>
        public object GetField(EngineField field)
        {
            ThrowIfCompiled();

            object value;
            var errorCode = (int)cl_error_t.CL_SUCCESS;

            if (field == EngineField.PuaCategories || field == EngineField.Keeptmp)
            {
                var data = NativeMethods.cl_engine_get_str(_handle.Handle, field, ref errorCode);
                errorCode.ThrowIfError();
                value = Marshal.PtrToStringAnsi(data);
            }
            else
            {
                value = NativeMethods.cl_engine_get_num(_handle.Handle, field, ref errorCode);
                errorCode.ThrowIfError();
            }

            return value;
        }
        #endregion

        #region Validators
        private void ThrowIfCompiled()
        {
            if (IsCompiled) throw new InvalidOperationException("Engine is already compiled.");
        }

        private void ThrowIsNotCompiled()
        {
            if (!IsCompiled) throw new InvalidOperationException("Engine is not compiled!");
        }
        
        private void ThrowIfNotLoaded()
        {
            if (!IsLoaded) throw new InvalidOperationException("Engine is not loaded!");
        }
        #endregion

        #region IDisposable Support
        private bool _disposedValue;
        
        void Dispose(bool disposing)
        {
            if (_disposedValue) return;
            if (disposing)
            {
                // TODO: dispose managed state (managed objects).
            }

            NativeMethods.cl_engine_free(_handle.Handle);

            _disposedValue = true;
        }

        /// <summary>
        /// Allows an object to try to free resources and perform other cleanup operations before it is reclaimed by garbage collection.
        /// </summary>
        ~ClamEngine()
        {
            Dispose(false);
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        /// <filterpriority>2</filterpriority>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
