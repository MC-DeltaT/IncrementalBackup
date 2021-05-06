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
    - C# 8
    - Visual Studio 2019 (probably? I don't know how VS compatibility works).


Building
    The project is set up as a stock standard Visual Studio 2019 solution.
    You should be able to just open the solution and build it. The build output is in the default location.


Usage
    IncrementalBackup.exe <source_dir> <target_dir> [<exclude_path1> <exclude_path2> ...]
    
    <source_dir> - The path of the directory to be backed up. May be at least any locally mapped drive (I'm not sure
    how other path types like UNC would work, I haven't tested). If relative, it's taken to be relative to the current
    directory.
    
    <target_dir> - The path of the directory to back up to. Same conditions as <source_dir> apply. It's highly
    recommended that <target_dir> is not contained within <source_dir> (except if you explicitly exclude <target_dir>).
    
    <exclude_path> - 0 or more paths which will never be backed up. If relative paths, they are relative to source_dir.


How It Works
    The premise of this tool is for it to be run regularly with the same target directory. Backups will be accumulated
    in the target directory, which are read during the next backup to determine which files to back up (hence
    "incremental" backup). This model allows use of the tool without any installation or data stored elsewhere on the
    system.
    Multiple source directories may be used with the same target directory. When performing a backup, the tool will
    only read previous backups for the same source directory.
    
    Determining if a file should be backed up is based on the last write time metadata. The tool will scan for the
    latest previous backup which contains the file. If the file has been written to since that backup, the file is
    backed up, otherwise it is skipped. (If there are no previous backups, all files are backed up.)
    Note that if you mess with files' last write times, or mess with the system clock, or your timezone is
    particularly strange, this tool may not work as expected.

    Note that matching of paths is done literally and is case insensitive. "Literally" meaning that path aliasing (for
    example symbolic links) is probably not handled. The case insensitivity is because Windows uses case insensitive
    paths.
    This applies to matching previous backups based on the source directory, and for matching excluded paths.

    Please see the BACKUP_SPECS.txt file for specific technical information on how the backups are stored.


Error Handling
    Since this tool performs a lot of file operations, there are many oppurtunities for unavoidable errors to occur.
    In general, this tool tries to back up as much of the source directory as possible, even when errors occur.
    If a directory or file can't be read, it will simply be skipped (this will be logged to the console).
    In very few cases an error may occur that prevents the backup from continuing. In such cases, large amounts of
    files may be skipped (which will also be logged to the console).
    The tool is designed to fail securely. In almost all error cases, the backup shall remain in a consistent, albeit
    perhaps incomplete, state. In the worst error case, some files will be backed up, but metadata for the backup won't
    be written, so the backup won't be used during the next backup.


Process Return Codes
    0 - Backup completed successfully, and no directories or files were skipped due to errors.
    1 - The backup completed successfully, but some directories or files were skipped, or backup metadata could not be
        written, due to filesystem errors.
    2 - The command line arguments are invalid.
    3 - The backup was aborted due to a runtime error, no files were backed up.
    4 - The backup was aborted due to a programmer error (sorry in advance).
