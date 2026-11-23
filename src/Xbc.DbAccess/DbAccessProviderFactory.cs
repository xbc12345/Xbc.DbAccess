using System.Data.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库访问提供程序工厂，封装特定数据库的提供程序、参数构建器和 SQL 构建器。
    /// </summary>
    public abstract class DbAccessProviderFactory
    {
        /// <summary>
        /// 数据库提供程序工厂。
        /// </summary>
        public abstract DbProviderFactory DbProviderFactory { get; }

        /// <summary>
        /// 参数构建器。
        /// </summary>
        public abstract IDbParameterBuilder ParameterBuilder { get; }

        /// <summary>
        /// SQL 构建器。
        /// </summary>
        public abstract ISqlBuilder SqlBuilder { get; }

        /// <summary>
        /// SQL 方言。
        /// </summary>
        public abstract ISqlDialect SqlDialect { get; }

        /// <summary>
        /// 保存点 SQL 方言。
        /// </summary>
        public abstract ISavepointSqlDialect SavepointSqlDialect { get; }

        /// <summary>
        /// 原生 bulk 插入器；基类默认返回 null（方言无原生批量写入 API），null 时实体层的 bulk 插入回退多值 INSERT。
        /// 支持原生批量写入的方言覆写返回各自实现（如 SQL Server 的 SqlBulkCopy、PostgreSQL 的 COPY、Oracle 的 ArrayBind）。
        /// </summary>
        public virtual IDbBulkInserter BulkInserter => null;

        /// <summary>
        /// 对刚创建的数据库命令执行提供程序级配置；基类默认无操作。
        /// 需在命令执行前设置提供程序特定属性的方言可覆写（如 ODP.NET 的 <c>BindByName</c>，
        /// 未显式开启时按占位符出现顺序绑定，与命名参数语义不符）。
        /// </summary>
        /// <param name="command">数据库命令。</param>
        public virtual void ConfigureCommand(DbCommand command)
        {
        }

        /// <summary>
        /// 创建数据适配器。
        /// </summary>
        /// <returns></returns>
        public DbDataAdapter CreateDataAdapter()
        {
            return DbProviderFactory.CreateDataAdapter();
        }
    }
}
