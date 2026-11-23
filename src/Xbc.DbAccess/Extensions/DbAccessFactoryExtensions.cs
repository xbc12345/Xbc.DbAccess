using System.Data.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// <see cref="IDbAccessFactory"/> 的扩展方法。
    /// </summary>
    public static class DbAccessFactoryExtensions
    {
        /// <summary>
        /// 创建原生提供程序连接（连接字符串取自工厂配置，未打开，打开与释放由调用方负责）。
        /// <para>返回的是框架体系外的自管连接，不参与连接池、连接追踪与事务协调；典型用途是配合
        /// <see cref="IDbBulkInserter.InsertAsync(DbConnection, DbTransaction, BulkInsertContext, BulkInsertOptions, CancellationToken)"/>
        /// 直连执行原生 bulk 写入，需要框架托管连接与事务时请改用 <see cref="IDbAccessFactory.Create"/>。</para>
        /// </summary>
        /// <param name="factory">数据库访问工厂。</param>
        /// <returns>原生提供程序连接（连接字符串已设置，未打开）。</returns>
        public static DbConnection CreateNativeConnection(this IDbAccessFactory factory)
        {
            Guard.ThrowIfNull(factory, nameof(factory));

            var connection = factory.ProviderFactory.DbProviderFactory.CreateConnection();
            connection.ConnectionString = factory.ConnectionManager.ConnectionString;
            return connection;
        }
    }
}
