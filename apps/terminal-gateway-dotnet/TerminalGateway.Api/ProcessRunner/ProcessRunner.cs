using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ProcessRunner
{
    /// <summary>
    /// ProcessRunner库的静态入口类，提供类似CliWrap的简洁API
    /// </summary>
    public static class ProcessRunner
    {
        // 用于验证Shell命令的安全字符白名单
        private static readonly Regex SafeShellArgumentRegex = new Regex(
            @"^[\w\s\-_./\\:@""]+$",
            RegexOptions.Compiled);

        // 危险字符和模式的黑名单
        private static readonly string[] DangerousPatterns = new[]
        {
            "&", "|", ";", "$", "`", "(", ")", "{", "}", "<", ">",
            "&&", "||", ";;", "|&", "&|",
            "$(", "${", "`",
        };

        /// <summary>
        /// 创建一个新的进程命令
        /// </summary>
        /// <param name="target">要执行的目标程序</param>
        /// <returns>可配置的进程命令</returns>
        public static ProcessCommand Wrap(string target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            return new ProcessCommand(target);
        }

        /// <summary>
        /// 创建跨平台的Shell命令
        /// 注意：此方法存在命令注入风险，如果命令参数来自用户输入，请使用 Wrap().AddArguments() 代替
        /// </summary>
        /// <param name="command">要执行的shell命令</param>
        /// <returns>可配置的进程命令</returns>
        public static ProcessCommand Shell(string command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));

            // 验证命令不包含危险的注入模式
            ValidateShellCommand(command);

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                return Wrap("cmd.exe")
                    .AddArguments("/c", command)
                    .SetEnvironmentVariable("COMSPEC", Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe");
            }
            else
            {
                return Wrap("/bin/sh")
                    .AddArguments("-c", command);
            }
        }

        /// <summary>
        /// 使用参数列表安全地创建Shell命令（推荐方式）
        /// </summary>
        /// <param name="command">要执行的命令</param>
        /// <param name="arguments">命令参数数组</param>
        /// <returns>可配置的进程命令</returns>
        public static ProcessCommand ShellSafe(string command, params string[] arguments)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));

            // 验证命令名安全性
            ValidateCommandName(command);

            // 验证所有参数安全性
            foreach (var arg in arguments)
            {
                ValidateShellArgument(arg);
            }

            var cmd = Wrap(command);
            if (arguments.Length > 0)
            {
                cmd.AddArguments(arguments);
            }
            return cmd;
        }

        /// <summary>
        /// 创建交互式Shell会话
        /// </summary>
        /// <returns>可配置的进程命令</returns>
        public static ProcessCommand InteractiveShell()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                return Wrap("cmd.exe");
            }
            else
            {
                return Wrap("/bin/bash")
                    .AddArguments("--norc", "--noprofile");
            }
        }

        /// <summary>
        /// 执行简单的命令并返回输出
        /// </summary>
        /// <param name="target">目标程序</param>
        /// <param name="arguments">命令行参数</param>
        /// <returns>命令执行结果</returns>
        public static async Task<ProcessResult> ExecuteAsync(string target, params string[] arguments)
        {
            return await Wrap(target).AddArguments(arguments).ExecuteAsync();
        }

        /// <summary>
        /// 执行简单的Shell命令并返回输出
        /// 注意：如果命令包含用户输入，存在命令注入风险，请谨慎使用
        /// </summary>
        /// <param name="command">Shell命令</param>
        /// <returns>命令执行结果</returns>
        public static async Task<ProcessResult> ExecuteShellAsync(string command)
        {
            return await Shell(command).ExecuteAsync();
        }

        #region 安全验证方法

        /// <summary>
        /// 验证Shell命令不包含危险的注入模式
        /// </summary>
        private static void ValidateShellCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                throw new ArgumentException("命令不能为空或空白", nameof(command));

            // 检查危险模式
            foreach (var pattern in DangerousPatterns)
            {
                if (command.Contains(pattern))
                {
                    throw new ArgumentException(
                        $"命令包含潜在危险字符或模式: '{pattern}'。如果确实需要使用这些字符，请使用 Wrap().AddArguments() 方法代替。",
                        nameof(command));
                }
            }

            // 检查换行符（可能用于多行注入）
            if (command.Contains('\n') || command.Contains('\r'))
            {
                throw new ArgumentException(
                    "命令包含换行符，可能用于命令注入攻击",
                    nameof(command));
            }
        }

        /// <summary>
        /// 验证命令名安全性
        /// </summary>
        private static void ValidateCommandName(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                throw new ArgumentException("命令名不能为空或空白", nameof(command));

            // 检查是否包含路径遍历或危险字符
            if (command.Contains("..") || command.Contains("./") || command.Contains(".\\"))
            {
                throw new ArgumentException(
                    "命令名包含潜在危险的路径遍历模式",
                    nameof(command));
            }

            // 只允许基本命令名或绝对路径
            if (!IsValidCommandName(command))
            {
                throw new ArgumentException(
                    "命令名包含非法字符。只允许字母、数字、下划线、连字符和点号",
                    nameof(command));
            }
        }

        /// <summary>
        /// 验证Shell参数安全性
        /// </summary>
        private static void ValidateShellArgument(string argument)
        {
            if (argument == null)
                throw new ArgumentNullException(nameof(argument));

            // 检查危险模式
            foreach (var pattern in DangerousPatterns)
            {
                if (argument.Contains(pattern))
                {
                    throw new ArgumentException(
                        $"参数包含潜在危险字符或模式: '{pattern}'",
                        nameof(argument));
                }
            }

            // 检查换行符
            if (argument.Contains('\n') || argument.Contains('\r'))
            {
                throw new ArgumentException(
                    "参数包含换行符",
                    nameof(argument));
            }
        }

        /// <summary>
        /// 检查命令名是否只包含允许的字符
        /// </summary>
        private static bool IsValidCommandName(string command)
        {
            // 允许绝对路径（Unix: /bin/ls, Windows: C:\Windows\System32\cmd.exe）
            // 或相对路径（不含 ..）
            // 或简单命令名
            foreach (char c in command)
            {
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-' && c != '.' &&
                    c != '\\' && c != '/' && c != ':' && c != ' ')
                {
                    return false;
                }
            }
            return true;
        }

        #endregion
    }
}