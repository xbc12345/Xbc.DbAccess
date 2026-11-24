using System.Data;
using System.Data.Common;

namespace Xbc.DbAccess.Benchmarks.Fakes
{
    /// <summary>
    /// 模拟数据库参数，配合 FakeDbCommand/FakeDbProviderFactory 构成无数据库的执行管道。
    /// </summary>
    public sealed class FakeDbParameter : DbParameter
    {
        /// <inheritdoc />
        public override DbType DbType { get; set; }

        /// <inheritdoc />
        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

        /// <inheritdoc />
        public override bool IsNullable { get; set; }

        /// <inheritdoc />
        public override string ParameterName { get; set; }

        /// <inheritdoc />
        public override int Size { get; set; }

        /// <inheritdoc />
        public override string SourceColumn { get; set; } = string.Empty;

        /// <inheritdoc />
        public override bool SourceColumnNullMapping { get; set; }

        /// <inheritdoc />
        public override DataRowVersion SourceVersion { get; set; } = DataRowVersion.Current;

        /// <inheritdoc />
        public override object Value { get; set; }

        /// <inheritdoc />
        public override void ResetDbType()
        {
            DbType = DbType.Object;
        }
    }
}
