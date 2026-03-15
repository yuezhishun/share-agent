using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProcessRunner
{
    /// <summary>
    /// 管道进程命令 - 包装管道执行器的特殊命令类
    /// 现在支持多层管道
    /// </summary>
    public class PipedProcessCommand : ProcessCommand
    {
        private readonly PipeableProcessCommand _pipeableCommand;

        /// <summary>
        /// 创建管道命令（两层管道）
        /// </summary>
        /// <param name="source">源命令</param>
        /// <param name="target">目标命令</param>
        internal PipedProcessCommand(ProcessCommand source, ProcessCommand target)
            : base("pipe") // 虚拟目标名
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));

            _pipeableCommand = new PipeableProcessCommand(source, null).PipeTo(target);
        }

        /// <summary>
        /// 创建管道命令（从 PipeableProcessCommand）
        /// </summary>
        internal PipedProcessCommand(PipeableProcessCommand pipeableCommand)
            : base("pipe") // 虚拟目标名
        {
            _pipeableCommand = pipeableCommand ?? throw new ArgumentNullException(nameof(pipeableCommand));
        }

        /// <summary>
        /// 执行管道命令
        /// </summary>
        public override Task<ProcessResult> ExecuteAsync(Encoding encoding, CancellationToken cancellationToken = default)
        {
            return _pipeableCommand.ExecuteAsync(encoding, cancellationToken);
        }

        /// <summary>
        /// 以流式方式监听管道执行事件
        /// </summary>
        public new IAsyncEnumerable<ProcessCommandEvent> ListenAsync(CancellationToken cancellationToken = default)
        {
            return _pipeableCommand.ListenAsync(cancellationToken);
        }

        /// <summary>
        /// 以流式方式监听管道执行事件
        /// </summary>
        public new IAsyncEnumerable<ProcessCommandEvent> ListenAsync(Encoding encoding, CancellationToken cancellationToken = default)
        {
            return _pipeableCommand.ListenAsync(encoding, cancellationToken);
        }

        /// <summary>
        /// 将当前管道连接到另一个命令
        /// </summary>
        public new PipeableProcessCommand PipeTo(ProcessCommand target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            return _pipeableCommand.PipeTo(target);
        }

        /// <summary>
        /// 将当前管道连接到另一个命令
        /// </summary>
        public new PipeableProcessCommand PipeTo(string target)
        {
            return PipeTo(new ProcessCommand(target));
        }
    }
}
