﻿using System.Collections.Generic;
using System.Diagnostics.Contracts;
using Bari.Core.Model;

namespace Bari.Plugins.VsCore.VisualStudio.SolutionName
{
    /// <summary>
    /// Interface for SLN name generators
    /// </summary>
    [ContractClass(typeof(ISlnNameGeneratorContracts))]
    public interface ISlnNameGenerator
    {
        /// <summary>
        /// Generates a file name for a VS solution file which will contain the given set of projects.
        /// </summary>
        /// <param name="projects">Set of projects to be included in the SLN file</param>
        /// <returns>Returns a valid file name without extension</returns>
        string GetName(IEnumerable<Project> projects);
    }

    [ContractClassFor(typeof(ISlnNameGenerator))]
    abstract class ISlnNameGeneratorContracts: ISlnNameGenerator
    {
        public string GetName(IEnumerable<Project> projects)
        {
            Contract.Requires(projects != null);
            Contract.Requires(Contract.ForAll(projects, p => p != null));
            Contract.Ensures(Contract.Result<string>() != null);

            return null; // dummy
        }
    }
}