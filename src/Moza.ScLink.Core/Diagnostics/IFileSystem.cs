namespace Moza.ScLink.Core.Diagnostics;

public interface IFileSystem
{
    IEnumerable<FileInfo> GetFiles(DirectoryInfo directory, string searchPattern);

    void Delete(FileInfo file);
}
