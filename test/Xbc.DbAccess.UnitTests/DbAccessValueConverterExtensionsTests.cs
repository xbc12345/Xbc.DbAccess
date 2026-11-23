using System;
using Microsoft.Extensions.DependencyInjection;
using Xbc.DbAccess.Entity;
using Xunit;

namespace Xbc.DbAccess.UnitTests
{
    /// <summary>
    /// AddDbAccessValueConverter DI 扩展的单元测试。
    /// <para>全局注册表为静态状态，测试通过 finally 中的 Unregister 清理，避免污染其他测试。</para>
    /// </summary>
    public class DbAccessValueConverterExtensionsTests
    {
        [Fact]
        public void 注册转换器实例后全局注册表生效()
        {
            try
            {
                var services = new ServiceCollection();

                var returned = services.AddDbAccessValueConverter(new TextValueConverter());

                Assert.Same(services, returned);// 返回同一服务集合，支持链式配置
                var from = DbValueConverterRegistry.GetFromConverter(typeof(TextValue));
                var to = DbValueConverterRegistry.GetToConverter(typeof(TextValue));
                Assert.NotNull(from);
                Assert.NotNull(to);

                var value = (TextValue)from("abc");
                Assert.Equal("abc", value.Text);
                Assert.Equal("abc", to(new TextValue { Text = "abc" }));
            }
            finally
            {
                DbValueConverterRegistry.Unregister<TextValue>();
            }
        }

        [Fact]
        public void 以转换委托注册后全局注册表生效()
        {
            try
            {
                new ServiceCollection()
                    .AddDbAccessValueConverter<TextValue>(dbValue => new TextValue { Text = dbValue.ToString() }, value => value.Text);

                var from = DbValueConverterRegistry.GetFromConverter(typeof(TextValue));
                var to = DbValueConverterRegistry.GetToConverter(typeof(TextValue));
                Assert.NotNull(from);
                Assert.NotNull(to);

                var value = (TextValue)from(123);
                Assert.Equal("123", value.Text);
                Assert.Equal("123", to(new TextValue { Text = "123" }));
            }
            finally
            {
                DbValueConverterRegistry.Unregister<TextValue>();
            }
        }

        [Fact]
        public void 参数为null时抛出ArgumentNullException()
        {
            var services = new ServiceCollection();

            Assert.Throws<ArgumentNullException>(() => services.AddDbAccessValueConverter<TextValue>(null));
            Assert.Throws<ArgumentNullException>(() => DbAccessValueConverterServiceCollectionExtensions.AddDbAccessValueConverter(null, new TextValueConverter()));
            Assert.Throws<ArgumentNullException>(() => services.AddDbAccessValueConverter<TextValue>(null, _ => new TextValue()));
            Assert.Throws<ArgumentNullException>(() => services.AddDbAccessValueConverter<TextValue>(_ => new TextValue(), null));
        }

        /// <summary>
        /// 测试用目标属性类型。
        /// </summary>
        private sealed class TextValue
        {
            public string Text { get; set; }
        }

        /// <summary>
        /// 测试用值转换器：数据库字符串与 TextValue 互转。
        /// </summary>
        private sealed class TextValueConverter : IDbValueConverter<TextValue>
        {
            public TextValue ConvertFrom(object dbValue)
            {
                return new TextValue { Text = dbValue.ToString() };
            }

            public object ConvertTo(TextValue value)
            {
                return value.Text;
            }
        }
    }
}
