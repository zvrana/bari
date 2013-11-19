﻿using System;
using System.Collections.Generic;
using System.IO;
using Bari.Core.Build.Dependencies;
using Bari.Core.Generic;
using Bari.Core.Model;

namespace Bari.Core.Build
{
    /// <summary>
    /// Copies the contents of a given project's <c>content</c> source set to the target directory
    /// </summary>
    public class ContentBuilder: IBuilder
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(typeof (ContentBuilder));

        private readonly Project project;
        private readonly ISourceSetFingerprintFactory fingerprintFactory;
        private readonly IFileSystemDirectory suiteRoot;
        private readonly IFileSystemDirectory targetRoot;

        public ContentBuilder(Project project, ISourceSetFingerprintFactory fingerprintFactory, [SuiteRoot] IFileSystemDirectory suiteRoot, [TargetRoot] IFileSystemDirectory targetRoot)
        {
            this.project = project;
            this.fingerprintFactory = fingerprintFactory;
            this.suiteRoot = suiteRoot;
            this.targetRoot = targetRoot;
        }

        /// <summary>
        /// Dependencies required for running this builder
        /// </summary>
        public IDependencies Dependencies
        {
            get
            {
                return new SourceSetDependencies(fingerprintFactory, project.GetSourceSet("content"));
            }
        }

        /// <summary>
        /// Gets an unique identifier which can be used to identify cached results
        /// </summary>
        public string Uid
        {
            get { return project.Module + "." + project.Name; }
        }

        /// <summary>
        /// Prepares a builder to be ran in a given build context.
        /// 
        /// <para>This is the place where a builder can add additional dependencies.</para>
        /// </summary>
        /// <param name="context">The current build context</param>
        public void AddToContext(IBuildContext context)
        {
            context.AddBuilder(this, new IBuilder[0]);
        }

        /// <summary>
        /// Runs this builder
        /// </summary>
        /// <param name="context">Current build context</param>
        /// <returns>Returns a set of generated files, in target relative paths</returns>
        public ISet<TargetRelativePath> Run(IBuildContext context)
        {
            var contents = project.GetSourceSet("content");
            var contentsDir = project.RootDirectory.GetChildDirectory("content");
            var targetDir = targetRoot.GetChildDirectory(project.Module.Name, createIfMissing: true);

            var result = new HashSet<TargetRelativePath>();
            foreach (var sourcePath in contents.Files)
            {
                log.DebugFormat("Copying content {0}...", sourcePath);

                var relativePath = suiteRoot.GetRelativePathFrom(contentsDir, sourcePath);

                using (var source = contentsDir.ReadBinaryFile(relativePath))
                using (var target = targetDir.CreateBinaryFile(relativePath))
                    StreamOperations.Copy(source, target);

                result.Add(new TargetRelativePath(Path.Combine(project.Module.Name, relativePath)));
            }

            return result;
        }

        public override string ToString()
        {
            return String.Format("[{0}.{1}/content]", project.Module.Name, project.Name);
        }
    }
}