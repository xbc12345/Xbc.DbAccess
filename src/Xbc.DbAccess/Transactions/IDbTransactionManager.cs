using System.Data;
using System.Data.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库事务管理器接口，负责事务的创建和生命周期管理。
    /// <para>事务提交或回滚后自动清除当前活动事务的引用。是否支持嵌套事务取决于实现：
    /// 不支持嵌套的实现同一时刻只允许一个活动事务；支持嵌套的实现允许在已有活动事务时创建子事务（保存点实现）。</para>
    /// </summary>
    public interface IDbTransactionManager : IDbConnectionAccessor, IDisposable
    {
        /// <summary>
        /// 获取当前活动事务，若无活动事务则返回 null。
        /// </summary>
        DbTransaction CurrentTransaction { get; }

        /// <summary>
        /// 异步开启事务。
        /// <para>获取连接与保存点语句异步执行；ADO.NET 无异步开启事务的标准 API，开启事务本身为同步调用。</para>
        /// </summary>
        /// <param name="transactionId">事务唯一标识，由调用者在非 async 入口生成。</param>
        /// <param name="isolationLevel">事务隔离级别，默认为 <see cref="IsolationLevel.Unspecified"/>。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        Task<DbTransaction> BeginTransactionAsync(Guid transactionId, IsolationLevel isolationLevel = IsolationLevel.Unspecified, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步使用指定的连接开启事务，事务完成后不释放该连接。
        /// <para>创建子事务（保存点）时异步执行保存点语句；ADO.NET 无异步开启事务的标准 API，开启事务本身为同步调用。</para>
        /// </summary>
        /// <param name="connection">数据库连接。</param>
        /// <param name="transactionId">事务唯一标识，由调用者在非 async 入口生成。</param>
        /// <param name="isolationLevel">事务隔离级别，默认为 <see cref="IsolationLevel.Unspecified"/>。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        Task<DbTransaction> BeginTransactionAsync(DbConnection connection, Guid transactionId, IsolationLevel isolationLevel = IsolationLevel.Unspecified, CancellationToken cancellationToken = default);
    }
}
