using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using Neo.IO.Data.LevelDB;
using System.Runtime.InteropServices;
using System.Text;
using Neo.Cryptography;

namespace Neo.UnitTests.IO.Data.LevelDb
{
    public class test {}
    [TestClass]
    public class UT_Slice
    {
        private Slice sliceTest;
        [TestMethod]
        public void TestConstructor()
        {
            IntPtr parr = Marshal.AllocHGlobal(1);
            Marshal.WriteByte(parr, 0x01);
            UIntPtr plength = new UIntPtr((uint)1);
            sliceTest = new Slice(parr, plength);
            Assert.IsNotNull(sliceTest);
            Assert.IsInstanceOfType(sliceTest, typeof(Slice));
            Slice slice = (byte)0x01;
            Assert.AreEqual(slice, sliceTest);
            Marshal.FreeHGlobal(parr);
        }
        [TestMethod]
        public void TestCompareTo()
        {
            byte[] arr = { 0x01, 0x02 };
            Slice slice = arr;
            sliceTest = arr;
            int result = sliceTest.CompareTo(slice);
            Assert.AreEqual(0, result);
            arr = new byte[] { 0x01 };
            sliceTest = arr;
            result = sliceTest.CompareTo(slice);
            Assert.AreEqual(-1, result);
            arr = new byte[] { 0x01, 0x02, 0x03 };
            result = sliceTest.CompareTo(slice);
            Assert.AreEqual(-1, result);
            arr = new byte[] { 0x01, 0x03 };
            result = sliceTest.CompareTo(slice);
            Assert.AreEqual(-1, result);
            arr = new byte[] { 0x01, 0x01 };
            result = sliceTest.CompareTo(slice);
            Assert.AreEqual(-1, result);
        }
        [TestMethod]
        public void TestEqualsSlice()
        {
            byte[] arr1 = { 0x01, 0x02 };
            byte[] arr2 = { 0x01, 0x02 };
            Slice slice = arr1;
            sliceTest = arr1;
            Assert.IsTrue(sliceTest.Equals(slice));
            sliceTest = arr2;
            Assert.IsTrue(sliceTest.Equals(slice));
            arr2 = new byte[] { 0x01, 0x03 };
            sliceTest = arr2;
            Assert.IsFalse(sliceTest.Equals(slice));

        }
        [TestMethod]
        public void TestEqualsObj()
        {
            sliceTest = new byte[]{0x01};
            object slice = null;
            bool result = sliceTest.Equals(slice);
            Assert.AreEqual(false, result);
            slice = new test();
            result = sliceTest.Equals(slice);
            Assert.AreEqual(false, result);
            slice = sliceTest;
            result = sliceTest.Equals(slice);
            Assert.AreEqual(true, result);
            Slice s = new byte[]{0x01};
            result = sliceTest.Equals(s);
            Assert.AreEqual(true, result);
            s = new byte[]{0x01, 0x02};
            result = sliceTest.Equals(s);
            Assert.AreEqual(false, result);
        }
        [TestMethod]
        public void TestGetHashCode()
        {
            byte[] arr = new byte[]{0x01, 0x02};
            sliceTest = arr;
            int hash1 = (int)arr.Murmur32(0);
            int hash2 = sliceTest.GetHashCode();
            Assert.AreEqual(hash2, hash1);
        }
        [TestMethod]
        public void TestFromArray()
        {
            byte[] arr = new byte[]{
                0x01,0x01,0x01,0x01,
            };
            IntPtr parr = Marshal.AllocHGlobal(arr.Length);
            for (int i = 0; i < arr.Length; i++)
            {
                Marshal.WriteByte(parr + i, 0x01);
            }
            UIntPtr plength = new UIntPtr((uint)arr.Length);
            Slice slice = new Slice(parr, plength);
            sliceTest = arr;
            Assert.AreEqual(slice, sliceTest);
            Marshal.FreeHGlobal(parr);
        }
        [TestMethod]
        public void TestFromBool()
        {
            byte[] arr = { 0x01 };
            Slice slice = arr;
            sliceTest = true;
            Assert.AreEqual(slice, sliceTest);
        }
        [TestMethod]
        public void TestFromByte()
        {
            sliceTest = (byte)0x01;
            byte[] arr = { 0x01 };
            Slice slice = arr;
            Assert.AreEqual(slice, sliceTest);
        }
        [TestMethod]
        public void TestFromDouble()
        {
            Slice slice = BitConverter.GetBytes(1.23D);
            sliceTest = 1.23D;
            Assert.AreEqual(slice, sliceTest);
        }
        [TestMethod]
        public void TestFromShort()
        {
            Slice slice = BitConverter.GetBytes((short)1234);
            sliceTest = (short)1234;
            Assert.AreEqual(slice, sliceTest);
        }
        [TestMethod]
        public void TestFromInt()
        {
            Slice slice = BitConverter.GetBytes(-1234);
            sliceTest = -1234;
            Assert.AreEqual(slice, sliceTest);
        }
        [TestMethod]
        public void TestFromLong()
        {
            Slice slice = BitConverter.GetBytes(-1234L);
            sliceTest = -1234L;
            Assert.AreEqual(slice, sliceTest);
        }
        [TestMethod]
        public void TestFromFloat()
        {
            Slice slice = BitConverter.GetBytes(1.234F);
            sliceTest = 1.234F;
            Assert.AreEqual(slice, sliceTest);
        }
        [TestMethod]
        public void TestFromString()
        {
            string str = "abcdefghijklmnopqrstuvwxwz!@#$%^&*&()_+?><你好";
            Slice slice = Encoding.UTF8.GetBytes(str);
            sliceTest = str;
            Assert.AreEqual(slice, sliceTest);
        }
        [TestMethod]
        public void TestFromUnshort()
        {
            Slice slice = BitConverter.GetBytes((ushort)12345);
            sliceTest = (ushort)12345;
            Assert.AreEqual(slice, sliceTest);
        }
        [TestMethod]
        public void TestFromUint()
        {
            Slice slice = BitConverter.GetBytes((uint)12345);
            sliceTest = (uint)12345;
            Assert.AreEqual(slice, sliceTest);
        }
        [TestMethod]
        public void TestFromUlong()
        {
            Slice slice = BitConverter.GetBytes(12345678UL);
            sliceTest = 12345678UL;
            Assert.AreEqual(slice, sliceTest);
        }
        [TestMethod]
        public void TestLessThan()
        {
            sliceTest = new byte[]{0x01};
            Slice slice = new byte[]{0x02};
            bool result = sliceTest < slice;
            Assert.AreEqual(true, result);
            slice = new byte[]{0x01};
            result = sliceTest < slice;
            Assert.AreEqual(false, result);
            slice = new byte[]{0x00};
            result = sliceTest < slice;
            Assert.AreEqual(false, result);
        }
        [TestMethod]
        public void TestLessThanAndEqual()
        {
            sliceTest = new byte[]{0x01};
            Slice slice = new byte[]{0x02};
            bool result = sliceTest <= slice;
            Assert.AreEqual(true, result);
            slice = new byte[]{0x01};
            result = sliceTest <= slice;
            Assert.AreEqual(true, result);
            slice = new byte[]{0x00};
            result = sliceTest <= slice;
            Assert.AreEqual(false, result);
        }
        [TestMethod]
        public void TestGreatThan()
        {
            sliceTest = new byte[]{0x01};
            Slice slice = new byte[]{0x00};
            bool result = sliceTest > slice;
            Assert.AreEqual(true, result);
            slice = new byte[]{0x01};
            result = sliceTest > slice;
            Assert.AreEqual(false, result);
            slice = new byte[]{0x02};
            result = sliceTest > slice;
            Assert.AreEqual(false, result);
        }
        [TestMethod]
        public void TestGreatThanAndEqual()
        {
            sliceTest = new byte[]{0x01};
            Slice slice = new byte[]{0x00};
            bool result = sliceTest >= slice;
            Assert.AreEqual(true, result);
            slice = new byte[]{0x01};
            result = sliceTest >= slice;
            Assert.AreEqual(true, result);
            slice = new byte[]{0x02};
            result = sliceTest >= slice;
            Assert.AreEqual(false, result);
        }
        [TestMethod]
        public void TestEqual()
        {
            sliceTest = new byte[]{0x01};
            Slice slice = new byte[]{0x00};
            bool result = sliceTest == slice;
            Assert.AreEqual(false, result);
            slice = new byte[]{0x01};
            result = sliceTest == slice;
            Assert.AreEqual(true, result);
            slice = new byte[]{0x02};
            result = sliceTest == slice;
            Assert.AreEqual(false, result);
        }
        [TestMethod]
        public void TestUnequal()
        {
            sliceTest = new byte[]{0x01};
            Slice slice = new byte[]{0x00};
            bool result = sliceTest != slice;
            Assert.AreEqual(true, result);
            slice = new byte[]{0x01};
            result = sliceTest != slice;
            Assert.AreEqual(false, result);
        }
    }
}