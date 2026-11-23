using System.Data.Common;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// WHERE 条件翻译结果：SQL 片段（不含 WHERE 关键字）与按出现顺序排列的参数清单。
    /// </summary>
    internal sealed class WhereClauseTranslation
    {
        /// <summary>
        /// WHERE 条件 SQL 片段（不含 WHERE 关键字）。
        /// </summary>
        public string WhereClause { get; internal set; }

        /// <summary>
        /// 翻译产生的参数（按 SQL 片段中的引用顺序排列），ParameterName 已含提供程序前缀，可直接加入 DbCommand。
        /// </summary>
        public List<DbParameter> Parameters { get; internal set; }
    }
}
