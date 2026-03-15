using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProcessRunner
{
    /// <summary>
    /// 管道进程执行器 - 将两个命令通过管道连接
    /// 现在内部使用 MultiPipedProcessExecutor 支持多层管道
    /// </summary>
    internal class PipedProcessExecutor
    {
        private readonly ProcessCommand _sourceCommand;
        private readonly ProcessCommand _targetCommand;
        private readonly MultiPipedProcessExecutor _multiExecutor;

        public PipedProcessExecutor(ProcessCommand sourceCommand, ProcessCommand targetCommand)
        {
            _sourceCommand = sourceCommand ?? throw new ArgumentNullException(nameof(sourceCommand));
            _targetCommand = targetCommand ?? throw new ArgumentNullException(nameof(targetCommand));

            // 使用新的多层管道执行器
            _multiExecutor = new MultiPipedProcessExecutor(
                new List<ProcessCommand> { _sourceCommand, _targetCommand },
                Encoding.Default);
        }

        /// <summary>
        /// 执行管道命令
        /// </summary>
        public async Task<ProcessResult> ExecuteAsync(Encoding encoding, CancellationToken cancellationToken = default)
        {
            return await _multiExecutor.ExecuteAsync(cancellationToken);
        }
    }
}
