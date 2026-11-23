using System;
using System.Data.Common;
using System.Threading;
using Moq;
using Xbc.DbAccess.Entity;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// 全局转换器注册表测试需串行执行（注册/清理是全局静态状态，与其他测试类并行会互相污染）。
    /// </summary>
    [CollectionDefinition("DbValueConverterRegistrySerial", DisableParallelization = true)]
    public class DbValueConverterRegistrySerialCollection
    {
    }

    [Collection("DbValueConverterRegistrySerial")]
    public class DbValueConverterRegistryTests : IDisposable
    {
        #region 测试用的转换器

        public class GuidStringConverter : IDbValueConverter<Guid>
        {
            public Guid ConvertFrom(object dbValue)
            {
                if (dbValue is string str)
                    return Guid.Parse(str);
                return (Guid)dbValue;
            }

            public object ConvertTo(Guid value)
            {
                return value.ToString("N");
            }
        }

        public class DateTimeOffsetConverter : IDbValueConverter<DateTimeOffset>
        {
            public DateTimeOffset ConvertFrom(object dbValue)
            {
                if (dbValue is DateTime dt)
                    return new DateTimeOffset(dt);
                return (DateTimeOffset)dbValue;
            }

            public object ConvertTo(DateTimeOffset value)
            {
                return value.UtcDateTime;
            }
        }

        #endregion

        public DbValueConverterRegistryTests()
        {
            // 每个测试前清理注册表
            UnregisterAll();
        }

        public void Dispose()
        {
            // 类结束后兜底清理，避免污染其他测试类（如 ColumnMetadataTests 读取全局转换器）
            UnregisterAll();
        }

        private static void UnregisterAll()
        {
            DbValueConverterRegistry.Unregister<Guid>();
            DbValueConverterRegistry.Unregister<DateTimeOffset>();
            DbValueConverterRegistry.Unregister<int>();
            DbValueConverterRegistry.Unregister<int?>();
        }

        #region Register<T>(IDbValueConverter<T>) 测试

        [Fact]
        public void Register_IDbValueConverter_应注册双向转换器()
        {
            var converter = new GuidStringConverter();

            DbValueConverterRegistry.Register(converter);

            var fromConverter = DbValueConverterRegistry.GetFromConverter(typeof(Guid));
            var toConverter = DbValueConverterRegistry.GetToConverter(typeof(Guid));

            Assert.NotNull(fromConverter);
            Assert.NotNull(toConverter);
        }

        [Fact]
        public void Register_IDbValueConverter_null转换器_应抛出ArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => DbValueConverterRegistry.Register<Guid>(null));
        }

        [Fact]
        public void Register_IDbValueConverter_重复注册_应覆盖原有转换器()
        {
            var converter1 = new GuidStringConverter();

            DbValueConverterRegistry.Register(converter1);

            // 使用委托方式注册新的转换器来覆盖
            Func<object, Guid> convertFrom = dbValue => new Guid("12345678-1234-1234-1234-123456789012");
            Func<Guid, object> convertTo = value => "overridden";

            DbValueConverterRegistry.Register(convertFrom, convertTo);

            var toConverter = DbValueConverterRegistry.GetToConverter(typeof(Guid));
            var result = toConverter(Guid.NewGuid());

            // 新转换器应该返回 "overridden" 而不是 GUID 字符串
            Assert.Equal("overridden", result);
        }

        #endregion

        #region Register<T>(Func<object, T>, Func<T, object>) 测试

        [Fact]
        public void Register_委托_应注册双向转换器()
        {
            Func<object, int> convertFrom = dbValue => Convert.ToInt32(dbValue) * 2;
            Func<int, object> convertTo = value => value / 2;

            DbValueConverterRegistry.Register(convertFrom, convertTo);

            var fromConverter = DbValueConverterRegistry.GetFromConverter(typeof(int));
            var toConverter = DbValueConverterRegistry.GetToConverter(typeof(int));

            Assert.NotNull(fromConverter);
            Assert.NotNull(toConverter);
        }

        [Fact]
        public void Register_委托_convertFrom为null_应抛出ArgumentNullException()
        {
            Func<int, object> convertTo = value => value;

            Assert.Throws<ArgumentNullException>(() => DbValueConverterRegistry.Register<int>(null, convertTo));
        }

        [Fact]
        public void Register_委托_convertTo为null_应抛出ArgumentNullException()
        {
            Func<object, int> convertFrom = dbValue => Convert.ToInt32(dbValue);

            Assert.Throws<ArgumentNullException>(() => DbValueConverterRegistry.Register(convertFrom, (Func<int, object>)null));
        }

        [Fact]
        public void Register_委托_转换功能应正常工作()
        {
            Func<object, Guid> convertFrom = dbValue => Guid.Parse((string)dbValue);
            Func<Guid, object> convertTo = value => value.ToString("D");

            DbValueConverterRegistry.Register(convertFrom, convertTo);

            var fromConverter = DbValueConverterRegistry.GetFromConverter(typeof(Guid));
            var toConverter = DbValueConverterRegistry.GetToConverter(typeof(Guid));

            var guid = Guid.NewGuid();
            var dbValue = toConverter(guid);
            var convertedBack = fromConverter(dbValue);

            Assert.IsType<string>(dbValue);
            Assert.Equal(guid, convertedBack);
        }

        #endregion

        #region Unregister<T> 测试

        [Fact]
        public void Unregister_已注册类型_应移除转换器()
        {
            var converter = new GuidStringConverter();
            DbValueConverterRegistry.Register(converter);

            DbValueConverterRegistry.Unregister<Guid>();

            var fromConverter = DbValueConverterRegistry.GetFromConverter(typeof(Guid));
            var toConverter = DbValueConverterRegistry.GetToConverter(typeof(Guid));

            Assert.Null(fromConverter);
            Assert.Null(toConverter);
        }

        [Fact]
        public void Unregister_未注册类型_不应抛出异常()
        {
            var exception = Record.Exception(() => DbValueConverterRegistry.Unregister<long>());

            Assert.Null(exception);
        }

        #endregion

        #region GetFromConverter 测试

        [Fact]
        public void GetFromConverter_已注册类型_应返回转换器()
        {
            var converter = new GuidStringConverter();
            DbValueConverterRegistry.Register(converter);

            var fromConverter = DbValueConverterRegistry.GetFromConverter(typeof(Guid));

            Assert.NotNull(fromConverter);
        }

        [Fact]
        public void GetFromConverter_未注册类型_应返回null()
        {
            var fromConverter = DbValueConverterRegistry.GetFromConverter(typeof(long));

            Assert.Null(fromConverter);
        }

        [Fact]
        public void GetFromConverter_null类型_应抛出ArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => DbValueConverterRegistry.GetFromConverter(null));
        }

        [Fact]
        public void GetFromConverter_转换器应正确转换值()
        {
            var converter = new GuidStringConverter();
            DbValueConverterRegistry.Register(converter);

            var fromConverter = DbValueConverterRegistry.GetFromConverter(typeof(Guid));
            var guidString = "12345678-1234-1234-1234-123456789012";
            var result = fromConverter(guidString);

            Assert.IsType<Guid>(result);
            Assert.Equal(Guid.Parse(guidString), result);
        }

        #endregion

        #region GetToConverter 测试

        [Fact]
        public void GetToConverter_已注册类型_应返回转换器()
        {
            var converter = new GuidStringConverter();
            DbValueConverterRegistry.Register(converter);

            var toConverter = DbValueConverterRegistry.GetToConverter(typeof(Guid));

            Assert.NotNull(toConverter);
        }

        [Fact]
        public void GetToConverter_未注册类型_应返回null()
        {
            var toConverter = DbValueConverterRegistry.GetToConverter(typeof(long));

            Assert.Null(toConverter);
        }

        [Fact]
        public void GetToConverter_null类型_应抛出ArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => DbValueConverterRegistry.GetToConverter(null));
        }

        [Fact]
        public void GetToConverter_转换器应正确转换值()
        {
            var converter = new GuidStringConverter();
            DbValueConverterRegistry.Register(converter);

            var toConverter = DbValueConverterRegistry.GetToConverter(typeof(Guid));
            var guid = Guid.NewGuid();
            var result = toConverter(guid);

            Assert.IsType<string>(result);
            Assert.Equal(guid.ToString("N"), result);
        }

        #endregion

        #region 复杂类型转换测试

        [Fact]
        public void Register_复杂类型_DateTimeOffset转换应正常工作()
        {
            var converter = new DateTimeOffsetConverter();
            DbValueConverterRegistry.Register(converter);

            var fromConverter = DbValueConverterRegistry.GetFromConverter(typeof(DateTimeOffset));
            var toConverter = DbValueConverterRegistry.GetToConverter(typeof(DateTimeOffset));

            var now = DateTimeOffset.Now;
            var dbValue = toConverter(now);
            var convertedBack = fromConverter(dbValue);

            Assert.IsType<DateTime>(dbValue);
            Assert.Equal(now, convertedBack);
        }

        [Fact]
        public void Register_值类型转换_应正确处理DBNull()
        {
            Func<object, int?> convertFrom = dbValue => dbValue == DBNull.Value ? null : Convert.ToInt32(dbValue);
            Func<int?, object> convertTo = value => value.HasValue ? value.Value : DBNull.Value;

            DbValueConverterRegistry.Register(convertFrom, convertTo);

            var fromConverter = DbValueConverterRegistry.GetFromConverter(typeof(int?));
            var toConverter = DbValueConverterRegistry.GetToConverter(typeof(int?));

            Assert.NotNull(fromConverter);
            Assert.NotNull(toConverter);

            var nullResult = fromConverter(DBNull.Value);
            Assert.Null(nullResult);
        }

        #endregion

        #region 映射委托缓存联动测试

        /// <summary>
        /// 联动测试用的实体：Amount 列在注册全局转换器前后分别走直接读取与转换器路径。
        /// </summary>
        public class EntityWithAmount
        {
            public int Amount { get; set; }
        }

        /// <summary>
        /// 创建单列（Amount，int）的数据读取器。
        /// </summary>
        /// <param name="amount">Amount 列的值。</param>
        /// <returns>模拟的 DbDataReader。</returns>
        private static DbDataReader CreateAmountReader(int amount)
        {
            var mockReader = new Mock<DbDataReader>();
            mockReader.Setup(r => r.FieldCount).Returns(1);
            mockReader.Setup(r => r.GetName(0)).Returns("Amount");
            mockReader.Setup(r => r.GetFieldType(0)).Returns(typeof(int));
            mockReader.SetupSequence(r => r.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
            mockReader.Setup(r => r.GetInt32(0)).Returns(amount);
            mockReader.Setup(r => r.GetValue(0)).Returns(amount);
            mockReader.Setup(r => r.IsDBNull(0)).Returns(false);
            return mockReader.Object;
        }

        [Fact]
        public async Task Register_映射委托已缓存_注册转换器后缓存应失效并生效()
        {
            // 第一次映射：未注册转换器，映射委托固化直接读取路径（GetInt32）并缓存
            var first = await EntityMapper<EntityWithAmount>.MapListAsync(CreateAmountReader(21));
            Assert.Equal(21, first[0].Amount);

            // 注册全局转换器：数值翻倍
            Func<object, int> convertFrom = dbValue => Convert.ToInt32(dbValue) * 2;
            Func<int, object> convertTo = value => value;
            DbValueConverterRegistry.Register(convertFrom, convertTo);

            // 第二次映射：同一实体与架构，已缓存的映射委托应失效并按转换器路径重建
            var second = await EntityMapper<EntityWithAmount>.MapListAsync(CreateAmountReader(21));
            Assert.Equal(42, second[0].Amount);
        }

        [Fact]
        public async Task Unregister_映射委托已缓存_注销转换器后缓存应失效并回退默认路径()
        {
            // 注册全局转换器：数值翻倍
            Func<object, int> convertFrom = dbValue => Convert.ToInt32(dbValue) * 2;
            Func<int, object> convertTo = value => value;
            DbValueConverterRegistry.Register(convertFrom, convertTo);

            // 第一次映射：固化转换器路径并缓存
            var first = await EntityMapper<EntityWithAmount>.MapListAsync(CreateAmountReader(21));
            Assert.Equal(42, first[0].Amount);

            // 注销转换器
            DbValueConverterRegistry.Unregister<int>();

            // 第二次映射：已缓存的映射委托应失效并回退直接读取路径（GetInt32）
            var second = await EntityMapper<EntityWithAmount>.MapListAsync(CreateAmountReader(21));
            Assert.Equal(21, second[0].Amount);
        }

        #endregion

        #region 变更事件触发测试

        [Fact]
        public void Unregister_未注册类型_不应触发变更事件()
        {
            // 无实际变更时不应触发 Changed 事件，避免无谓的映射缓存全量失效
            int changedCount = 0;
            Action handler = () => changedCount++;
            DbValueConverterRegistry.Changed += handler;
            try
            {
                DbValueConverterRegistry.Unregister<long>();// long 未注册
                DbValueConverterRegistry.Clear();// 注册表为空

                Assert.Equal(0, changedCount);
            }
            finally
            {
                DbValueConverterRegistry.Changed -= handler;
            }
        }

        [Fact]
        public void Unregister_已注册类型_应触发变更事件一次()
        {
            DbValueConverterRegistry.Register(new GuidStringConverter());

            int changedCount = 0;
            Action handler = () => changedCount++;
            DbValueConverterRegistry.Changed += handler;
            try
            {
                DbValueConverterRegistry.Unregister<Guid>();

                Assert.Equal(1, changedCount);
            }
            finally
            {
                DbValueConverterRegistry.Changed -= handler;
            }
        }

        #endregion

        #region 版本号机制测试

        [Fact]
        public void Version_注册与注销转换器_版本号应单调递增()
        {
            var initial = DbValueConverterRegistry.Version;

            DbValueConverterRegistry.Register(new GuidStringConverter());
            var afterFirstRegister = DbValueConverterRegistry.Version;

            DbValueConverterRegistry.Register(new DateTimeOffsetConverter());
            var afterSecondRegister = DbValueConverterRegistry.Version;

            DbValueConverterRegistry.Unregister<Guid>();
            var afterUnregister = DbValueConverterRegistry.Version;

            DbValueConverterRegistry.Clear();
            var afterClear = DbValueConverterRegistry.Version;

            Assert.True(afterFirstRegister > initial, "注册转换器后版本号应递增。");
            Assert.True(afterSecondRegister > afterFirstRegister, "再次注册转换器后版本号应继续递增。");
            Assert.True(afterUnregister > afterSecondRegister, "注销已注册的转换器后版本号应递增。");
            Assert.True(afterClear > afterUnregister, "清空非空注册表后版本号应递增。");
        }

        [Fact]
        public void Version_未发生实际变更_版本号不应变化()
        {
            var initial = DbValueConverterRegistry.Version;

            DbValueConverterRegistry.Unregister<long>();// long 未注册
            DbValueConverterRegistry.Clear();// 注册表为空

            Assert.Equal(initial, DbValueConverterRegistry.Version);
        }

        [Fact]
        public void Register_变更事件触发时_版本号应已递增()
        {
            // 版本号必须先于 Changed 事件递增：事件处理器（ClearAllCache 联动）执行时，
            // 后续查询读到的已是新版本，旧版本键下的缓存条目（含并发回写的）不再被命中
            int versionInHandler = 0;
            Action handler = () => versionInHandler = DbValueConverterRegistry.Version;
            DbValueConverterRegistry.Changed += handler;
            try
            {
                DbValueConverterRegistry.Register(new GuidStringConverter());

                Assert.True(versionInHandler > 0, "变更事件触发时版本号应已完成递增。");
                Assert.Equal(DbValueConverterRegistry.Version, versionInHandler);
            }
            finally
            {
                DbValueConverterRegistry.Changed -= handler;
            }
        }

        [Fact]
        public async Task Register_多轮注册覆盖_映射结果应始终跟随最新转换器()
        {
            // 第一轮：未注册转换器，映射委托走直接读取路径（GetInt32）
            var first = await EntityMapper<EntityWithAmount>.MapListAsync(CreateAmountReader(21));
            Assert.Equal(21, first[0].Amount);

            // 第二轮：注册翻倍转换器，缓存应失效并按转换器路径重建
            Func<object, int> doubleFrom = dbValue => Convert.ToInt32(dbValue) * 2;
            Func<int, object> doubleTo = value => value;
            DbValueConverterRegistry.Register(doubleFrom, doubleTo);
            var second = await EntityMapper<EntityWithAmount>.MapListAsync(CreateAmountReader(21));
            Assert.Equal(42, second[0].Amount);

            // 第三轮：同类型覆盖为三倍转换器（版本号再次递增），映射结果应跟随最新注册
            Func<object, int> tripleFrom = dbValue => Convert.ToInt32(dbValue) * 3;
            Func<int, object> tripleTo = value => value;
            DbValueConverterRegistry.Register(tripleFrom, tripleTo);
            var third = await EntityMapper<EntityWithAmount>.MapListAsync(CreateAmountReader(21));
            Assert.Equal(63, third[0].Amount);
        }

        #endregion
    }
}
