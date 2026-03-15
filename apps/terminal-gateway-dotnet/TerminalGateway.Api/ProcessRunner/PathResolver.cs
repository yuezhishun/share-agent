using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ProcessRunner;

/// <summary>
/// 可执行文件路径解析器，负责在环境变量和系统PATH中查找可执行文件
/// </summary>
public class PathResolver
{
    /// <summary>
    /// 在用户环境变量和系统PATH中查找可执行文件
    /// </summary>
    /// <param name="fileName">要查找的可执行文件名</param>
    /// <param name="userEnvironmentVariables">用户自定义环境变量</param>
    /// <returns>找到的完整路径，如果未找到则返回null</returns>
    public string? FindExecutableInPath(string fileName, IReadOnlyDictionary<string, string> userEnvironmentVariables)
    {
        if (string.IsNullOrEmpty(fileName))
            return null;

        // 如果是完整路径且文件存在，直接返回
        if (File.Exists(fileName))
            return Path.GetFullPath(fileName);

        // 第一阶段：用户环境变量值中的直接可执行文件查找
        var directMatch = FindInUserEnvironmentVariables(fileName, userEnvironmentVariables);
        if (directMatch != null)
            return directMatch;

        // 第二阶段：用户环境变量值中的文件夹路径查找
        var directoryMatch = FindInUserEnvironmentDirectories(fileName, userEnvironmentVariables);
        if (directoryMatch != null)
            return directoryMatch;

        // 第三阶段：系统 PATH 查找
        return SearchInSystemPath(fileName);
    }

    /// <summary>
    /// 在用户环境变量值中直接查找可执行文件
    /// </summary>
    private string? FindInUserEnvironmentVariables(string fileName, IReadOnlyDictionary<string, string> userEnvironmentVariables)
    {
        if (userEnvironmentVariables == null || userEnvironmentVariables.Count == 0)
            return null;

        foreach (var kvp in userEnvironmentVariables)
        {
            var envValue = kvp.Value;
            if (string.IsNullOrEmpty(envValue))
                continue;

            // 检查环境变量值是否直接指向目标可执行文件
            if (File.Exists(envValue))
            {
                var envFileName = Path.GetFileNameWithoutExtension(envValue);
                if (string.Equals(envFileName, fileName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(envValue), fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFullPath(envValue);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 在用户环境变量值中的文件夹路径里查找可执行文件
    /// </summary>
    private string? FindInUserEnvironmentDirectories(string fileName, IReadOnlyDictionary<string, string> userEnvironmentVariables)
    {
        if (userEnvironmentVariables == null || userEnvironmentVariables.Count == 0)
            return null;

        var searchDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in userEnvironmentVariables)
        {
            var envValue = kvp.Value;
            if (string.IsNullOrEmpty(envValue))
                continue;

            // 从环境变量值中提取可能的文件夹路径
            var directories = ExtractDirectoriesFromEnvironmentValue(envValue);
            foreach (var dir in directories)
            {
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    searchDirectories.Add(dir);
                }
            }
        }

        // 在提取的目录中查找可执行文件
        foreach (var dir in searchDirectories)
        {
            var foundPath = SearchInDirectory(fileName, dir);
            if (foundPath != null)
                return foundPath;
        }

        return null;
    }

    /// <summary>
    /// 从环境变量值中提取可能的文件夹路径
    /// </summary>
    private static List<string> ExtractDirectoriesFromEnvironmentValue(string envValue)
    {
        var directories = new List<string>();

        if (string.IsNullOrEmpty(envValue))
            return directories;

        // 检查是否是完整的文件路径，如果是则提取其目录
        if (File.Exists(envValue))
        {
            var directory = Path.GetDirectoryName(envValue);
            if (!string.IsNullOrEmpty(directory))
                directories.Add(directory);
            return directories;
        }

        // 检查是否是目录路径
        if (Directory.Exists(envValue))
        {
            directories.Add(envValue);
            return directories;
        }

        // 按分隔符分割多个路径（Windows使用;，Unix使用:）
        var separators = Environment.OSVersion.Platform == PlatformID.Win32NT ? new[] { ';' } : new[] { ':' };
        var paths = envValue.Split(separators, StringSplitOptions.RemoveEmptyEntries);

        foreach (var path in paths)
        {
            var trimmedPath = path.Trim();
            if (!string.IsNullOrEmpty(trimmedPath))
            {
                if (File.Exists(trimmedPath))
                {
                    var directory = Path.GetDirectoryName(trimmedPath);
                    if (!string.IsNullOrEmpty(directory))
                        directories.Add(directory);
                }
                else if (Directory.Exists(trimmedPath))
                {
                    directories.Add(trimmedPath);
                }
            }
        }

        return directories;
    }

    /// <summary>
    /// 在指定目录中查找可执行文件
    /// </summary>
    private static string? SearchInDirectory(string fileName, string directory)
    {
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            var extensions = GetWindowsExecutableExtensions();

            foreach (var ext in extensions)
            {
                var fullPath = Path.Combine(directory, fileName + ext);
                if (File.Exists(fullPath))
                    return fullPath;
            }

            // 也检查不带扩展名的文件
            var fullPathWithoutExt = Path.Combine(directory, fileName);
            if (File.Exists(fullPathWithoutExt))
                return fullPathWithoutExt;
        }
        else
        {
            var fullPath = Path.Combine(directory, fileName);
            if (File.Exists(fullPath))
                return fullPath;
        }

        return null;
    }

    /// <summary>
    /// 在系统 PATH 中查找可执行文件
    /// </summary>
    private static string? SearchInSystemPath(string fileName)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var separators = Environment.OSVersion.Platform == PlatformID.Win32NT ? new[] { ';' } : new[] { ':' };
        var pathDirectories = pathVar.Split(separators, StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in pathDirectories)
        {
            var foundPath = SearchInDirectory(fileName, dir.Trim());
            if (foundPath != null)
                return foundPath;
        }

        return null;
    }

    /// <summary>
    /// 获取Windows可执行文件扩展名列表
    /// </summary>
    private static string[] GetWindowsExecutableExtensions()
    {
        var pathExt = Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";
        return pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// 检查文件是否是可执行文件
    /// </summary>
    public static bool IsExecutable(string filePath)
    {
        if (!File.Exists(filePath))
            return false;

        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            var extensions = GetWindowsExecutableExtensions();
            var fileExtension = Path.GetExtension(filePath).ToUpperInvariant();
            return extensions.Contains(fileExtension);
        }
        else
        {
            // 在Unix系统上，检查文件是否有执行权限
            try
            {
                var fileInfo = new FileInfo(filePath);
                return (fileInfo.Attributes & FileAttributes.System) != FileAttributes.System;
            }
            catch
            {
                return false;
            }
        }
    }
}