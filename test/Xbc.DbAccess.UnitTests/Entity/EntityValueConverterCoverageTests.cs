using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// 实体列值转换器的读取守卫与写方向归一分支覆盖测试（Stryker NoCoverage 补测，直接调内部静态方法）：
    /// 读取侧（ConvertFrom）有损小数映射显式失败、整数值小数源无损放行；
    /// 写方向（ConvertTo）整数窄化超范围保持原值交存储层裁决、单字符字符串归一为 char、多字符与域外形态直通。
    /// </summary>
    public class EntityValueConverterCoverageTests
    {
        #region ConvertFrom（读取侧：有损映射显式失败）

        [Fact]
        public void ConvertFrom_FractionalDoubleToInteger_Throws()
        {
            // double 3.7 映射 int 目标带非零小数部分：ChangeType 会静默四舍五入产生错数据，读取侧显式失败
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityValueConverter.ConvertFrom(3.7, typeof(int)));

            Assert.Contains("3.7", ex.Message);
            Assert.Contains("非零小数部分", ex.Message);
        }

        [Fact]
        public void ConvertFrom_FractionalDecimalToLong_Throws()
        {
            // decimal 与 double/float 同为小数源：带非零小数部分映射整数族同样显式失败
            Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityValueConverter.ConvertFrom(3.5m, typeof(long)));
        }

        [Fact]
        public void ConvertFrom_ZeroFractionalValue_MapsWithoutLoss()
        {
            // 整数值的小数源（3.0）为无损形态，应正常映射而非误拒
            Assert.Equal(3, (int)XbcDbEntity.EntityValueConverter.ConvertFrom(3.0, typeof(int)));
            Assert.Equal(3L, (long)XbcDbEntity.EntityValueConverter.ConvertFrom(3.0m, typeof(long)));
        }

        #endregion

        #region ConvertTo（写方向：窄化超范围保持原值）

        [Fact]
        public void ConvertTo_LongOverflowIntoInt_KeepsOriginalValue()
        {
            // long 2^31 超出 int 范围：Convert.ChangeType 抛 OverflowException，捕获后保持原值交存储层裁决
            var result = XbcDbEntity.EntityValueConverter.ConvertTo(2_147_483_648L, typeof(int));

            Assert.Equal(2_147_483_648L, (long)result);
        }

        [Fact]
        public void ConvertTo_LongNarrowIntoShort_MapsInValue()
        {
            // 范围内的整数窄化（300 → short）：经 Convert.ChangeType 正常对齐
            var result = XbcDbEntity.EntityValueConverter.ConvertTo(300L, typeof(short));

            Assert.Equal((short)300, (short)result);
        }

        #endregion

        #region ConvertTo（char 列的字符串归一）

        [Fact]
        public void ConvertTo_SingleCharStringToChar_ReturnsChar()
        {
            // 弱类型集合经非泛型 Contains 路径带入的单字符字符串：归一为 char（字符列比较才可命中）
            var result = XbcDbEntity.EntityValueConverter.ConvertTo("M", typeof(char));

            Assert.Equal('M', (char)result);
        }

        [Fact]
        public void ConvertTo_MultiCharStringToChar_KeepsString()
        {
            // 多字符字符串不构成 char 归一：无损表示归一直通原值
            var result = XbcDbEntity.EntityValueConverter.ConvertTo("AB", typeof(char));

            Assert.Equal("AB", (string)result);
        }

        [Fact]
        public void ConvertTo_NumericValueToChar_ReturnsChar()
        {
            // 数值提升值（77）给 char 列：走数值 → char 的域内对齐转换（int 列域外路径对照）
            var result = XbcDbEntity.EntityValueConverter.ConvertTo(77, typeof(char));

            Assert.Equal('M', (char)result);
        }

        #endregion
    }
}
