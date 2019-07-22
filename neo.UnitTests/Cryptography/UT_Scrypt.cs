using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.Cryptography;
using System;
using System.Reflection;
using System.Security.Cryptography;

namespace Neo.UnitTests.Cryptography
{
    [TestClass]
    public class UT_SCrypt
    {
        [TestMethod]
        public void DeriveKeyTest()
        {
            int N = 32, r = 2, p = 2;

            var derivedkey = SCrypt.DeriveKey(new byte[] { 0x01, 0x02, 0x03 }, new byte[] { 0x04, 0x05, 0x06 }, N, r, p, 64).ToHexString();
            Assert.AreEqual("b6274d3a81892c24335ab46a08ec16d040ac00c5943b212099a44b76a9b8102631ab988fa07fb35357cee7b0e3910098c0774c0e97399997676d890b2bf2bb25", derivedkey);
        }

        [TestMethod]
        public void TestPBKDF2SHA256()
        {
            byte[] password = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            var mac = new HMACSHA256(password);
            byte[] salt = new byte[] { 1, 2 };
            long iterationCount = 1;
            int derivedKeyLength = 128;
            byte[] derivedKey = new byte[derivedKeyLength];

            MethodInfo dynMethod = typeof(SCrypt).GetMethod("PBKDF2_SHA256", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            dynMethod.Invoke(null, new object[] { mac, password, salt, salt.Length, iterationCount, derivedKey, derivedKeyLength });
            derivedKey.Should().BeEquivalentTo(new byte[] { 132, 254, 130, 62, 110, 249, 255, 127, 212, 209, 43, 6, 15, 254, 181, 70, 126, 187, 77, 122, 210, 116, 22, 181, 108, 101, 168,
                65, 31, 149, 248, 37, 137, 102, 180, 179, 75, 240, 70, 140, 178, 15, 240, 6, 3, 210, 154, 27, 77, 25, 169, 54, 149, 160, 179, 234, 89, 20, 230, 29, 144, 203, 128, 148,
                144, 179, 194, 60, 218, 70, 19, 101, 213, 75, 2, 242, 206, 38, 78, 68, 68, 116, 48, 108, 77, 49, 53, 221, 26, 58, 125, 138, 41, 130, 122, 202, 111, 122, 161, 131, 93, 71,
                233, 73, 2, 118, 235, 155, 202, 72, 94, 60, 216, 205, 159, 141, 21, 236, 236, 252, 69, 228, 6, 134, 247, 171, 31, 211 });

            iterationCount = 2;
            dynMethod.Invoke(null, new object[] { mac, password, salt, salt.Length, iterationCount, derivedKey, derivedKeyLength });
            derivedKey.Should().BeEquivalentTo(new byte[] { 26, 137, 115, 92, 126, 227, 26, 200, 71, 145, 245, 205, 195, 191, 160, 231, 209, 139, 140, 224, 213, 35, 92, 108, 110, 233,
                107, 221, 35, 8, 164, 10, 246, 171, 33, 46, 207, 132, 191, 220, 247, 240, 183, 250, 15, 49, 197, 76, 210, 190, 202, 218, 73, 82, 81, 186, 96, 101, 38, 44, 169, 47, 193,
                29, 53, 205, 50, 170, 116, 79, 2, 147, 134, 23, 10, 18, 243, 214, 86, 144, 208, 23, 51, 238, 187, 152, 128, 130, 93, 41, 196, 208, 244, 125, 213, 143, 14, 75, 249, 95,
                226, 42, 171, 216, 242, 211, 14, 216, 26, 237, 39, 55, 183, 127, 143, 248, 177, 80, 84, 120, 233, 234, 125, 46, 9, 83, 143, 96 });

            Array.Clear(derivedKey, 0, derivedKeyLength);
            derivedKeyLength = 100;
            dynMethod.Invoke(null, new object[] { mac, password, salt, salt.Length, iterationCount, derivedKey, derivedKeyLength });
            derivedKey.Should().BeEquivalentTo(new byte[] { 26, 137, 115, 92, 126, 227, 26, 200, 71, 145, 245, 205, 195, 191, 160, 231, 209, 139, 140, 224, 213, 35, 92, 108, 110, 233, 107, 221, 35, 8, 164, 10, 246, 171, 33, 46, 207, 132, 191, 220, 247, 240, 183, 250, 15, 49, 197, 76, 210, 190, 202, 218, 73, 82, 81, 186, 96, 101, 38, 44, 169, 47, 193, 29, 53, 205, 50, 170, 116, 79, 2, 147, 134, 23, 10, 18, 243, 214, 86, 144, 208, 23, 51, 238, 187, 152, 128, 130, 93, 41, 196, 208, 244, 125, 213, 143, 14, 75, 249, 95, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        }

        [TestMethod]
        public void TestR()
        {
            MethodInfo dynMethod = typeof(SCrypt).GetMethod("R", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            ((uint)dynMethod.Invoke(null, new object[] { 10u, 1 })).Should().Be(20u);
        }
    }
}