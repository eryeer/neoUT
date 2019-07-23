using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.Network.P2P;
using Neo.SmartContract;
using Neo.Wallets;
using System;
using System.Collections.Generic;
using System.Net;
using System.Numerics;
using System.Security.Cryptography;

namespace Neo.UnitTests
{
    [TestClass]
    public class UT_Helper
    {
        [TestMethod]
        public void GetHashData()
        {
            TestVerifiable verifiable = new TestVerifiable();
            byte[] res = verifiable.GetHashData();
            res.Length.Should().Be(8);
            byte[] requiredData = new byte[] { 7, 116, 101, 115, 116, 83, 116, 114 };
            for (int i = 0; i < requiredData.Length; i++)
            {
                res[i].Should().Be(requiredData[i]);
            }
        }

        [TestMethod]
        public void Sign()
        {
            TestVerifiable verifiable = new TestVerifiable();
            byte[] res = verifiable.Sign(new KeyPair(TestUtils.GetByteArray(32, 0x42)));
            res.Length.Should().Be(64);
        }

        [TestMethod]
        public void ToScriptHash()
        {
            byte[] testByteArray = TestUtils.GetByteArray(64, 0x42);
            UInt160 res = testByteArray.ToScriptHash();
            res.Should().Be(UInt160.Parse("2d3b96ae1bcc5a585e075e3b81920210dec16302"));
        }

        [TestMethod]
        public void TestGetLowestSetBit()
        {
            var big1 = new BigInteger(0);
            big1.GetLowestSetBit().Should().Be(-1);

            var big2 = new BigInteger(100);
            big2.GetLowestSetBit().Should().Be(2);
        }

        [TestMethod]
        public void TestHexToBytes()
        {
            string nullStr = null;
            nullStr.HexToBytes().ToHexString().Should().Be(new byte[0].ToHexString());
            string emptyStr = "";
            emptyStr.HexToBytes().ToHexString().Should().Be(new byte[0].ToHexString());
            string str1 = "hab";
            Action action = () => str1.HexToBytes();
            action.ShouldThrow<FormatException>();
            string str2 = "0102";
            byte[] bytes = str2.HexToBytes();
            bytes.ToHexString().Should().Be(new byte[] { 0x01, 0x02 }.ToHexString());
        }

        [TestMethod]
        public void TestNextBigIntegerForRandom()
        {
            Random ran = new Random();
            Action action1 = () => ran.NextBigInteger(-1);
            action1.ShouldThrow<ArgumentException>();

            ran.NextBigInteger(0).Should().Be(0);

            Action action2 = () => ran.NextBigInteger(8);
            action2.ShouldNotThrow();

            Action action3 = () => ran.NextBigInteger(9);
            action3.ShouldNotThrow();
        }

        [TestMethod]
        public void TestNextBigIntegerForRandomNumberGenerator()
        {
            var ran = RandomNumberGenerator.Create();
            Action action1 = () => ran.NextBigInteger(-1);
            action1.ShouldThrow<ArgumentException>();

            ran.NextBigInteger(0).Should().Be(0);

            Action action2 = () => ran.NextBigInteger(8);
            action2.ShouldNotThrow();

            Action action3 = () => ran.NextBigInteger(9);
            action3.ShouldNotThrow();
        }

        [TestMethod]
        public void TestToInt64()
        {
            byte[] bytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            var ret = bytes.ToInt64(0);
            ret.GetType().Should().Be(typeof(long));
            ret.Should().Be(67305985);
        }

        [TestMethod]
        public void TestToUInt16()
        {
            byte[] bytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            var ret = bytes.ToUInt16(0);
            ret.GetType().Should().Be(typeof(ushort));
            ret.Should().Be(513);
        }

        [TestMethod]
        public void TestToUInt64()
        {
            byte[] bytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            var ret = bytes.ToUInt64(0);
            ret.GetType().Should().Be(typeof(ulong));
            ret.Should().Be(67305985);
        }

        [TestMethod]
        public void TestUnmapForIPAddress()
        {
            var addr = new IPAddress(new byte[] { 127, 0, 0, 1 });
            addr.Unmap().Should().Be(addr);

            var addr2 = addr.MapToIPv6();
            addr2.Unmap().Should().Be(addr);
        }

        [TestMethod]
        public void TestUnmapForIPEndPoin()
        {
            var addr = new IPAddress(new byte[] { 127, 0, 0, 1 });
            var endPoint = new IPEndPoint(addr, 8888);
            endPoint.Unmap().Should().Be(endPoint);

            var addr2 = addr.MapToIPv6();
            var endPoint2 = new IPEndPoint(addr2, 8888);
            endPoint2.Unmap().Should().Be(endPoint);
        }

        [TestMethod]
        public void TestWeightedAverage()
        {
            var foo1 = new Foo
            {
                Value = 1,
                Weight = 2
            };
            var foo2 = new Foo
            {
                Value = 2,
                Weight = 3
            };
            var list = new List<Foo>
            {
                foo1,foo2
            };
            list.WeightedAverage(p => p.Value, p => p.Weight).Should().Be(new BigInteger(1));

            var foo3 = new Foo
            {
                Value = 1,
                Weight = 0
            };
            var foo4 = new Foo
            {
                Value = 2,
                Weight = 0
            };
            var list2 = new List<Foo>
            {
                foo3, foo4
            };
            list2.WeightedAverage(p => p.Value, p => p.Weight).Should().Be(BigInteger.Zero);
        }

        [TestMethod]
        public void WeightFilter()
        {
            var w1 = new Woo
            {
                Value = 1
            };
            var w2 = new Woo
            {
                Value = 2
            };
            var list = new List<Woo>
            {
                w1, w2
            };
            var ret = list.WeightedFilter(0.3, 0.6, p => p.Value, (p, w) => new Result
            {
                Info = p,
                Weight = w
            });
            var sum = BigInteger.Zero;
            foreach (Result res in ret) {
                sum = BigInteger.Add(res.Weight, sum);
            }
            sum.Should().Be(BigInteger.Zero);

            var w3 = new Woo
            {
                Value = 3
            };
           
            var list2 = new List<Woo>
            {
                w1, w2, w3
            };
            var ret2 = list2.WeightedFilter(0.3, 0.4, p => p.Value, (p, w) => new Result
            {
                Info = p,
                Weight = w
            });
            sum = BigInteger.Zero;
            foreach (Result res in ret2)
            {
                sum = BigInteger.Add(res.Weight, sum);
            }
            sum.Should().Be(BigInteger.Zero);
        }
    }

    class Foo
    {
        public int Weight { set; get; }
        public int Value { set; get; }
    }

    class Woo
    {
        public int Value { set; get; }
    }

    class Result
    {
        public Woo Info { set; get; }
        public BigInteger Weight { set; get; }
    }
}