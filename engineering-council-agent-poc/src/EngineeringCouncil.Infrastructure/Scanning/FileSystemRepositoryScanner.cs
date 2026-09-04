using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineeringCouncil.Infrastructure.Scanning;

/// <summary>
/// Read-only scanner over the local file system.
/// It never opens files for writing and never mutates the target tree — it only
/// enumerates and reads. Directories in <see cref="ScanOptions.IgnoredDirectories"/>
/// (bin, obj, .git, node_modules, packages, artifacts, .vs) are skipped entirely.
/// </summary>
public sealed class FileSystemRepositoryScanner : IRepositoryScanner
{
    private readonly ILogger<FileSystemRepositoryScanner> _logger;

    public FileSystemRepositoryScanner(ILogger<FileSystemRepositoryScanner>? logger = null)
        => _logger = logger ?? NullLogger<FileSystemRepositoryScanner>.Instance;

    public async Task<RepositorySnapshot> ScanAsync(
        string rootPath,
        ScanOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path is required.", nameof(rootPath));

        var root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Target path does not exist: {root}");

        _logger.LogInformation("Scanning repository (read-only): {Root}", root);

        var files = new List<ScannedFile>();
        var countByExt = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var solutionFiles = new List<string>();
        var projectFiles = new List<string>();
        long totalSize = 0;
        var contentLoaded = 0;

        foreach (var absolutePath in EnumerateFiles(root, options, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = ToRelative(root, absolutePath);
            var ext = Path.GetExtension(absolutePath).ToLowerInvariant();

            long size;
            try
            {
                size = new FileInfo(absolutePath).Length;
            }
            catch (IOException)
            {
                continue; // skip files we cannot stat
            }

            totalSize += size;
            countByExt[ext] = countByExt.GetValueOrDefault(ext) + 1;

            if (ext == ".sln") solutionFiles.Add(relative);
            else if (ext == ".csproj") projectFiles.Add(relative);

            string? content = null;
            var lineCount = 0;

            var shouldLoad =
                options.LoadContent &&
                options.IncludedExtensions.Contains(ext) &&
                size <= options.MaxFileSizeBytesForContent &&
                contentLoaded < options.MaxFilesWithContent;

            if (shouldLoad)
            {
                try
                {
                    content = await File.ReadAllTextAsync(absolutePath, cancellationToken)
                        .ConfigureAwait(false);
                    lineCount = CountLines(content);
                    contentLoaded++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogDebug(ex, "Skipping unreadable file {File}", relative);
                }
            }

            files.Add(new ScannedFile
            {
                RelativePath = relative,
                Extension = ext,
                SizeBytes = size,
                LineCount = lineCount,
                Content = content
            });
        }

        var solutionName = solutionFiles.Count > 0
            ? Path.GetFileNameWithoutExtension(solutionFiles[0])
            : new DirectoryInfo(root).Name;

        var (branch, commit) = GitProbe.Probe(root);

        _logger.LogInformation(
            "Scan complete: {Files} files, {WithContent} with content, {Projects} projects",
            files.Count, contentLoaded, projectFiles.Count);

        return new RepositorySnapshot
        {
            RootPath = root,
            SolutionName = solutionName,
            Branch = branch,
            Commit = commit,
            Files = files,
            SolutionFiles = solutionFiles,
            ProjectFiles = projectFiles,
            FileCountByExtension = countByExt,
            TotalSizeBytes = totalSize
        };
    }

    private IEnumerable<string> EnumerateFiles(
        string root,
        ScanOptions options,
        CancellationToken cancellationToken)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = stack.Pop();

            string[] subDirs;
            try
            {
                subDirs = Directory.GetDirectories(current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var dir in subDirs)
            {
                var name = Path.GetFileName(dir);
                if (options.IgnoredDirectories.Contains(name))
                {
                    _logger.LogTrace("Ignoring directory {Dir}", name);
                    continue;
                }
                stack.Push(dir);
            }

            string[] filesInDir;
            try
            {
                filesInDir = Directory.GetFiles(current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in filesInDir)
                yield return file;
        }
    }

    private static string ToRelative(string root, string absolutePath)
        => Path.GetRelativePath(root, absolutePath).Replace('\\', '/');

    private static int CountLines(string content)
    {
        if (string.IsNullOrEmpty(content)) return 0;
        var count = 1;
        foreach (var c in content)
            if (c == '\n') count++;
        return count;
    }
}
