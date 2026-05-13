namespace Moza.ScLink.Core.Diagnostics;

/// <summary>Abstraction over file-system operations used by retention and diagnostic workers.</summary>
public interface IFileSystem
{
    /// <summary>Returns files in <paramref name="directory"/> matching <paramref name="searchPattern"/>.</summary>
    public IEnumerable<FileInfo> GetFiles(DirectoryInfo directory, string searchPattern);

    /// <summary>Deletes the specified file.</summary>
    public void Delete(FileInfo file);
}
