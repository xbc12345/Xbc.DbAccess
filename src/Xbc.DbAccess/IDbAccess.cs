using System.Data;
using System.Data.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库访问核心接口，提供 SQL 执行、连接作用域与事务管理能力。
    /// <para>封装连接获取/释放、命令创建与执行等底层细节，各成员的连接生命周期约定见成员说明。</para>
    /// </summary>
    public interface IDbAccess : IDisposable
    {
        /// <summary>
        /// 数据库访问提供程序工厂。
        /// </summary>
        DbAccessProviderFactory ProviderFactory { get; }

        /// <summary>
        /// 数据库访问工厂，即创建了自身实例的工厂对象。
        /// </summary>
        IDbAccessFactory Factory { get; }

        /// <summary>
        /// 异步开启连接作用域。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        Task<IDbConnectionScope> BeginConnectionScopeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步开启事务；ADO.NET 的 <see cref="DbConnection"/> 未提供异步开启事务的标准 API，开启事务本身保持同步调用。
        /// <para><b>并发警示</b>：同一实例禁止以 Task.WhenAll 等方式并发调用各自开启事务的方法：分叉的异步流共享执行上下文，
        /// 并发事务会在共享事务栈上相互嵌套为保存点父子事务，产生部分提交。需要并行时请使用独立实例。</para>
        /// </summary>
        /// <param name="isolationLevel">事务隔离级别。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        Task<DbTransaction> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.Unspecified, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步执行非查询 SQL 语句，返回受影响的行数。
        /// </summary>
        /// <param name="sql">SQL 语句。</param>
        /// <param name="parameters">参数数组。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        Task<int> ExecuteNonQueryAsync(string sql, DbParameter[] parameters = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步执行非查询 SQL 语句或存储过程，返回受影响的行数。
        /// </summary>
        /// <param name="sql">SQL 语句或存储过程名称。</param>
        /// <param name="commandType">命令类型。</param>
        /// <param name="parameters">参数数组。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        Task<int> ExecuteNonQueryAsync(string sql, CommandType commandType, DbParameter[] parameters = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步执行查询 SQL 语句，返回结果集中第一行第一列的值。
        /// <para>返回值遵循 ADO.NET 契约：结果集为空（零行）时返回 null，有行但第一列为 NULL 时返回 <see cref="DBNull.Value"/>。</para>
        /// </summary>
        /// <param name="sql">SQL 语句。</param>
        /// <param name="parameters">参数数组。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>第一行第一列的值；结果集为空时为 null，空列时为 <see cref="DBNull.Value"/>。</returns>
        Task<object> ExecuteScalarAsync(string sql, DbParameter[] parameters = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步执行查询 SQL 语句或存储过程，返回结果集中第一行第一列的值。
        /// </summary>
        /// <param name="sql">SQL 语句或存储过程名称。</param>
        /// <param name="commandType">命令类型。</param>
        /// <param name="parameters">参数数组。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        Task<object> ExecuteScalarAsync(string sql, CommandType commandType, DbParameter[] parameters = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步执行查询 SQL 语句，返回 <see cref="DbDataReader"/> 实例。
        /// <para>连接生命周期由框架管理：存在事务或连接作用域时由对应管理器负责，否则连接在读取器释放时
        /// 自动释放或回收到连接池。返回的读取器必须先于所在的连接作用域或事务结束而释放，
        /// 否则作用域释放时连接被回收，读取器将不可用。</para>
        /// </summary>
        /// <param name="sql">SQL 语句。</param>
        /// <param name="parameters">参数数组。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        Task<DbDataReader> ExecuteReaderAsync(string sql, DbParameter[] parameters = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步执行查询 SQL 语句或存储过程，返回 <see cref="DbDataReader"/> 实例。
        /// </summary>
        /// <param name="sql">SQL 语句或存储过程名称。</param>
        /// <param name="commandType">命令类型。</param>
        /// <param name="parameters">参数数组。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        Task<DbDataReader> ExecuteReaderAsync(string sql, CommandType commandType, DbParameter[] parameters = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步执行查询 SQL 语句，返回 <see cref="DataSet"/> 实例。
        /// </summary>
        /// <param name="sql">SQL 语句。</param>
        /// <param name="parameters">参数数组。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        Task<DataSet> ExecuteDataSetAsync(string sql, DbParameter[] parameters = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步执行查询 SQL 语句或存储过程，返回 <see cref="DataSet"/> 实例。
        /// </summary>
        /// <param name="sql">SQL 语句或存储过程名称。</param>
        /// <param name="commandType">命令类型。</param>
        /// <param name="parameters">参数数组。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        Task<DataSet> ExecuteDataSetAsync(string sql, CommandType commandType, DbParameter[] parameters = null, CancellationToken cancellationToken = default);
    }
}
