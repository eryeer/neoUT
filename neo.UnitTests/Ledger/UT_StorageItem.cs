using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.IO;
using Neo.Ledger;
using System.IO;
using System.Text;

namespace Neo.UnitTests.Ledger
{
    [TestClass]
    public class UT_StorageItem
    {
        StorageItem uut;

        [TestInitialize]
        public void TestSetup()
        {
            uut = new StorageItem();
        }

        [TestMethod]
        public void TestSize_Get()
        {
            uut.Value = TestUtils.GetByteArray(10, 0x42);
            uut.Size.Should().Be(12); // 2 + 10

            uut.Value = TestUtils.GetByteArray(88, 0x42);
            uut.Size.Should().Be(90); // 2 + 88
        }

        [TestMethod]
        public void TestClone()
        {
            uut.Value = TestUtils.GetByteArray(10, 0x42);

            StorageItem newSi = ((ICloneable<StorageItem>)uut).Clone();
            newSi.Value.Length.Should().Be(10);
            newSi.Value[0].Should().Be(0x42);
            for (int i = 1; i < 10; i++)
            {
                newSi.Value[i].Should().Be(0x20);
            }
        }

        [TestMethod]
        public void TestDeserialize()
        {
            byte[] data = new byte[] { 10, 66, 32, 32, 32, 32, 32, 32, 32, 32, 32, 0 };
            int index = 0;
            using (MemoryStream ms = new MemoryStream(data, index, data.Length - index, false))
            {
                using (BinaryReader reader = new BinaryReader(ms))
                {
                    uut.Deserialize(reader);
                }
            }
            uut.Value.Length.Should().Be(10);
            uut.Value[0].Should().Be(0x42);
            for (int i = 1; i < 10; i++)
            {
                uut.Value[i].Should().Be(0x20);
            }
        }

        [TestMethod]
        public void TestSerialize()
        {
            uut.Value = TestUtils.GetByteArray(10, 0x42);

            byte[] data;
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream, Encoding.ASCII, true))
                {
                    uut.Serialize(writer);
                    data = stream.ToArray();
                }
            }

            byte[] requiredData = new byte[] { 10, 66, 32, 32, 32, 32, 32, 32, 32, 32, 32, 0 };

            data.Length.Should().Be(requiredData.Length);
            data.ToHexString().Should().Be(requiredData.ToHexString());
        }

        [TestMethod]
        public void TestFromReplica()
        {
            uut.Value = TestUtils.GetByteArray(10, 0x42);
            uut.IsConstant = true;
            StorageItem dest = new StorageItem();
            ((ICloneable<StorageItem>)dest).FromReplica(uut);
            dest.Value.Should().BeEquivalentTo(uut.Value);
            dest.IsConstant.Should().Be(uut.IsConstant);
        }
    }
}
