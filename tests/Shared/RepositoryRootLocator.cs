using System.Runtime.CompilerServices;

namespace IncidentCompass.TestSupport;

// Shared by both test projects via a <Compile Include> file link (see each project's .csproj) -
// UnitTests and IntegrationTests do not reference one another, so this is the only way for them
// to share a single implementation instead of each test file carrying its own copy.
//
// Different callers previously started their walk from different places (AppContext.BaseDirectory,
// Environment.CurrentDirectory, or the calling source file's directory). All of those starting
// points already live somewhere under the repository tree, so probing all of them and walking
// upward from each until IncidentCompass.slnx is found resolves to the same repository root for
// every caller; it never changes what any caller used to resolve to.
public static class RepositoryRootLocator
{
    public static string Find([CallerFilePath] string sourceFilePath = "")
    {
        foreach (var startPath in new[] { sourceFilePath, AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            if (string.IsNullOrEmpty(startPath))
            {
                continue;
            }

            var directory = File.Exists(startPath) ? new FileInfo(startPath).Directory : new DirectoryInfo(startPath);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "IncidentCompass.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the IncidentCompass repository root.");
    }
}
