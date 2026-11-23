// using Xunit;
// using HashCode = Xbc.DbAccess.Common.HashCode;

// namespace Xbc.DbAccess.UnitTests.Common
// {
//     /// <summary>
//     /// HashCode 的单元测试。
//     /// </summary>
//     public class HashCodeTests
//     {
//         #region Add 测试

//         [Fact]
//         public void Add_SingleValue_UpdatesHash()
//         {
//             var hashCode = new HashCode();
//             hashCode.Add(42);
//             var result = hashCode.GetHashValue();
//             Assert.NotEqual(0, result);
//         }

//         [Fact]
//         public void Add_NullValue_DoesNotThrow()
//         {
//             var hashCode = new HashCode();
//             hashCode.Add<string>(null);
//             var result = hashCode.GetHashValue();
//             Assert.NotEqual(0, result);
//         }

//         [Fact]
//         public void Add_MultipleValues_UpdatesHash()
//         {
//             var hashCode = new HashCode();
//             hashCode.Add(1);
//             hashCode.Add(2);
//             hashCode.Add(3);
//             var result = hashCode.GetHashValue();
//             Assert.NotEqual(0, result);
//         }

//         [Fact]
//         public void Add_SameValuesInDifferentOrder_DifferentHash()
//         {
//             var hashCode1 = new HashCode();
//             hashCode1.Add(1);
//             hashCode1.Add(2);

//             var hashCode2 = new HashCode();
//             hashCode2.Add(2);
//             hashCode2.Add(1);

//             Assert.NotEqual(hashCode1.GetHashValue(), hashCode2.GetHashValue());
//         }

//         [Fact]
//         public void Add_SameValuesSameOrder_SameHash()
//         {
//             var hashCode1 = new HashCode();
//             hashCode1.Add(1);
//             hashCode1.Add(2);
//             hashCode1.Add("test");

//             var hashCode2 = new HashCode();
//             hashCode2.Add(1);
//             hashCode2.Add(2);
//             hashCode2.Add("test");

//             Assert.Equal(hashCode1.GetHashValue(), hashCode2.GetHashValue());
//         }

//         #endregion

//         #region AddRange 测试

//         [Fact]
//         public void AddRange_ValidCollection_AddsAllValues()
//         {
//             var hashCode = new HashCode();
//             var values = new List<int> { 1, 2, 3 };
//             hashCode.AddRange(values);
//             var result = hashCode.GetHashValue();
//             Assert.NotEqual(0, result);
//         }

//         [Fact]
//         public void AddRange_NullCollection_ThrowsArgumentNullException()
//         {
//             var hashCode = new HashCode();
//             Assert.Throws<ArgumentNullException>(() => hashCode.AddRange<int>(null));
//         }

//         [Fact]
//         public void AddRange_EmptyCollection_DoesNotChangeHash()
//         {
//             var hashCode = new HashCode();
//             var initialHash = hashCode.GetHashValue();
//             hashCode.AddRange(new List<int>());
//             var result = hashCode.GetHashValue();
//             Assert.Equal(initialHash, result);
//         }

//         [Fact]
//         public void AddRange_WithNullValues_HandlesCorrectly()
//         {
//             var hashCode = new HashCode();
//             var values = new List<string> { "a", null, "c" };
//             hashCode.AddRange(values);
//             var result = hashCode.GetHashValue();
//             Assert.NotEqual(0, result);
//         }

//         #endregion

//         #region GetHashValue 测试

//         [Fact]
//         public void GetHashValue_NoValuesAdded_ReturnsNonZero()
//         {
//             var hashCode = new HashCode();
//             var result = hashCode.GetHashValue();
//             Assert.NotEqual(0, result);
//         }

//         [Fact]
//         public void GetHashValue_CalledMultipleTimes_ReturnsSameValue()
//         {
//             var hashCode = new HashCode();
//             hashCode.Add(42);
//             hashCode.Add("test");

//             var hash1 = hashCode.GetHashValue();
//             var hash2 = hashCode.GetHashValue();

//             Assert.Equal(hash1, hash2);
//         }

//         #endregion

//         #region GetHashCode 测试

//         [Fact]
//         public void GetHashCode_ReturnsSameAsGetHashValue()
//         {
//             var hashCode = new HashCode();
//             hashCode.Add(42);

//             var hashValue = hashCode.GetHashValue();
//             var hashCodeValue = hashCode.GetHashCode();

//             Assert.Equal(hashValue, hashCodeValue);
//         }

//         #endregion

//         #region Combine 测试

//         [Fact]
//         public void Combine_TwoValues_ReturnsHash()
//         {
//             var result = HashCode.Combine(1, 2);
//             Assert.NotEqual(0, result);
//         }

//         [Fact]
//         public void Combine_SameValues_ReturnsSameHash()
//         {
//             var hash1 = HashCode.Combine(1, 2);
//             var hash2 = HashCode.Combine(1, 2);

//             Assert.Equal(hash1, hash2);
//         }

//         [Fact]
//         public void Combine_DifferentValues_ReturnsDifferentHash()
//         {
//             var hash1 = HashCode.Combine(1, 2);
//             var hash2 = HashCode.Combine(2, 1);

//             Assert.NotEqual(hash1, hash2);
//         }

//         [Fact]
//         public void Combine_WithNullValue_ReturnsHash()
//         {
//             var result = HashCode.Combine(1, (string)null);
//             Assert.NotEqual(0, result);
//         }

//         [Fact]
//         public void Combine_ThreeValues_ReturnsHash()
//         {
//             var result = HashCode.Combine(1, 2, 3);
//             Assert.NotEqual(0, result);
//         }

//         [Fact]
//         public void Combine_FourValues_ReturnsHash()
//         {
//             var result = HashCode.Combine(1, 2, 3, 4);
//             Assert.NotEqual(0, result);
//         }

//         [Fact]
//         public void Combine_FiveValues_ReturnsHash()
//         {
//             var result = HashCode.Combine(1, 2, 3, 4, 5);
//             Assert.NotEqual(0, result);
//         }

//         [Fact]
//         public void Combine_SixValues_ReturnsHash()
//         {
//             var result = HashCode.Combine(1, 2, 3, 4, 5, 6);
//             Assert.NotEqual(0, result);
//         }

//         #endregion

//         #region 碰撞测试

//         [Fact]
//         public void HashCode_DifferentObjects_LowCollision()
//         {
//             var hashes = new HashSet<int>();
//             var collisionCount = 0;

//             for (int i = 0; i < 1000; i++)
//             {
//                 var hashCode = new HashCode();
//                 hashCode.Add(i);
//                 hashCode.Add($"string{i}");
//                 var hash = hashCode.GetHashValue();

//                 if (!hashes.Add(hash))
//                     collisionCount++;
//             }

//             // 碰撞率应该很低（小于 1%）
//             Assert.True(collisionCount < 10, $"Too many collisions: {collisionCount}");
//         }

//         #endregion

//         #region 复杂对象测试

//         [Fact]
//         public void Add_ComplexObject_UsesObjectHashCode()
//         {
//             var obj = new TestObject { Id = 1, Name = "Test" };
//             var hashCode = new HashCode();
//             hashCode.Add(obj);

//             var result = hashCode.GetHashValue();
//             Assert.NotEqual(0, result);
//         }

//         [Fact]
//         public void Add_DifferentComplexObjects_DifferentHash()
//         {
//             var obj1 = new TestObject { Id = 1, Name = "Test1" };
//             var obj2 = new TestObject { Id = 2, Name = "Test2" };

//             var hashCode1 = new HashCode();
//             hashCode1.Add(obj1);

//             var hashCode2 = new HashCode();
//             hashCode2.Add(obj2);

//             Assert.NotEqual(hashCode1.GetHashValue(), hashCode2.GetHashValue());
//         }

//         [Fact]
//         public void Add_EquivalentComplexObjects_SameHash()
//         {
//             var obj1 = new TestObject { Id = 1, Name = "Test" };
//             var obj2 = new TestObject { Id = 1, Name = "Test" };

//             var hashCode1 = new HashCode();
//             hashCode1.Add(obj1);

//             var hashCode2 = new HashCode();
//             hashCode2.Add(obj2);

//             Assert.Equal(hashCode1.GetHashValue(), hashCode2.GetHashValue());
//         }

//         #endregion

//         #region 测试辅助类

//         public class TestObject : IEquatable<TestObject>
//         {
//             public int Id { get; set; }
//             public string Name { get; set; }

//             public bool Equals(TestObject other)
//             {
//                 if (other is null) return false;
//                 return Id == other.Id && Name == other.Name;
//             }

//             public override bool Equals(object obj) => Equals(obj as TestObject);

//             public override int GetHashCode()
//             {
//                 var hash = new HashCode();
//                 hash.Add(Id);
//                 hash.Add(Name);
//                 return hash.GetHashValue();
//             }
//         }

//         #endregion
//     }
// }
