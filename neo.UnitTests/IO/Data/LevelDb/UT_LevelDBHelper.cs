using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.IO;
using Neo.IO.Data.LevelDB;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Neo.UnitTests.IO.Data.LevelDB
{
    internal class DBContent : ISerializable
    {
        public byte[] content = null;

        public DBContent()
        {
        }

        public DBContent(string input)
        {
            content = Encoding.Default.GetBytes(input);
        }

        public override string ToString()
        {
            if (content == null) return null;
            return Encoding.Default.GetString(content);
        }

        public int Size => content.Length;

        public void Deserialize(BinaryReader reader)
        {
            ulong length = reader.ReadVarInt();
            content = new byte[length];

            for (ulong i = 0; i < length; i++)
            {
                content[i] = reader.ReadByte();
            }
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.WriteVarBytes(content);
        }
    }

    internal class DBContentPair
    {
        public DBContent key;
        public DBContent value;

        public DBContentPair(DBContent key, DBContent value)
        {
            this.key = key;
            this.value = value;
        }
    }

    [TestClass]
    public class UT_LevelDBHelper
    {
        private WriteBatch batch = null;
        private DB db = null;
        private string path = "testDB";

        [TestInitialize]
        public void Initialize()
        {
            DirectoryInfo dir = new DirectoryInfo(path);
            db = DB.Open(path, new Options { CreateIfMissing = true });
            batch = new WriteBatch();
        }

        [TestCleanup]
        public void CleanUp()
        {
            db.Dispose();
            DirectoryInfo dir = new DirectoryInfo(path);
            dir.Delete(true);
            batch.Clear();
        }

        [TestMethod]
        public void TestDelete()
        {
            byte prefix = 1;
            WriteBatch batch = new WriteBatch();
            DBContentPair pair = new DBContentPair(new DBContent("testDeleteKey"), new DBContent("testDeleteValue"));

            batch.Put(prefix, pair.key, pair.value);
            db.Write(WriteOptions.Default, batch);
            db.Get<DBContent>(ReadOptions.Default, prefix, pair.key).Should().NotBeNull();

            batch.Delete(prefix, pair.key);
            db.Write(WriteOptions.Default, batch);
            db.TryGet<DBContent>(ReadOptions.Default, prefix, pair.key).Should().BeNull();

            batch = new WriteBatch();
            batch.Put(prefix, pair.key, pair.value);
            batch.Delete(prefix, pair.key);
            db.Write(WriteOptions.Default, batch);
            db.TryGet<DBContent>(ReadOptions.Default, prefix, pair.key).Should().BeNull();
        }

        [TestMethod]
        public void TestFind1()
        {
            byte prefix = 2;
            WriteBatch batch = new WriteBatch();
            DBContentPair[] pairs = new DBContentPair[] {
                new DBContentPair(new DBContent("testFind1Key1"), new DBContent("testFind1Value1")),
                new DBContentPair(new DBContent("testFind1Key2"), new DBContent("testFind1Value2")),
                new DBContentPair(new DBContent("testFind1Key3"), new DBContent("testFind1Value3")),
                new DBContentPair(new DBContent("testFind1Key4"), new DBContent("testFind1Value4")),
                new DBContentPair(new DBContent("testFind1Key5"), new DBContent("testFind1Value5"))};

            for (int i = 0; i < pairs.Length; i++)
            {
                batch.Put(prefix, pairs[i].key, pairs[i].value);
            }
            db.Write(WriteOptions.Default, batch);

            DBContent[] result = db.Find<DBContent>(ReadOptions.Default, prefix).ToArray();
            DBContent[] expected = pairs.Select(p => p.value).ToArray();

            result.Length.Should().Be(expected.Length);
            for (int i = 0; i < result.Length; i++)
            {
                result[i].ShouldBeEquivalentTo(expected[i]);
            }
        }

        [TestMethod]
        public void TestFind2()
        {
            byte prefix = 3;
            WriteBatch batch = new WriteBatch();
            DBContentPair[] pairs = new DBContentPair[] {
                new DBContentPair(new DBContent("testFind2Key1"), new DBContent("testFind2Value1")),
                new DBContentPair(new DBContent("testFind2Key2"), new DBContent("testFind2Value2")),
                new DBContentPair(new DBContent("testFind2Key3"), new DBContent("testFind2Value3")),
                new DBContentPair(new DBContent("testFind2Key4"), new DBContent("testFind2Value4")),
                new DBContentPair(new DBContent("testFind2Key5"), new DBContent("testFind2Value5"))};

            for (int i = 0; i < pairs.Length; i++)
            {
                batch.Put(prefix, pairs[i].key, pairs[i].value);
            }
            db.Write(WriteOptions.Default, batch);

            DBContentPair[] result = db.Find<DBContentPair>(ReadOptions.Default, prefix,
                (k, v) => new DBContentPair(k.ToArray().AsSerializable<DBContent>(), v.ToArray().AsSerializable<DBContent>())).ToArray();
            result.Length.Should().Be(pairs.Length);
            for (int i = 0; i < result.Length; i++)
            {
                result[i].value.ShouldBeEquivalentTo(pairs[i].value);
            }
        }

        [TestMethod]
        public void TestGet1()
        {
            byte prefix = 4;
            WriteBatch batch = new WriteBatch();
            DBContentPair pair = new DBContentPair(new DBContent("testGet1Key"), new DBContent("testGet1Value"));

            batch.Put(prefix, pair.key, pair.value);
            db.Write(WriteOptions.Default, batch);
            db.Get<DBContent>(ReadOptions.Default, prefix, pair.key).ShouldBeEquivalentTo(pair.value);
            Action action = () => db.Get<DBContent>(ReadOptions.Default, prefix, new DBContent("testGet1Key2"));
            action.ShouldThrow<LevelDBException>();
        }

        [TestMethod]
        public void TestGet2()
        {
            byte prefix = 5;
            WriteBatch batch = new WriteBatch();
            DBContentPair pair = new DBContentPair(new DBContent("testGet2Key"), new DBContent("testGet2Value"));

            batch.Put(prefix, pair.key, pair.value);
            db.Write(WriteOptions.Default, batch);
            db.Get<string>(ReadOptions.Default, prefix, pair.key, v => v.ToArray().AsSerializable<DBContent>().ToString()).Should().Be(pair.value.ToString());
            Action action = () => db.Get<string>(ReadOptions.Default, prefix, new DBContent("testGet2Key2"), v => v.ToArray().AsSerializable<DBContent>().ToString());
            action.ShouldThrow<LevelDBException>();
        }

        [TestMethod]
        public void TestPut()
        {
            byte prefix = 6;
            WriteBatch batch = new WriteBatch();
            DBContentPair pair = new DBContentPair(new DBContent("testPutKey"), new DBContent("testPutValue"));

            batch.Put(prefix, pair.key, pair.value);
            db.Write(WriteOptions.Default, batch);
            db.Get<DBContent>(ReadOptions.Default, prefix, pair.key).ShouldBeEquivalentTo(pair.value);
        }

        [TestMethod]
        public void TestTryGet1()
        {
            byte prefix = 7;
            WriteBatch batch = new WriteBatch();
            DBContentPair pair = new DBContentPair(new DBContent("testTryGet1Key"), new DBContent("testTryGet1Value"));

            batch.Put(prefix, pair.key, pair.value);
            db.Write(WriteOptions.Default, batch);
            db.TryGet<DBContent>(ReadOptions.Default, prefix, pair.key).ShouldBeEquivalentTo(pair.value);
            db.TryGet<DBContent>(ReadOptions.Default, prefix, new DBContent("testTryGet1Key2")).Should().BeNull();
        }

        [TestMethod]
        public void TestTryGet2()
        {
            byte prefix = 8;
            WriteBatch batch = new WriteBatch();
            DBContentPair pair = new DBContentPair(new DBContent("testTryGet2Key"), new DBContent("testTryGet2Value"));

            batch.Put(prefix, pair.key, pair.value);
            db.Write(WriteOptions.Default, batch);
            db.TryGet<string>(ReadOptions.Default, prefix, pair.key, v => v.ToArray().AsSerializable<DBContent>().ToString()).Should().Be(pair.value.ToString());
            db.TryGet<string>(ReadOptions.Default, prefix, new DBContent("testTryGet2Key2"), v => v.ToArray().AsSerializable<DBContent>().ToString()).Should().BeNull();
        }
    }
}