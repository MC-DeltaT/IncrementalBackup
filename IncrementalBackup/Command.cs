using System;
using System.Collections.Generic;


namespace IncrementalBackup
{
    /// <summary>
    /// Interface for a "subapplication" which handles specific functionality (e.g creating backups).
    /// </summary>
    interface ICommand
    {
        /// <summary>
        /// Runs the command.
        /// </summary>
        /// <param name="arguments">Arguments for the command from the program command line arguments.</param>
        /// <returns>The exit code to be returned from the process.</returns>
        ProcessExitCode Run(string[] arguments);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="CommandString">The string used to invoke the command.</param>
    /// <param name="ArgumentSpec">Human-readable specification of this command's arguments, for printing usage info.
    /// </param>
    /// <param name="Factory">Instantiates the command.</param>
    record Command(
        string CommandString,
        string ArgumentSpec,
        Func<Logger, ICommand> Factory
    );

    static class Commands
    {
        public static IReadOnlyList<Command> COMMANDS = new List<Command>() {
            BackupCommand.COMMAND
        };
    }
}
