Incremental Backup Utility
by Reece Jones


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

    At this time, there is the ability to back up files, but no ability to restore them. I will probably implement this
    feature soon. In the meantime, the backup structure is so simple that you can probably implement this yourself.
    
    Note that this tool is intended for low-risk personal use. I have tried to make it as robust as possible, but if
    you use this tool and lose all your data as a result, that's on you.


Requirements
    - Windows
    - .NET 5.0
    - C# 9
    - Visual Studio 2019 (probably? I don't know how VS compatibility works).


Application Structure
    The application is a standard Visual Studio 2019 solution.
    The solution is divided into 3 projects:
        "Backup" - the program for creating backups (see "BACKUP.txt"). Builds into an executable.
        "BackupMeta" - functionality for manipulating backup structure and metadata. Builds into a class library (DLL).
        "Utility" - miscellaneous common functionality. Builds into a class library (DLL).


Building
    You should be able to just open the Visual Studio solution and build it. The build output is in the default
    location set by Visual Studio.
    The output of building the "Backup" project will be the backup creation tool executable.


Usage
    To find out how to use this tool to back up files (which is probably what you want to do to start off with), please
    see the "BACKUP.txt" file.
