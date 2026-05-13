namespace Moza.ScLink.Core.Diagnostics;

public interface IFileSystem
{
    public IEnumerable<FileInfo> GetFiles(DirectoryInfo directory, string searchPattern);

    public void Delete(FileInfo file);
}
