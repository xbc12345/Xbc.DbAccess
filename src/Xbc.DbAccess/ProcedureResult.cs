using System.Data;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 存储过程执行结果，包含受影响的行数、数据集和输出参数。
    /// </summary>
    public class ProcedureResult
    {
        /// <summary>
        /// 受影响的行数（非查询存储过程使用）。
        /// </summary>
        public int RowsAffected { get; set; }

        /// <summary>
        /// 数据集（查询存储过程使用）。
        /// </summary>
        public DataSet DataSet { get; set; }

        /// <summary>
        /// 输出参数集合。
        /// </summary>
        public DbParameterCollection OutputParameters { get; set; }
    }
}
