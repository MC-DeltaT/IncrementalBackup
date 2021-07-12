using System;
using System.Linq;


namespace IncrementalBackup
{
    /// <summary>
    /// The top-level program class. Mostly delegates to the <see cref="BackupCommand"/> and
    /// <see cref="RestoreCommand"/> classes for specific functionality.
    /// </summary>
    class Application
    {
        /// <summary>
        /// Application entry point. <br/>
        /// Instantiates the <see cref="Application"/> instance and calls <see cref="Run(string[])"/>.
        /// </summary>
        static int Main(string[] cmdArgs) =>
            (int)new Application().Run(cmdArgs);

        private const string EXECUTABLE_NAME = "IncrementalBackup.exe";

        /// <summary>
        /// Logger responsible for writing info to the console and/or log file.
        /// </summary>
        private readonly Logger Logger;

        private Application() {
            Logger = new(new(), null);
        }

        /// <summary>
        /// Main application functionality.
        /// </summary>
        /// <param name="programArgs">The program's command line arguments.</param>
        /// <returns>The process return code.</returns>
        private ProcessExitCode Run(string[] programArgs) {
            try {
                var (command, arguments) = ParseProgramArgs(programArgs);
                var commandInstance = command.Factory(Logger);
                return commandInstance.Run(arguments);
            }
            catch (InvalidProgramArgsError) {
                Console.Out.WriteLine("Usage:");
                Console.Out.WriteLine(
                    string.Join('\n',
                        Commands.COMMANDS.Select(
                            command => $"  {EXECUTABLE_NAME} {command.CommandString} {command.ArgumentSpec}")));
                return ProcessExitCode.InvalidArgs;
            }
            catch (InvalidCommandArgsError e) {
                Console.Out.WriteLine($"Usage: {EXECUTABLE_NAME} {e.Command.CommandString} {e.Command.ArgumentSpec}");
                return ProcessExitCode.InvalidArgs;
            }
            catch (ApplicationRuntimeError e) {
                Logger.Error(e.Message);
                return ProcessExitCode.RuntimeError;
            }
            catch (Exception e) {
                Logger.Error($"Unhandled exception: {e}");
                return ProcessExitCode.LogicError;
            }
        }

        /// <summary>
        /// Parses the program's command line arguments. <br/>
        /// Does not validate or parse the specific command's arguments (that is done by the command itself).
        /// </summary>
        /// <param name="programArgs">The program's command line arguments.</param>
        /// <returns>A tuple of the parsed command and command arguments.</returns>
        /// <exception cref="InvalidProgramArgsError">If the command line arguments are invalid.</exception>
        private (Command, string[]) ParseProgramArgs(string[] programArgs) {
            if (programArgs.Length < 1) {
                throw new InvalidProgramArgsError();
            }

            var commandString = programArgs[0];
            var arguments = programArgs[1..];

            foreach (var command in Commands.COMMANDS) {
                if (command.CommandString == commandString) {
                    return (command, arguments);
                }
            }
            throw new InvalidProgramArgsError();
        }
    }

    enum ProcessExitCode
    {
        /// <summary>
        /// The operation completed successfully.
        /// </summary>
        Success = 0,
        /// <summary>
        /// The operation completed mostly successfully, but with some warnings (e.g. files were skipped).
        /// </summary>
        Warning = 1,
        /// <summary>
        /// The operation was aborted due to invalid command line arguments.
        /// </summary>
        InvalidArgs = 2,
        /// <summary>
        /// The operation was aborted due to some runtime error.
        /// </summary>
        RuntimeError = 3,
        /// <summary>
        /// The application was aborted due to an unhandled exception (bad programmer!).
        /// </summary>
        LogicError = 4,
    }

    /// <summary>
    /// Indicates a high-level operation failed and as a result the application cannot continue. <br/>
    /// <see cref="Exception.Message"/> is the message to write to the logs.
    /// </summary>
    class ApplicationRuntimeError : Exception
    {
        public ApplicationRuntimeError(string message) :
            base(message) { }
    }

    /// <summary>
    /// Indicates the application's command line arguments are invalid.
    /// </summary>
    class InvalidProgramArgsError : Exception
    {
        public InvalidProgramArgsError() :
            base("Invalid program arguments.") { }
    }

    /// <summary>
    /// Indicates a command's arguments are invalid.
    /// </summary>
    class InvalidCommandArgsError : Exception
    {
        public InvalidCommandArgsError(Command command) :
            base("Invalid command arguments.") {
            Command = command;
        }

        /// <summary>
        /// The command which raised the exception.
        /// </summary>
        public readonly Command Command;
    }
}
