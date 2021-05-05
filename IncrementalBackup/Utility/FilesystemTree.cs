using System.Collections.Generic;


namespace IncrementalBackup
{
    /// <summary>
    /// Represents a directory in a filesystem.
    /// </summary>
    class DirectoryNode
    {
        public DirectoryNode(string name) {
            Name = name;
            Files = new();
            Subdirectories = new();
        }

        /// <summary>
        /// The name of the directory (not path).
        /// </summary>
        public string Name;
        /// <summary>
        /// Files directly contained in this directory.
        /// </summary>
        public List<string> Files;
        /// <summary>
        /// Directories directly contained in this directory.
        /// </summary>
        public List<DirectoryNode> Subdirectories;
    }
}
