using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.Cryptography;
using System.Reflection;

namespace Neo.UnitTests.Cryptography
{
    [TestClass]
    public class UT_Murmur3
    {
        [TestMethod]
        public void TestGetHashSize()
        {
            Murmur3 murmur3 = new Murmur3(1);
            murmur3.HashSize.Should().Be(32);
        }

        [TestMethod]
        public void TestHashCore()
        {
            Murmur3 murmur3 = new Murmur3(1);
            byte[] array = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 0 };
            int ibStart = 2, cbSize = 4;
            MethodInfo dynMethod = typeof(Murmur3).GetMethod("HashCore", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            FieldInfo seedField = typeof(Murmur3).GetField("seed", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo hashField = typeof(Murmur3).GetField("hash", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo lengthField = typeof(Murmur3).GetField("length", BindingFlags.NonPublic | BindingFlags.Instance);

            dynMethod.Invoke(murmur3, new object[] { array, ibStart, cbSize });
            seedField.GetValue(murmur3).Should().Be(1u);
            hashField.GetValue(murmur3).Should().Be(3097162471u);
            lengthField.GetValue(murmur3).Should().Be(4);

            cbSize = 1;
            dynMethod.Invoke(murmur3, new object[] { array, ibStart, cbSize });
            seedField.GetValue(murmur3).Should().Be(1u);
            hashField.GetValue(murmur3).Should().Be(2902277616u);
            lengthField.GetValue(murmur3).Should().Be(5);

            cbSize = 2;
            dynMethod.Invoke(murmur3, new object[] { array, ibStart, cbSize });
            seedField.GetValue(murmur3).Should().Be(1u);
            hashField.GetValue(murmur3).Should().Be(3565298997u);
            lengthField.GetValue(murmur3).Should().Be(7);

            cbSize = 3;
            dynMethod.Invoke(murmur3, new object[] { array, ibStart, cbSize });
            seedField.GetValue(murmur3).Should().Be(1u);
            hashField.GetValue(murmur3).Should().Be(3363440867u);
            lengthField.GetValue(murmur3).Should().Be(10);
        }
    }
}
