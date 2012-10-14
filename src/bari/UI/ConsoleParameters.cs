﻿using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using Bari.Core.Model.Loader;
using Bari.Core.UI;

namespace Bari.Console.UI
{
    /// <summary>
    /// Implements the <see cref="IParameters"/> interface by parsing the command line    
    /// </summary>
    public class ConsoleParameters: IParameters
    {
        private readonly string cmd;
        private readonly string[] cmdParams;
        private readonly string[] globalParams;
        private readonly string initialCurrentDirectory = Environment.CurrentDirectory;

        /// <summary>
        /// Creates the parameter object and immediately parse the arguments
        /// </summary>
        /// <param name="args">The command line arguments to be parsed</param>
        public ConsoleParameters(string[] args)
        {
            Contract.Requires(args != null);

            var gp = new List<string>();
            var remainingParams = new List<string>();

            int i = 0;
            for (; i < args.Length; i++)
            {
                string current = args[i];
                if (current.StartsWith("-") ||
                    current.StartsWith("/"))
                    gp.Add(current);
                else
                    break;
            }
            remainingParams.AddRange(args.Skip(i));
            globalParams = gp.ToArray();

            if (remainingParams.Count > 0)
            {
                cmd = remainingParams[0];
                cmdParams = new string[remainingParams.Count - 1];
                for (int j = 1; j < remainingParams.Count; j++)
                    cmdParams[j - 1] = remainingParams[j];
            }
            else
            {
                // Default command is 'help':
                cmd = "help";
                cmdParams = new string[0];
            }
        }

        /// <summary>
        /// Gets the name of the command to be performed
        /// </summary>
        public string Command
        {
            get { return cmd; }
        }

        /// <summary>
        /// Gets the parameters given to the command specified by the <see cref="IParameters.Command"/> property
        /// </summary>
        public string[] CommandParameters
        {
            get { return cmdParams; }
        }

        /// <summary>
        /// Gets the name (or url, etc.) of the suite specification to be loaded.
        /// 
        /// <para>Every registered <see cref="IModelLoader"/> will be asked to interpret the
        /// given suite name.</para>
        /// </summary>
        public string Suite
        {
            get
            {                
                return Path.Combine(initialCurrentDirectory, "suite.yaml");
            }
        }

        /// <summary>
        /// True if verbose output should be printed
        /// </summary>
        public bool VerboseOutput
        {
            get
            {
                return globalParams.Any(opt => opt == "-v" || opt == "/v");
            }
        }
    }
}