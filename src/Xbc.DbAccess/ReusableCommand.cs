using System.Data;
using System.Data.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 可复用命令租约：在同一连接作用域/事务内反复执行时复用连接与命令，执行流程由 <see cref="DbAccess"/> 的执行核心承担，
    /// 本类仅持有复用状态与命令配置规则。
    /// <para>复用规则见 <see cref="Configure"/>；使用契约见 <see cref="DbAccess.CreateReusableCommand"/>。</para>
    /// </summary>
    internal sealed class ReusableCommand : IDisposable
    {
        private readonly DbAccess owner;
        private DbConnection connection;
        private DbCommand command;
        private string currentSql;
        private DbTransaction appliedTransaction;

        /// <summary>
        /// 初始化可复用命令租约。
        /// </summary>
        /// <param name="owner">所属的数据库访问实例。</param>
        internal ReusableCommand(DbAccess owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        /// <summary>
        /// 所属的数据库访问实例。
        /// </summary>
        internal DbAccess Owner => owner;

        /// <summary>
        /// 当前解析的数据库连接：首次执行由执行核心解析并经 <see cref="Configure"/> 记录，此后在同一作用域内复用。
        /// </summary>
        internal DbConnection Connection => connection;

        /// <summary>
        /// 按执行需要配置租约并返回可执行的命令：首次执行时创建（含提供程序级配置）；此后 SQL 文本变化或含非输入方向参数
        /// （如 Oracle RETURNING INTO 输出参数，须全量重建以保证输出值写回调用方持有的参数对象）时全量重建参数，
        /// 事务变化时同步命令绑定的事务；SQL 文本不变且参数全为输入方向时仅重绑参数值。
        /// </summary>
        /// <param name="connection">数据库连接（由执行核心解析）。</param>
        /// <param name="sql">SQL 语句。</param>
        /// <param name="transaction">当前异步调用链上的事务。</param>
        /// <param name="parameters">参数数组。</param>
        /// <returns>配置完成的命令。</returns>
        internal DbCommand Configure(DbConnection connection, string sql, DbTransaction transaction, DbParameter[] parameters)
        {
            this.connection = connection;

            // 首次执行：创建命令（含提供程序级配置，如 Oracle 的 BindByName），记录当前 SQL 与事务
            if (command == null)
            {
                command = owner.CreateConfiguredCommand(connection, sql, CommandType.Text, transaction, parameters);
                currentSql = sql;
                appliedTransaction = transaction;
                return command;
            }

            // 事务变化（如进入新的嵌套事务）：同步命令绑定的事务（框架事务为包装器，解包后绑定）
            if (!ReferenceEquals(appliedTransaction, transaction))
            {
                command.Transaction = transaction is DbTransactionWrapper transactionWrapper
                    ? transactionWrapper.InnerTransaction
                    : transaction;
                appliedTransaction = transaction;
            }

            // SQL 文本变化（如逐条路径在 INSERT 与 SELECT IDENTITY、两类列集间交替）：
            // 更新命令文本并全量重建参数（语句准备由提供程序按新文本重新执行）
            if (!string.Equals(sql, currentSql, StringComparison.Ordinal))
            {
                command.Parameters.Clear();
                command.CommandText = sql;

                if (parameters != null && parameters.Length > 0)
                    command.Parameters.AddRange(parameters);

                currentSql = sql;
                return command;
            }

            // 参数为空：清空参数集合（如无参数的 SELECT IDENTITY 语句）
            if (parameters == null || parameters.Length == 0)
            {
                command.Parameters.Clear();
                return command;
            }

            // 含非输入方向参数（如 Oracle RETURNING INTO 输出参数）或参数数量变化（列集变化）：
            // 全量重建参数集合，保证输出值写回调用方持有的参数对象（值重绑的复制目标会成为陈旧引用）
            if (parameters.Any(parameter => parameter.Direction != ParameterDirection.Input)
                || command.Parameters.Count != parameters.Length)
            {
                command.Parameters.Clear();
                command.Parameters.AddRange(parameters);
                return command;
            }

            // SQL 文本相同且参数全为输入方向（列集一致，参数顺序不变）：仅重绑参数值，
            // 避免参数集合重建与重新绑定的开销（逐条批量插入的热路径）
            for (int i = 0; i < parameters.Length; i++)
            {
                command.Parameters[i].Value = parameters[i].Value;
            }

            return command;
        }

        /// <summary>
        /// 释放资源：释放复用的命令；连接在事务/连接作用域中时交由对应管理器接管，否则归还连接管理器。
        /// </summary>
        public void Dispose()
        {
            // 先清空参数集合解除参数归属（AddRange 进来的调用方参数可再次提交，见 DbAccess.DisposeCommand）
            DbAccess.DisposeCommand(command);
            command = null;
            currentSql = null;

            if (connection != null)
            {
                owner.DisposeDbConnection(connection);
                connection = null;
            }
        }
    }
}
