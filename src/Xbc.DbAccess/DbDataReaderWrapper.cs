using System.Collections;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库数据读取器包装类，封装内部读取器、连接和命令并管理其生命周期。
    /// <para>纯透传设计：不做逐访问释放守卫，释放后的访问行为由内部读取器的 ADO.NET 契约决定
    /// （多数提供程序抛 <see cref="ObjectDisposedException"/>）；Dispose 幂等由 <see cref="DisposableState"/> 保证。</para>
    /// </summary>
#if !NETFRAMEWORK
    internal class DbDataReaderWrapper : DbDataReader, IDbColumnSchemaGenerator
#else
    internal class DbDataReaderWrapper : DbDataReader
#endif
    {
        private readonly DbDataReader innerDataReader;// 内部数据读取器
        private readonly DbConnection innerConnection;// 内部数据库连接
        private readonly DbCommand innerCommand;// 内部命令（用于管理生命周期）
        private readonly bool manageConnection;// 是否管理连接的释放
        private readonly DisposableState disposableState = new DisposableState();// 仅用于 Dispose 幂等（释放权竞争），不做逐访问守卫

        /// <summary>
        /// 数据读取器的唯一标识符。
        /// </summary>
        public Guid ReaderId { get; } = Guid.NewGuid();

        /// <summary>
        /// 读取器的嵌套深度。
        /// </summary>
        public override int Depth
        {
            get
            {
                return innerDataReader.Depth;
            }
        }

        /// <summary>
        /// 字段数量。
        /// </summary>
        public override int FieldCount
        {
            get
            {
                return innerDataReader.FieldCount;
            }
        }

        /// <summary>
        /// 是否有数据行。
        /// </summary>
        public override bool HasRows
        {
            get
            {
                return innerDataReader.HasRows;
            }
        }

        /// <summary>
        /// 读取器是否已关闭。
        /// </summary>
        /// <remarks>遵循 ADO.NET 契约：释放后返回 <c>true</c> 而不抛异常，故不做释放守卫。</remarks>
        public override bool IsClosed => innerDataReader.IsClosed;

        /// <summary>
        /// 受影响的记录数。
        /// </summary>
        /// <remarks>遵循 ADO.NET 契约：最终值须在读取器关闭后方可读取（如经 ExecuteReader 执行增删改语句后取受影响行数），
        /// 释放后仍可访问，故不做释放守卫。</remarks>
        public override int RecordsAffected => innerDataReader.RecordsAffected;

        /// <summary>
        /// 获取指定名称列的值。
        /// </summary>
        /// <param name="name">列名。</param>
        /// <returns></returns>
        public override object this[string name]
        {
            get
            {
                return innerDataReader[name];
            }
        }

        /// <summary>
        /// 获取指定序数列的值。
        /// </summary>
        /// <param name="ordinal">列序数。</param>
        /// <returns></returns>
        public override object this[int ordinal]
        {
            get
            {
                return innerDataReader[ordinal];
            }
        }

        /// <summary>
        /// 初始化 <see cref="DbDataReaderWrapper"/> 类的新实例。
        /// </summary>
        /// <param name="innerDataReader">内部数据读取器。</param>
        /// <param name="innerConnection">内部数据库连接。</param>
        /// <param name="manageConnection">是否管理连接的释放。</param>
        public DbDataReaderWrapper(DbDataReader innerDataReader, DbConnection innerConnection, bool manageConnection)
            : this(innerDataReader, innerConnection, null, manageConnection)
        {
        }

        /// <summary>
        /// 初始化 <see cref="DbDataReaderWrapper"/> 类的新实例。
        /// </summary>
        /// <param name="innerDataReader">内部数据读取器。</param>
        /// <param name="innerConnection">内部数据库连接。</param>
        /// <param name="innerCommand">内部数据库命令。</param>
        /// <param name="manageConnection">是否管理连接的释放。</param>
        public DbDataReaderWrapper(DbDataReader innerDataReader, DbConnection innerConnection, DbCommand innerCommand, bool manageConnection)
        {
            this.innerDataReader = innerDataReader ?? throw new ArgumentNullException(nameof(innerDataReader));
            this.innerConnection = innerConnection ?? throw new ArgumentNullException(nameof(innerConnection));
            this.innerCommand = innerCommand;
            this.manageConnection = manageConnection;
        }

        /// <summary>
        /// 关闭数据读取器。
        /// </summary>
        /// <remarks>遵循 ADO.NET 契约：关闭操作幂等（重复 Close 不抛异常），且 <see cref="Dispose(bool)"/> 内部会调用本方法，
        /// 故不做释放守卫（Dispose 窗口期内调用属于正常清理链路）。</remarks>
        public override void Close()
        {
            if (innerDataReader.IsClosed)
                return;

            innerDataReader.Close();
        }

        /// <summary>
        /// 释放资源。
        /// </summary>
        /// <param name="disposing">是否释放托管资源。</param>
        protected override void Dispose(bool disposing)
        {
            // 仅用于 Dispose 幂等；释放后的访问守卫交由内部读取器自身的 ADO.NET 契约行为兜底
            if (!disposableState.TryDispose())
                return;

            try
            {
                if (disposing)
                {
                    // 如果读取器还未关闭，先关闭它；关闭操作抛异常时仅记录日志，
                    // 不让异常跳过后续命令与连接的释放（连接泄漏比关闭失败更严重）
                    if (!innerDataReader.IsClosed)
                    {
                        try
                        {
                            Close();
                        }
                        catch (Exception ex)
                        {
                            DiagnosticLog.Error(nameof(DbDataReaderWrapper), "关闭数据读取器时发生异常：{Exception}。", ex);
                        }
                    }

                    innerDataReader.Dispose();

                    // 释放命令（如果持有）：先清空参数集合解除参数的集合归属，调用方持有的
                    // 参数数组/查询对象可再次提交执行（多数驱动 Dispose 不解除归属）
                    if (innerCommand != null)
                    {
                        innerCommand.Parameters.Clear();
                        innerCommand.Dispose();
                    }

                    // 如果需要管理连接，则在释放读取器时释放连接
                    if (manageConnection)
                        innerConnection.Dispose();
                }
            }
            finally
            {
                disposableState.Complete();
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// 返回列架构信息。
        /// <para>透传内部读取器；基类 <see cref="DbDataReader.GetSchemaTable"/> 默认抛出
        /// <see cref="NotSupportedException"/>，不重写会导致调用方无法获取列架构。</para>
        /// </summary>
        /// <returns>描述列元数据的 <see cref="DataTable"/>。</returns>
        public override DataTable GetSchemaTable()
        {
            return innerDataReader.GetSchemaTable();
        }

        /// <summary>
        /// 获取指定列的数据类型名称。
        /// </summary>
        /// <param name="ordinal">列序数。</param>
        /// <returns></returns>
        public override string GetDataTypeName(int ordinal)
        {
            return innerDataReader.GetDataTypeName(ordinal);
        }

        /// <summary>
        /// 返回循环访问集合的枚举器。
        /// </summary>
        /// <returns></returns>
        public override IEnumerator GetEnumerator()
        {
            return innerDataReader.GetEnumerator();
        }

        /// <summary>
        /// 获取指定列的数据类型。
        /// </summary>
        /// <param name="ordinal">列序数。</param>
        /// <returns></returns>
        public override Type GetFieldType(int ordinal)
        {
            return innerDataReader.GetFieldType(ordinal);
        }

        /// <summary>
        /// 获取指定列的名称。
        /// </summary>
        /// <param name="ordinal">列序数。</param>
        /// <returns></returns>
        public override string GetName(int ordinal)
        {
            return innerDataReader.GetName(ordinal);
        }

        /// <summary>
        /// 获取指定列名的序数。
        /// </summary>
        /// <param name="name">列名。</param>
        /// <returns></returns>
        public override int GetOrdinal(string name)
        {
            return innerDataReader.GetOrdinal(name);
        }

        /// <summary>
        /// 获取指定列的布尔值。
        /// </summary>
        /// <param name="ordinal">列序数。</param>
        /// <returns></returns>
        public override bool GetBoolean(int ordinal)
        {
            return innerDataReader.GetBoolean(ordinal);
        }

        /// <summary>
        /// 获取指定列的字节值。
        /// </summary>
        /// <param name="ordinal">列序数。</param>
        /// <returns></returns>
        public override byte GetByte(int ordinal)
        {
            return innerDataReader.GetByte(ordinal);
        }

        /// <summary>
        /// 从指定列读取字节流。
        /// </summary>
        /// <param name="ordinal">列序数。</param>
        /// <param name="dataOffset">数据中的起始偏移量。</param>
        /// <param name="buffer">目标缓冲区。</param>
        /// <param name="bufferOffset">缓冲区中的起始偏移量。</param>
        /// <param name="length">要读取的字节数。</param>
        /// <returns></returns>
        public override long GetBytes(int ordinal, long dataOffset, byte[] buffer, int bufferOffset, int length)
        {
            return innerDataReader.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
        }

        /// <summary>
        /// 获取指定列的字符值。
        /// </summary>
        /// <param name="ordinal">列序数。</param>
        /// <returns></returns>
        public override char GetChar(int ordinal)
        {
            return innerDataReader.GetChar(ordinal);
        }

        /// <summary>
        /// 从指定列读取字符流。
        /// </summary>
        /// <param name="ordinal">列序数。</param>
        /// <param name="dataOffset">数据中的起始偏移量。</param>
        /// <param name="buffer">目标缓冲区。</param>
        /// <param name="bufferOffset">缓冲区中的起始偏移量。</param>
        /// <param name="length">要读取的字符数。</param>
        /// <returns></returns>
        public override long GetChars(int ordinal, long dataOffset, char[] buffer, int bufferOffset, int length)
        {
            return innerDataReader.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
        }

        /// <summary>
        /// 获取指定列的日期时间值。
        /// </summary>
        /// <param name="ordinal">列序数。</param>
        /// <returns></returns>
        public override DateTime GetDateTime(int ordinal)
        {
            return innerDataReader.GetDateTime(ordinal);
        }

        /// <summary>
        /// 获取指定列的十进制值。
        /// </summary>
        /// <param name="ordinal">列序数。</param>
        /// <returns></returns>
        public override decimal GetDecimal(int ordinal)
        {
            return innerDataReader.GetDecimal(ordinal);
        }

        /// <summary>
        /// 获取指定列的双精度浮点值。
        /// </summary>
        /// <param name="ordinal">列序数。</param>
        /// <returns></returns>
        public override double GetDouble(int ordinal)
        {
            return innerDataReader.GetDouble(ordinal);
        }

        /// <summary>
        /// 获取指定列的单精度浮点值。
        /// </summary>
        /// <param name="ordinal">列序数。</param>
        /// <returns></returns>
        public override float GetFloat(int ordinal)
        {
            return innerDataReader.GetFloat(ordinal);
        }

        /// <summary>
        /// 获取指定列的 GUID 值。
        /// </summary>
        /// <param name="ordinal">列序数。</param>
        /// <returns></returns>
        public override Guid GetGuid(int ordinal)
        {
            return innerDataReader.GetGuid(ordinal);
        }

        /// <summary>
        /// 获取指定列的 16 位整数值。
        /// </summary>
        /// <param name="ordinal">列序数。</param>
        /// <returns></returns>
        public override short GetInt16(int ordinal)
        {
            return innerDataReader.GetInt16(ordinal);
        }

        /// <summary>
        /// 获取指定列的 32 位整数值。
        /// </summary>
        /// <param name="ordinal">列序数。</param>
        /// <returns></returns>
        public override int GetInt32(int ordinal)
        {
            return innerDataReader.GetInt32(ordinal);
        }

        /// <summary>
        /// 获取指定列的 64 位整数值。
        /// </summary>
        /// <param name="ordinal">列序数。</param>
        /// <returns></returns>
        public override long GetInt64(int ordinal)
        {
            return innerDataReader.GetInt64(ordinal);
        }

        /// <summary>
        /// 获取指定列的字符串值。
        /// </summary>
        /// <param name="ordinal">列序数。</param>
        /// <returns></returns>
        public override string GetString(int ordinal)
        {
            return innerDataReader.GetString(ordinal);
        }

        /// <summary>
        /// 获取指定列的值。
        /// </summary>
        /// <param name="ordinal">列序数。</param>
        /// <returns></returns>
        public override object GetValue(int ordinal)
        {
            return innerDataReader.GetValue(ordinal);
        }

        /// <summary>
        /// 获取当前行中所有列的值。
        /// </summary>
        /// <param name="values">目标数组。</param>
        /// <returns></returns>
        public override int GetValues(object[] values)
        {
            return innerDataReader.GetValues(values);
        }

        /// <summary>
        /// 判断指定列是否为 DBNull。
        /// </summary>
        /// <param name="ordinal">列序数。</param>
        /// <returns></returns>
        public override bool IsDBNull(int ordinal)
        {
            return innerDataReader.IsDBNull(ordinal);
        }

        /// <summary>
        /// 前进到下一个结果集。
        /// </summary>
        /// <returns></returns>
        public override bool NextResult()
        {
            return innerDataReader.NextResult();
        }

        /// <summary>
        /// 前进到下一行记录。
        /// </summary>
        /// <returns></returns>
        public override bool Read()
        {
            return innerDataReader.Read();
        }

        /// <summary>
        /// 异步前进到下一行记录。直通内部读取器，避免基类实现为每行包裹 try/catch 与 Task.FromResult；
        /// 内部读取器未重写 ReadAsync 时（如 System.Data.SQLite）自然退化为同步读取。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public override Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<bool>(cancellationToken);

            return innerDataReader.ReadAsync(cancellationToken);
        }

        /// <summary>
        /// 异步前进到下一个结果集。直通内部读取器，与 <see cref="ReadAsync(CancellationToken)"/> 一致，
        /// 避免基类的 Task.FromResult 同步包装。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public override Task<bool> NextResultAsync(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<bool>(cancellationToken);

            return innerDataReader.NextResultAsync(cancellationToken);
        }

#if !NETFRAMEWORK
        /// <summary>
        /// 返回列架构信息（<see cref="DbColumn"/> 模型），实现 <see cref="IDbColumnSchemaGenerator"/> 接口。
        /// <para><c>DbDataReaderExtensions.GetColumnSchema()</c> 扩展方法按本接口识别；内部读取器实现了接口
        /// （如 SqlDataReader）时直接透传其结果。.NET Framework 4.6.2 未提供此接口，故不在此目标框架实现。</para>
        /// </summary>
        /// <returns>列架构描述集合。</returns>
        public ReadOnlyCollection<DbColumn> GetColumnSchema()
        {
            return innerDataReader.GetColumnSchema();
        }
#endif

        /// <summary>
        /// 获取指定列的提供程序特定字段类型。
        /// <para>透传内部读取器；基类默认实现直接抛出 <see cref="NotSupportedException"/>。</para>
        /// </summary>
        /// <param name="ordinal">列序数。</param>
        /// <returns></returns>
        public override Type GetProviderSpecificFieldType(int ordinal)
        {
            return innerDataReader.GetProviderSpecificFieldType(ordinal);
        }

        /// <summary>
        /// 获取指定列的提供程序特定值。
        /// <para>透传内部读取器；基类默认实现直接抛出 <see cref="NotSupportedException"/>。</para>
        /// </summary>
        /// <param name="ordinal">列序数。</param>
        /// <returns></returns>
        public override object GetProviderSpecificValue(int ordinal)
        {
            return innerDataReader.GetProviderSpecificValue(ordinal);
        }

        /// <summary>
        /// 使用提供程序特定类型的列值填充数组。
        /// <para>透传内部读取器；基类默认实现直接抛出 <see cref="NotSupportedException"/>。</para>
        /// </summary>
        /// <param name="values">要填充的对象数组。</param>
        /// <returns>数组中的列值数量。</returns>
        public override int GetProviderSpecificValues(object[] values)
        {
            return innerDataReader.GetProviderSpecificValues(values);
        }

        /// <summary>
        /// 以流的形式获取指定列的数据。
        /// <para>透传内部读取器：基类默认实现基于 GetValue 整块缓冲复制，大 BLOB 场景会失去流式读取优势。</para>
        /// </summary>
        /// <param name="ordinal">列序数。</param>
        /// <returns></returns>
        public override Stream GetStream(int ordinal)
        {
            return innerDataReader.GetStream(ordinal);
        }

        /// <summary>
        /// 以文本读取器的形式获取指定列的数据。
        /// <para>透传内部读取器：基类默认实现基于 GetString 构造字符串副本。</para>
        /// </summary>
        /// <param name="ordinal">列序数。</param>
        /// <returns></returns>
        public override TextReader GetTextReader(int ordinal)
        {
            return innerDataReader.GetTextReader(ordinal);
        }

        /// <summary>
        /// 以强类型方式获取指定列的值。
        /// <para>透传内部读取器：基类默认实现为 <c>(T)GetValue(ordinal)</c>（装箱 + 通用转换），
        /// 透传后可走提供程序原生的强类型快速路径（如 SqlDataReader 的零装箱实现）。</para>
        /// </summary>
        /// <typeparam name="T">值的类型。</typeparam>
        /// <param name="ordinal">列序数。</param>
        /// <returns></returns>
        public override T GetFieldValue<T>(int ordinal)
        {
            return innerDataReader.GetFieldValue<T>(ordinal);
        }

        /// <summary>
        /// 以强类型方式异步获取指定列的值。
        /// <para>透传内部读取器：基类默认实现为 Task.FromResult 同步包装，透传后提供程序的真异步实现才能生效。</para>
        /// </summary>
        /// <typeparam name="T">值的类型。</typeparam>
        /// <param name="ordinal">列序数。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken)
        {
            return innerDataReader.GetFieldValueAsync<T>(ordinal, cancellationToken);
        }

        /// <summary>
        /// 异步判断指定列的值是否为 DBNull。
        /// <para>透传内部读取器：基类默认实现为 Task.FromResult 同步包装，透传后提供程序的真异步实现才能生效。</para>
        /// </summary>
        /// <param name="ordinal">列序数。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken)
        {
            return innerDataReader.IsDBNullAsync(ordinal, cancellationToken);
        }

        /// <summary>
        /// 获取可见字段数量。
        /// <para>透传内部读取器：基类默认实现返回 <see cref="FieldCount"/>，仅碰巧在多数场景成立；
        /// 部分命令行为（如 CommandBehavior.KeyInfo）下提供程序会隐藏列，透传才能忠实反映真实值。</para>
        /// </summary>
        public override int VisibleFieldCount => innerDataReader.VisibleFieldCount;
    }
}
