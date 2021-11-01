Incremental Backup Utility
by Reece Jones


Notice
    This project has been superseded by the project here: https://github.com/MC-DeltaT/IncrementalBackup2
    No further updates to this project will be released.


Purpose
    Unlike Linux, which has awesome tools like rsync, Windows does not have a good selection of free backup tools.
    There is the Windows system image backup, however that does full backups only. There is also File History, but that
    is janky and largely opaque.
    Thus I created this tool. Some of its design goals:
        - free, open source
        - as simple as possible (no installation, no GUI, no fluff)
        - robust
        - fast
        - transparent backup format

    Note that this application is intended for low-risk personal use. I have tried to make it as robust as possible,
    but if you use this software and lose all your data as a result, that's on you.


Requirements
    - Windows
    - .NET 5.0
    - C# 9
    - Visual Studio 2019


Application Structure
    The application is a standard Visual Studio 2019 solution.
    The solution is divided into 3 projects:
        "IncrementalBackup" - The main application executable.
        "Backup" - high-level functionality for backup creation. Builds into a class library (DLL).
        "BackupMeta" - functionality for manipulating backup structure and metadata. Builds into a class library (DLL).
        "Utility" - miscellaneous common functionality. Builds into a class library (DLL).


Building
    You should be able to just open the Visual Studio solution and build it. The build output is in the default
    location set by Visual Studio.
    The output of building the "IncrementalBackup" project will be the built application.


Usage
    The application uses a single executable with multiple "commands" for different functionality. Usage of the
    executable is as follows:
        IncrementalBackup.exe <command> <command_args>

    Commands:
        backup - Creates a new backup. See "BACKUP.txt" for details.

    To start using this application, you should probably start by looking at "BACKUP.txt".
