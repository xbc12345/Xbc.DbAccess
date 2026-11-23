using System.Data;
using System.Data.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库参数构建器接口，负责创建数据库参数。
    /// </summary>
    public interface IDbParameterBuilder
    {
        /// <summary>
        /// 数据库参数前缀。
        /// </summary>
        string ParameterPrefix { get; }

        /// <summary>
        /// 获取带前缀的参数名。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <returns></returns>
        string GetParameterName(string name);

        /// <summary>
        /// 创建输入参数。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <param name="value">参数值。</param>
        /// <param name="dbType">参数数据库类型。</param>
        /// <returns></returns>
        DbParameter CreateParameter(string name, object value, DbType? dbType = null);

        /// <summary>
        /// 创建数据库参数。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <param name="value">参数值。</param>
        /// <param name="dbType">参数数据库类型。</param>
        /// <param name="direction">参数方向。</param>
        /// <param name="size">参数长度（字符/字节数）。输出类方向的字符串/二进制参数必须指定长度（未指定时使用默认值 4000）。</param>
        /// <returns></returns>
        DbParameter CreateParameter(string name, object value, DbType? dbType, ParameterDirection direction, int? size = null);
    }
}
