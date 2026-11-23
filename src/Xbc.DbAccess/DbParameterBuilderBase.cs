using System.Data;
using System.Data.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库参数构建器基类，负责创建数据库参数。
    /// </summary>
    public abstract class DbParameterBuilderBase : IDbParameterBuilder
    {
        private readonly string parameterPrefix;

        /// <summary>
        /// 数据库提供程序工厂。
        /// </summary>
        public abstract DbProviderFactory ProviderFactory { get; }

        /// <summary>
        /// 数据库参数前缀。
        /// </summary>
        public string ParameterPrefix => parameterPrefix;

        /// <summary>
        /// 初始化数据库参数构建器基类。
        /// </summary>
        /// <param name="parameterPrefix">数据库参数前缀。</param>
        protected DbParameterBuilderBase(string parameterPrefix)
        {
            Guard.ThrowIfNull(parameterPrefix, nameof(parameterPrefix));
            this.parameterPrefix = parameterPrefix;
        }

        /// <summary>
        /// 获取带前缀的参数名。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <returns></returns>
        public string GetParameterName(string name)
        {
            Guard.ThrowIfNullOrWhiteSpace(name, nameof(name));

            // 如果参数名称以前缀开头，直接使用；否则添加前缀。
            if (name.StartsWith(ParameterPrefix, StringComparison.Ordinal))
                return name;
            else
                return $"{ParameterPrefix}{name}";
        }

        /// <summary>
        /// 创建输入参数。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <param name="value">参数值。</param>
        /// <param name="dbType">参数数据库类型。</param>
        /// <returns></returns>
        public virtual DbParameter CreateParameter(string name, object value, DbType? dbType = null)
        {
            return CreateParameter(name, value, dbType, ParameterDirection.Input);
        }

        /// <summary>
        /// 创建数据库参数。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <param name="value">参数值。</param>
        /// <param name="dbType">参数数据库类型。</param>
        /// <param name="direction">参数方向。</param>
        /// <param name="size">参数长度（字符/字节数），null 时对输出类方向的字符串/二进制参数使用默认值 4000。</param>
        /// <returns></returns>
        public virtual DbParameter CreateParameter(string name, object value, DbType? dbType, ParameterDirection direction, int? size = null)
        {
            var parameterName = GetParameterName(name);
            var parameter = ProviderFactory.CreateParameter();
            parameter.ParameterName = parameterName;
            parameter.Value = value ?? DBNull.Value;// 空值转换为 DBNull.Value
            parameter.Direction = direction;

            if (dbType.HasValue)
                parameter.DbType = dbType.Value;

            if (size.HasValue)
            {
                // 调用方显式指定的长度（如超长字符串或二进制输出参数），不再应用默认值
                parameter.Size = size.Value;
            }
            else if (direction != ParameterDirection.Input && parameter.Size == 0)
            {
                // 输出类方向的字符串/二进制参数无法从值推导长度，SQL Server 与 Oracle 拒绝 Size=0；
                // 4000 为跨库安全的通用默认长度（MySQL/PostgreSQL 忽略该属性）
                var effectiveDbType = parameter.DbType;
                if (effectiveDbType == DbType.String || effectiveDbType == DbType.AnsiString
                    || effectiveDbType == DbType.StringFixedLength || effectiveDbType == DbType.AnsiStringFixedLength
                    || effectiveDbType == DbType.Binary)
                {
                    parameter.Size = 4000;
                }
            }

            return parameter;
        }
    }
}
