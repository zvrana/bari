﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Bari.Core.Build.Dependencies.Protocol;
using Bari.Core.Generic;
using MD5 = System.Security.Cryptography.MD5;

namespace Bari.Core.Build.Cache
{
    /// <summary>
    /// Build cache storing build outputs together with their actual dependency fingerprint
    /// in the file system.
    /// </summary>
    public class FileBuildCache : IBuildCache
    {
        private const bool EnableFingerprintDiff = false;
        private readonly log4net.ILog log = log4net.LogManager.GetLogger(typeof (FileBuildCache));

        private const string DepsFileName = ".deps";
        private const string NamesFileName = ".names";

        private readonly IFileSystemDirectory cacheRoot;
        private readonly IProtocolSerializer protocolSerializer;
        private readonly IDictionary<BuildKey, ReaderWriterLockSlim> locks;

        /// <summary>
        /// Constructs the cache
        /// </summary>
        /// <param name="cacheRoot">Root directory where the cache will store its contents.</param>
        /// <param name="protocolSerializer">The serializer to be used for saving dependency fingerprint protocols</param>
        public FileBuildCache([CacheRoot] IFileSystemDirectory cacheRoot, IProtocolSerializer protocolSerializer)
        {
            this.cacheRoot = cacheRoot;
            this.protocolSerializer = protocolSerializer;
            locks = new Dictionary<BuildKey, ReaderWriterLockSlim>();
        }

        /// <summary>
        /// Locks the cache for a given builder. 
        /// 
        /// <para>Until calling <see cref="IBuildCache.UnlockForBuilder"/>, it is guaranteed that no
        /// <see cref="IBuildCache.Store"/> operation will be ran for the given builder from other
        /// threads.</para>
        /// </summary>
        /// <param name="builder">Builder key</param>
        public void LockForBuilder(BuildKey builder)
        {
            var lck = GetOrCreateLock(builder);
            lck.EnterUpgradeableReadLock();
        }

        /// <summary>
        /// Removes the lock put by the <see cref="IBuildCache.LockForBuilder"/> method.
        /// </summary>
        /// <param name="builder">Builder key</param>
        public void UnlockForBuilder(BuildKey builder)
        {
            ReaderWriterLockSlim lck;
            if (locks.TryGetValue(builder, out lck))
                lck.ExitUpgradeableReadLock();
        }

        /// <summary>
        /// Store build outputs in the cache by reading them from the file system
        /// </summary>
        /// <param name="builder">Builder key (first part of the key)</param>
        /// <param name="fingerprint">Dependency fingerprint created when the builder was executed (second part of the key)</param>
        /// <param name="outputs">Target-relative path of the build outputs to be cached</param>
        /// <param name="targetRoot">File system abstraction of the root target directory</param>
        public void Store(BuildKey builder, IDependencyFingerprint fingerprint, IEnumerable<TargetRelativePath> outputs, IFileSystemDirectory targetRoot)
        {
            var lck = GetOrCreateLock(builder);
            lck.EnterWriteLock();
            try
            {
                var cacheDir = cacheRoot.GetChildDirectory(GetCacheDirectoryName(builder), createIfMissing: true);

                SaveDependencyFingerprint(fingerprint, cacheDir);
                SaveOutputs(outputs, targetRoot, cacheDir);
            }
            finally
            {
                lck.ExitWriteLock();
            }
        }

        /// <summary>
        /// Checks if the cache contains stored outputs for a given builder with a given dependency fingerprint
        /// 
        /// <para>If <see cref="IBuildCache.Restore"/> will be also called, the cache must be locked first using
        /// the <see cref="IBuildCache.LockForBuilder"/> method.</para>
        /// </summary>
        /// <param name="builder">Builder key</param>
        /// <param name="fingerprint">Current dependency fingerprint</param>
        /// <returns>Returns <c>true</c> if there are stored outputs for the given builder and fingerprint combination.</returns>
        public bool Contains(BuildKey builder, IDependencyFingerprint fingerprint)
        {
            var lck = GetOrCreateLock(builder);
            lck.EnterReadLock();
            try
            {
                var dirName = GetCacheDirectoryName(builder);
                if (cacheRoot.ChildDirectories.Contains(dirName))
                {
                    var cacheDir = cacheRoot.GetChildDirectory(dirName);
                    if (cacheDir.Files.Contains(DepsFileName))
                    {
                        using (var depsStream = cacheDir.ReadBinaryFile(DepsFileName))
                        {
                            var fpType = fingerprint.GetType();
                            var storedFp = Activator.CreateInstance(fpType, protocolSerializer, depsStream);
                            
                            bool fingerprintEquals = fingerprint.Equals(storedFp);

                            if (!fingerprintEquals && EnableFingerprintDiff)
                            {
                                log.DebugFormat("[{0}] Fingerprint differs", dirName);
                                log.DebugFormat("[{1}] Cached: {0}", storedFp, dirName);
                                log.DebugFormat("[{1}] Current: {0}", fingerprint, dirName);
                            }

                            return fingerprintEquals;
                        }
                    }
                }

                return false;
            }
            finally
            {
                lck.ExitReadLock();
            }
        }

        /// <summary>
        /// Restores the stored files for a given builder to a file system directory
        /// 
        /// <para>The cache only stores the latest stored results and this is what will be restored
        /// to the target directory. To verify if it was generated with the correct dependency fingerprint,
        /// use <see cref="IBuildCache.Contains"/>.</para>
        /// <para>To ensure thread safety, use <see cref="IBuildCache.LockForBuilder"/>.</para>
        /// </summary>
        /// <param name="builder">Builder key</param>
        /// <param name="targetRoot">Target file system directory</param>
        /// <returns>Returns the target root relative paths of all the restored files</returns>
        public ISet<TargetRelativePath> Restore(BuildKey builder, IFileSystemDirectory targetRoot)
        {
            var lck = GetOrCreateLock(builder);
            lck.EnterReadLock();
            try
            {
                var dirName = GetCacheDirectoryName(builder);
                if (cacheRoot.ChildDirectories.Contains(dirName))
                {
                    var cacheDir = cacheRoot.GetChildDirectory(dirName);
                    if (cacheDir.Files.Contains(NamesFileName))
                    {
                        using (var reader = cacheDir.ReadTextFile(NamesFileName))
                        {
                            var result = new HashSet<TargetRelativePath>();

                            int idx = 0;
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                var parts = line.Split(';');
                                if (parts.Length == 2)
                                {
                                    var relativeRoot = parts[0];
                                    var relativePath = parts[1];
                                    var fullPath = Path.Combine(relativeRoot, relativePath);

                                    var cacheFileName = idx.ToString(CultureInfo.InvariantCulture);

                                    // It is possible that only a file name (a virtual file) was cached without any contents:
                                    if (cacheDir.Exists(cacheFileName))
                                    {
                                        CopyIfDifferent(cacheDir, cacheFileName, targetRoot, fullPath);
                                    }

                                    result.Add(new TargetRelativePath(relativeRoot, relativePath));
                                }
                                idx++;
                            }

                            return result;
                        }
                    }
                }

                return new HashSet<TargetRelativePath>();
            }
            finally
            {
                lck.ExitReadLock();
            }
        }

        /// <summary>
        /// Copies a source file to a target location, but only if it does not exist yet, with the same MD5 checksum
        /// as the source
        /// </summary>
        /// <param name="sourceDirectory">Source root directory</param>
        /// <param name="sourceFileName">Source file's relative path</param>
        /// <param name="targetRoot">Target root directory</param>
        /// <param name="targetRelativePath">Target file's relative path</param>
        private void CopyIfDifferent(IFileSystemDirectory sourceDirectory, string sourceFileName, IFileSystemDirectory targetRoot, string targetRelativePath)
        {
            bool copy = true;
            long sourceSize = sourceDirectory.GetFileSize(sourceFileName);
            if (targetRoot.Exists(targetRelativePath))
            {
                long targetSize = targetRoot.GetFileSize(targetRelativePath);
                if (sourceSize == targetSize)
                {
                    byte[] sourceChecksum = ComputeChecksum(sourceDirectory, sourceFileName);
                    byte[] targetChecksum = ComputeChecksum(targetRoot, targetRelativePath);

                    copy = !sourceChecksum.SequenceEqual(targetChecksum);
                }
            }

            if (copy)
            {
                using (var source = sourceDirectory.ReadBinaryFile(sourceFileName))
                using (var target = targetRoot.CreateBinaryFileWithDirectories(targetRelativePath))
                    StreamOperations.Copy(source, target);
            }
            else
            {
                log.DebugFormat("File {0} is the same as the cached one", targetRelativePath);
            }
        }

        private byte[] ComputeChecksum(IFileSystemDirectory root, string path)
        {
            using (var stream = root.ReadBinaryFile(path))
            using (var bufferedStream = new BufferedStream(stream, 1048576))
            using (var md5 = MD5.Create())
            {
                return md5.ComputeHash(bufferedStream);
            }
        }

        /// <summary>
        /// Gets an existing lock or creates a new one
        /// </summary>
        /// <param name="builder">Builder key used as a key to get locks</param>
        /// <returns>Returns a reader-writer lock</returns>
        private ReaderWriterLockSlim GetOrCreateLock(BuildKey builder)
        {
            ReaderWriterLockSlim lck;
            if (!locks.TryGetValue(builder, out lck))
                locks.Add(builder, lck = new ReaderWriterLockSlim());
            return lck;
        }

        /// <summary>
        /// Copies output files to the cache directory, and also saves a '.names' file referring to he original target relative
        /// paths of these files.
        /// </summary>
        /// <param name="outputs">Build outputs to be copied</param>
        /// <param name="targetRoot">Root directory for the build outputs</param>
        /// <param name="cacheDir">Target directory for the copy operation</param>
        private void SaveOutputs(IEnumerable<TargetRelativePath> outputs, IFileSystemDirectory targetRoot, IFileSystemDirectory cacheDir)
        {
            using (var names = cacheDir.CreateTextFile(NamesFileName))
            {
                int idx = 0;
                foreach (var outputPath in outputs)
                {
                    try
                    {
                        // It is possible that the returned path is a special path and does not refer to an existing file
                        // In this case we only have to save the filename, without its contents
                        if (targetRoot.Exists(outputPath))
                        {
                            using (var source = targetRoot.ReadBinaryFile(outputPath))
                            using (var target = cacheDir.CreateBinaryFile(idx.ToString(CultureInfo.InvariantCulture)))
                            {
                                StreamOperations.Copy(source, target);
                            }
                        }

                        names.WriteLine("{0};{1}", outputPath.RelativeRoot, outputPath.RelativePath);
                        idx++;
                    }
                    catch (IOException ex)
                    {
                        log.WarnFormat("IOException while reading {0}: {1}", outputPath, ex.Message);

                        if (!outputPath.RelativePath.ToLowerInvariant().EndsWith(".vshost.exe"))
                            throw;
                    }
                }
            }
        }

        /// <summary>
        /// Saves a dependency fingerprint to the cache directory
        /// </summary>
        /// <param name="fingerprint">Fingerprint to be saved</param>
        /// <param name="cacheDir">Target directory</param>
        private void SaveDependencyFingerprint(IDependencyFingerprint fingerprint, IFileSystemDirectory cacheDir)
        {
            using (var depStream = cacheDir.CreateBinaryFile(DepsFileName))
                fingerprint.Save(protocolSerializer, depStream);
        }

        /// <summary>
        /// Gets the directory name associated with a given Builder key
        /// </summary>
        /// <param name="builder">Builder key</param>
        /// <returns>Returns a valid directory name</returns>
        private static string GetCacheDirectoryName(BuildKey builder)
        {
            return builder.ToString().Replace("/", "___");
        }
    }
}