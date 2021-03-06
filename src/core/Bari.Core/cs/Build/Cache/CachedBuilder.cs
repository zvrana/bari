﻿using System.Collections.Generic;
using System.Diagnostics.Contracts;
using Bari.Core.Generic;

namespace Bari.Core.Build.Cache
{
    /// <summary>
    /// Wraps a builder so it is only ran if its dependencies has been modified,
    /// or the cache has been corrupted.
    /// </summary>
    public class CachedBuilder: IBuilder
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(typeof (CachedBuilder));

        private readonly IBuilder wrappedBuilder;
        private readonly IBuildCache cache;
        private readonly IFileSystemDirectory targetDir;

        /// <summary>
        /// Dependencies required for running this builder
        /// </summary>
        public IDependencies Dependencies
        {
            get { return wrappedBuilder.Dependencies; }
        }

        /// <summary>
        /// Gets an unique identifier which can be used to identify cached results
        /// </summary>
        public string Uid
        {
            get { return wrappedBuilder.Uid; }
        }

        /// <summary>
        /// Creates a cached builder
        /// </summary>
        /// <param name="wrappedBuilder">The builder instance to be wrapped</param>
        /// <param name="cache">The cache implementation to be used</param>
        /// <param name="targetDir">The target directory's file system abstraction</param>
        public CachedBuilder(IBuilder wrappedBuilder, IBuildCache cache, [TargetRoot] IFileSystemDirectory targetDir)
        {
            Contract.Requires(wrappedBuilder != null);
            Contract.Requires(cache != null);
            Contract.Requires(targetDir != null);

            this.wrappedBuilder = wrappedBuilder;
            this.cache = cache;
            this.targetDir = targetDir;
        }

        /// <summary>
        /// Prepares a builder to be ran in a given build context.
        /// 
        /// <para>This is the place where a builder can add additional dependencies.</para>
        /// </summary>
        /// <param name="context">The current build context</param>
        public void AddToContext(IBuildContext context)
        {
            wrappedBuilder.AddToContext(context);
        }

        /// <summary>
        /// Runs this builder
        /// </summary>
        /// <param name="context"> </param>
        /// <returns>Returns a set of generated files, in suite relative paths</returns>
        public ISet<TargetRelativePath> Run(IBuildContext context)
        {
            var currentFingerprint = wrappedBuilder.Dependencies.CreateFingerprint();
            var buildKey = new BuildKey(wrappedBuilder.GetType(), wrappedBuilder.Uid);

            cache.LockForBuilder(buildKey);
            try
            {
                if (cache.Contains(buildKey, currentFingerprint))
                {
                    log.DebugFormat("Restoring cached build outputs for {0}", buildKey);
                    return cache.Restore(buildKey, targetDir);
                }
                else
                {
                    log.DebugFormat("Running builder {0}", buildKey);
                    var files = wrappedBuilder.Run(context);

                    log.DebugFormat("Storing build outputs of {0}", buildKey);
                    cache.Store(buildKey, currentFingerprint, files, targetDir);
                    return files;
                }
            }
            finally
            {
                cache.UnlockForBuilder(buildKey);
            }
        }

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        /// <returns>
        /// A string that represents the current object.
        /// </returns>
        /// <filterpriority>2</filterpriority>
        public override string ToString()
        {
            return wrappedBuilder + "*";
        }
    }
}