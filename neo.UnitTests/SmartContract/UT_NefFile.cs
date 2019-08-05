using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.IO;
using Neo.SmartContract;
using System;
using System.IO;

namespace Neo.UnitTests.SmartContract
{
    [TestClass]
    public class UT_NefFile
    {
        public NefFile file = new NefFile()
        {
            Compiler = "".PadLeft(32, ' '),
            Version = new Version(1, 2, 3, 4),
            Script = new byte[] { 0x01, 0x02, 0x03 }
        };

        [TestInitialize]
        public void TestSetup()
        {
            file.ScriptHash = file.Script.ToScriptHash();
            file.CheckSum = NefFile.ComputeChecksum(file);
        }

        [TestMethod]
        public void TestDeserialize()
        {
            byte[] wrongMagic = { 0x00, 0x00, 0x00, 0x00 };
            using (MemoryStream ms = new MemoryStream(1024))
            using (BinaryWriter writer = new BinaryWriter(ms))
            using (BinaryReader reader = new BinaryReader(ms))
            {
                ((ISerializable)file).Serialize(writer);
                ms.Seek(0, SeekOrigin.Begin);
                ms.Write(wrongMagic, 0, 4);
                ms.Seek(0, SeekOrigin.Begin);
                ISerializable newFile = new NefFile();
                Action action = () => newFile.Deserialize(reader);
                action.ShouldThrow<FormatException>();
            }

            file.CheckSum = 0;
            using (MemoryStream ms = new MemoryStream(1024))
            using (BinaryWriter writer = new BinaryWriter(ms))
            using (BinaryReader reader = new BinaryReader(ms))
            {
                ((ISerializable)file).Serialize(writer);
                ms.Seek(0, SeekOrigin.Begin);
                ISerializable newFile = new NefFile();
                Action action = () => newFile.Deserialize(reader);
                action.ShouldThrow<FormatException>();
            }

            file.CheckSum = NefFile.ComputeChecksum(file);
            file.ScriptHash = new byte[] { 0x01 }.ToScriptHash();
            using (MemoryStream ms = new MemoryStream(1024))
            using (BinaryWriter writer = new BinaryWriter(ms))
            using (BinaryReader reader = new BinaryReader(ms))
            {
                ((ISerializable)file).Serialize(writer);
                ms.Seek(0, SeekOrigin.Begin);
                ISerializable newFile = new NefFile();
                Action action = () => newFile.Deserialize(reader);
                action.ShouldThrow<FormatException>();
            }

            file.ScriptHash = file.Script.ToScriptHash();
            var data = file.ToArray();
            var newFile1 = data.AsSerializable<NefFile>();
            newFile1.Version.Should().Be(file.Version);
            newFile1.Compiler.Should().Be(file.Compiler);
            newFile1.ScriptHash.Should().Be(file.ScriptHash);
            newFile1.CheckSum.Should().Be(file.CheckSum);
            newFile1.Script.Should().BeEquivalentTo(file.Script);
        }

        [TestMethod]
        public void TestGetSize()
        {
            file.Size.Should().Be( 4 + 32 + 16 + 20 + 4 + 4 );
        }
    }
}
